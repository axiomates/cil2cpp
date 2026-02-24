using Mono.Cecil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    // Active generic type parameter map (set during ConvertMethodBodyWithGenerics)
    private Dictionary<string, string>? _activeTypeParamMap;

    // Deferred generic specialization bodies — collected in Pass 1.5, converted after Pass 3.3
    // (disambiguation). Converting bodies before disambiguation causes call sites to use
    // pre-disambiguation names (e.g., Dictionary__ctor instead of Dictionary__ctor__System_Int32).
    private readonly List<(MethodDefinition CecilMethod, IRMethod IrMethod, Dictionary<string, string> TypeParamMap)>
        _deferredGenericBodies = new();

    /// <summary>
    /// Construct a unique key for a generic method instantiation.
    /// Includes parameter types to distinguish overloads (e.g. GetReference(Span) vs GetReference(ReadOnlySpan)).
    /// Shared between scanning (CollectGenericMethod) and call emission (EmitMethodCall).
    /// </summary>
    private static string MakeGenericMethodKey(string declaringType, string methodName,
        List<string> typeArgs, string paramSig = "")
        => $"{declaringType}::{methodName}<{string.Join(",", typeArgs)}>({paramSig})";

    /// <summary>
    /// Mangle a generic method instantiation name for C++ emission.
    /// Shared between scanning (CollectGenericMethod) and call emission fallback (EmitMethodCall).
    /// </summary>
    private static string MangleGenericMethodName(string declaringType, string methodName, List<string> typeArgs)
    {
        var typeCppName = CppNameMapper.MangleTypeName(declaringType);
        var argParts = string.Join("_", typeArgs.Select(CppNameMapper.MangleTypeName));
        return $"{typeCppName}_{CppNameMapper.MangleTypeName(methodName)}_{argParts}";
    }

    /// <summary>
    /// Pass 0: Scan all method bodies for GenericInstanceType references.
    /// Collects unique generic instantiations (e.g., Wrapper`1&lt;System.Int32&gt;).
    /// </summary>
    private void ScanGenericInstantiations()
    {
        foreach (var typeDef in _allTypes!)
        {
            // Scan interface references on the type itself
            var cecilTypeDef = typeDef.GetCecilType();
            if (cecilTypeDef.HasInterfaces)
            {
                foreach (var iface in cecilTypeDef.Interfaces)
                    CollectGenericType(iface.InterfaceType);
            }

            foreach (var methodDef in typeDef.Methods)
            {
                // Only scan reachable methods to avoid pulling in
                // unnecessary generic specializations from unreachable BCL methods
                if (!methodDef.HasGenericParameters
                    && !_reachability.IsReachable(methodDef.GetCecilMethod()))
                    continue;

                // Scan method signatures (return type, parameters)
                var cecilMethodSig = methodDef.GetCecilMethod();
                CollectGenericType(cecilMethodSig.ReturnType);
                foreach (var p in cecilMethodSig.Parameters)
                    CollectGenericType(p.ParameterType);

                if (!methodDef.HasBody) continue;
                var cecilMethod = cecilMethodSig;
                if (!cecilMethod.HasBody) continue;

                // Scan local variables
                foreach (var local in cecilMethod.Body.Variables)
                {
                    CollectGenericType(local.VariableType);
                }

                // Scan instructions
                foreach (var instr in cecilMethod.Body.Instructions)
                {
                    switch (instr.Operand)
                    {
                        case MethodReference methodRef:
                            CollectGenericType(methodRef.DeclaringType);
                            if (methodRef.ReturnType is GenericInstanceType)
                                CollectGenericType(methodRef.ReturnType);
                            foreach (var p in methodRef.Parameters)
                                CollectGenericType(p.ParameterType);
                            // Collect generic method instantiations
                            if (methodRef is GenericInstanceMethod gim)
                                CollectGenericMethod(gim);
                            break;
                        case FieldReference fieldRef:
                            CollectGenericType(fieldRef.DeclaringType);
                            CollectGenericType(fieldRef.FieldType);
                            break;
                        case TypeReference typeRef:
                            CollectGenericType(typeRef);
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Namespaces whose generic specializations should be skipped (BCL internal).
    /// These types can't be usefully compiled to C++.
    /// </summary>
    /// <summary>
    /// Namespaces whose generic specializations should be skipped.
    /// Only filter namespaces with types that truly can't compile to C++
    /// (JIT intrinsics, COM interop, runtime internals).
    /// Removed: System.Globalization (needed for number/string formatting),
    ///          System.IO (needed for Console BCL chain).
    /// </summary>
    private static readonly HashSet<string> FilteredGenericNamespaces =
    [
        "System.Runtime.Intrinsics",
        "System.Runtime.InteropServices",
        "System.Runtime.Loader",
        "System.Reflection",
        "System.Diagnostics",
        "System.Diagnostics.Tracing",
        "System.Resources",
        "System.Security",
        "System.Net",
        "System.Buffers.IndexOfAnyAsciiSearcher",  // SIMD-dependent search internals
        "Internal",
    ];

    /// <summary>
    /// Vector types in System.Runtime.Intrinsics that should be allowed through
    /// generic specialization despite the namespace filter. These are value types
    /// used as fields/locals in BCL code (String, Span, Number operations).
    /// Their IsSupported returns 0, forcing BCL to use scalar fallback paths.
    /// Only the struct definition is needed — method bodies are already SIMD-stubbed.
    /// </summary>
    private static readonly HashSet<string> VectorScalarFallbackTypes =
    [
        "System.Runtime.Intrinsics.Vector64`1",
        "System.Runtime.Intrinsics.Vector128`1",
        "System.Runtime.Intrinsics.Vector256`1",
        "System.Runtime.Intrinsics.Vector512`1",
        "System.Runtime.Intrinsics.Vector64",
        "System.Runtime.Intrinsics.Vector128",
        "System.Runtime.Intrinsics.Vector256",
        "System.Runtime.Intrinsics.Vector512",
    ];

    /// <summary>
    /// Check if an IL type full name is a Vector scalar fallback type (open or closed generic).
    /// Matches Vector64/128/256/512 base types but NOT X86/Arm/Wasm platform intrinsics.
    /// </summary>
    internal static bool IsVectorScalarFallbackILType(string ilFullName)
    {
        if (ilFullName.StartsWith("System.Runtime.Intrinsics.X86.") ||
            ilFullName.StartsWith("System.Runtime.Intrinsics.Arm.") ||
            ilFullName.StartsWith("System.Runtime.Intrinsics.Wasm."))
            return false;

        return ilFullName.StartsWith("System.Runtime.Intrinsics.Vector64") ||
               ilFullName.StartsWith("System.Runtime.Intrinsics.Vector128") ||
               ilFullName.StartsWith("System.Runtime.Intrinsics.Vector256") ||
               ilFullName.StartsWith("System.Runtime.Intrinsics.Vector512");
    }

    /// <summary>
    /// Types that should be filtered from generic specialization arguments only.
    /// More aggressive than ClrInternalTypeNames — these types may still compile as types,
    /// but creating generic specializations like List&lt;TimeZoneInfo&gt; produces stubs.
    /// </summary>
    private static readonly HashSet<string> FilteredGenericArgTypes =
    [
        "System.TimeZoneInfo",
        "System.Runtime.ExceptionServices.ExceptionDispatchInfo",
        "Internal.Win32.RegistryKey",
        "System.Attribute",
        "System.AttributeUsageAttribute",
    ];

    private void CollectGenericType(TypeReference typeRef)
    {
        if (typeRef is not GenericInstanceType git) return;

        // Skip if any type argument contains an unresolved generic parameter
        // (e.g., TResult, TResult[], Task<TResult> — all contain GenericParameter)
        if (git.GenericArguments.Any(ContainsGenericParameter))
            return;

        // Skip generic types from BCL internal namespaces
        // Exception: Vector64/128/256/512<T> types are allowed through —
        // they need struct definitions for BCL scalar fallback paths.
        var elemNs = git.ElementType.Namespace;
        if (!string.IsNullOrEmpty(elemNs) &&
            FilteredGenericNamespaces.Any(f => elemNs.StartsWith(f)))
        {
            if (!VectorScalarFallbackTypes.Contains(git.ElementType.FullName))
                return;
        }

        // Skip generic specializations where any type argument is from a filtered namespace
        // or is a CLR-internal type. Prevents creating List<RuntimePropertyInfo>,
        // Dictionary<String, RuntimeType>, etc. that can never compile.
        foreach (var arg in git.GenericArguments)
        {
            var argFullName = arg.FullName;
            // Strip nested type references (e.g., "Foo/Bar" → "Foo")
            var argNs = arg.Namespace;
            if (string.IsNullOrEmpty(argNs) && arg is TypeDefinition td)
                argNs = td.Namespace;
            if (string.IsNullOrEmpty(argNs) && argFullName.Contains('.'))
                argNs = argFullName[..argFullName.LastIndexOf('.')];

            if (!string.IsNullOrEmpty(argNs) &&
                FilteredGenericNamespaces.Any(f => argNs.StartsWith(f)))
            {
                if (!IsVectorScalarFallbackILType(argFullName))
                    return;
            }

            if (ClrInternalTypeNames.Contains(argFullName) ||
                FilteredGenericArgTypes.Contains(argFullName))
                return;

            // Also check nested types: "Outer/Inner" should match if "Outer" is CLR-internal
            if (argFullName.Contains('/'))
            {
                var outerTypeName = argFullName[..argFullName.IndexOf('/')];
                if (ClrInternalTypeNames.Contains(outerTypeName) ||
                    FilteredGenericArgTypes.Contains(outerTypeName))
                    return;
            }
        }

        var openTypeName = git.ElementType.FullName;
        var typeArgs = git.GenericArguments.Select(a => a.FullName).ToList();
        var key = $"{openTypeName}<{string.Join(",", typeArgs)}>";

        if (_genericInstantiations.ContainsKey(key)) return;

        var mangledName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, typeArgs);
        var cecilOpenType = git.ElementType.Resolve();

        _genericInstantiations[key] = new GenericInstantiationInfo(
            openTypeName, typeArgs, mangledName, cecilOpenType);
    }

    private void CollectGenericMethod(GenericInstanceMethod gim)
    {
        var elementMethod = gim.ElementMethod;
        var declaringType = elementMethod.DeclaringType.FullName;
        var methodName = elementMethod.Name;

        // Skip if any type argument contains an unresolved generic parameter
        if (gim.GenericArguments.Any(ContainsGenericParameter))
            return;

        var typeArgs = gim.GenericArguments.Select(a => a.FullName).ToList();
        // Include parameter types in key to distinguish overloads
        // (e.g. GetReference<T>(Span<T>) vs GetReference<T>(ReadOnlySpan<T>))
        var paramSig = string.Join(",", elementMethod.Parameters.Select(p => p.ParameterType.FullName));
        var key = MakeGenericMethodKey(declaringType, methodName, typeArgs, paramSig);
        if (_genericMethodInstantiations.ContainsKey(key)) return;

        var cecilMethod = elementMethod.Resolve();
        if (cecilMethod == null) return;

        var mangledName = MangleGenericMethodName(declaringType, methodName, typeArgs);

        // Disambiguate mangled name if another overload already uses it
        if (_genericMethodInstantiations.Values.Any(v => v.MangledName == mangledName))
        {
            // Build type parameter map to resolve generic params (!!0 etc.) before mangling
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < cecilMethod.GenericParameters.Count && i < typeArgs.Count; i++)
                typeParamMap[cecilMethod.GenericParameters[i].Name] = typeArgs[i];

            var paramSuffix = string.Join("_", cecilMethod.Parameters
                .Select(p => CppNameMapper.MangleTypeName(
                    ResolveGenericTypeName(p.ParameterType, typeParamMap))));
            mangledName += $"__{paramSuffix}";
        }

        _genericMethodInstantiations[key] = new GenericMethodInstantiationInfo(
            declaringType, methodName, typeArgs, mangledName, cecilMethod);
    }

    /// <summary>
    /// Create specialized IRMethods for each generic method instantiation found in Pass 0.
    /// </summary>
    private void CreateGenericMethodSpecializations()
    {
        // Fixpoint loop: processing a specialization body may discover new transitive
        // generic method instantiations (e.g., GetNameInlined<Byte> calls FindDefinedIndex<Byte>).
        var processed = new HashSet<string>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            // Snapshot keys to avoid collection-modified-during-enumeration
            var snapshot = _genericMethodInstantiations.ToList();
            foreach (var (key, info) in snapshot)
            {
                if (processed.Contains(key)) continue;
                processed.Add(key);
                changed = true;
                ProcessGenericMethodSpecialization(key, info);
            }
        }
    }

    private void ProcessGenericMethodSpecialization(string key, GenericMethodInstantiationInfo info)
    {
        var cecilMethod = info.CecilMethod;

        // Find the declaring IRType
        if (!_typeCache.TryGetValue(info.DeclaringTypeName, out var declaringIrType))
            return;

            // Build method-level type parameter map
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < cecilMethod.GenericParameters.Count && i < info.TypeArguments.Count; i++)
            {
                typeParamMap[cecilMethod.GenericParameters[i].Name] = info.TypeArguments[i];
            }

            // Validate generic constraints
            if (cecilMethod.HasGenericParameters)
                ValidateGenericConstraints(cecilMethod.GenericParameters, info.TypeArguments, info.MangledName);

            var returnTypeName = ResolveGenericTypeName(cecilMethod.ReturnType, typeParamMap);

            var irMethod = new IRMethod
            {
                Name = cecilMethod.Name,
                CppName = info.MangledName,
                DeclaringType = declaringIrType,
                // Use ResolveTypeForDecl (not GetCppTypeForDecl) to leverage _typeCache
                // for correct value type detection, especially for ref parameters (& → *)
                ReturnTypeCpp = ResolveTypeForDecl(returnTypeName),
                IsStatic = cecilMethod.IsStatic,
                IsVirtual = cecilMethod.IsVirtual,
                IsAbstract = cecilMethod.IsAbstract,
                IsConstructor = cecilMethod.IsConstructor,
                IsStaticConstructor = cecilMethod.IsConstructor && cecilMethod.IsStatic,
                IsGenericInstance = true,
            };

            // Propagate HasICallMapping from base method
            // e.g. Volatile.Write<T> has wildcard icall — all specializations are dead code
            if (declaringIrType.ILFullName != null &&
                ICallRegistry.Lookup(declaringIrType.ILFullName, cecilMethod.Name, cecilMethod.Parameters.Count) != null)
            {
                irMethod.HasICallMapping = true;
            }

            // Parameters
            foreach (var paramDef in cecilMethod.Parameters)
            {
                var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                irMethod.Parameters.Add(new IRParameter
                {
                    Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                    CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                    CppTypeName = ResolveTypeForDecl(paramTypeName),
                    ILTypeName = paramTypeName,
                    Index = paramDef.Index,
                });
            }

            // Local variables
            if (cecilMethod.HasBody)
            {
                foreach (var localDef in cecilMethod.Body.Variables)
                {
                    var localTypeName = ResolveGenericTypeName(localDef.VariableType, typeParamMap);
                    var resolvedCpp = ResolveTypeForDecl(localTypeName);
                    irMethod.Locals.Add(new IRLocal
                    {
                        Index = localDef.Index,
                        CppName = $"loc_{localDef.Index}",
                        CppTypeName = resolvedCpp,
                    });
                }
            }

            declaringIrType.Methods.Add(irMethod);

            // Convert method body with generic substitution context
            // (not added to methodBodies — we convert immediately with the type param map)
            if (cecilMethod.HasBody && !cecilMethod.IsAbstract)
            {
                // Skip methods with CLR-internal dependencies — generate stub instead
                if (HasClrInternalDependencies(cecilMethod))
                {
                    GenerateStubBody(irMethod);
                }
                else
                {
                    var methodInfo = new IL.MethodInfo(cecilMethod);
                    ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
                }
            }
    }

    /// <summary>
    /// Create specialized IRTypes for each generic instantiation found in Pass 0.
    /// All generic types (user + BCL) are monomorphized from their Cecil definitions.
    /// </summary>
    private void CreateGenericSpecializations()
    {
        foreach (var (key, info) in _genericInstantiations)
        {
            // Skip types already created (e.g., by BCL proxy system or reachability)
            if (_typeCache.ContainsKey(key)) continue;

            // Skip types we can't resolve (no Cecil definition available)
            if (info.CecilOpenType == null) continue;

            var openType = info.CecilOpenType;

            // Build type parameter map
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];

            // Validate generic constraints
            if (openType.HasGenericParameters)
                ValidateGenericConstraints(openType.GenericParameters, info.TypeArguments, key);

            var isDelegate = openType.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate";

            var irType = new IRType
            {
                ILFullName = key,
                CppName = info.MangledName,
                Name = info.MangledName,
                Namespace = openType.Namespace,
                IsValueType = openType.IsValueType,
                IsInterface = openType.IsInterface,
                IsAbstract = openType.IsAbstract,
                IsSealed = openType.IsSealed,
                IsGenericInstance = true,
                IsDelegate = isDelegate,
                GenericArguments = info.TypeArguments,
                IsRuntimeProvided = RuntimeProvidedTypes.Contains(info.OpenTypeName),
                SourceKind = _assemblySet.ClassifyAssembly(openType.Module.Assembly.Name.Name),
            };

            // Propagate generic parameter variances from open type definition
            if (openType.HasGenericParameters)
            {
                irType.GenericDefinitionCppName = CppNameMapper.MangleTypeName(info.OpenTypeName);
                foreach (var gp in openType.GenericParameters)
                {
                    var variance = (gp.Attributes & Mono.Cecil.GenericParameterAttributes.VarianceMask) switch
                    {
                        Mono.Cecil.GenericParameterAttributes.Covariant => GenericVariance.Covariant,
                        Mono.Cecil.GenericParameterAttributes.Contravariant => GenericVariance.Contravariant,
                        _ => GenericVariance.Invariant,
                    };
                    irType.GenericParameterVariances.Add(variance);
                }
            }
            else if (_typeCache.TryGetValue(info.OpenTypeName, out var openIrType)
                     && openIrType.GenericParameterVariances.Count > 0)
            {
                irType.GenericDefinitionCppName = openIrType.CppName;
                irType.GenericParameterVariances.AddRange(openIrType.GenericParameterVariances);
            }

            // Register value types
            if (openType.IsValueType)
            {
                CppNameMapper.RegisterValueType(key);
                CppNameMapper.RegisterValueType(info.MangledName);
            }

            // Fields from Cecil definition
            foreach (var fieldDef in openType.Fields)
            {
                var fieldTypeName = ResolveGenericTypeName(fieldDef.FieldType, typeParamMap);
                var irField = new IRField
                {
                    Name = fieldDef.Name,
                    CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                    FieldTypeName = fieldTypeName,
                    IsStatic = fieldDef.IsStatic,
                    IsPublic = fieldDef.IsPublic,
                    DeclaringType = irType,
                };
                if (fieldDef.IsStatic)
                    irType.StaticFields.Add(irField);
                else
                    irType.Fields.Add(irField);
            }

            // Propagate ExplicitSize from Cecil metadata (ECMA-335 II.10.1.2).
            // Critical for Vector128<T> (ClassSize=16), Vector256<T> (ClassSize=32), etc.
            if (irType.IsValueType && openType.HasLayoutInfo && openType.ClassSize > 0)
                irType.ExplicitSize = openType.ClassSize;

            // Calculate instance size
            CalculateInstanceSize(irType);

            // Register type early so self-referencing static field accesses work
            _module.Types.Add(irType);
            _typeCache[key] = irType;

            // Skip method specialization for Vector scalar fallback types.
            // We only need their struct definitions (for sizeof/locals in BCL methods).
            // Vector method call sites are already intercepted in IRBuilder.Emit.cs.
            if (IsVectorScalarFallbackILType(info.OpenTypeName))
                continue;

            // Create method specializations from Cecil definition
            if (openType != null)
            {
                foreach (var methodDef in openType.Methods)
                {
                    var returnTypeName = ResolveGenericTypeName(methodDef.ReturnType, typeParamMap);
                    var cppName = CppNameMapper.MangleMethodName(info.MangledName, methodDef.Name);
                    // op_Explicit/op_Implicit: disambiguate by return type (C++ can't overload by return type)
                    if (methodDef.Name is "op_Explicit" or "op_Implicit" or "op_CheckedExplicit" or "op_CheckedImplicit")
                        cppName = $"{cppName}_{CppNameMapper.MangleTypeName(returnTypeName)}";

                    var irMethod = new IRMethod
                    {
                        Name = methodDef.Name,
                        CppName = cppName,
                        DeclaringType = irType,
                        ReturnTypeCpp = ResolveTypeForDecl(returnTypeName),
                        IsStatic = methodDef.IsStatic,
                        IsVirtual = methodDef.IsVirtual,
                        IsAbstract = methodDef.IsAbstract,
                        IsConstructor = methodDef.IsConstructor,
                        IsStaticConstructor = methodDef.IsConstructor && methodDef.IsStatic,
                    };

                    // Propagate HasICallMapping for methods with icall mappings
                    if (ICallRegistry.Lookup(openType.FullName, methodDef.Name, methodDef.Parameters.Count) != null)
                        irMethod.HasICallMapping = true;

                    // Parameters
                    foreach (var paramDef in methodDef.Parameters)
                    {
                        var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                        irMethod.Parameters.Add(new IRParameter
                        {
                            Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                            CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                            CppTypeName = ResolveTypeForDecl(paramTypeName),
                            ILTypeName = paramTypeName,
                            Index = paramDef.Index,
                        });
                    }

                    // Local variables
                    if (methodDef.HasBody)
                    {
                        foreach (var localDef in methodDef.Body.Variables)
                        {
                            var localTypeName = ResolveGenericTypeName(localDef.VariableType, typeParamMap);
                            irMethod.Locals.Add(new IRLocal
                            {
                                Index = localDef.Index,
                                CppName = $"loc_{localDef.Index}",
                                CppTypeName = ResolveTypeForDecl(localTypeName),
                            });
                        }
                    }

                    irType.Methods.Add(irMethod);

                    // Defer method body conversion to after DisambiguateOverloadedMethods (Pass 3.3).
                    // Converting bodies before disambiguation causes call sites to use
                    // pre-disambiguation names, leading to undeclared/mismatched function calls.
                    // Only convert reachable methods — same check as Pass 3.
                    // Unreachable methods keep BasicBlocks empty → not declared in header.
                    if (methodDef.HasBody && !methodDef.IsAbstract
                        && _reachability.IsReachable(methodDef))
                    {
                        // Skip methods with CLR-internal dependencies — generate stub instead
                        if (HasClrInternalDependencies(methodDef))
                        {
                            GenerateStubBody(irMethod);
                        }
                        else
                        {
                            // Clone typeParamMap since the outer loop reuses it
                            _deferredGenericBodies.Add((methodDef, irMethod,
                                new Dictionary<string, string>(typeParamMap)));
                        }
                    }
                }
            }
        }

        // Pass 1.5: Auto-create nested type specializations (Entry, Enumerator, etc.)
        // When Dictionary<String,Object> is created, also create Dictionary<String,Object>.Entry,
        // Dictionary<String,Object>.Enumerator, etc. with the same type arguments.
        // Iterate until fixpoint to handle nested-nested types (e.g., KeyCollection.Enumerator).
        int prevCount;
        do
        {
            prevCount = _genericInstantiations.Count;
            CreateNestedGenericSpecializations();
        } while (_genericInstantiations.Count > prevCount);

        // Second pass: resolve base types, interfaces, HasCctor for generic specializations.
        // Done after all specializations are in the cache so cross-references work
        // (e.g., SpecialWrapper<int> : Wrapper<int> needs Wrapper<int> already cached).
        foreach (var (key, info) in _genericInstantiations)
        {
            if (info.CecilOpenType == null) continue;
            if (!_typeCache.TryGetValue(key, out var irType)) continue;

            var openType = info.CecilOpenType;
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];

            // Base type
            if (openType.BaseType != null && !irType.IsValueType)
            {
                var baseName = ResolveGenericTypeName(openType.BaseType, typeParamMap);
                if (_typeCache.TryGetValue(baseName, out var baseType))
                    irType.BaseType = baseType;
            }

            // Interfaces (Cecil flattens the list)
            foreach (var iface in openType.Interfaces)
            {
                var ifaceName = ResolveGenericTypeName(iface.InterfaceType, typeParamMap);
                if (_typeCache.TryGetValue(ifaceName, out var ifaceType))
                    irType.Interfaces.Add(ifaceType);
            }

            // Static constructor flag — set if cctor body was converted OR is deferred.
            // Bodies may be deferred to after disambiguation (Pass 3.4), so check both
            // already-converted bodies AND the deferred list.
            var hasCctorMethod = openType.Methods.Any(m => m.IsConstructor && m.IsStatic);
            if (hasCctorMethod)
            {
                var cctorIrMethod = irType.Methods.FirstOrDefault(m => m.IsStaticConstructor);
                if (cctorIrMethod != null)
                {
                    irType.HasCctor = cctorIrMethod.BasicBlocks.Count > 0
                        || _deferredGenericBodies.Any(d => d.IrMethod == cctorIrMethod);
                }
            }

            // Recalculate instance size (BaseType may contribute inherited fields)
            CalculateInstanceSize(irType);
        }
    }

    /// <summary>
    /// Auto-create specializations for nested types of generic types.
    /// When Dictionary&lt;String,Object&gt; is specialized, also create:
    ///   - Dictionary&lt;String,Object&gt;.Entry (used by Dictionary method bodies)
    ///   - Dictionary&lt;String,Object&gt;.Enumerator (returned by GetEnumerator)
    ///   - List&lt;T&gt;.Enumerator, etc.
    /// </summary>
    private void CreateNestedGenericSpecializations()
    {
        // Collect nested types to add (can't modify _genericInstantiations while iterating)
        var nestedToAdd = new List<(string key, GenericInstantiationInfo info)>();

        foreach (var (key, info) in _genericInstantiations)
        {
            if (info.CecilOpenType == null) continue;
            if (!info.CecilOpenType.HasNestedTypes) continue;

            foreach (var nestedTypeDef in info.CecilOpenType.NestedTypes)
            {
                // Skip non-generic nested types or those with their own additional generic params
                // We only handle nested types that inherit the parent's generic params exactly
                if (nestedTypeDef.HasGenericParameters &&
                    nestedTypeDef.GenericParameters.Count != info.CecilOpenType.GenericParameters.Count)
                    continue;

                var nestedOpenName = nestedTypeDef.FullName; // e.g. "System.Collections.Generic.Dictionary`2/Entry"
                var nestedKey = $"{nestedOpenName}<{string.Join(",", info.TypeArguments)}>";

                // Skip if already created
                if (_genericInstantiations.ContainsKey(nestedKey)) continue;
                if (_typeCache.ContainsKey(nestedKey)) continue;

                var nestedMangledName = CppNameMapper.MangleGenericInstanceTypeName(nestedOpenName, info.TypeArguments);
                var nestedInfo = new GenericInstantiationInfo(
                    nestedOpenName, info.TypeArguments, nestedMangledName, nestedTypeDef);

                nestedToAdd.Add((nestedKey, nestedInfo));
            }
        }

        if (nestedToAdd.Count == 0) return;

        // Register nested types and create IRType objects for them
        foreach (var (nestedKey, info) in nestedToAdd)
        {
            if (_genericInstantiations.ContainsKey(nestedKey)) continue;
            _genericInstantiations[nestedKey] = info;

            var openType = info.CecilOpenType;
            if (openType == null) continue;

            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];

            var irType = new IRType
            {
                ILFullName = nestedKey,
                CppName = info.MangledName,
                Name = info.MangledName,
                Namespace = openType.Namespace,
                IsValueType = openType.IsValueType,
                IsInterface = openType.IsInterface,
                IsAbstract = openType.IsAbstract,
                IsSealed = openType.IsSealed,
                IsGenericInstance = true,
                IsDelegate = openType.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate",
                GenericArguments = info.TypeArguments,
                IsRuntimeProvided = false,
                SourceKind = _assemblySet.ClassifyAssembly(openType.Module.Assembly.Name.Name),
            };

            if (openType.IsValueType)
            {
                CppNameMapper.RegisterValueType(nestedKey);
                CppNameMapper.RegisterValueType(info.MangledName);
            }

            // Fields
            foreach (var fieldDef in openType.Fields)
            {
                var fieldTypeName = ResolveGenericTypeName(fieldDef.FieldType, typeParamMap);
                var irField = new IRField
                {
                    Name = fieldDef.Name,
                    CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                    FieldTypeName = fieldTypeName,
                    IsStatic = fieldDef.IsStatic,
                    IsPublic = fieldDef.IsPublic,
                    DeclaringType = irType,
                };
                if (fieldDef.IsStatic)
                    irType.StaticFields.Add(irField);
                else
                    irType.Fields.Add(irField);
            }

            if (irType.IsValueType && openType.HasLayoutInfo && openType.ClassSize > 0)
                irType.ExplicitSize = openType.ClassSize;

            CalculateInstanceSize(irType);

            _module.Types.Add(irType);
            _typeCache[nestedKey] = irType;

            // Create method stubs for nested type (declarations only, no body conversion)
            // The bodies are mostly inaccessible from user code anyway — we only need
            // the struct definition for sizeof/locals in parent type methods.
            foreach (var methodDef in openType.Methods)
            {
                var returnTypeName = ResolveGenericTypeName(methodDef.ReturnType, typeParamMap);
                var cppName = CppNameMapper.MangleMethodName(info.MangledName, methodDef.Name);

                var irMethod = new IRMethod
                {
                    Name = methodDef.Name,
                    CppName = cppName,
                    DeclaringType = irType,
                    ReturnTypeCpp = ResolveTypeForDecl(returnTypeName),
                    IsStatic = methodDef.IsStatic,
                    IsVirtual = methodDef.IsVirtual,
                    IsAbstract = methodDef.IsAbstract,
                    IsConstructor = methodDef.IsConstructor,
                    IsStaticConstructor = methodDef.IsConstructor && methodDef.IsStatic,
                };

                foreach (var paramDef in methodDef.Parameters)
                {
                    var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                    irMethod.Parameters.Add(new IRParameter
                    {
                        Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                        CppName = SanitizeParamName(paramDef.Name, paramDef.Index),
                        CppTypeName = ResolveTypeForDecl(paramTypeName),
                        ILTypeName = paramTypeName,
                        Index = paramDef.Index,
                    });
                }

                irType.Methods.Add(irMethod);

                // TODO: Nested type method bodies are stubbed for now.
                // The struct definitions are sufficient for resolving HasUnknownBodyReferences
                // in parent type methods (e.g., Dictionary uses Entry as local variable type).
                // Future: compile bodies for methods like Enumerator.MoveNext when needed.
                if (methodDef.HasBody && !methodDef.IsAbstract)
                {
                    GenerateStubBody(irMethod);
                }
            }
        }
    }

    /// <summary>
    /// Check if a TypeReference contains unresolved generic parameters (recursively).
    /// Handles GenericParameter, ArrayType (TResult[]), ByReferenceType, PointerType,
    /// and nested GenericInstanceType (Task&lt;TResult&gt;).
    /// </summary>
    private static bool ContainsGenericParameter(TypeReference typeRef)
    {
        if (typeRef is GenericParameter) return true;
        if (typeRef is ArrayType at) return ContainsGenericParameter(at.ElementType);
        if (typeRef is ByReferenceType brt) return ContainsGenericParameter(brt.ElementType);
        if (typeRef is PointerType pt) return ContainsGenericParameter(pt.ElementType);
        if (typeRef is GenericInstanceType git)
            return git.GenericArguments.Any(ContainsGenericParameter);
        return false;
    }

    /// <summary>
    /// Resolve a Cecil TypeReference to an IL type name, substituting generic parameters.
    /// </summary>
    private string ResolveGenericTypeName(TypeReference typeRef, Dictionary<string, string> typeParamMap)
    {
        if (typeRef is GenericParameter gp)
        {
            return typeParamMap.TryGetValue(gp.Name, out var resolved) ? resolved : gp.FullName;
        }

        if (typeRef is GenericInstanceType git)
        {
            var openName = git.ElementType.FullName;
            var args = git.GenericArguments.Select(a => ResolveGenericTypeName(a, typeParamMap)).ToList();
            var key = $"{openName}<{string.Join(",", args)}>";
            return key;
        }

        if (typeRef is ArrayType at)
        {
            return ResolveGenericTypeName(at.ElementType, typeParamMap) + "[]";
        }

        if (typeRef is ByReferenceType brt)
        {
            return ResolveGenericTypeName(brt.ElementType, typeParamMap) + "&";
        }

        if (typeRef is PointerType ptr)
        {
            return ResolveGenericTypeName(ptr.ElementType, typeParamMap) + "*";
        }

        // Handle PinnedType — locals with 'pinned' modifier (from fixed/stackalloc)
        // PinnedType is a GC annotation only; it doesn't change the C++ type representation.
        if (typeRef is PinnedType pnt)
        {
            return ResolveGenericTypeName(pnt.ElementType, typeParamMap);
        }

        // Handle RequiredModifierType (modreq) — common with Unsafe methods
        if (typeRef is RequiredModifierType rmt)
        {
            return ResolveGenericTypeName(rmt.ElementType, typeParamMap);
        }

        // Handle OptionalModifierType (modopt)
        if (typeRef is OptionalModifierType omt)
        {
            return ResolveGenericTypeName(omt.ElementType, typeParamMap);
        }

        // Fallback: Cecil sometimes represents method-level generic parameters as plain
        // TypeReference (not GenericParameter) in local variable signatures.
        // Check if the type's Name or FullName matches a key in the map.
        if (typeParamMap.TryGetValue(typeRef.Name, out var fallbackResolved))
            return fallbackResolved;
        if (typeParamMap.TryGetValue(typeRef.FullName, out var fallbackResolved2))
            return fallbackResolved2;

        return typeRef.FullName;
    }

    /// <summary>
    /// Resolve a type operand from IL instructions using the active type parameter map.
    /// Used during method body conversion for generic types.
    /// Returns the resolved IL type name (e.g., "System.Int32" instead of "T").
    /// </summary>
    private string ResolveTypeRefOperand(TypeReference typeRef)
    {
        if (_activeTypeParamMap != null)
            return ResolveGenericTypeName(typeRef, _activeTypeParamMap);
        return typeRef.FullName;
    }

    /// <summary>
    /// Check if a type reference (potentially a generic parameter) resolves to a value type.
    /// Used by Ldelem_Any/Stelem_Any to distinguish reference types (stored as pointers)
    /// from value types (stored inline) in arrays.
    /// </summary>
    private bool IsResolvedValueType(TypeReference typeRef)
    {
        // Resolve generic parameters through the active map
        if (typeRef is GenericParameter gp && _activeTypeParamMap != null)
        {
            if (_activeTypeParamMap.TryGetValue(gp.Name, out var resolvedILName))
            {
                // Check primitive value types (int, bool, float, etc.)
                // Note: IsPrimitive includes String/Object which are reference types.
                // Use IsValueType which correctly excludes them.
                if (CppNameMapper.IsValueType(resolvedILName))
                    return true;

                // Check module types (both user and BCL types in the IR)
                var irType = _module.Types.FirstOrDefault(t => t.ILFullName == resolvedILName);
                if (irType != null)
                    return irType.IsValueType;

                // Fallback: assume reference type for unknown types
                return false;
            }
            // Unresolved generic param — check constraints
            return gp.HasConstraints &&
                   gp.Constraints.Any(c => c.ConstraintType?.FullName == "System.ValueType");
        }

        // Non-generic: use Cecil metadata directly
        var resolved = typeRef.Resolve();
        return resolved?.IsValueType ?? typeRef.IsValueType;
    }

    /// <summary>
    /// Convert deferred generic specialization bodies. Must be called AFTER
    /// DisambiguateOverloadedMethods so that call sites resolve to the correct
    /// disambiguated function names.
    /// </summary>
    private void ConvertDeferredGenericBodies()
    {
        foreach (var (cecilMethod, irMethod, typeParamMap) in _deferredGenericBodies)
        {
            // Skip record compiler-generated methods — Pass 7 synthesizes replacements
            if (irMethod.DeclaringType?.IsRecord == true && IsRecordSynthesizedMethod(irMethod.Name))
                continue;

            var methodInfo = new IL.MethodInfo(cecilMethod);
            ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
        }
        _deferredGenericBodies.Clear();
    }

    /// <summary>
    /// Convert a method body from an open generic type with generic parameter substitution.
    /// </summary>
    private void ConvertMethodBodyWithGenerics(IL.MethodInfo methodDef, IRMethod irMethod,
        Dictionary<string, string> typeParamMap)
    {
        _activeTypeParamMap = typeParamMap;
        try
        {
            ConvertMethodBody(methodDef, irMethod);
        }
        finally
        {
            _activeTypeParamMap = null;
        }
    }

    /// <summary>
    /// Resolve a Cecil TypeReference to a cache key, handling GenericInstanceType.
    /// </summary>
    private string ResolveCacheKey(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType git)
        {
            var openTypeName = git.ElementType.FullName;
            var typeArgs = git.GenericArguments.Select(a =>
            {
                if (a is GenericParameter gp && _activeTypeParamMap != null)
                    return _activeTypeParamMap.TryGetValue(gp.Name, out var r) ? r : a.FullName;
                return a.FullName;
            }).ToList();
            return $"{openTypeName}<{string.Join(",", typeArgs)}>";
        }

        if (typeRef is GenericParameter gp2 && _activeTypeParamMap != null)
            return _activeTypeParamMap.TryGetValue(gp2.Name, out var resolved) ? resolved : typeRef.FullName;

        // Handle open generic type definitions when inside a generic context
        // (e.g., GenericCache`1 inside its own method body with T → System.Int32)
        if (_activeTypeParamMap != null)
        {
            if (typeRef.HasGenericParameters)
            {
                var openTypeName = typeRef.FullName;
                var typeArgs = typeRef.GenericParameters.Select(gp =>
                    _activeTypeParamMap.TryGetValue(gp.Name, out var r) ? r : gp.FullName
                ).ToList();
                return $"{openTypeName}<{string.Join(",", typeArgs)}>";
            }
            // Fallback: detect open generic by backtick in name (Cecil TypeReference may not have HasGenericParameters)
            var fullName = typeRef.FullName;
            var btIdx = fullName.IndexOf('`');
            if (btIdx > 0 && !fullName.Contains('<'))
            {
                // Try to resolve by looking up the actual type definition
                try
                {
                    var resolved = typeRef.Resolve();
                    if (resolved != null && resolved.HasGenericParameters)
                    {
                        var typeArgs = resolved.GenericParameters.Select(gp =>
                            _activeTypeParamMap.TryGetValue(gp.Name, out var r) ? r : gp.FullName
                        ).ToList();
                        return $"{fullName}<{string.Join(",", typeArgs)}>";
                    }
                }
                catch { /* Resolve may fail for external types */ }
            }
        }

        return typeRef.FullName;
    }

    /// <summary>
    /// Get the C++ mangled type name for a Cecil TypeReference.
    /// </summary>
    private string GetMangledTypeNameForRef(TypeReference typeRef)
    {
        var key = ResolveCacheKey(typeRef);
        if (_typeCache.TryGetValue(key, out var irType))
            return irType.CppName;
        // For generic instance types, use MangleGenericInstanceTypeName to avoid
        // trailing underscore from '>' → '_' that MangleTypeName produces
        var backtickIdx = key.IndexOf('`');
        if (backtickIdx > 0 && key.Contains('<'))
        {
            var angleBracket = key.IndexOf('<');
            var openTypeName = key[..angleBracket];
            var argsStr = key[(angleBracket + 1)..^1];
            var args = argsStr.Split(',').Select(a => a.Trim()).ToList();
            return CppNameMapper.MangleGenericInstanceTypeName(openTypeName, args);
        }
        return CppNameMapper.MangleTypeName(key);
    }

    /// <summary>
    /// Sanitize a parameter name for C++ — C# compiler-generated names like &lt;&gt;1__state
    /// contain angle brackets which are invalid in C++ identifiers.
    /// </summary>
    private static string SanitizeParamName(string name, int index)
    {
        if (name.Length == 0) return $"p{index}";
        if (name.Contains('<') || name.Contains('>'))
            return $"p_{name.Replace("<", "").Replace(">", "")}";
        return name;
    }

    /// <summary>
    /// Resolve a type name string (possibly containing generic syntax) to C++ declaration type.
    /// </summary>
    private string ResolveTypeForDecl(string ilTypeName)
    {
        // Handle array types — all .NET arrays are reference types (heap-allocated).
        // Must be handled BEFORE generic parsing, because array-of-generic like
        // `Dictionary`2/Entry<String,Object>[]` would corrupt the generic arg parsing
        // (line ^1 removes `]` instead of `>`, producing garbled type names).
        if (ilTypeName.EndsWith("[]"))
            return "cil2cpp::Array*";
        // Multi-dimensional arrays
        if (ilTypeName.EndsWith("]") && ilTypeName.Contains("[") &&
            (ilTypeName.Contains(",") || ilTypeName.Contains("...")))
        {
            // Check it's actually an array notation, not a generic arg
            var lastBracket = ilTypeName.LastIndexOf('[');
            var section = ilTypeName[lastBracket..];
            if (section.Contains(',') || section.Contains("..."))
                return "cil2cpp::MdArray*";
        }

        // Handle ByReference types (ref/out) — strip & suffix and recurse.
        // Critical for ref T where T is a reference type: ref Encoding → Encoding** (not Encoding*)
        if (ilTypeName.EndsWith("&"))
            return ResolveTypeForDecl(ilTypeName[..^1]) + "*";

        // Handle pointer types — strip * suffix and recurse
        if (ilTypeName.EndsWith("*"))
            return ResolveTypeForDecl(ilTypeName[..^1]) + "*";

        // Primitive types always map to C++ primitives (int32_t, bool, void, etc.)
        // regardless of whether BCL struct definitions exist in the type cache
        if (CppNameMapper.IsPrimitive(ilTypeName))
            return CppNameMapper.GetCppTypeForDecl(ilTypeName);

        if (_typeCache.TryGetValue(ilTypeName, out var cached))
        {
            if (cached.IsValueType)
                return cached.CppName;
            return cached.CppName + "*";
        }

        var backtickIdx = ilTypeName.IndexOf('`');
        if (backtickIdx > 0 && ilTypeName.Contains('<'))
        {
            var angleBracket = ilTypeName.IndexOf('<');
            var openTypeName = ilTypeName[..angleBracket];
            var argsStr = ilTypeName[(angleBracket + 1)..^1];
            var args = argsStr.Split(',').Select(a => a.Trim()).ToList();
            var key = $"{openTypeName}<{string.Join(",", args)}>";

            if (_typeCache.TryGetValue(key, out var genericCached))
            {
                if (genericCached.IsValueType)
                    return genericCached.CppName;
                return genericCached.CppName + "*";
            }

            // Check if the open generic type definition is a value type
            // (e.g. Span<T> is a value type → Span<Byte> is also a value type)
            var cppGenName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, args);
            if (_typeCache.TryGetValue(openTypeName, out var openCached))
            {
                if (openCached.IsValueType)
                    return cppGenName;
                return cppGenName + "*";
            }
            // Open generic not in _typeCache (skipped in Pass 1) — check _userValueTypes
            if (CppNameMapper.IsValueType(openTypeName))
                return cppGenName;
        }

        return CppNameMapper.GetCppTypeForDecl(ilTypeName);
    }

    /// <summary>
    /// Validate generic constraints for a type or method specialization.
    /// Emits warnings to stderr for violated constraints but does not block compilation.
    /// </summary>
    private void ValidateGenericConstraints(
        IList<GenericParameter> genericParams,
        IList<string> typeArguments,
        string context)
    {
        for (int i = 0; i < genericParams.Count && i < typeArguments.Count; i++)
        {
            var gp = genericParams[i];
            var argName = typeArguments[i];

            // Resolve the actual type to check constraints
            var argType = ResolveTypeForConstraintCheck(argName);
            if (argType == null) continue;

            // Check 'struct' constraint (HasNotNullableValueTypeConstraint)
            if (gp.HasNotNullableValueTypeConstraint && !argType.IsValueType)
            {
                Console.Error.WriteLine(
                    $"WARNING: Generic constraint violation in {context}: " +
                    $"type argument '{argName}' must be a non-nullable value type (struct constraint on '{gp.Name}')");
            }

            // Check 'class' constraint (HasReferenceTypeConstraint)
            if (gp.HasReferenceTypeConstraint && argType.IsValueType)
            {
                Console.Error.WriteLine(
                    $"WARNING: Generic constraint violation in {context}: " +
                    $"type argument '{argName}' must be a reference type (class constraint on '{gp.Name}')");
            }

            // Check 'new()' constraint (HasDefaultConstructorConstraint)
            if (gp.HasDefaultConstructorConstraint && !argType.IsValueType)
            {
                var hasDefaultCtor = argType.Methods.Any(m =>
                    m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
                if (!hasDefaultCtor)
                {
                    Console.Error.WriteLine(
                        $"WARNING: Generic constraint violation in {context}: " +
                        $"type argument '{argName}' must have a parameterless constructor (new() constraint on '{gp.Name}')");
                }
            }

            // Check interface/base type constraints
            foreach (var constraint in gp.Constraints)
            {
                var constraintTypeName = constraint.ConstraintType.FullName;
                // Skip System.ValueType (handled by struct constraint above)
                if (constraintTypeName is "System.ValueType" or "System.Object") continue;

                if (!TypeSatisfiesConstraint(argType, constraintTypeName))
                {
                    Console.Error.WriteLine(
                        $"WARNING: Generic constraint violation in {context}: " +
                        $"type argument '{argName}' must implement or inherit from '{constraintTypeName}' (constraint on '{gp.Name}')");
                }
            }
        }
    }

    /// <summary>
    /// Resolve a type argument IL name to its Cecil TypeDefinition for constraint checking.
    /// </summary>
    private TypeDefinition? ResolveTypeForConstraintCheck(string ilTypeName)
    {
        // Try to find in our type cache → the original Cecil type
        if (_typeCache.TryGetValue(ilTypeName, out var irType))
        {
            // Look up the Cecil type from the _allTypes list
            var cecilDef = _allTypes?.FirstOrDefault(t => t.FullName == ilTypeName)?.GetCecilType();
            if (cecilDef != null) return cecilDef;
        }

        // Try to resolve from loaded assemblies
        try
        {
            foreach (var (_, asm) in _assemblySet.LoadedAssemblies)
            {
                var td = asm.MainModule.Types.FirstOrDefault(t => t.FullName == ilTypeName);
                if (td != null) return td;
            }
        }
        catch { /* ignore resolution failures */ }

        return null;
    }

    /// <summary>
    /// Check if a type satisfies an interface or base type constraint.
    /// </summary>
    private static bool TypeSatisfiesConstraint(TypeDefinition argType, string constraintTypeName)
    {
        // Check direct interface implementation
        if (argType.Interfaces.Any(i => i.InterfaceType.FullName == constraintTypeName))
            return true;

        // Walk base type chain
        var baseType = argType.BaseType;
        while (baseType != null)
        {
            if (baseType.FullName == constraintTypeName) return true;
            // Check interfaces of base type
            try
            {
                var baseDef = baseType.Resolve();
                if (baseDef == null) break;
                if (baseDef.Interfaces.Any(i => i.InterfaceType.FullName == constraintTypeName))
                    return true;
                baseType = baseDef.BaseType;
            }
            catch { break; }
        }

        return false;
    }
}
