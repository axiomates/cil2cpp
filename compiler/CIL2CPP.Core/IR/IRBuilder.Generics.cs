using Mono.Cecil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    // Active generic type parameter map (set during ConvertMethodBodyWithGenerics)
    private Dictionary<string, string>? _activeTypeParamMap;

    /// <summary>
    /// Construct a unique key for a generic method instantiation.
    /// Shared between scanning (CollectGenericMethod) and call emission (EmitMethodCall).
    /// </summary>
    private static string MakeGenericMethodKey(string declaringType, string methodName, List<string> typeArgs)
        => $"{declaringType}::{methodName}<{string.Join(",", typeArgs)}>";

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
    private static readonly HashSet<string> FilteredGenericNamespaces =
    [
        "System.Runtime.Intrinsics",
        "System.Runtime.InteropServices",
        "System.Reflection",
        "System.Diagnostics",
        "System.Diagnostics.Tracing",
        "System.Globalization",
        "System.Resources",
        "System.Security",
        "System.IO",
        "System.Net",
        "Internal",
    ];

    private void CollectGenericType(TypeReference typeRef)
    {
        if (typeRef is not GenericInstanceType git) return;

        // Skip if any type argument contains an unresolved generic parameter
        // (e.g., TResult, TResult[], Task<TResult> — all contain GenericParameter)
        if (git.GenericArguments.Any(ContainsGenericParameter))
            return;

        // Skip generic types from BCL internal namespaces
        var elemNs = git.ElementType.Namespace;
        if (!string.IsNullOrEmpty(elemNs) &&
            FilteredGenericNamespaces.Any(f => elemNs.StartsWith(f)))
            return;

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
        var key = MakeGenericMethodKey(declaringType, methodName, typeArgs);
        if (_genericMethodInstantiations.ContainsKey(key)) return;

        var cecilMethod = elementMethod.Resolve();
        if (cecilMethod == null) return;

        var mangledName = MangleGenericMethodName(declaringType, methodName, typeArgs);

        _genericMethodInstantiations[key] = new GenericMethodInstantiationInfo(
            declaringType, methodName, typeArgs, mangledName, cecilMethod);
    }

    /// <summary>
    /// Create specialized IRMethods for each generic method instantiation found in Pass 0.
    /// </summary>
    private void CreateGenericMethodSpecializations()
    {
        foreach (var (key, info) in _genericMethodInstantiations)
        {
            var cecilMethod = info.CecilMethod;

            // Find the declaring IRType
            if (!_typeCache.TryGetValue(info.DeclaringTypeName, out var declaringIrType))
                continue;

            // Build method-level type parameter map
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < cecilMethod.GenericParameters.Count && i < info.TypeArguments.Count; i++)
            {
                typeParamMap[cecilMethod.GenericParameters[i].Name] = info.TypeArguments[i];
            }

            var returnTypeName = ResolveGenericTypeName(cecilMethod.ReturnType, typeParamMap);

            var irMethod = new IRMethod
            {
                Name = cecilMethod.Name,
                CppName = info.MangledName,
                DeclaringType = declaringIrType,
                ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(returnTypeName),
                IsStatic = cecilMethod.IsStatic,
                IsVirtual = cecilMethod.IsVirtual,
                IsAbstract = cecilMethod.IsAbstract,
                IsConstructor = cecilMethod.IsConstructor,
                IsStaticConstructor = cecilMethod.IsConstructor && cecilMethod.IsStatic,
                IsGenericInstance = true,
            };

            // Parameters
            foreach (var paramDef in cecilMethod.Parameters)
            {
                var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                irMethod.Parameters.Add(new IRParameter
                {
                    Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                    CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                    CppTypeName = CppNameMapper.GetCppTypeForDecl(paramTypeName),
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
                    irMethod.Locals.Add(new IRLocal
                    {
                        Index = localDef.Index,
                        CppName = $"loc_{localDef.Index}",
                        CppTypeName = CppNameMapper.GetCppTypeForDecl(localTypeName),
                    });
                }
            }

            declaringIrType.Methods.Add(irMethod);

            // Convert method body with generic substitution context
            // (not added to methodBodies — we convert immediately with the type param map)
            if (cecilMethod.HasBody && !cecilMethod.IsAbstract)
            {
                var methodInfo = new IL.MethodInfo(cecilMethod);
                ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
            }
        }
    }

    /// <summary>
    /// Create specialized IRTypes for each generic instantiation found in Pass 0.
    /// </summary>
    private void CreateGenericSpecializations()
    {
        foreach (var (key, info) in _genericInstantiations)
        {
            // Skip types already created (e.g., by BCL proxy system)
            if (_typeCache.ContainsKey(key)) continue;

            var isAsyncBcl = IsAsyncBclGenericType(info.OpenTypeName);
            var isSpanBcl = IsSpanBclGenericType(info.OpenTypeName);
            var isCollectionBcl = IsCollectionBclGenericType(info.OpenTypeName);
            var isSyntheticBcl = isAsyncBcl || isSpanBcl || isCollectionBcl;

            // Skip types we can't resolve — except synthetic BCL types
            if (info.CecilOpenType == null && !isSyntheticBcl) continue;

            var openType = info.CecilOpenType; // may be null for async BCL types

            // Build type parameter map
            var typeParamMap = new Dictionary<string, string>();
            if (openType != null)
            {
                for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                    typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];
            }
            else if (isSyntheticBcl && info.TypeArguments.Count > 0)
            {
                if (isCollectionBcl)
                {
                    // List<T> has one param "T", Dictionary<K,V> has "TKey"/"TValue"
                    if (info.OpenTypeName.StartsWith("System.Collections.Generic.List`"))
                        typeParamMap["T"] = info.TypeArguments[0];
                    else if (info.OpenTypeName.StartsWith("System.Collections.Generic.Dictionary`")
                             && info.TypeArguments.Count >= 2)
                    {
                        typeParamMap["TKey"] = info.TypeArguments[0];
                        typeParamMap["TValue"] = info.TypeArguments[1];
                    }
                }
                else
                {
                    // Async/Span BCL types have a single generic parameter
                    typeParamMap[isAsyncBcl ? "TResult" : "T"] = info.TypeArguments[0];
                }
            }

            // Determine type flags
            bool isValueType;
            string namespaceName;
            if (openType != null)
            {
                isValueType = openType.IsValueType;
                namespaceName = openType.Namespace;
            }
            else if (isSpanBcl)
            {
                // Span<T> and ReadOnlySpan<T> are value types
                isValueType = true;
                namespaceName = "System";
            }
            else if (isCollectionBcl)
            {
                // List<T> and Dictionary<K,V> are reference types (classes)
                isValueType = false;
                namespaceName = "System.Collections.Generic";
            }
            else
            {
                // Async BCL: TaskAwaiter<T> and AsyncTaskMethodBuilder<T> are value types
                isValueType = !info.OpenTypeName.StartsWith("System.Threading.Tasks.Task`");
                namespaceName = info.OpenTypeName.Contains("CompilerServices")
                    ? "System.Runtime.CompilerServices"
                    : "System.Threading.Tasks";
            }

            var irType = new IRType
            {
                ILFullName = key,
                CppName = info.MangledName,
                Name = info.MangledName,
                Namespace = namespaceName,
                IsValueType = isValueType,
                IsInterface = openType?.IsInterface ?? false,
                IsAbstract = openType?.IsAbstract ?? false,
                IsSealed = openType?.IsSealed ?? true,
                IsGenericInstance = true,
                GenericArguments = info.TypeArguments,
                IsRuntimeProvided = isSyntheticBcl && !isCollectionBcl,
                SourceKind = isSyntheticBcl ? AssemblyKind.BCL
                    : (openType != null && _assemblySet != null
                        ? _assemblySet.ClassifyAssembly(openType.Module.Assembly.Name.Name)
                        : AssemblyKind.User),
            };

            // Propagate generic parameter variances from open type definition
            if (openType != null && openType.HasGenericParameters)
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
            if (isValueType)
            {
                CppNameMapper.RegisterValueType(key);
                CppNameMapper.RegisterValueType(info.MangledName);
            }

            // Fields: synthetic for BCL generic types, Cecil-based for everything else
            if (isAsyncBcl)
            {
                irType.Fields.AddRange(CreateAsyncSyntheticFields(info.OpenTypeName, irType, typeParamMap));
            }
            else if (isSpanBcl)
            {
                irType.Fields.AddRange(CreateSpanSyntheticFields(info.OpenTypeName, irType, typeParamMap));
            }
            else if (isCollectionBcl)
            {
                if (info.OpenTypeName.StartsWith("System.Collections.Generic.List`"))
                    irType.Fields.AddRange(CreateListSyntheticFields(irType));
                else if (info.OpenTypeName.StartsWith("System.Collections.Generic.Dictionary`"))
                    irType.Fields.AddRange(CreateDictionarySyntheticFields(irType));
            }
            else
            {
                foreach (var fieldDef in openType!.Fields)
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
            }

            // Calculate instance size
            CalculateInstanceSize(irType);

            // Register type early so self-referencing static field accesses work
            _module.Types.Add(irType);
            _typeCache[key] = irType;

            // Methods: skip entirely for async/collection BCL types (all calls are intercepted)
            if (openType != null && !isAsyncBcl && !isCollectionBcl)
            {
                foreach (var methodDef in openType.Methods)
                {
                    var returnTypeName = ResolveGenericTypeName(methodDef.ReturnType, typeParamMap);
                    var cppName = CppNameMapper.MangleMethodName(info.MangledName, methodDef.Name);
                    // op_Explicit/op_Implicit: disambiguate by return type (C++ can't overload by return type)
                    if (methodDef.Name is "op_Explicit" or "op_Implicit")
                        cppName = $"{cppName}_{CppNameMapper.MangleTypeName(returnTypeName)}";

                    var irMethod = new IRMethod
                    {
                        Name = methodDef.Name,
                        CppName = cppName,
                        DeclaringType = irType,
                        ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(returnTypeName),
                        IsStatic = methodDef.IsStatic,
                        IsVirtual = methodDef.IsVirtual,
                        IsAbstract = methodDef.IsAbstract,
                        IsConstructor = methodDef.IsConstructor,
                        IsStaticConstructor = methodDef.IsConstructor && methodDef.IsStatic,
                    };

                    // Parameters
                    foreach (var paramDef in methodDef.Parameters)
                    {
                        var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                        irMethod.Parameters.Add(new IRParameter
                        {
                            Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                            CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                            CppTypeName = CppNameMapper.GetCppTypeForDecl(paramTypeName),
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
                                CppTypeName = CppNameMapper.GetCppTypeForDecl(localTypeName),
                            });
                        }
                    }

                    irType.Methods.Add(irMethod);

                    // Convert method body with generic substitution context
                    // Skip BCL generic types — their method calls are intercepted instead
                    var isBclGeneric = info.OpenTypeName.StartsWith("System.Nullable`")
                        || info.OpenTypeName.StartsWith("System.ValueTuple`")
                        || IsEqualityComparerBclGenericType(info.OpenTypeName);
                    if (!isBclGeneric && methodDef.HasBody && !methodDef.IsAbstract)
                    {
                        var methodInfo = new IL.MethodInfo(methodDef);
                        ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
                    }
                }
            }
        }

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

            // Static constructor flag — only set if the cctor body was actually converted
            // (BCL types like EqualityComparer<T> have cctors but we skip their bodies)
            var hasCctorMethod = openType.Methods.Any(m => m.IsConstructor && m.IsStatic);
            if (hasCctorMethod)
            {
                var cctorIrMethod = irType.Methods.FirstOrDefault(m => m.IsStaticConstructor);
                irType.HasCctor = cctorIrMethod != null && cctorIrMethod.BasicBlocks.Count > 0;
            }

            // Recalculate instance size (BaseType may contribute inherited fields)
            CalculateInstanceSize(irType);
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
        return CppNameMapper.MangleTypeName(key);
    }

    /// <summary>
    /// Resolve a type name string (possibly containing generic syntax) to C++ declaration type.
    /// </summary>
    private string ResolveTypeForDecl(string ilTypeName)
    {
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
        }

        return CppNameMapper.GetCppTypeForDecl(ilTypeName);
    }
}
