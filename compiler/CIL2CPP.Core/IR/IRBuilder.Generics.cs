using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    // Active generic type parameter map (set during ConvertMethodBodyWithGenerics)
    private Dictionary<string, string>? _activeTypeParamMap;

    // Compile-time constant locals: tracks local variables known to hold compile-time constant values.
    // Used for dead branch elimination: IsSupported=0 → stloc → ldloc → brfalse eliminates dead SIMD paths.
    // Cleared at method entry and at control flow merge points (branch target labels).
    private readonly Dictionary<int, int> _compileTimeConstantLocals = new();

    /// <summary>
    /// Collect ALL generic parameters for a type, including parameters from declaring (parent) types.
    /// Cecil's TypeDefinition.GenericParameters only includes the type's OWN params, but
    /// GenericInstanceType.GenericArguments includes ALL params (parent + nested) in parent-first order.
    /// For example, AsyncTaskMethodBuilder`1/AsyncStateMachineBox`1:
    ///   - GenericParameters = [TStateMachine] (only the nested type's own param)
    ///   - GenericArguments = [TResult, TStateMachine] (parent's TResult + nested's TStateMachine)
    /// This method returns [TResult, TStateMachine] by walking the declaring type chain.
    /// </summary>
    private static List<GenericParameter> CollectAllGenericParameters(TypeDefinition type)
    {
        // Fast path: no declaring type → just use the type's own params
        if (type.DeclaringType == null || !type.DeclaringType.HasGenericParameters)
            return type.HasGenericParameters ? new List<GenericParameter>(type.GenericParameters) : new();

        var chain = new List<TypeDefinition>();
        var current = type;
        while (current != null)
        {
            chain.Add(current);
            current = current.DeclaringType;
        }
        chain.Reverse(); // outermost first

        var allParams = new List<GenericParameter>();
        foreach (var t in chain)
        {
            if (t.HasGenericParameters)
                allParams.AddRange(t.GenericParameters);
        }
        return allParams;
    }

    /// <summary>
    /// Build a type parameter map for a generic instantiation, correctly handling nested generic types.
    /// When openType.GenericParameters.Count matches typeArguments.Count, uses direct mapping.
    /// When they differ (nested type with parent args), walks the declaring type chain.
    /// </summary>
    private static Dictionary<string, string> BuildTypeParamMap(TypeDefinition openType, List<string> typeArguments)
    {
        var typeParamMap = new Dictionary<string, string>();
        if (openType.GenericParameters.Count == typeArguments.Count)
        {
            // Direct mapping — counts match (non-nested, or nested with same param count as parent)
            for (int i = 0; i < openType.GenericParameters.Count && i < typeArguments.Count; i++)
                typeParamMap[openType.GenericParameters[i].Name] = typeArguments[i];
        }
        else
        {
            // Nested generic type — collect all params from the declaring chain
            var allParams = CollectAllGenericParameters(openType);
            if (allParams.Count == typeArguments.Count)
            {
                for (int i = 0; i < allParams.Count; i++)
                    typeParamMap[allParams[i].Name] = typeArguments[i];
            }
            else
            {
                // Fallback: map what we can with direct params (shouldn't normally happen)
                for (int i = 0; i < openType.GenericParameters.Count && i < typeArguments.Count; i++)
                    typeParamMap[openType.GenericParameters[i].Name] = typeArguments[i];
            }
        }
        return typeParamMap;
    }

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
    /// <summary>
    /// Checks if a type is a compiler-generated closure/display class.
    /// These types have methods invoked indirectly through delegates, so
    /// _calledSpecializedMethods won't track them. Their methods should
    /// always be compiled when the type is discovered.
    /// </summary>
    private static bool IsCompilerGeneratedClosureType(Mono.Cecil.TypeDefinition typeDef)
    {
        if (typeDef == null) return false;
        var name = typeDef.Name;
        // C# compiler-generated:
        //   <>c__DisplayClass, <>c — closure/display classes (delegate invocation)
        //   <MethodName>d__N — async/iterator state machines (constrained call from builder)
        // These types have methods invoked indirectly, not through tracked callvirt/call.
        return name.StartsWith("<>") || name.Contains("__DisplayClass") || name.Contains(">d__");
    }

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

    /// <summary>
    /// Build a type parameter map for a generic type, including parent type params for nested types.
    /// For nested types like AsyncTaskMethodBuilder`1/AsyncStateMachineBox`1, the GenericParameters
    /// on the nested type only includes its own params, but GenericInstanceType.GenericArguments
    /// includes ALL params (parent + nested). This method collects params from the full chain.
    /// </summary>
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
        // Determine which companion types are needed for AOT.
        // CreateDefaultComparer/CreateDefaultEqualityComparer use MakeGenericType +
        // CreateInstanceForAnotherGenericParameter at runtime, so we must pre-generate
        // all possible result types for the given T.
        string[]? companionOpenNames = openTypeName switch
        {
            "System.Collections.Generic.Comparer`1" => new[] {
                "System.Collections.Generic.ObjectComparer`1",
                "System.Collections.Generic.GenericComparer`1",
                "System.IComparable`1",  // needed by MakeGenericType check
            },
            "System.Collections.Generic.EqualityComparer`1" => new[] {
                "System.Collections.Generic.ObjectEqualityComparer`1",
                "System.Collections.Generic.GenericEqualityComparer`1",
                "System.IEquatable`1",  // needed by MakeGenericType check
            },
            _ => null
        };
        if (companionOpenNames == null) return;

        foreach (var companionOpenName in companionOpenNames)
            EnsureGenericCompanionInstantiation(companionOpenName, typeArgs);
    }

    private void EnsureGenericCompanionInstantiation(string companionOpenName, List<string> typeArgs)
    {
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

        // Skip methods that reference SIMD types in parameters, locals, or body instructions.
        // E.g., IndexOfAnyVectorized<Negate, Default> takes Vector256<byte>* parameter —
        // accessing fields on the opaque SIMD struct causes C++ errors.
        if (ReferencesSimdTypes(cecilMethod))
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

            // For abstract interface methods (static abstract/virtual), resolve to the
            // implementing type's concrete method body. This handles constrained calls like:
            //   constrained. Byte call INumberBase<Byte>.TryConvertToChecked<Int32>(...)
            var bodyMethod = cecilMethod;
            if (cecilMethod.IsAbstract && cecilMethod.DeclaringType.IsInterface)
            {
                bodyMethod = ResolveInterfaceMethodImplementation(cecilMethod, declaringIrType, typeParamMap);
                if (bodyMethod != null && bodyMethod.HasBody)
                    irMethod.IsAbstract = false; // Resolved to concrete implementation
            }

            // If body comes from a different method (e.g. resolved interface implementation),
            // rebuild locals from the actual body method since it may have different/more locals.
            if (bodyMethod != null && bodyMethod != cecilMethod && bodyMethod.HasBody)
            {
                irMethod.Locals.Clear();
                var localParamMap = BuildMethodLevelGenericParamMap(bodyMethod, typeParamMap);
                foreach (var localDef in bodyMethod.Body.Variables)
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

            // Convert method body with generic substitution context
            if (bodyMethod != null && bodyMethod.HasBody)
            {
                // Skip methods with CLR-internal dependencies — generate stub instead
                if (HasClrInternalDependencies(bodyMethod))
                {
                    GenerateStubBody(irMethod);
                }
                else
                {
                    // Pre-scan: discover generic types referenced in the body that need to
                    // exist as IRTypes before body conversion. E.g., Array.Sort<String> body
                    // calls IArraySortHelper<String>.Sort() — the interface type must be in
                    // _typeCache for virtual dispatch resolution in EmitMethodCall.
                    EnsureBodyReferencedTypesExist(bodyMethod, typeParamMap);

                    var methodInfo = new IL.MethodInfo(bodyMethod);
                    ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
                }
            }
    }

    /// <summary>
    /// Resolve an abstract interface method to its implementing type's concrete method.
    /// For generic interfaces like INumberBase&lt;Byte&gt;, the implementing type is typically
    /// one of the generic arguments (e.g., Byte implements INumberBase&lt;Byte&gt;).
    /// </summary>
    private Mono.Cecil.MethodDefinition? ResolveInterfaceMethodImplementation(
        Mono.Cecil.MethodDefinition interfaceMethod,
        IRType declaringIrType,
        Dictionary<string, string> typeParamMap)
    {
        // The declaring type is a generic interface specialization like INumberBase<Byte>.
        // Find the implementing type by checking each generic argument's type definition
        // for explicit interface implementations of this method.
        var interfaceTypeDef = interfaceMethod.DeclaringType;

        // Collect candidate implementing types from the generic arguments
        var candidateTypeNames = new List<string>();
        if (declaringIrType.GenericArguments != null)
            candidateTypeNames.AddRange(declaringIrType.GenericArguments);

        foreach (var candidateName in candidateTypeNames)
        {
            // Resolve the candidate type to a Cecil TypeDefinition
            Mono.Cecil.TypeDefinition? candidateTypeDef = null;
            foreach (var asm in _assemblySet.LoadedAssemblies.Values)
            {
                candidateTypeDef = asm.MainModule.GetType(candidateName);
                if (candidateTypeDef != null) break;
            }
            if (candidateTypeDef == null) continue;

            // Search for explicit interface method implementations (overrides)
            foreach (var method in candidateTypeDef.Methods)
            {
                if (!method.HasOverrides) continue;
                foreach (var ovr in method.Overrides)
                {
                    var ovrDeclaring = ovr.DeclaringType;
                    if (ovrDeclaring is GenericInstanceType ovrGit)
                        ovrDeclaring = ovrGit.ElementType;

                    if (ovrDeclaring.FullName == interfaceTypeDef.FullName
                        && ovr.Name == interfaceMethod.Name
                        && ovr.Parameters.Count == interfaceMethod.Parameters.Count)
                    {
                        return method;
                    }
                }
            }
        }

        return null;
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

        // Constrained static abstract interface method resolution:
        // When a generic method body has "constrained. !!T call IFace<!!T>::Method()",
        // and !!T resolves to a concrete type (e.g., int32), we must ensure the concrete
        // implementation's body is compiled. The reachability analyzer can't resolve these
        // because !!T is unresolved during open-method analysis.
        EnsureConstrainedMethodBodiesExist(cecilMethod, typeParamMap);

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
    /// Scan a generic method body for constrained. + call/callvirt patterns and ensure
    /// the resolved concrete method implementations have compiled bodies.
    /// This handles the case where the reachability analyzer can't resolve constrained
    /// calls with generic type parameters (!!T) that only become concrete during specialization.
    /// </summary>
    private void EnsureConstrainedMethodBodiesExist(MethodDefinition cecilMethod, Dictionary<string, string> typeParamMap)
    {
        if (!cecilMethod.HasBody) return;

        foreach (var instr in cecilMethod.Body.Instructions)
        {
            if (instr.OpCode.Code != Code.Constrained) continue;
            var constrainedTypeRef = instr.Operand as TypeReference;
            if (constrainedTypeRef == null) continue;

            var nextInstr = instr.Next;
            if (nextInstr == null) continue;
            if (nextInstr.OpCode.Code is not (Code.Call or Code.Callvirt)) continue;
            var methodRef = nextInstr.Operand as MethodReference;
            if (methodRef == null) continue;

            // Only handle static abstract interface method calls.
            // Instance virtual constrained calls (e.g., Object.GetHashCode) are handled
            // by the normal virtual dispatch mechanism and don't need body compilation.
            if (methodRef.HasThis) continue;
            try
            {
                var declType = methodRef.DeclaringType;
                if (declType is GenericInstanceType git) declType = git.ElementType;
                var resolved = declType.Resolve();
                if (resolved == null || !resolved.IsInterface) continue;
            }
            catch { continue; }

            // Resolve the constrained type through the generic type parameter map
            var resolvedTypeName = ResolveGenericTypeName(constrainedTypeRef, typeParamMap);
            if (resolvedTypeName.StartsWith("!!") || resolvedTypeName.StartsWith("!")) continue; // Still unresolved

            // Find the IRType for the resolved constrained type
            var irType = _typeCache.GetValueOrDefault(resolvedTypeName);
            if (irType == null)
            {
                // The constrained type is not in the module — e.g., marker structs like
                // DontNegate/Negate used only as generic type arguments for constrained
                // static abstract interface dispatch. Create the type on demand.
                irType = CreateDemandDrivenConstrainedType(constrainedTypeRef, resolvedTypeName);
                if (irType == null) continue;
            }

            // Skip non-value types (constrained calls on reference types use virtual dispatch).
            if (!irType.IsValueType) continue;
            // Skip non-primitive types that have aliases to C++ primitives (e.g., Utf16Char → char16_t).
            // Their method bodies construct the IL struct type, but the return type maps to the
            // primitive alias, causing C++ type mismatches. Standard primitives (Int32 → int32_t)
            // are fine because their methods use the primitive type consistently.
            // Generic instance types (containing '<') are never aliased — they always use
            // MangleGenericInstanceTypeName which may differ from MangleTypeName only by trailing '_'.
            if (!irType.IsPrimitiveType && !resolvedTypeName.Contains('<'))
            {
                var mappedCppName = CppNameMapper.GetCppTypeName(resolvedTypeName);
                var mangledName = CppNameMapper.MangleTypeName(resolvedTypeName);
                if (mappedCppName != mangledName) continue; // Has a non-standard alias
            }

            // Find the implementing method on the constrained type
            var targetMethodName = methodRef.Name;
            var targetParamCount = methodRef.Parameters.Count;

            // First, try to find an existing IRMethod shell (added by CreateGenericSpecializations)
            IRMethod? targetIrMethod = null;
            foreach (var irMethod in irType.Methods)
            {
                if (irMethod.BasicBlocks.Count > 0) continue; // Already has body
                if (irMethod.Parameters.Count != targetParamCount) continue;
                if (irMethod.IsInternalCall || irMethod.HasICallMapping) continue;

                // Match by name — exact match or explicit interface impl suffix
                if (MatchesConstrainedMethodName(irMethod.Name, targetMethodName))
                {
                    targetIrMethod = irMethod;
                    break;
                }
            }

            // Resolve Cecil type definition for body compilation.
            var cecilTypeDef = ResolveCecilTypeDefinition(constrainedTypeRef, resolvedTypeName);
            if (cecilTypeDef == null) continue;

            // Find the matching Cecil method
            MethodDefinition? cecilTargetMethod = null;
            foreach (var m in cecilTypeDef.Methods)
            {
                if (m.Parameters.Count != targetParamCount) continue;
                if (!MatchesConstrainedMethodName(m.Name, targetMethodName)) continue;
                // Skip generic method definitions — they require full specialization through
                // the generic method pipeline, not direct body compilation. TryConvertToChecked<TOther>
                // etc. have unresolvable method-level generic params in this context.
                if (m.HasGenericParameters) continue;
                // Skip SIMD overloads — e.g., NegateIfNeeded(Vector128<ushort>) has the same
                // param count as NegateIfNeeded(bool). Without this filter, the SIMD overload
                // may be found first, then rejected later by ReferencesSimdTypes, causing the
                // correct non-SIMD overload to never be compiled.
                if (ReferencesSimdTypes(m)) continue;
                if (m.HasBody)
                {
                    cecilTargetMethod = m;
                    break;
                }
            }
            // If not found on the type, search interface hierarchy for Default Interface
            // Methods (DIM). E.g., Char doesn't override INumber<Char>.Max — the default
            // implementation from INumber<T> should be used.
            if (cecilTargetMethod == null)
            {
                cecilTargetMethod = FindDefaultInterfaceMethod(
                    cecilTypeDef, methodRef, targetMethodName, targetParamCount);
            }
            if (cecilTargetMethod == null)
                continue;

            // Build a type parameter map that includes interface generic params.
            // When a value type like Char implements INumber<Char>, the Cecil method body
            // for Char.Max(TSelf, TSelf) uses `TSelf` from the interface. We need to map
            // TSelf → System.Char so parameter types resolve correctly.
            var effectiveParamMap = BuildConstrainedTypeParamMap(
                cecilTypeDef, cecilTargetMethod, typeParamMap);

            // If no IRMethod shell exists, create one. This handles static abstract interface
            // implementations that CreateGenericSpecializations skipped because they're static
            // (not virtual/constructor) and not in _calledSpecializedMethods.
            if (targetIrMethod == null)
            {
                // Use irType.CppName (from MangleGenericInstanceTypeName) rather than
                // MangleTypeName(resolvedTypeName) to ensure consistent method names
                // with shells created at emit time.
                var returnTypeName = ResolveGenericTypeName(cecilTargetMethod.ReturnType, effectiveParamMap);
                var newCppName = CppNameMapper.MangleMethodName(irType.CppName, cecilTargetMethod.Name);
                targetIrMethod = new IRMethod
                {
                    Name = cecilTargetMethod.Name,
                    CppName = newCppName,
                    DeclaringType = irType,
                    ReturnTypeCpp = ResolveTypeForDecl(returnTypeName),
                    IsStatic = cecilTargetMethod.IsStatic,
                };
                foreach (var paramDef in cecilTargetMethod.Parameters)
                {
                    var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, effectiveParamMap);
                    targetIrMethod.Parameters.Add(new IRParameter
                    {
                        Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                        CppName = CppNameMapper.MangleIdentifier(
                            paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}"),
                        CppTypeName = ResolveTypeForDecl(paramTypeName),
                        ILTypeName = paramTypeName,
                        Index = paramDef.Index,
                    });
                }
                // Disambiguate overloaded method names
                var existingWithSameName = irType.Methods.Count(m => m.CppName == targetIrMethod.CppName);
                if (existingWithSameName > 0)
                {
                    var paramSuffix = string.Join("_", targetIrMethod.Parameters.Select(p =>
                        CppNameMapper.MangleTypeName(p.ILTypeName ?? p.CppTypeName)));
                    targetIrMethod.CppName = $"{targetIrMethod.CppName}__{paramSuffix}";
                }
                irType.Methods.Add(targetIrMethod);
            }

            // Skip if already compiled or shouldn't be compiled
            if (targetIrMethod.BasicBlocks.Count > 0) continue;
            // Only skip reachable methods if their body was compiled under THIS shell's name.
            // When an explicit interface impl (e.g., System.Numerics.INumber<Char>.Max) is
            // reachable and compiled under the full interface name, the constrained call's
            // short-name shell (System_Char_Max) still needs its body compiled.
            if (_reachability.IsReachable(cecilTargetMethod))
            {
                // Check if the existing compiled method has the SAME CppName as this shell.
                // If not, the reachable version was compiled under a different name and this
                // shell needs its own body.
                var existingCompiled = irType.Methods.FirstOrDefault(m =>
                    m.BasicBlocks.Count > 0 &&
                    MatchesConstrainedMethodName(m.Name, cecilTargetMethod.Name) &&
                    m.Parameters.Count == targetIrMethod.Parameters.Count);
                if (existingCompiled != null && existingCompiled.CppName == targetIrMethod.CppName)
                    continue;
                // Fall through to compile the body for this shell
            }
            if (HasClrInternalDependencies(cecilTargetMethod)) continue;
            if (ReferencesSimdTypes(cecilTargetMethod)) continue;

            // Build locals from Cecil method body
            targetIrMethod.Locals.Clear();
            foreach (var localDef in cecilTargetMethod.Body.Variables)
            {
                var localTypeName = ResolveGenericTypeName(localDef.VariableType, effectiveParamMap);
                targetIrMethod.Locals.Add(new IRLocal
                {
                    Index = localDef.Index,
                    CppName = $"loc_{localDef.Index}",
                    CppTypeName = ResolveTypeForDecl(localTypeName),
                });
            }

            // Compile the body (use generic variant if effectiveParamMap is non-empty
            // to resolve type parameters like TSelf → Char in the IL)
            var methodInfo = new IL.MethodInfo(cecilTargetMethod);
            if (effectiveParamMap.Count > 0)
                ConvertMethodBodyWithGenerics(methodInfo, targetIrMethod, effectiveParamMap);
            else
                ConvertMethodBody(methodInfo, targetIrMethod);

            // Discover secondary generic dependencies in the compiled body
            EnsureBodyReferencedTypesExist(cecilTargetMethod, effectiveParamMap);

            // Compile transitive unreachable callees
            CompileTransitiveUnreachableCallees(cecilTargetMethod);
        }
    }

    /// <summary>
    /// Build a type parameter map for a constrained method that includes interface generic params.
    /// When a type like Char implements INumber&lt;Char&gt;, the Cecil method Char.Max(TSelf, TSelf)
    /// uses TSelf from the interface. This method maps interface generic params (TSelf → System.Char)
    /// by scanning the constrained type's interface implementations.
    /// </summary>
    private Dictionary<string, string> BuildConstrainedTypeParamMap(
        TypeDefinition cecilTypeDef,
        MethodDefinition cecilTargetMethod,
        Dictionary<string, string> callerTypeParamMap)
    {
        var result = new Dictionary<string, string>(callerTypeParamMap);

        // Scan the method's Overrides to find which interface method this implements
        foreach (var ov in cecilTargetMethod.Overrides)
        {
            var ifaceRef = ov.DeclaringType;
            if (ifaceRef is GenericInstanceType ifaceGit)
            {
                // Map each generic parameter of the open interface to its concrete arg
                var openIface = ifaceGit.ElementType.Resolve();
                if (openIface?.HasGenericParameters == true)
                {
                    for (int i = 0; i < openIface.GenericParameters.Count && i < ifaceGit.GenericArguments.Count; i++)
                    {
                        var gpName = openIface.GenericParameters[i].Name; // e.g., "TSelf"
                        var concreteArg = ifaceGit.GenericArguments[i]; // e.g., System.Char
                        var resolvedName = ResolveGenericTypeName(concreteArg, callerTypeParamMap);
                        if (!result.ContainsKey(gpName))
                            result[gpName] = resolvedName;
                        // Also map by position: !0, !1, etc.
                        var posKey = $"!{i}";
                        if (!result.ContainsKey(posKey))
                            result[posKey] = resolvedName;
                    }
                }
            }
        }

        // If no overrides found, scan interface implementations of the declaring type
        // and try to match by method parameter types
        if (result.Count == callerTypeParamMap.Count)
        {
            foreach (var ifaceImpl in cecilTypeDef.Interfaces)
            {
                if (ifaceImpl.InterfaceType is GenericInstanceType ifaceGit2)
                {
                    var openIface = ifaceGit2.ElementType.Resolve();
                    if (openIface?.HasGenericParameters == true)
                    {
                        for (int i = 0; i < openIface.GenericParameters.Count && i < ifaceGit2.GenericArguments.Count; i++)
                        {
                            var gpName = openIface.GenericParameters[i].Name;
                            var concreteArg = ifaceGit2.GenericArguments[i];
                            var resolvedName = ResolveGenericTypeName(concreteArg, callerTypeParamMap);
                            if (!result.ContainsKey(gpName))
                                result[gpName] = resolvedName;
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Search the constrained type's interface hierarchy for a Default Interface Method (DIM).
    /// When a value type like Char doesn't override INumber&lt;Char&gt;.Max(), the default
    /// implementation from INumber&lt;T&gt; provides the method body.
    /// </summary>
    private MethodDefinition? FindDefaultInterfaceMethod(
        TypeDefinition constrainedTypeDef,
        MethodReference methodRef,
        string targetMethodName,
        int targetParamCount)
    {
        // Get the declaring interface of the method reference
        var declType = methodRef.DeclaringType;
        TypeDefinition? ifaceTypeDef = null;
        if (declType is GenericInstanceType git)
            ifaceTypeDef = git.ElementType.Resolve();
        else
            ifaceTypeDef = declType.Resolve();

        if (ifaceTypeDef == null) return null;

        // Search the interface for a matching method with a body (DIM)
        foreach (var m in ifaceTypeDef.Methods)
        {
            if (m.Parameters.Count != targetParamCount) continue;
            if (m.Name != targetMethodName) continue;
            if (m.HasGenericParameters) continue;
            if (m.IsAbstract) continue; // Abstract methods have no DIM body
            if (m.HasBody)
                return m;
        }

        // Also search parent interfaces
        foreach (var ifaceImpl in ifaceTypeDef.Interfaces)
        {
            var parentIface = ifaceImpl.InterfaceType.Resolve();
            if (parentIface == null) continue;
            foreach (var m in parentIface.Methods)
            {
                if (m.Parameters.Count != targetParamCount) continue;
                if (m.Name != targetMethodName) continue;
                if (m.HasGenericParameters) continue;
                if (m.IsAbstract) continue;
                if (m.HasBody)
                    return m;
            }
        }

        return null;
    }

    /// <summary>
    /// Match method names for constrained call resolution. Handles exact match
    /// and explicit interface implementation names (suffix after last '.').
    /// </summary>
    private static bool MatchesConstrainedMethodName(string methodName, string targetName)
    {
        if (methodName == targetName) return true;
        var lastDot = methodName.LastIndexOf('.');
        return lastDot >= 0 && methodName.AsSpan(lastDot + 1).SequenceEqual(targetName);
    }

    /// <summary>
    /// Create a minimal IRType for a value type that is only used as a constrained call target.
    /// Marker structs like DontNegate/Negate are used as generic type arguments for constrained
    /// static abstract interface dispatch but are never directly referenced in reachable method
    /// bodies, so the reachability analyzer misses them. This creates the type on demand with
    /// method shells for the static methods needed by constrained call resolution.
    /// </summary>
    private IRType? CreateDemandDrivenConstrainedType(TypeReference constrainedTypeRef, string resolvedTypeName)
    {
        // SIMD container types are opaque stubs — never create demand-driven constrained types for them.
        // Their constrained static abstract methods (ISimdVector, IAdditionOperators, etc.) are dead code.
        var openName = resolvedTypeName.Contains('<')
            ? resolvedTypeName[..resolvedTypeName.IndexOf('<')]
            : resolvedTypeName;
        if (IsSimdContainerType(openName)) return null;

        var cecilTypeDef = ResolveCecilTypeDefinition(constrainedTypeRef, resolvedTypeName);
        if (cecilTypeDef == null || !cecilTypeDef.IsValueType) return null;

        var mangledName = CppNameMapper.MangleTypeName(resolvedTypeName);
        var irType = new IRType
        {
            ILFullName = resolvedTypeName,
            CppName = mangledName,
            Name = cecilTypeDef.Name,
            Namespace = cecilTypeDef.Namespace,
            IsValueType = true,
            IsSealed = cecilTypeDef.IsSealed,
            MetadataToken = cecilTypeDef.MetadataToken.ToUInt32(),
            SourceKind = _assemblySet.ClassifyAssembly(cecilTypeDef.Module.Assembly.Name.Name),
        };

        // Add instance fields (for correct struct layout)
        foreach (var fieldDef in cecilTypeDef.Fields)
        {
            if (fieldDef.IsStatic) continue;
            irType.Fields.Add(new IRField
            {
                Name = fieldDef.Name,
                CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                FieldTypeName = fieldDef.FieldType.FullName,
                IsPublic = fieldDef.IsPublic,
                DeclaringType = irType,
            });
        }

        // Create method shells for static methods (constrained static abstract calls)
        foreach (var methodDef in cecilTypeDef.Methods)
        {
            if (!methodDef.IsStatic || !methodDef.HasBody) continue;
            // Skip generic method definitions (unresolvable !!0 params)
            if (methodDef.HasGenericParameters) continue;
            // Skip SIMD overloads (e.g., NegateIfNeeded(Vector128<byte>))
            if (ReferencesSimdTypes(methodDef)) continue;

            var returnTypeName = methodDef.ReturnType.FullName;
            var irMethod = new IRMethod
            {
                Name = methodDef.Name,
                CppName = CppNameMapper.MangleMethodName(mangledName, methodDef.Name),
                DeclaringType = irType,
                ReturnTypeCpp = ResolveTypeForDecl(returnTypeName),
                IsStatic = true,
            };

            foreach (var paramDef in methodDef.Parameters)
            {
                irMethod.Parameters.Add(new IRParameter
                {
                    Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                    CppName = CppNameMapper.MangleIdentifier(
                        paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}"),
                    CppTypeName = ResolveTypeForDecl(paramDef.ParameterType.FullName),
                    ILTypeName = paramDef.ParameterType.FullName,
                    Index = paramDef.Index,
                });
            }

            irType.Methods.Add(irMethod);
        }

        // Disambiguate overloaded method names (e.g., multiple NegateIfNeeded overloads)
        var methodGroups = irType.Methods.GroupBy(m => m.CppName).Where(g => g.Count() > 1);
        foreach (var group in methodGroups)
        {
            foreach (var m in group)
            {
                var paramSuffix = string.Join("_", m.Parameters.Select(p =>
                    CppNameMapper.MangleTypeName(p.ILTypeName ?? p.CppTypeName)));
                m.CppName = $"{m.CppName}__{paramSuffix}";
            }
        }

        CalculateInstanceSize(irType);
        _module.Types.Add(irType);
        _typeCache[resolvedTypeName] = irType;

        return irType;
    }

    /// <summary>
    /// Recursively compile bodies of non-reachable methods called from a newly compiled body.
    /// This handles transitive reachability gaps from constrained call chains:
    /// e.g. Byte.Max (constrained, compiled above) → Math.Max(byte,byte) (unreachable, stub).
    /// Without this, the callee remains a default-return stub causing runtime errors.
    /// </summary>
    private void CompileTransitiveUnreachableCallees(MethodDefinition cecilMethod)
    {
        if (!cecilMethod.HasBody) return;

        var worklist = new Queue<MethodDefinition>();
        var visited = new HashSet<string>();
        worklist.Enqueue(cecilMethod);
        visited.Add(cecilMethod.FullName);

        while (worklist.Count > 0)
        {
            var current = worklist.Dequeue();
            foreach (var instr in current.Body.Instructions)
            {
                if (instr.OpCode.Code is not (Code.Call or Code.Callvirt)) continue;
                if (instr.Operand is not MethodReference calledRef) continue;
                // Skip generic method calls — handled by CollectGenericMethod/CreateGenericMethodSpecializations
                if (calledRef is GenericInstanceMethod) continue;
                // Skip calls on generic types — handled by generic specialization pipeline
                if (calledRef.DeclaringType is GenericInstanceType) continue;

                MethodDefinition? calledDef;
                try { calledDef = calledRef.Resolve(); }
                catch { continue; }
                if (calledDef == null || !calledDef.HasBody) continue;
                if (_reachability.IsReachable(calledDef)) continue;
                if (!visited.Add(calledDef.FullName)) continue;
                if (HasClrInternalDependencies(calledDef)) continue;
                if (ReferencesSimdTypes(calledDef)) continue;

                // Find the corresponding IRMethod (match by name + parameter types)
                var declTypeName = calledDef.DeclaringType.FullName;
                if (!_typeCache.TryGetValue(declTypeName, out var declIrType)) continue;

                IRMethod? targetIrMethod = null;
                foreach (var m in declIrType.Methods)
                {
                    if (m.BasicBlocks.Count > 0) continue; // Already has body
                    if (m.Name != calledDef.Name) continue;
                    if (m.Parameters.Count != calledDef.Parameters.Count) continue;
                    // Match parameter types (IL types) to identify the correct overload
                    bool paramsMatch = true;
                    for (int i = 0; i < calledDef.Parameters.Count; i++)
                    {
                        var expectedILType = calledDef.Parameters[i].ParameterType.FullName;
                        if (m.Parameters[i].ILTypeName != expectedILType)
                        {
                            paramsMatch = false;
                            break;
                        }
                    }
                    if (paramsMatch) { targetIrMethod = m; break; }
                }
                if (targetIrMethod == null) continue;

                // Build locals and compile
                targetIrMethod.Locals.Clear();
                foreach (var localDef in calledDef.Body.Variables)
                {
                    targetIrMethod.Locals.Add(new IRLocal
                    {
                        Index = localDef.Index,
                        CppName = $"loc_{localDef.Index}",
                        CppTypeName = ResolveTypeForDecl(
                            ResolveGenericTypeName(localDef.VariableType, new Dictionary<string, string>())),
                    });
                }

                var methodInfo = new IL.MethodInfo(calledDef);
                ConvertMethodBody(methodInfo, targetIrMethod);

                // Queue this method for transitive scanning
                worklist.Enqueue(calledDef);
            }
        }
    }

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

            // Safety net: skip SIMD container types even if they leaked into _genericInstantiations
            if (IsSimdContainerType(info.OpenTypeName)) continue;

            var openType = info.CecilOpenType;

            // Build type parameter map
            var typeParamMap = BuildTypeParamMap(openType, info.TypeArguments);

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
                IsByRefLike = openType.CustomAttributes.Any(
                    a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute"),
                MetadataToken = openType.MetadataToken.ToUInt32(),
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

                    // Propagate explicit interface overrides (.override directive)
                    // Needed for BuildInterfaceImpls (Pass 5) to match interface methods
                    if (methodDef.HasOverrides)
                    {
                        foreach (var ovr in methodDef.Overrides)
                        {
                            var resolvedTypeName = ResolveGenericTypeName(ovr.DeclaringType, typeParamMap);
                            irMethod.ExplicitOverrides.Add((resolvedTypeName, ovr.Name));
                        }
                    }

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
                        else if (IsCompilerGeneratedClosureType(info.CecilOpenType))
                        {
                            // Compiler-generated display class / closure types (<>c, __DisplayClass)
                            // have methods invoked through delegates, not direct callvirt/call.
                            // _calledSpecializedMethods won't track them, so always compile.
                            shouldCompile = true;
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

            // For already-resolved types, re-check missing base types and interfaces
            // (they may have been created by a later fixpoint iteration)
            if (alreadyResolved)
            {
                var typeParamMap2 = BuildTypeParamMap(info.CecilOpenType, info.TypeArguments);
                if (irType.BaseType == null && info.CecilOpenType.BaseType != null && !irType.IsValueType)
                {
                    var baseName2 = ResolveGenericTypeName(info.CecilOpenType.BaseType, typeParamMap2);
                    if (_typeCache.TryGetValue(baseName2, out var baseType2))
                    {
                        irType.BaseType = baseType2;
                        CalculateInstanceSize(irType);
                    }
                }
                // Re-check interfaces — some may have been created after initial resolution
                var existingIfaceNames = new HashSet<string>(irType.Interfaces.Select(i => i.ILFullName));
                foreach (var iface in info.CecilOpenType.Interfaces)
                {
                    var ifaceName2 = ResolveGenericTypeName(iface.InterfaceType, typeParamMap2);
                    if (!existingIfaceNames.Contains(ifaceName2) && _typeCache.TryGetValue(ifaceName2, out var ifaceType2))
                        irType.Interfaces.Add(ifaceType2);
                }
                continue;
            }

            var openType = info.CecilOpenType;
            var typeParamMap = BuildTypeParamMap(openType, info.TypeArguments);

            // Base type — auto-discover generic base types for demand-driven pipeline
            if (openType.BaseType != null && !irType.IsValueType)
            {
                var baseName = ResolveGenericTypeName(openType.BaseType, typeParamMap);
                if (_typeCache.TryGetValue(baseName, out var baseType))
                    irType.BaseType = baseType;
                else if (openType.BaseType is GenericInstanceType)
                    TryCollectResolvedGenericType(openType.BaseType, typeParamMap);
            }

            // Interfaces — resolve and add all interfaces from Cecil metadata.
            // If the interface type already exists in cache, always add it (type correctness).
            // Only use dispatch filtering to gate *creation* of new interface types.
            foreach (var iface in openType.Interfaces)
            {
                var ifaceName = ResolveGenericTypeName(iface.InterfaceType, typeParamMap);
                if (_typeCache.TryGetValue(ifaceName, out var ifaceType))
                {
                    irType.Interfaces.Add(ifaceType);
                }
                else if (iface.InterfaceType is GenericInstanceType ifaceGit)
                {
                    // Only auto-discover new generic interface types if dispatched on
                    var openName = ifaceGit.ElementType.FullName;
                    if (_dispatchedInterfaces.Contains(openName))
                        TryCollectResolvedGenericType(iface.InterfaceType, typeParamMap);
                }
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

            var typeParamMap = BuildTypeParamMap(openType, info.TypeArguments);

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
                IsByRefLike = openType.CustomAttributes.Any(
                    a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute"),
                MetadataToken = openType.MetadataToken.ToUInt32(),
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
        // Use backward scan to find the correct generic '<', skipping compiler-generated '<>'
        var backtickIdx = resolvedTypeName.IndexOf('`');
        var ltIdx = backtickIdx > 0
            ? CppNameMapper.FindGenericArgsOpen(resolvedTypeName, backtickIdx)
            : CppNameMapper.FindGenericArgsOpen(resolvedTypeName);
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

        // All SIMD code is dead (IsHardwareAccelerated=false) — skip ALL SIMD container types.
        if (IsSimdContainerType(openTypeName))
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

                // For nested generic types, openType.GenericParameters only has the type's own
                // params, but git.GenericArguments has ALL params (parent + nested).
                // Walk the declaring chain when counts don't match.
                var genericParams = openType.GenericParameters.Count == git.GenericArguments.Count
                    ? (IList<GenericParameter>)openType.GenericParameters
                    : CollectAllGenericParameters(openType);
                for (int i = 0; i < genericParams.Count && i < git.GenericArguments.Count; i++)
                {
                    var paramName = genericParams[i].Name;
                    var argResolved = ResolveTypeRefOperand(git.GenericArguments[i]);
                    localMap[paramName] = argResolved;
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

            // If new generic types were discovered during body compilation
            // (e.g., display classes, closures referenced in locals/instructions),
            // create their type shells and method shells now. This ensures
            // compiler-generated types get their methods compiled in subsequent batches.
            if (_genericInstantiations.Count > _resolvedGenericTypeKeys.Count)
            {
                CreateGenericSpecializations();
                int prevNested;
                do
                {
                    prevNested = _genericInstantiations.Count;
                    CreateNestedGenericSpecializations();
                } while (_genericInstantiations.Count > prevNested);
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
            var typeArgs = git.GenericArguments.Select(a => ResolveCacheKey(a)).ToList();
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
            var angleBracket = CppNameMapper.FindGenericArgsOpen(key, backtickIdx);
            if (angleBracket > 0)
            {
                var openTypeName = key[..angleBracket];
                var argsStr = key[(angleBracket + 1)..^1];
                var args = CppNameMapper.ParseGenericArgs(argsStr);
                var mangledName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, args);
                return mangledName;
            }
        }
        return CppNameMapper.MangleTypeName(key);
    }

    /// <summary>
    /// Look up the canonical mangled C++ name for an IL type name string.
    /// First checks _typeCache for the IRType's CppName (matches header struct definition).
    /// Then tries MangleGenericInstanceTypeName for generic types (avoids trailing underscore).
    /// Falls back to MangleTypeNameClean for non-generic types.
    /// </summary>
    private string LookupMangledTypeName(string ilTypeName)
    {
        if (_typeCache.TryGetValue(ilTypeName, out var irType))
            return irType.CppName;
        // For generic instance types, use the same split-and-mangle approach as GetMangledTypeNameForRef
        var backtickIdx = ilTypeName.IndexOf('`');
        if (backtickIdx > 0 && ilTypeName.Contains('<'))
        {
            var angleBracket = CppNameMapper.FindGenericArgsOpen(ilTypeName, backtickIdx);
            if (angleBracket > 0)
            {
                var openTypeName = ilTypeName[..angleBracket];
                var argsStr = ilTypeName[(angleBracket + 1)..^1];
                var args = CppNameMapper.ParseGenericArgs(argsStr);
                return CppNameMapper.MangleGenericInstanceTypeName(openTypeName, args);
            }
        }
        return CppNameMapper.MangleTypeNameClean(ilTypeName);
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
            var angleBracket = CppNameMapper.FindGenericArgsOpen(ilTypeName, backtickIdx);
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
        // The type itself satisfies its own constraint (e.g. SafeHandle : SafeHandle)
        if (ConstraintNameMatches(argType.FullName, constraintTypeName))
            return true;

        // Check direct interface implementation (exact match or open generic match)
        if (argType.Interfaces.Any(i => ConstraintNameMatches(i.InterfaceType.FullName, constraintTypeName)))
            return true;

        // Walk base type chain
        var baseType = argType.BaseType;
        while (baseType != null)
        {
            if (ConstraintNameMatches(baseType.FullName, constraintTypeName)) return true;
            // Check interfaces of base type
            try
            {
                var baseDef = baseType.Resolve();
                if (baseDef == null) break;
                if (baseDef.Interfaces.Any(i => ConstraintNameMatches(i.InterfaceType.FullName, constraintTypeName)))
                    return true;
                baseType = baseDef.BaseType;
            }
            catch { break; }
        }

        return false;
    }

    /// <summary>
    /// Match constraint type names, handling generic type name variations.
    /// For generic constraints like "System.Numerics.INumber`1", also matches
    /// closed generic forms like "System.Numerics.INumber`1&lt;System.Int32&gt;".
    /// Also handles modreq/modopt suffixes that Cecil appends to some constraint names.
    /// </summary>
    private static bool ConstraintNameMatches(string typeName, string constraintName)
    {
        if (typeName == constraintName) return true;

        // Generic open type match: constraint "Ns.IFace`1" matches type "Ns.IFace`1<Arg>"
        // Extract the open generic prefix (up to and including backtick + arity)
        int backTick = constraintName.IndexOf('`');
        if (backTick >= 0)
        {
            // Find end of arity digits after backtick
            int arityEnd = backTick + 1;
            while (arityEnd < constraintName.Length && char.IsDigit(constraintName[arityEnd]))
                arityEnd++;
            var openPrefix = constraintName.Substring(0, arityEnd);
            // Type matches if it starts with the same open prefix
            if (typeName.StartsWith(openPrefix, StringComparison.Ordinal))
                return true;
        }

        // modreq/modopt suffix: constraint may be "System.ValueType modreq(...)"
        // Strip modreq/modopt and retry
        if (constraintName.Contains(" modreq(") || constraintName.Contains(" modopt("))
        {
            int modIdx = constraintName.IndexOf(" mod", StringComparison.Ordinal);
            if (modIdx > 0)
            {
                var stripped = constraintName.Substring(0, modIdx);
                if (typeName == stripped) return true;
                // Also try open generic match on stripped name
                if (ConstraintNameMatches(typeName, stripped)) return true;
            }
        }

        return false;
    }
}
