using Mono.Cecil;
using Mono.Cecil.Cil;
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
    /// Tracks which methods on which generic specializations are actually called.
    /// Key format: "{OpenTypeName}&lt;{arg1},{arg2}&gt;::{MethodName}/{ParamCount}"
    /// Used to skip compilation of non-virtual methods that exist on a specialization but are never called.
    /// </summary>
    private readonly HashSet<string> _calledSpecializedMethods = new();

    /// <summary>
    /// Methods that were skipped during CreateGenericSpecializations because their specKey
    /// wasn't in _calledSpecializedMethods at that time. If body compilation later adds
    /// the key, these methods can be recovered and compiled.
    /// </summary>
    private readonly List<(string SpecKey, MethodDefinition CecilMethod, IRMethod IrMethod,
        Dictionary<string, string> TypeParamMap)> _skippedSpecializedMethods = new();

    /// <summary>
    /// Check if any constructed type inherits from or implements the given generic type.
    /// If so, virtual methods on this type may be needed for vtable dispatch.
    /// Uses the IL full name of the specialization (e.g., "System.Collections.Generic.IList`1&lt;System.String&gt;").
    /// </summary>
    private bool HasConstructedSubtype(string openTypeName, List<string> typeArgs)
    {
        var fullName = $"{openTypeName}<{string.Join(",", typeArgs)}>";
        // Check if this exact specialization is a base/interface of any constructed type
        foreach (var (_, irType) in _typeCache)
        {
            if (!_module.ConstructedTypes.Contains(irType.ILFullName))
                continue;
            // Walk base chain
            var baseType = irType.BaseType;
            while (baseType != null)
            {
                if (baseType.ILFullName == fullName) return true;
                baseType = baseType.BaseType;
            }
            // Check interfaces
            foreach (var iface in irType.Interfaces)
                if (iface.ILFullName == fullName) return true;
        }
        return false;
    }

    /// <summary>
    /// Build a specialized method key from Cecil GenericInstanceType + MethodReference.
    /// Used in Pass 0 (ScanGenericInstantiations) to record which specialized methods are called.
    /// </summary>
    private static string GetSpecializedMethodKey(GenericInstanceType git, MethodReference methodRef)
    {
        var openName = git.ElementType.FullName;
        var args = string.Join(",", git.GenericArguments.Select(a => a.FullName));
        return $"{openName}<{args}>::{methodRef.Name}/{methodRef.Parameters.Count}";
    }

    /// <summary>
    /// Build a specialized method key from IRBuilder internal state.
    /// Used in CreateGenericSpecializations to check if a method should be compiled.
    /// </summary>
    private static string GetSpecializedMethodKey(string openTypeName, List<string> typeArgs, MethodDefinition methodDef)
    {
        var args = string.Join(",", typeArgs);
        return $"{openTypeName}<{args}>::{methodDef.Name}/{methodDef.Parameters.Count}";
    }

    /// <summary>
    /// Build a specialized method key with generic type param resolution.
    /// Used in EnsureBodyReferencedTypesExist where type args may contain generic parameters
    /// that need to be resolved through the typeParamMap.
    /// Returns null if any type argument cannot be fully resolved.
    /// </summary>
    private string? GetResolvedSpecializedMethodKey(
        GenericInstanceType git, MethodReference methodRef, Dictionary<string, string> typeParamMap)
    {
        var openName = git.ElementType.FullName;
        var resolvedArgs = new List<string>();
        foreach (var arg in git.GenericArguments)
        {
            var resolved = ResolveGenericTypeName(arg, typeParamMap);
            // If still contains unresolved generic params, skip — conservative
            if (resolved.Contains('!') || resolved.StartsWith("T") && resolved.Length <= 2)
                return null;
            resolvedArgs.Add(resolved);
        }
        var args = string.Join(",", resolvedArgs);
        return $"{openName}<{args}>::{methodRef.Name}/{methodRef.Parameters.Count}";
    }

    /// <summary>
    /// Build a specialized method key from an open generic type method call.
    /// Used when scanning open generic bodies where the declaring type has unresolved
    /// generic parameters (e.g., Dictionary`2 calling FindValue on itself).
    /// </summary>
    private string? TryBuildSpecKeyFromOpenType(
        MethodReference methodRef, TypeReference declType, Dictionary<string, string> typeParamMap)
    {
        if (!declType.HasGenericParameters) return null;

        var openName = declType.FullName;
        var resolvedArgs = new List<string>();
        foreach (var gp in declType.GenericParameters)
        {
            if (typeParamMap.TryGetValue(gp.Name, out var resolved))
                resolvedArgs.Add(resolved);
            else
                return null; // can't fully resolve
        }
        var args = string.Join(",", resolvedArgs);
        return $"{openName}<{args}>::{methodRef.Name}/{methodRef.Parameters.Count}";
    }

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
        // Use MangleTypeNameClean for type args to handle nested generics correctly
        // (MangleTypeName converts '>' to '_', producing double underscores at arg boundaries)
        var argParts = string.Join("_", typeArgs.Select(CppNameMapper.MangleTypeNameClean));
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
            // Scan interface references on the type itself — only collect generic interfaces
            // that are actually dispatched on (callvirt, castclass, constrained, etc.).
            // Non-dispatched generic interfaces (e.g., INumber<Int32> from .NET 7+ generic math)
            // are skipped to avoid materializing hundreds of unused interface specializations.
            var cecilTypeDef = typeDef.GetCecilType();
            if (cecilTypeDef.HasInterfaces)
            {
                foreach (var iface in cecilTypeDef.Interfaces)
                {
                    if (iface.InterfaceType is GenericInstanceType git)
                    {
                        var openName = git.ElementType.FullName;
                        if (!_dispatchedInterfaces.Contains(openName))
                            continue;
                    }
                    CollectGenericType(iface.InterfaceType);
                }
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
                            // Track which methods on which specializations are called
                            if (methodRef.DeclaringType is GenericInstanceType calledGit
                                && !calledGit.GenericArguments.Any(ContainsGenericParameter))
                            {
                                _calledSpecializedMethods.Add(
                                    GetSpecializedMethodKey(calledGit, methodRef));
                            }
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
    /// Detects recursive generic instantiations where the open type appears
    /// nested within its own type arguments (e.g., DE&lt;DE&lt;DE&lt;BDD&gt;&gt;&gt;).
    /// This causes infinite growth during fixpoint monomorphization.
    /// Allows 1 level of self-reference (e.g., Dict&lt;string, Dict&lt;int, bool&gt;&gt;)
    /// but blocks 2+ (the growth pattern from recursive BCL types).
    /// </summary>
    private static bool IsRecursiveGenericInstantiation(GenericInstanceType git)
    {
        var openTypeName = git.ElementType.FullName;
        int selfRefs = 0;
        foreach (var arg in git.GenericArguments)
            selfRefs += CountOpenTypeOccurrences(arg, openTypeName);
        return selfRefs >= 2;
    }

    private static int CountOpenTypeOccurrences(TypeReference typeRef, string openTypeName)
    {
        if (typeRef is GenericInstanceType innerGit)
        {
            int count = innerGit.ElementType.FullName == openTypeName ? 1 : 0;
            foreach (var arg in innerGit.GenericArguments)
                count += CountOpenTypeOccurrences(arg, openTypeName);
            return count;
        }
        if (typeRef is ArrayType arr)
            return CountOpenTypeOccurrences(arr.ElementType, openTypeName);
        if (typeRef is ByReferenceType byRef)
            return CountOpenTypeOccurrences(byRef.ElementType, openTypeName);
        return 0;
    }

    /// <summary>
    /// SIMD container types that only accept numeric primitive type arguments.
    /// Specializing these with reference types (e.g., Vector64&lt;RuntimeMethodInfo&gt;)
    /// is semantically invalid — they represent hardware SIMD registers.
    /// </summary>
    private static bool IsSimdContainerType(string openTypeName) =>
        openTypeName.StartsWith("System.Runtime.Intrinsics.Vector") ||
        openTypeName == "System.Numerics.Vector`1" ||
        openTypeName.StartsWith("System.Numerics.Vector`");

    /// <summary>
    /// Valid type arguments for SIMD container types.
    /// Matches .NET runtime constraint: only blittable primitive numeric types.
    /// </summary>
    private static readonly HashSet<string> ValidSimdElementTypes = new()
    {
        "System.Byte", "System.SByte",
        "System.Int16", "System.UInt16",
        "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64",
        "System.Single", "System.Double",
        "System.IntPtr", "System.UIntPtr",
    };

    /// <summary>
    /// Checks if a generic instantiation is a SIMD type with invalid (non-primitive) type arguments.
    /// Returns true if the specialization should be skipped.
    /// </summary>
    private static bool IsInvalidSimdSpecialization(string openTypeName, IEnumerable<string> typeArgNames)
    {
        if (!IsSimdContainerType(openTypeName)) return false;
        return typeArgNames.Any(arg => !ValidSimdElementTypes.Contains(arg));
    }

    /// <summary>
    /// Checks if a generic method's declaring type is a SIMD type and its method-level type arguments
    /// are non-primitive. E.g., Vector64.GetElementUnsafe&lt;RuntimeMethodInfo&gt; is invalid.
    /// </summary>
    private static bool IsInvalidSimdMethodSpecialization(string declaringType, IEnumerable<string> typeArgNames)
    {
        // Methods on SIMD types or in SIMD helper classes (Vector64, Vector128, Vector256, ThrowHelper)
        if (declaringType.StartsWith("System.Runtime.Intrinsics.") ||
            declaringType.StartsWith("System.Numerics.Vector"))
        {
            return typeArgNames.Any(arg => !ValidSimdElementTypes.Contains(arg));
        }
        // ThrowHelper.ThrowForUnsupportedIntrinsicsVector* methods
        if (declaringType == "System.ThrowHelper")
        {
            return typeArgNames.Any(arg => !ValidSimdElementTypes.Contains(arg));
        }
        return false;
    }

    private void CollectGenericType(TypeReference typeRef)
    {
        if (typeRef is not GenericInstanceType git) return;

        // Skip if any type argument contains an unresolved generic parameter
        // (e.g., TResult, TResult[], Task<TResult> — all contain GenericParameter)
        if (git.GenericArguments.Any(ContainsGenericParameter))
            return;

        // Safety: prevent infinite recursive generic nesting (e.g., DE<DE<DE<BDD>>>)
        if (IsRecursiveGenericInstantiation(git))
            return;

        // Skip generic specializations where any type argument is a CLR-internal type.
        // Prevents creating List<RuntimePropertyInfo>, Dictionary<String, RuntimeType>, etc.
        foreach (var arg in git.GenericArguments)
        {
            var argFullName = arg.FullName;

            if (ClrInternalTypeNames.Contains(argFullName))
                return;

            // Also check nested types: "Outer/Inner" should match if "Outer" is CLR-internal
            if (argFullName.Contains('/'))
            {
                var outerTypeName = argFullName[..argFullName.IndexOf('/')];
                if (ClrInternalTypeNames.Contains(outerTypeName))
                    return;
            }
        }

        var openTypeName = git.ElementType.FullName;

        // All SIMD code is dead: IsHardwareAccelerated=false, IsSupported=false.
        // Skip ALL SIMD type instantiations, not just invalid ones.
        if (IsSimdContainerType(openTypeName))
            return;
        var typeArgs = git.GenericArguments.Select(a => a.FullName).ToList();
        var key = $"{openTypeName}<{string.Join(",", typeArgs)}>";

        if (_genericInstantiations.ContainsKey(key)) return;

        var mangledName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, typeArgs);
        var cecilOpenType = git.ElementType.Resolve();

        _genericInstantiations[key] = new GenericInstantiationInfo(
            openTypeName, typeArgs, mangledName, cecilOpenType);

        // AOT companion types: EqualityComparer<T> needs ObjectEqualityComparer<T>,
        // Comparer<T> needs ObjectComparer<T>. The BCL creates these via MakeGenericType
        // at runtime (AOT-incompatible), so we must pre-generate the correct specialization.
        EnsureComparerCompanionType(openTypeName, typeArgs);
    }

    /// <summary>
    /// When EqualityComparer&lt;T&gt; or Comparer&lt;T&gt; is discovered, also ensure
    /// ObjectEqualityComparer&lt;T&gt; or ObjectComparer&lt;T&gt; is instantiated.
    /// These are needed by AOT replacement cctors to allocate the correct type
    /// that implements IEqualityComparer&lt;T&gt; / IComparer&lt;T&gt;.
    /// </summary>
    private void EnsureComparerCompanionType(string openTypeName, List<string> typeArgs)
    {
        string? companionOpenName = openTypeName switch
        {
            "System.Collections.Generic.EqualityComparer`1" => "System.Collections.Generic.ObjectEqualityComparer`1",
            "System.Collections.Generic.Comparer`1" => "System.Collections.Generic.ObjectComparer`1",
            _ => null
        };
        if (companionOpenName == null) return;

        var companionKey = $"{companionOpenName}<{string.Join(",", typeArgs)}>";
        if (_genericInstantiations.ContainsKey(companionKey)) return;

        // Find the Cecil type definition for the companion type
        TypeDefinition? companionCecil = null;
        foreach (var (_, asm) in _assemblySet.LoadedAssemblies)
        {
            companionCecil = asm.MainModule.GetType(companionOpenName);
            if (companionCecil != null) break;
        }
        if (companionCecil == null) return;

        var companionMangled = CppNameMapper.MangleGenericInstanceTypeName(companionOpenName, typeArgs);
        _genericInstantiations[companionKey] = new GenericInstantiationInfo(
            companionOpenName, typeArgs, companionMangled, companionCecil);
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

        // All SIMD code is dead (IsHardwareAccelerated=false) — skip all SIMD methods
        if (IsSimdContainerType(declaringType) ||
            declaringType.StartsWith("System.Runtime.Intrinsics.") ||
            declaringType == "System.ThrowHelper")
            return;
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

        // Also collect generic types used AS method type arguments.
        // Example: IndexOf<Byte, DontNegate<Byte>> — DontNegate<Byte> is a GenericInstanceType
        // passed as a type argument. Without this, nested generic types like INegator<T>,
        // DontNegate<T>, etc. are not discovered during Pass 0 type scanning.
        foreach (var ga in gim.GenericArguments)
            CollectGenericType(ga);
    }

    /// <summary>
    /// Create specialized IRMethods for each generic method instantiation found in Pass 0.
    /// </summary>
    private void CreateGenericMethodSpecializations()
    {
        // Fixpoint loop: processing a specialization body may discover new transitive
        // generic method instantiations (e.g., GetNameInlined<Byte> calls FindDefinedIndex<Byte>).
        // Uses class-level _processedMethodSpecKeys so this method can be safely re-called
        // (e.g., from Pass 3.6 re-discovery) without reprocessing already-created methods.
        //
        // Termination: finite input assemblies → finite set of (method × type-arg) combinations
        // → each key processed at most once → the pending set shrinks monotonically to empty.
        var pending = _genericMethodInstantiations.Keys
            .Where(k => !_processedMethodSpecKeys.Contains(k))
            .ToList();

        while (pending.Count > 0)
        {
            foreach (var key in pending)
            {
                _processedMethodSpecKeys.Add(key);
                if (_genericMethodInstantiations.TryGetValue(key, out var info))
                    ProcessGenericMethodSpecialization(key, info);
            }

            // Collect keys discovered during this round's processing
            pending = _genericMethodInstantiations.Keys
                .Where(k => !_processedMethodSpecKeys.Contains(k))
                .ToList();
        }
    }

    private void ProcessGenericMethodSpecialization(string key, GenericMethodInstantiationInfo info)
    {
        var cecilMethod = info.CecilMethod;

        // Skip all SIMD method specializations — all SIMD code is dead (IsHardwareAccelerated=false)
        if (IsSimdContainerType(info.DeclaringTypeName) ||
            info.DeclaringTypeName.StartsWith("System.Runtime.Intrinsics."))
            return;

        // Skip SIMD-specific ThrowHelper methods (only called from dead SIMD branches).
        // Other ThrowHelper methods (ThrowKeyNotFoundException, ThrowArgumentOutOfRange_Range)
        // are genuinely needed by Dictionary, DateTimeFormatInfo, etc.
        if (info.DeclaringTypeName == "System.ThrowHelper"
            && (info.MethodName.Contains("Intrinsics") || info.MethodName.Contains("Numerics")))
            return;

        // Find the declaring IRType
        if (!_typeCache.TryGetValue(info.DeclaringTypeName, out var declaringIrType))
            return;

            // Build type parameter map: BOTH declaring type params AND method-level params.
            // E.g., for EnumInfo<Byte>.CloneValues<SByte>:
            //   TStorage=System.Byte (from declaring type EnumInfo<Byte>)
            //   TResult=System.SByte (from method CloneValues<SByte>)
            var typeParamMap = new Dictionary<string, string>();

            // Add declaring type's generic params (if the type is a generic specialization)
            if (cecilMethod.DeclaringType.HasGenericParameters && declaringIrType.GenericArguments != null)
            {
                var typeGenericParams = cecilMethod.DeclaringType.GenericParameters;
                for (int i = 0; i < typeGenericParams.Count && i < declaringIrType.GenericArguments.Count; i++)
                    typeParamMap[typeGenericParams[i].Name] = declaringIrType.GenericArguments[i];
            }

            // Add method-level generic params
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
                var rawParamName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}";
                irMethod.Parameters.Add(new IRParameter
                {
                    Name = rawParamName,
                    CppName = CppNameMapper.MangleIdentifier(rawParamName),
                    CppTypeName = ResolveTypeForDecl(paramTypeName),
                    ILTypeName = paramTypeName,
                    Index = paramDef.Index,
                });
            }

            // Local variables
            if (cecilMethod.HasBody)
            {
                var localParamMap = BuildMethodLevelGenericParamMap(cecilMethod, typeParamMap);
                foreach (var localDef in cecilMethod.Body.Variables)
                {
                    var localTypeName = ResolveGenericTypeName(localDef.VariableType, localParamMap);
                    irMethod.Locals.Add(new IRLocal
                    {
                        Index = localDef.Index,
                        CppName = $"loc_{localDef.Index}",
                        CppTypeName = ResolveTypeForDecl(localTypeName),
                    });
                }
            }

            declaringIrType.Methods.Add(irMethod);

            // Skip body conversion for ICall-mapped methods — dead code
            // (callers are redirected to the ICall function by EmitMethodCall)
            if (irMethod.HasICallMapping) return;

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
                    // Pre-scan: discover generic types referenced in the body that need to
                    // exist as IRTypes before body conversion. E.g., Array.Sort<String> body
                    // calls IArraySortHelper<String>.Sort() — the interface type must be in
                    // _typeCache for virtual dispatch resolution in EmitMethodCall.
                    EnsureBodyReferencedTypesExist(cecilMethod, typeParamMap);

                    var methodInfo = new IL.MethodInfo(cecilMethod);
                    ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
                }
            }
    }

    /// <summary>
    /// Just-in-time discovery: scan a generic method body for GenericInstanceType references
    /// and ensure they exist as IRTypes in _typeCache before body conversion.
    /// This handles cases where a generic METHOD body references generic TYPES parameterized
    /// by the method's own type arguments (e.g., Array.Sort&lt;String&gt; calls IArraySortHelper&lt;String&gt;.Sort()).
    /// These types may not have been discovered in Pass 0.5 because the method's type arguments
    /// were still unresolved generic parameters at scan time.
    /// </summary>
    private void EnsureBodyReferencedTypesExist(MethodDefinition cecilMethod, Dictionary<string, string> typeParamMap)
    {
        if (!cecilMethod.HasBody) return;

        var prevCount = _genericInstantiations.Count;

        foreach (var instr in cecilMethod.Body.Instructions)
        {
            switch (instr.Operand)
            {
                case MethodReference methodRef:
                    // Late-discovery: if a method body references a generic interface via
                    // callvirt/constrained, add it to _dispatchedInterfaces so it gets materialized
                    if (instr.OpCode.Code == Code.Callvirt
                        && methodRef.DeclaringType is GenericInstanceType callvirtGit)
                    {
                        try
                        {
                            var resolved = callvirtGit.ElementType.Resolve();
                            if (resolved?.IsInterface == true)
                                _dispatchedInterfaces.Add(callvirtGit.ElementType.FullName);
                        }
                        catch { /* Cecil resolve failures are handled elsewhere */ }
                    }
                    TryCollectResolvedGenericType(methodRef.DeclaringType, typeParamMap);
                    // Track specialized method calls with resolved type params
                    if (methodRef.DeclaringType is GenericInstanceType ensureGit)
                    {
                        var resolvedKey = GetResolvedSpecializedMethodKey(ensureGit, methodRef, typeParamMap);
                        if (resolvedKey != null)
                            _calledSpecializedMethods.Add(resolvedKey);
                    }
                    else if (typeParamMap.Count > 0)
                    {
                        // Open generic body calling another method on the same open type.
                        var declType = methodRef.DeclaringType;
                        TypeReference? resolvedDecl = null;
                        if (declType.HasGenericParameters)
                        {
                            resolvedDecl = declType;
                        }
                        else
                        {
                            try
                            {
                                var resolved = declType.Resolve();
                                if (resolved?.HasGenericParameters == true)
                                    resolvedDecl = resolved;
                            }
                            catch { /* ignore resolve failures */ }
                        }
                        if (resolvedDecl != null)
                        {
                            var openKey = TryBuildSpecKeyFromOpenType(methodRef, resolvedDecl, typeParamMap);
                            if (openKey != null)
                                _calledSpecializedMethods.Add(openKey);
                        }
                    }
                    TryCollectResolvedGenericType(methodRef.ReturnType, typeParamMap);
                    foreach (var p in methodRef.Parameters)
                        TryCollectResolvedGenericType(p.ParameterType, typeParamMap);
                    if (methodRef is GenericInstanceMethod gim)
                        foreach (var ga in gim.GenericArguments)
                            TryCollectResolvedGenericType(ga, typeParamMap);
                    break;
                case FieldReference fieldRef:
                    TryCollectResolvedGenericType(fieldRef.DeclaringType, typeParamMap);
                    TryCollectResolvedGenericType(fieldRef.FieldType, typeParamMap);
                    break;
                case TypeReference typeRef:
                    TryCollectResolvedGenericType(typeRef, typeParamMap);
                    break;
            }
        }

        // Scan local variables — demand-driven needs this since Pass 0.5 is removed
        foreach (var local in cecilMethod.Body.Variables)
            TryCollectResolvedGenericType(local.VariableType, typeParamMap);

        // Fixpoint: create types → base chain/interface auto-discovery may find more → iterate
        if (_genericInstantiations.Count > prevCount)
        {
            int fixpointCount;
            do
            {
                fixpointCount = _genericInstantiations.Count;
                CreateGenericSpecializations();
                int prevNested;
                do
                {
                    prevNested = _genericInstantiations.Count;
                    CreateNestedGenericSpecializations();
                } while (_genericInstantiations.Count > prevNested);
            } while (_genericInstantiations.Count > fixpointCount);
        }
    }

    /// <summary>
    /// <summary>
    /// Create specialized IRTypes for each generic instantiation found in Pass 0.
    /// All generic types (user + BCL) are monomorphized from their Cecil definitions.
    /// </summary>
    private void CreateGenericSpecializations()
    {
        // Pre-scan: ensure ObjectEqualityComparer<T>/ObjectComparer<T> companions exist
        // for all EqualityComparer<T>/Comparer<T> discovered so far. This catches types
        // discovered via transitive generic resolution (Pass 0.5) and re-discovery (Pass 3.6).
        var snapshot = _genericInstantiations.ToList();
        foreach (var (_, info) in snapshot)
            EnsureComparerCompanionType(info.OpenTypeName, info.TypeArguments);

        // Use snapshot — base chain / interface auto-discovery may add to _genericInstantiations
        foreach (var (key, info) in snapshot)
        {
            // Skip types already created (e.g., by BCL proxy system or reachability)
            if (_typeCache.ContainsKey(key))
            {
                continue;
            }

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
                IsEnum = openType.IsEnum,
                IsGenericInstance = true,
                IsDelegate = isDelegate,
                IsPublic = openType.IsPublic,
                IsNestedPublic = openType.IsNestedPublic,
                GenericArguments = info.TypeArguments,
                IsRuntimeProvided = RuntimeProvidedTypes.Contains(info.OpenTypeName),
                SourceKind = _assemblySet.ClassifyAssembly(openType.Module.Assembly.Name.Name),
            };

            // Propagate enum underlying type from Cecil definition
            if (openType.IsEnum)
            {
                var valueField = openType.Fields.FirstOrDefault(f => f.Name == "value__");
                irType.EnumUnderlyingType = valueField?.FieldType.FullName ?? "System.Int32";
            }

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
                // Skip value__ backing field for enums (same as IRBuilder.Types.cs)
                if (irType.IsEnum && fieldDef.Name == "value__") continue;

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

                // For enum constant fields, extract the constant value
                if (irType.IsEnum && fieldDef.IsStatic && fieldDef.HasConstant)
                    irField.ConstantValue = fieldDef.Constant?.ToString();

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

            // Create method specializations from Cecil definition — skip unreachable non-virtual, non-ctor
            if (openType != null)
            {
                foreach (var methodDef in openType.Methods)
                {
                    // Skip unreachable methods that aren't needed for vtable or construction.
                    // Their empty shells would only add overhead (forward declarations, stub candidates).
                    if (!methodDef.IsVirtual && !methodDef.IsConstructor
                        && !_reachability.IsReachable(methodDef))
                        continue;
                    // Skip methods with their own generic parameters (method-level generics).
                    // These can't be compiled in the type specialization because the method-level
                    // params (TResult, TKey, etc.) are unresolved. They'll be compiled via
                    // _genericMethodInstantiations with concrete type args at each call site.
                    // CLR guarantees: generic methods cannot be virtual, so no vtable impact.
                    if (methodDef.HasGenericParameters)
                        continue;
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
                        var rawParamName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}";
                        irMethod.Parameters.Add(new IRParameter
                        {
                            Name = rawParamName,
                            CppName = CppNameMapper.MangleIdentifier(rawParamName),
                            CppTypeName = ResolveTypeForDecl(paramTypeName),
                            ILTypeName = paramTypeName,
                            Index = paramDef.Index,
                        });
                    }

                    // Local variables
                    if (methodDef.HasBody)
                    {
                        // Build supplementary map for method-level generic params that leak into
                        // local types via Cecil. E.g., MemoryMarshal.Cast<TFrom, TResult> — the
                        // return type uses TResult, which appears in locals of calling methods.
                        var localParamMap = BuildMethodLevelGenericParamMap(methodDef, typeParamMap);
                        foreach (var localDef in methodDef.Body.Variables)
                        {
                            var localTypeName = ResolveGenericTypeName(localDef.VariableType, localParamMap);
                            irMethod.Locals.Add(new IRLocal
                            {
                                Index = localDef.Index,
                                CppName = $"loc_{localDef.Index}",
                                CppTypeName = ResolveTypeForDecl(localTypeName),
                            });
                        }
                    }

                    irType.Methods.Add(irMethod);

                    // Skip body conversion for ICall-mapped methods — dead code
                    if (irMethod.HasICallMapping) continue;

                    // Defer method body conversion to after DisambiguateOverloadedMethods (Pass 3.3).
                    // Converting bodies before disambiguation causes call sites to use
                    // pre-disambiguation names, leading to undeclared/mismatched function calls.
                    // Only convert reachable methods — same check as Pass 3.
                    // Unreachable methods keep BasicBlocks empty → not declared in header.
                    if (methodDef.HasBody && !methodDef.IsAbstract)
                    {
                        bool shouldCompile;
                        if (!_reachability.IsReachable(methodDef))
                        {
                            shouldCompile = false; // open method not reachable at all
                        }
                        else if (methodDef.IsConstructor)
                        {
                            shouldCompile = true; // constructors always compiled
                        }
                        else if (methodDef.IsVirtual)
                        {
                            // Virtual methods on constructed types must be compiled (vtable dispatch).
                            // For non-constructed types with no constructed subtypes, virtual methods
                            // can never be dispatched — apply specialized method tracking.
                            if (_module.ConstructedTypes.Contains(info.OpenTypeName)
                                || _module.ConstructedTypes.Contains(key)
                                || HasConstructedSubtype(info.OpenTypeName, info.TypeArguments))
                            {
                                shouldCompile = true;
                            }
                            else
                            {
                                var specKey = GetSpecializedMethodKey(
                                    info.OpenTypeName, info.TypeArguments, methodDef);
                                shouldCompile = _calledSpecializedMethods.Contains(specKey);
                            }
                        }
                        else
                        {
                            // Non-virtual, non-constructor, reachable open method:
                            // only compile if THIS specific specialization has the method called
                            var specKey = GetSpecializedMethodKey(
                                info.OpenTypeName, info.TypeArguments, methodDef);
                            shouldCompile = _calledSpecializedMethods.Contains(specKey);
                        }

                        if (shouldCompile)
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
                        else if (_reachability.IsReachable(methodDef)
                                 && !methodDef.IsConstructor && !methodDef.IsAbstract
                                 && !HasClrInternalDependencies(methodDef))
                        {
                            // Store skipped methods so they can be recovered if body compilation
                            // later adds their key to _calledSpecializedMethods.
                            var skipKey = GetSpecializedMethodKey(
                                info.OpenTypeName, info.TypeArguments, methodDef);
                            _skippedSpecializedMethods.Add((skipKey, methodDef, irMethod,
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
        // Uses _resolvedGenericTypeKeys to skip already-resolved types on re-invocation
        // (e.g., from demand-driven re-creation), preventing duplicate interface entries.
        // Use snapshot — base chain / interface auto-discovery may add to _genericInstantiations.
        var secondPassSnapshot = _genericInstantiations.ToList();
        foreach (var (key, info) in secondPassSnapshot)
        {
            bool alreadyResolved = _resolvedGenericTypeKeys.Contains(key);
            if (!alreadyResolved)
                _resolvedGenericTypeKeys.Add(key);
            if (info.CecilOpenType == null) continue;
            if (!_typeCache.TryGetValue(key, out var irType)) continue;

            // For already-resolved types, only re-check missing base types
            // (the base type may have been created by a later fixpoint iteration)
            if (alreadyResolved)
            {
                if (irType.BaseType == null && info.CecilOpenType.BaseType != null && !irType.IsValueType)
                {
                    var typeParamMap2 = new Dictionary<string, string>();
                    for (int i = 0; i < info.CecilOpenType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                        typeParamMap2[info.CecilOpenType.GenericParameters[i].Name] = info.TypeArguments[i];
                    var baseName2 = ResolveGenericTypeName(info.CecilOpenType.BaseType, typeParamMap2);
                    if (_typeCache.TryGetValue(baseName2, out var baseType2))
                    {
                        irType.BaseType = baseType2;
                        CalculateInstanceSize(irType);
                    }
                }
                continue;
            }

            var openType = info.CecilOpenType;
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];

            // Base type — auto-discover generic base types for demand-driven pipeline
            if (openType.BaseType != null && !irType.IsValueType)
            {
                var baseName = ResolveGenericTypeName(openType.BaseType, typeParamMap);
                if (_typeCache.TryGetValue(baseName, out var baseType))
                    irType.BaseType = baseType;
                else if (openType.BaseType is GenericInstanceType)
                    TryCollectResolvedGenericType(openType.BaseType, typeParamMap);
            }

            // Interfaces — auto-discover generic interface types (filtered by dispatch)
            foreach (var iface in openType.Interfaces)
            {
                // Skip generic interfaces that are never dispatched on
                if (iface.InterfaceType is GenericInstanceType ifaceGit)
                {
                    var openName = ifaceGit.ElementType.FullName;
                    if (!_dispatchedInterfaces.Contains(openName))
                        continue;
                }
                var ifaceName = ResolveGenericTypeName(iface.InterfaceType, typeParamMap);
                if (_typeCache.TryGetValue(ifaceName, out var ifaceType))
                    irType.Interfaces.Add(ifaceType);
                else if (iface.InterfaceType is GenericInstanceType)
                    TryCollectResolvedGenericType(iface.InterfaceType, typeParamMap);
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

                // Only create nested types that are referenced by reachable parent methods.
                // Value types are always kept (may be embedded as fields/array elements).
                if (!nestedTypeDef.IsValueType &&
                    !IsNestedTypeReferencedByReachableMethods(info.CecilOpenType, nestedTypeDef))
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
            EnsureComparerCompanionType(info.OpenTypeName, info.TypeArguments);

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
                IsEnum = openType.IsEnum,
                IsGenericInstance = true,
                IsDelegate = openType.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate",
                IsPublic = openType.IsPublic,
                IsNestedPublic = openType.IsNestedPublic,
                GenericArguments = info.TypeArguments,
                IsRuntimeProvided = false,
                SourceKind = _assemblySet.ClassifyAssembly(openType.Module.Assembly.Name.Name),
            };

            // Propagate enum underlying type from Cecil definition
            if (openType.IsEnum)
            {
                var valueField = openType.Fields.FirstOrDefault(f => f.Name == "value__");
                irType.EnumUnderlyingType = valueField?.FieldType.FullName ?? "System.Int32";
            }

            if (openType.IsValueType)
            {
                CppNameMapper.RegisterValueType(nestedKey);
                CppNameMapper.RegisterValueType(info.MangledName);
            }

            // Fields
            foreach (var fieldDef in openType.Fields)
            {
                // Skip value__ backing field for enums (same as IRBuilder.Types.cs)
                if (irType.IsEnum && fieldDef.Name == "value__") continue;

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

                // For enum constant fields, extract the constant value
                if (irType.IsEnum && fieldDef.IsStatic && fieldDef.HasConstant)
                    irField.ConstantValue = fieldDef.Constant?.ToString();

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

            // Create method shells + defer body conversion for nested type methods.
            // Same pattern as CreateGenericSpecializations: reachability + CLR gate → defer.
            foreach (var methodDef in openType.Methods)
            {
                // Skip unreachable non-virtual, non-ctor methods (same as CreateGenericSpecializations)
                if (!methodDef.IsVirtual && !methodDef.IsConstructor
                    && !_reachability.IsReachable(methodDef))
                    continue;
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

                foreach (var paramDef in methodDef.Parameters)
                {
                    var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                    var rawParamName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}";
                    irMethod.Parameters.Add(new IRParameter
                    {
                        Name = rawParamName,
                        CppName = SanitizeParamName(paramDef.Name, paramDef.Index),
                        CppTypeName = ResolveTypeForDecl(paramTypeName),
                        ILTypeName = paramTypeName,
                        Index = paramDef.Index,
                    });
                }

                // Local variables (needed for body conversion)
                if (methodDef.HasBody)
                {
                    var localParamMap = BuildMethodLevelGenericParamMap(methodDef, typeParamMap);
                    foreach (var localDef in methodDef.Body.Variables)
                    {
                        var localTypeName = ResolveGenericTypeName(localDef.VariableType, localParamMap);
                        irMethod.Locals.Add(new IRLocal
                        {
                            Index = localDef.Index,
                            CppName = $"loc_{localDef.Index}",
                            CppTypeName = ResolveTypeForDecl(localTypeName),
                        });
                    }
                }

                irType.Methods.Add(irMethod);

                // Skip body conversion for ICall-mapped methods — dead code
                if (irMethod.HasICallMapping) continue;

                // Defer body conversion to after Pass 3.3 (same as parent type methods).
                // Only convert reachable, non-CLR-internal methods.
                if (methodDef.HasBody && !methodDef.IsAbstract)
                {
                    if (!_reachability.IsReachable(methodDef))
                    {
                        // Unreachable — leave empty (no stub, no body)
                    }
                    else if (HasClrInternalDependencies(methodDef))
                    {
                        GenerateStubBody(irMethod);
                    }
                    else
                    {
                        _deferredGenericBodies.Add((methodDef, irMethod,
                            new Dictionary<string, string>(typeParamMap)));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check if a nested type is referenced by any reachable method of the parent type.
    /// Used to skip creating unnecessary nested type specializations (e.g., Dictionary.KeyCollection
    /// when only Dictionary.Add/ContainsKey are used).
    /// </summary>
    private bool IsNestedTypeReferencedByReachableMethods(TypeDefinition parentOpen, TypeDefinition nestedDef)
    {
        var nestedName = nestedDef.Name;
        foreach (var method in parentOpen.Methods)
        {
            if (!_reachability.IsReachable(method)) continue;
            if (!method.HasBody) continue;
            foreach (var instr in method.Body.Instructions)
            {
                switch (instr.Operand)
                {
                    case TypeReference tr when tr.Name == nestedName || tr.FullName.Contains($"/{nestedName}"):
                        return true;
                    case MethodReference mr when mr.DeclaringType.Name == nestedName || mr.DeclaringType.FullName.Contains($"/{nestedName}"):
                        return true;
                    case FieldReference fr when fr.DeclaringType.Name == nestedName || fr.DeclaringType.FullName.Contains($"/{nestedName}"):
                        return true;
                }
            }
        }
        return false;
    }

    // REMOVED: DiscoverTransitiveGenericTypes and DiscoverTransitiveGenericTypesFromMethods
    // These bulk pre-scanning methods were used by Pass 0.5 (now removed).
    // Demand-driven discovery via EnsureBodyReferencedTypesExist in ConvertDeferredGenericBodies
    // replaces their functionality — types are discovered when method bodies are compiled.

    /// <summary>
    /// Attempt to resolve a GenericInstanceType's generic parameters using a name map
    /// and register the fully resolved type as a generic instantiation.
    /// </summary>
    private void TryCollectResolvedGenericType(TypeReference typeRef, Dictionary<string, string> nameMap,
        bool shallowOnly = false)
    {
        // Handle GenericParameter types (e.g., !!1 from constrained calls).
        // Resolve through nameMap to get the actual type name, then if it's a generic
        // instantiation, parse and register it.
        if (typeRef is Mono.Cecil.GenericParameter gp)
        {
            if (nameMap.TryGetValue(gp.Name, out var resolvedName))
            {
                // In shallow mode, skip types with nested generic arguments to avoid
                // MangleTypeName/MangleTypeNameClean naming inconsistency
                if (shallowOnly && resolvedName.Contains('<'))
                    return;
                TryCollectFromResolvedName(resolvedName);
            }
            return;
        }

        if (typeRef is not GenericInstanceType git) return;

        // If already fully resolved (no generic params), delegate to normal collection
        if (!git.GenericArguments.Any(ContainsGenericParameter))
        {
            // In shallow mode, skip if any arg is itself a generic instance
            if (shallowOnly && git.GenericArguments.Any(a => a is GenericInstanceType))
                return;
            CollectGenericType(typeRef);
            return;
        }

        // Resolve each generic argument
        var resolvedArgs = new List<string>();
        foreach (var arg in git.GenericArguments)
        {
            var resolved = ResolveTypeArgument(arg, nameMap);
            if (resolved == null) return; // Can't fully resolve — skip
            resolvedArgs.Add(resolved);
        }

        // In shallow mode, skip types with nested generic arguments
        if (shallowOnly && resolvedArgs.Any(a => a.Contains('<')))
            return;

        var openTypeName = git.ElementType.FullName;

        // All SIMD code is dead (IsHardwareAccelerated=false)
        if (IsSimdContainerType(openTypeName))
            return;

        var instKey = $"{openTypeName}<{string.Join(",", resolvedArgs)}>";

        if (_genericInstantiations.ContainsKey(instKey)) return;
        if (_typeCache.ContainsKey(instKey)) return;

        // Skip generic specializations where any type argument is a CLR-internal type.
        foreach (var argName in resolvedArgs)
        {
            if (ClrInternalTypeNames.Contains(argName))
                return;
            if (argName.Contains('/'))
            {
                var outerTypeName = argName[..argName.IndexOf('/')];
                if (ClrInternalTypeNames.Contains(outerTypeName))
                    return;
            }
        }

        var mangledName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, resolvedArgs);
        var cecilOpenType = git.ElementType.Resolve();

        _genericInstantiations[instKey] = new GenericInstantiationInfo(
            openTypeName, resolvedArgs, mangledName, cecilOpenType);

        EnsureComparerCompanionType(openTypeName, resolvedArgs);
    }

    /// <summary>
    /// Try to collect a generic type from a fully resolved type name string.
    /// Used when a GenericParameter is resolved through a nameMap to a concrete type.
    /// Parses names like "System.SpanHelpers/DontNegate`1&lt;System.Byte&gt;" and registers them.
    /// </summary>
    private void TryCollectFromResolvedName(string resolvedTypeName)
    {
        // Only generic instantiation names contain '<'
        if (!resolvedTypeName.Contains('<')) return;

        // Parse: "Outer/Inner`1<Arg1,Arg2>" → openTypeName="Outer/Inner`1", args=["Arg1","Arg2"]
        // Find the generic '<' after the backtick to skip '<' in compiler-generated names
        var backtickIdx = resolvedTypeName.IndexOf('`');
        var ltIdx = backtickIdx > 0
            ? resolvedTypeName.IndexOf('<', backtickIdx)
            : resolvedTypeName.IndexOf('<');
        if (ltIdx < 0) return;
        var openTypeName = resolvedTypeName[..ltIdx];
        var argsStr = resolvedTypeName[(ltIdx + 1)..^1]; // strip < and >

        // Parse arguments respecting nested generics (< > nesting)
        var args = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < argsStr.Length; i++)
        {
            if (argsStr[i] == '<') depth++;
            else if (argsStr[i] == '>') depth--;
            else if (argsStr[i] == ',' && depth == 0)
            {
                args.Add(argsStr[start..i]);
                start = i + 1;
            }
        }
        args.Add(argsStr[start..]);

        // Build key and check if already known
        var instKey = $"{openTypeName}<{string.Join(",", args)}>";
        if (_genericInstantiations.ContainsKey(instKey)) return;
        if (_typeCache.ContainsKey(instKey)) return;

        // Skip generic specializations where any type argument is a CLR-internal type.
        foreach (var argName in args)
        {
            if (ClrInternalTypeNames.Contains(argName))
                return;
        }

        // SIMD container types only accept numeric primitive type arguments.
        if (IsInvalidSimdSpecialization(openTypeName, args))
            return;

        // Resolve the Cecil open type definition
        TypeDefinition? cecilOpenType = null;
        foreach (var asm in _assemblySet!.LoadedAssemblies.Values)
        {
            cecilOpenType = asm.MainModule.GetType(openTypeName.Replace('/', '+'));
            if (cecilOpenType != null) break;
            // Try without the nested type separator
            cecilOpenType = asm.MainModule.GetType(openTypeName);
            if (cecilOpenType != null) break;
        }
        // Also try finding it through existing _genericInstantiations that share the open type
        if (cecilOpenType == null)
        {
            foreach (var (_, info) in _genericInstantiations)
            {
                if (info.OpenTypeName == openTypeName && info.CecilOpenType != null)
                {
                    cecilOpenType = info.CecilOpenType;
                    break;
                }
            }
        }

        var mangledName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, args);
        _genericInstantiations[instKey] = new GenericInstantiationInfo(
            openTypeName, args, mangledName, cecilOpenType);

        EnsureComparerCompanionType(openTypeName, args);
    }

    /// <summary>
    /// Resolve a generic method call with unresolved type arguments using nameMap,
    /// and add the resolved method to _genericMethodInstantiations.
    /// This discovers transitive generic method calls: when IndexOf&lt;Byte, DontNegate&lt;Byte&gt;&gt;
    /// calls FindValue&lt;!!T, !!TNegator&gt;, resolve T→Byte, TNegator→DontNegate&lt;Byte&gt; to discover
    /// FindValue&lt;Byte, DontNegate&lt;Byte&gt;&gt; as a new method instantiation.
    /// </summary>
    private void TryCollectResolvedGenericMethod(GenericInstanceMethod gim, Dictionary<string, string> nameMap)
    {
        // If already fully resolved (no generic params), delegate to normal collection
        if (!gim.GenericArguments.Any(ContainsGenericParameter))
        {
            CollectGenericMethod(gim);
            return;
        }

        // Resolve each generic argument
        var resolvedArgs = new List<string>();
        foreach (var arg in gim.GenericArguments)
        {
            var resolved = ResolveTypeArgument(arg, nameMap);
            if (resolved == null) return; // Can't fully resolve — skip
            resolvedArgs.Add(resolved);
        }

        var elementMethod = gim.ElementMethod;
        var declaringType = elementMethod.DeclaringType.FullName;
        var methodName = elementMethod.Name;

        var paramSig = string.Join(",", elementMethod.Parameters.Select(p => p.ParameterType.FullName));
        var key = MakeGenericMethodKey(declaringType, methodName, resolvedArgs, paramSig);
        if (_genericMethodInstantiations.ContainsKey(key)) return;

        var cecilMethod = elementMethod.Resolve();
        if (cecilMethod == null) return;

        // Skip generic specializations where any type argument is a CLR-internal type.
        foreach (var argName in resolvedArgs)
        {
            if (ClrInternalTypeNames.Contains(argName))
                return;
        }

        // All SIMD code is dead (IsHardwareAccelerated=false) — skip all SIMD methods
        if (IsSimdContainerType(declaringType) ||
            declaringType.StartsWith("System.Runtime.Intrinsics.") ||
            declaringType == "System.ThrowHelper")
            return;

        var mangledName = MangleGenericMethodName(declaringType, methodName, resolvedArgs);

        // Disambiguate mangled name if another overload already uses it
        if (_genericMethodInstantiations.Values.Any(v => v.MangledName == mangledName))
        {
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < cecilMethod.GenericParameters.Count && i < resolvedArgs.Count; i++)
                typeParamMap[cecilMethod.GenericParameters[i].Name] = resolvedArgs[i];

            var paramSuffix = string.Join("_", cecilMethod.Parameters
                .Select(p => CppNameMapper.MangleTypeName(
                    ResolveGenericTypeName(p.ParameterType, typeParamMap))));
            mangledName += $"__{paramSuffix}";
        }

        _genericMethodInstantiations[key] = new GenericMethodInstantiationInfo(
            declaringType, methodName, resolvedArgs, mangledName, cecilMethod);
    }

    /// <summary>
    /// Resolve a type argument reference using a name map. Returns null if unresolvable.
    /// </summary>
    private static string? ResolveTypeArgument(TypeReference typeRef, Dictionary<string, string> nameMap)
    {
        if (typeRef is GenericParameter gp)
            return nameMap.TryGetValue(gp.Name, out var resolved) ? resolved : null;

        if (typeRef is ArrayType at)
        {
            var elem = ResolveTypeArgument(at.ElementType, nameMap);
            return elem != null ? elem + "[]" : null;
        }

        if (typeRef is ByReferenceType brt)
        {
            var elem = ResolveTypeArgument(brt.ElementType, nameMap);
            return elem != null ? elem + "&" : null;
        }

        if (typeRef is PointerType pt)
        {
            var elem = ResolveTypeArgument(pt.ElementType, nameMap);
            return elem != null ? elem + "*" : null;
        }

        if (typeRef is GenericInstanceType git)
        {
            var args = new List<string>();
            foreach (var arg in git.GenericArguments)
            {
                var resolved = ResolveTypeArgument(arg, nameMap);
                if (resolved == null) return null;
                args.Add(resolved);
            }
            return $"{git.ElementType.FullName}<{string.Join(",", args)}>";
        }

        // Non-generic type — return as-is
        return typeRef.FullName;
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
    /// Build an augmented type param map that includes method-level generic params
    /// from GenericInstanceMethod calls in the body. Cecil sometimes stores local variable types
    /// using the callee method's formal generic param names (e.g., local type Span&lt;TResult&gt;
    /// from MemoryMarshal.Cast&lt;TFrom, TResult&gt;). These need to be resolved to the actual
    /// type arguments at the call site.
    /// </summary>
    private Dictionary<string, string> BuildMethodLevelGenericParamMap(
        Mono.Cecil.MethodDefinition methodDef, Dictionary<string, string> typeParamMap)
    {
        if (!methodDef.HasBody)
            return typeParamMap;

        // Scan the body for GenericInstanceMethod calls and build supplementary mappings
        var augmented = new Dictionary<string, string>(typeParamMap);
        foreach (var instr in methodDef.Body.Instructions)
        {
            var operand = instr.Operand;
            if (operand is Mono.Cecil.GenericInstanceMethod gim)
            {
                var elemMethod = gim.ElementMethod.Resolve();
                if (elemMethod == null || !elemMethod.HasGenericParameters) continue;
                for (int i = 0; i < elemMethod.GenericParameters.Count && i < gim.GenericArguments.Count; i++)
                {
                    var formalName = elemMethod.GenericParameters[i].Name;
                    if (augmented.ContainsKey(formalName)) continue;
                    // Resolve the actual type argument through the current type param map
                    var argResolved = ResolveGenericTypeName(gim.GenericArguments[i], typeParamMap);
                    augmented[formalName] = argResolved;
                }
            }
        }
        return augmented;
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
    /// Resolve a field's type through its declaring generic instance type.
    /// When accessing fields of generic types (e.g., Span&lt;char&gt;._reference),
    /// Cecil gives the unresolved field type (ByReference&lt;T&gt; / T&amp;). This method
    /// builds a temporary type param map from the declaring GenericInstanceType
    /// and resolves T → the concrete type argument (e.g., System.Char).
    /// </summary>
    private string ResolveFieldTypeRef(FieldReference fieldRef)
    {
        // If the declaring type is a generic instance, resolve generic params in the field type
        if (fieldRef.DeclaringType is GenericInstanceType git)
        {
            var openType = git.ElementType.Resolve();
            if (openType != null && openType.HasGenericParameters)
            {
                // Build combined map: start with active type param map (if in generic context),
                // then overlay with declaring type's generic arguments
                var localMap = _activeTypeParamMap != null
                    ? new Dictionary<string, string>(_activeTypeParamMap)
                    : new Dictionary<string, string>();

                for (int i = 0; i < openType.GenericParameters.Count && i < git.GenericArguments.Count; i++)
                {
                    var paramName = openType.GenericParameters[i].Name;
                    // Resolve the argument through active map (handles nested generics)
                    var argResolved = ResolveTypeRefOperand(git.GenericArguments[i]);
                    localMap[paramName] = argResolved;
                    // Also add position-based key (!0, !1, etc.) for Cecil field types
                    // that use position syntax instead of named parameters
                    localMap[$"!{i}"] = argResolved;
                }

                return ResolveGenericTypeName(fieldRef.FieldType, localMap);
            }
        }

        return ResolveTypeRefOperand(fieldRef.FieldType);
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
        // Draining-queue fixpoint: compile a batch of deferred bodies → EnsureBody may
        // discover new generic types → CreateGenericSpecializations adds new deferred
        // bodies → process next batch until empty.
        var processed = new HashSet<IRMethod>();
        while (_deferredGenericBodies.Count > 0)
        {
            var batch = new List<(MethodDefinition CecilMethod, IRMethod IrMethod,
                Dictionary<string, string> TypeParamMap)>(_deferredGenericBodies);
            _deferredGenericBodies.Clear();

            foreach (var (cecilMethod, irMethod, typeParamMap) in batch)
            {
                if (!processed.Add(irMethod)) continue;

                // Skip record compiler-generated methods — Pass 7 synthesizes replacements
                if (irMethod.DeclaringType?.IsRecord == true && IsRecordSynthesizedMethod(irMethod.Name))
                    continue;

                // Skip ICall-mapped methods — dead code (callers redirected to ICall function)
                if (irMethod.HasICallMapping) continue;

                // Demand-driven: discover types referenced in this body BEFORE compiling.
                // Without Pass 0.5, transitive generic types are only found here.
                EnsureBodyReferencedTypesExist(cecilMethod, typeParamMap);

                var methodInfo = new IL.MethodInfo(cecilMethod);
                ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
            }

            // Recover skipped methods whose keys are now in _calledSpecializedMethods.
            for (int i = _skippedSpecializedMethods.Count - 1; i >= 0; i--)
            {
                var (specKey, cecilMethod, irMethod, typeParamMap) = _skippedSpecializedMethods[i];
                if (_calledSpecializedMethods.Contains(specKey) && irMethod.BasicBlocks.Count == 0)
                {
                    _skippedSpecializedMethods.RemoveAt(i);
                    if (HasClrInternalDependencies(cecilMethod))
                        GenerateStubBody(irMethod);
                    else
                        _deferredGenericBodies.Add((cecilMethod, irMethod, typeParamMap));
                }
            }

            // If new types were discovered during body compilation, build VTables
            // and disambiguate before the next batch (needed for callvirt resolution)
            if (_deferredGenericBodies.Count > 0)
            {
                foreach (var irType in _module.Types)
                    BuildVTableRecursive(irType, _vtableBuilt);
                DisambiguateOverloadedMethods();
            }
        }
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

        // Post-process: resolve any remaining unresolved generic parameter names
        // in ResultTypeCpp fields of emitted instructions. Some code paths in the
        // stack simulation don't go through _activeTypeParamMap resolution.
        ResolveRemainingGenericParams(irMethod, typeParamMap);
    }

    /// <summary>
    /// Post-process method instructions to resolve unresolved generic parameter names
    /// (e.g., TChar, TKey) in ResultTypeCpp fields and IRRawCpp code strings.
    /// </summary>
    private static void ResolveRemainingGenericParams(IRMethod irMethod, Dictionary<string, string> typeParamMap)
    {
        // Build C++ name resolution map: "TChar" → "char16_t", etc.
        Dictionary<string, string>? cppResolvedMap = null;
        foreach (var (paramName, ilName) in typeParamMap)
        {
            var cppName = CppNameMapper.GetCppTypeName(ilName);
            if (cppName != paramName)
            {
                cppResolvedMap ??= new Dictionary<string, string>();
                cppResolvedMap[paramName] = cppName;
            }
        }
        if (cppResolvedMap == null) return; // nothing to resolve

        // Build mangled name resolution map for generic params embedded in C++ identifiers.
        // In mangled names like "IEqualityComparer_1_TKey", "TKey" is preceded by "_"
        // which ReplaceWholeWord treats as a word char. We use targeted replacement:
        // only replace "_N_ParamName" patterns (generic arity prefix + param name).
        var mangledResolvedMap = new Dictionary<string, string>();
        foreach (var (paramName, ilName) in typeParamMap)
        {
            var mangledResolved = CppNameMapper.MangleTypeName(ilName);
            if (mangledResolved != paramName)
            {
                // Add arity-prefixed patterns: _1_TKey → _1_System_String, _2_TKey → _2_System_String
                for (int arity = 1; arity <= 8; arity++)
                    mangledResolvedMap[$"_{arity}_{paramName}"] = $"_{arity}_{mangledResolved}";

                // Add non-arity patterns for generic method params embedded in function names.
                // E.g., MemoryMarshal_Cast_TStorage_System_Char → MemoryMarshal_Cast_System_Byte_System_Char
                // The regex lookahead (?![a-zA-Z]) ensures _TStorage_ matches (underscore not a letter)
                // but _TStorageHelper does NOT match (letter follows).
                mangledResolvedMap[$"_{paramName}"] = $"_{mangledResolved}";
            }
        }

        // Resolve generic params in TempVarTypes (cross-scope variable pre-declarations)
        var tempKeys = irMethod.TempVarTypes.Keys.ToList();
        foreach (var key in tempKeys)
        {
            var resolved = ResolveGenericParamInCppType(irMethod.TempVarTypes[key], typeParamMap);
            if (resolved != irMethod.TempVarTypes[key])
                irMethod.TempVarTypes[key] = resolved;
            // Also resolve mangled names in TempVarTypes (boundary-aware to avoid
            // _1_T matching inside _1_ThreadLocalArray)
            foreach (var (from, to) in mangledResolvedMap)
            {
                if (irMethod.TempVarTypes[key].Contains(from))
                    irMethod.TempVarTypes[key] = System.Text.RegularExpressions.Regex.Replace(
                        irMethod.TempVarTypes[key],
                        System.Text.RegularExpressions.Regex.Escape(from) + @"(?![a-zA-Z])",
                        to);
            }
        }

        // Resolve generic params in local variable types
        foreach (var local in irMethod.Locals)
        {
            var resolved = ResolveGenericParamInCppType(local.CppTypeName, typeParamMap);
            if (resolved != local.CppTypeName)
                local.CppTypeName = resolved;
            foreach (var (from, to) in mangledResolvedMap)
            {
                if (local.CppTypeName.Contains(from))
                    local.CppTypeName = System.Text.RegularExpressions.Regex.Replace(
                        local.CppTypeName,
                        System.Text.RegularExpressions.Regex.Escape(from) + @"(?![a-zA-Z])",
                        to);
            }
        }

        foreach (var block in irMethod.BasicBlocks)
        {
            foreach (var instr in block.Instructions)
            {
                // Resolve ResultTypeCpp on all instruction types that have it
                string? resultType = instr switch
                {
                    IRRawCpp raw => raw.ResultTypeCpp,
                    IRCall call => call.ResultTypeCpp,
                    IRFieldAccess fa => fa.ResultTypeCpp,
                    IRStaticFieldAccess sfa => sfa.ResultTypeCpp,
                    _ => null,
                };

                if (resultType != null)
                {
                    var resolved = ResolveGenericParamInCppType(resultType, typeParamMap);
                    if (resolved != resultType)
                    {
                        switch (instr)
                        {
                            case IRRawCpp raw: raw.ResultTypeCpp = resolved; break;
                            case IRCall call: call.ResultTypeCpp = resolved; break;
                            case IRFieldAccess fa: fa.ResultTypeCpp = resolved; break;
                            case IRStaticFieldAccess sfa: sfa.ResultTypeCpp = resolved; break;
                        }
                    }
                }

                // Also resolve generic params in IRRawCpp.Code strings
                // (e.g., "TChar __t7 = static_cast<TChar>(45);" → "char16_t __t7 = ...")
                if (instr is IRRawCpp rawInstr)
                {
                    var code = rawInstr.Code;
                    foreach (var (paramName, cppName) in cppResolvedMap)
                    {
                        if (code.Contains(paramName))
                            code = ReplaceWholeWord(code, paramName, cppName);
                    }
                    if (code != rawInstr.Code)
                        rawInstr.Code = code;
                }

                // Resolve generic params in all IRCall string fields
                // (FunctionName, Arguments, VTableReturnType, VTableParamTypes, InterfaceTypeCppName)
                if (instr is IRCall callInstr)
                {
                    foreach (var (paramName, cppName) in cppResolvedMap)
                    {
                        if (callInstr.FunctionName.Contains(paramName))
                            callInstr.FunctionName = ReplaceWholeWord(callInstr.FunctionName, paramName, cppName);
                        for (int argIdx = 0; argIdx < callInstr.Arguments.Count; argIdx++)
                        {
                            if (callInstr.Arguments[argIdx].Contains(paramName))
                                callInstr.Arguments[argIdx] = ReplaceWholeWord(callInstr.Arguments[argIdx], paramName, cppName);
                        }
                        if (callInstr.VTableReturnType?.Contains(paramName) == true)
                            callInstr.VTableReturnType = ReplaceWholeWord(callInstr.VTableReturnType, paramName, cppName);
                        if (callInstr.VTableParamTypes != null)
                        {
                            for (int vtIdx = 0; vtIdx < callInstr.VTableParamTypes.Count; vtIdx++)
                            {
                                if (callInstr.VTableParamTypes[vtIdx].Contains(paramName))
                                    callInstr.VTableParamTypes[vtIdx] = ReplaceWholeWord(callInstr.VTableParamTypes[vtIdx], paramName, cppName);
                            }
                        }
                        if (callInstr.InterfaceTypeCppName?.Contains(paramName) == true)
                            callInstr.InterfaceTypeCppName = ReplaceWholeWord(callInstr.InterfaceTypeCppName, paramName, cppName);
                    }
                }

                // Resolve generic params in all other instruction type fields
                ResolveInstructionGenericParams(instr, cppResolvedMap);

                // Second pass: resolve mangled generic params embedded in C++ identifiers
                // (e.g., "_TKey_" → "_System_String_" in "Dictionary_2_Entry_1_TKey_TValue")
                if (mangledResolvedMap.Count > 0)
                    ResolveMangledGenericParams(instr, mangledResolvedMap);
            }
        }
    }

    /// <summary>
    /// Resolve mangled generic param names embedded in C++ identifiers.
    /// Uses boundary-aware replacement to avoid replacing inside other identifiers
    /// (e.g., "_1_T" in "_1_ThreadLocalArray" should NOT match).
    /// </summary>
    private static void ResolveMangledGenericParams(IRInstruction instr, Dictionary<string, string> mangledMap)
    {
        static string Resolve(string text, Dictionary<string, string> map)
        {
            foreach (var (from, to) in map)
            {
                if (text.Contains(from))
                {
                    // Boundary-aware: only replace when NOT followed by a letter.
                    // This prevents _1_T matching inside _1_ThreadLocalArray.
                    text = System.Text.RegularExpressions.Regex.Replace(
                        text,
                        System.Text.RegularExpressions.Regex.Escape(from) + @"(?![a-zA-Z])",
                        to);
                }
            }
            return text;
        }

        switch (instr)
        {
            case IRRawCpp raw:
                raw.Code = Resolve(raw.Code, mangledMap);
                if (raw.ResultTypeCpp != null) raw.ResultTypeCpp = Resolve(raw.ResultTypeCpp, mangledMap);
                break;
            case IRCall call:
                call.FunctionName = Resolve(call.FunctionName, mangledMap);
                for (int i = 0; i < call.Arguments.Count; i++)
                    call.Arguments[i] = Resolve(call.Arguments[i], mangledMap);
                if (call.ResultTypeCpp != null) call.ResultTypeCpp = Resolve(call.ResultTypeCpp, mangledMap);
                if (call.VTableReturnType != null) call.VTableReturnType = Resolve(call.VTableReturnType, mangledMap);
                if (call.VTableParamTypes != null)
                    for (int i = 0; i < call.VTableParamTypes.Count; i++)
                        call.VTableParamTypes[i] = Resolve(call.VTableParamTypes[i], mangledMap);
                if (call.InterfaceTypeCppName != null) call.InterfaceTypeCppName = Resolve(call.InterfaceTypeCppName, mangledMap);
                break;
            case IRFieldAccess fa:
                fa.FieldCppName = Resolve(fa.FieldCppName, mangledMap);
                fa.ObjectExpr = Resolve(fa.ObjectExpr, mangledMap);
                if (fa.CastToType != null) fa.CastToType = Resolve(fa.CastToType, mangledMap);
                if (fa.StoreValue != null) fa.StoreValue = Resolve(fa.StoreValue, mangledMap);
                if (fa.ResultTypeCpp != null) fa.ResultTypeCpp = Resolve(fa.ResultTypeCpp, mangledMap);
                break;
            case IRStaticFieldAccess sfa:
                sfa.TypeCppName = Resolve(sfa.TypeCppName, mangledMap);
                if (sfa.StoreValue != null) sfa.StoreValue = Resolve(sfa.StoreValue, mangledMap);
                if (sfa.ResultTypeCpp != null) sfa.ResultTypeCpp = Resolve(sfa.ResultTypeCpp, mangledMap);
                break;
            case IRCast cast:
                cast.TargetTypeCpp = Resolve(cast.TargetTypeCpp, mangledMap);
                cast.SourceExpr = Resolve(cast.SourceExpr, mangledMap);
                if (cast.TypeInfoCppName != null) cast.TypeInfoCppName = Resolve(cast.TypeInfoCppName, mangledMap);
                break;
            case IRBox box:
                box.ValueTypeCppName = Resolve(box.ValueTypeCppName, mangledMap);
                box.ValueExpr = Resolve(box.ValueExpr, mangledMap);
                if (box.TypeInfoCppName != null) box.TypeInfoCppName = Resolve(box.TypeInfoCppName, mangledMap);
                break;
            case IRUnbox unbox:
                unbox.ValueTypeCppName = Resolve(unbox.ValueTypeCppName, mangledMap);
                unbox.ObjectExpr = Resolve(unbox.ObjectExpr, mangledMap);
                break;
            case IRNewObj newObj:
                newObj.TypeCppName = Resolve(newObj.TypeCppName, mangledMap);
                newObj.CtorName = Resolve(newObj.CtorName, mangledMap);
                for (int i = 0; i < newObj.CtorArgs.Count; i++)
                    newObj.CtorArgs[i] = Resolve(newObj.CtorArgs[i], mangledMap);
                break;
            case IRInitObj initObj:
                initObj.TypeCppName = Resolve(initObj.TypeCppName, mangledMap);
                initObj.AddressExpr = Resolve(initObj.AddressExpr, mangledMap);
                break;
            case IRStaticCtorGuard guard:
                guard.TypeCppName = Resolve(guard.TypeCppName, mangledMap);
                break;
        }
    }

    /// <summary>
    /// Resolve generic params in all string fields of non-IRRawCpp/IRCall instructions.
    /// Covers IRFieldAccess, IRStaticFieldAccess, IRCast, IRBox, IRUnbox, IRNewObj, IRInitObj, IRStaticCtorGuard.
    /// </summary>
    private static void ResolveInstructionGenericParams(IRInstruction instr, Dictionary<string, string> cppResolvedMap)
    {
        static string Resolve(string text, Dictionary<string, string> map)
        {
            foreach (var (paramName, cppName) in map)
            {
                if (text.Contains(paramName))
                    text = ReplaceWholeWord(text, paramName, cppName);
            }
            return text;
        }

        switch (instr)
        {
            case IRFieldAccess fa:
                fa.CastToType = fa.CastToType != null ? Resolve(fa.CastToType, cppResolvedMap) : null;
                fa.FieldCppName = Resolve(fa.FieldCppName, cppResolvedMap);
                fa.ObjectExpr = Resolve(fa.ObjectExpr, cppResolvedMap);
                fa.StoreValue = fa.StoreValue != null ? Resolve(fa.StoreValue, cppResolvedMap) : null;
                break;
            case IRStaticFieldAccess sfa:
                sfa.TypeCppName = Resolve(sfa.TypeCppName, cppResolvedMap);
                sfa.StoreValue = sfa.StoreValue != null ? Resolve(sfa.StoreValue, cppResolvedMap) : null;
                break;
            case IRCast cast:
                cast.TargetTypeCpp = Resolve(cast.TargetTypeCpp, cppResolvedMap);
                cast.TypeInfoCppName = cast.TypeInfoCppName != null ? Resolve(cast.TypeInfoCppName, cppResolvedMap) : null;
                cast.SourceExpr = Resolve(cast.SourceExpr, cppResolvedMap);
                break;
            case IRBox box:
                box.ValueTypeCppName = Resolve(box.ValueTypeCppName, cppResolvedMap);
                box.TypeInfoCppName = box.TypeInfoCppName != null ? Resolve(box.TypeInfoCppName, cppResolvedMap) : null;
                box.ValueExpr = Resolve(box.ValueExpr, cppResolvedMap);
                break;
            case IRUnbox unbox:
                unbox.ValueTypeCppName = Resolve(unbox.ValueTypeCppName, cppResolvedMap);
                unbox.ObjectExpr = Resolve(unbox.ObjectExpr, cppResolvedMap);
                break;
            case IRNewObj newObj:
                newObj.TypeCppName = Resolve(newObj.TypeCppName, cppResolvedMap);
                newObj.CtorName = Resolve(newObj.CtorName, cppResolvedMap);
                for (int i = 0; i < newObj.CtorArgs.Count; i++)
                    newObj.CtorArgs[i] = Resolve(newObj.CtorArgs[i], cppResolvedMap);
                break;
            case IRInitObj initObj:
                initObj.TypeCppName = Resolve(initObj.TypeCppName, cppResolvedMap);
                initObj.AddressExpr = Resolve(initObj.AddressExpr, cppResolvedMap);
                break;
            case IRStaticCtorGuard guard:
                guard.TypeCppName = Resolve(guard.TypeCppName, cppResolvedMap);
                break;
        }
    }

    /// <summary>
    /// Replace whole-word occurrences of a name in a string.
    /// Avoids replacing "TChar" inside "TCharSet" by checking word boundaries.
    /// </summary>
    private static string ReplaceWholeWord(string text, string oldWord, string newWord)
    {
        var result = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int idx = text.IndexOf(oldWord, i, StringComparison.Ordinal);
            if (idx < 0)
            {
                result.Append(text, i, text.Length - i);
                break;
            }
            // Check word boundary before
            if (idx > 0 && (char.IsLetterOrDigit(text[idx - 1]) || text[idx - 1] == '_'))
            {
                result.Append(text, i, idx + 1 - i);
                i = idx + 1;
                continue;
            }
            // Check word boundary after
            var afterIdx = idx + oldWord.Length;
            if (afterIdx < text.Length && (char.IsLetterOrDigit(text[afterIdx]) || text[afterIdx] == '_'))
            {
                result.Append(text, i, afterIdx - i);
                i = afterIdx;
                continue;
            }
            // Whole word match — replace
            result.Append(text, i, idx - i);
            result.Append(newWord);
            i = afterIdx;
        }
        return result.ToString();
    }

    /// <summary>
    /// Resolve generic parameter names in a C++ type string.
    /// Handles patterns like "TChar", "TChar*", "TKey**".
    /// </summary>
    private static string ResolveGenericParamInCppType(string cppType, Dictionary<string, string> typeParamMap)
    {
        var baseType = cppType.TrimEnd('*');
        if (typeParamMap.TryGetValue(baseType, out var resolved))
        {
            var resolvedCpp = CppNameMapper.GetCppTypeName(resolved);
            var suffix = cppType[baseType.Length..]; // preserve pointer suffix
            return resolvedCpp + suffix;
        }
        return cppType;
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
            // Find the generic '<' after the backtick, not an earlier '<' from
            // compiler-generated names like "<InvokeAsync>d__7`1<...>"
            var angleBracket = key.IndexOf('<', backtickIdx);
            if (angleBracket > 0)
            {
                var openTypeName = key[..angleBracket];
                var argsStr = key[(angleBracket + 1)..^1];
                var args = CppNameMapper.ParseGenericArgs(argsStr);
                return CppNameMapper.MangleGenericInstanceTypeName(openTypeName, args);
            }
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
            // Find the generic '<' after the backtick, not an earlier '<' from
            // compiler-generated names like "<InvokeAsync>d__7`1<...>"
            var angleBracket = ilTypeName.IndexOf('<', backtickIdx);
            if (angleBracket < 0) goto fallback;
            var openTypeName = ilTypeName[..angleBracket];
            var argsStr = ilTypeName[(angleBracket + 1)..^1];
            // Use bracket-aware parser to correctly handle nested generics
            // (simple Split(',') breaks on KeyValuePair<K,V> inner commas)
            var args = CppNameMapper.ParseGenericArgs(argsStr);
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

        fallback:
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
