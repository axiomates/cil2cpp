using Mono.Cecil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Converts IL (from Mono.Cecil) into IR representation.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Well-known vtable slot indices for System.Object virtual methods.
    /// These must match the order used in BuildVTable root seeding and EmitMethodCall.
    /// </summary>
    private static class ObjectVTableSlots
    {
        public const int ToStringSlot = 0;
        public const int EqualsSlot = 1;
        public const int GetHashCodeSlot = 2;
    }

    // Type classification is centralized in RuntimeTypeRegistry.
    // Cached HashSets for backward-compatible callsites (populated once from registry).
    internal static readonly HashSet<string> RuntimeProvidedTypes = new(RuntimeTypeRegistry.GetILNames(RuntimeTypeFlags.RuntimeProvided));
    internal static readonly HashSet<string> CoreRuntimeTypes = new(RuntimeTypeRegistry.GetILNames(RuntimeTypeFlags.CoreRuntime));
    internal static readonly HashSet<string> ReflectionAliasedTypes = new(RuntimeTypeRegistry.GetILNames(RuntimeTypeFlags.ReflectionAliased));
    internal static readonly HashSet<string> SkipAllMethodsTypes = new(RuntimeTypeRegistry.GetILNames(RuntimeTypeFlags.SkipAllMethods));

    /// <summary>
    /// CLR runtime-internal types that cannot be compiled to C++.
    /// Methods whose locals or parameters reference these types get stub bodies.
    /// These depend on CLR internal memory layout (QCall bridge, RuntimeType, etc.).
    /// </summary>
    internal static readonly HashSet<string> ClrInternalTypeNames = new()
    {
        // QCall/P-Invoke bridge types
        "System.Runtime.CompilerServices.QCallTypeHandle",
        "System.Runtime.CompilerServices.QCallAssembly",
        "System.Runtime.CompilerServices.ObjectHandleOnStack",
        "System.Runtime.CompilerServices.MethodTable",
        // Phase I.2: RuntimeType removed — aliased to cil2cpp::Type (same as Unity IL2CPP Il2CppReflectionType)
        // Phase I.3: RuntimeTypeHandle/RuntimeMethodHandle/RuntimeFieldHandle removed — value type thin wrappers (intptr_t)
        // Phase II.3: RuntimeMethodInfo/RuntimeFieldInfo/RuntimeConstructorInfo/TypeInfo removed — aliased to runtime types
        // Phase II.4: RuntimePropertyInfo/Assembly/RuntimeAssembly removed — aliased to new runtime structs
        // CLR-internal reflection types (still blocked)
        "System.Reflection.MetadataImport",
        "System.Reflection.RuntimeCustomAttributeData",
        // RuntimeMethodHandleInternal: CLR-internal struct wrapping method pointer
        "System.RuntimeMethodHandleInternal",
        // Phase II.2: DefaultBinder/DBNull/Signature/ThreadInt64PersistentCounter/IAsyncLocal/PosixSignalRegistration removed — BCL IL compiles
        // Phase II.1: CalendarId/EraInfo removed — BCL enums/structs compile from IL
        // Phase II.5: WaitHandle removed — RuntimeProvided with OS primitive ICalls
    };

    /// <summary>
    /// Check if a Cecil method's body references CLR-internal types that cannot be compiled.
    /// When true, the method body should be replaced with a stub.
    /// Detects: CLR-internal locals/params, BCL compiler-generated generic display classes.
    /// </summary>
    internal static bool HasClrInternalDependencies(Mono.Cecil.MethodDefinition cecilMethod)
        => HasClrInternalDependencies(cecilMethod, out _);

    /// <summary>
    /// Check if a Cecil method's body references CLR-internal types that cannot be compiled.
    /// Returns the specific reason via <paramref name="reason"/> for diagnostics.
    /// </summary>
    internal static bool HasClrInternalDependencies(Mono.Cecil.MethodDefinition cecilMethod, out string? reason)
    {
        reason = null;

        // Check if declaring type is CLR-internal (all methods on these types are unstubable)
        var declType = cecilMethod.DeclaringType;
        if (ClrInternalTypeNames.Contains(declType.FullName))
        {
            reason = $"declaring type '{declType.FullName}'";
            return true;
        }
        // Nested types within CLR-internal types
        if (declType.DeclaringType != null && ClrInternalTypeNames.Contains(declType.DeclaringType.FullName))
        {
            reason = $"nested in CLR-internal type '{declType.DeclaringType.FullName}'";
            return true;
        }

        if (!cecilMethod.HasBody) return false;

        // Check local variable types
        foreach (var local in cecilMethod.Body.Variables)
        {
            var typeName = local.VariableType.FullName;
            if (ClrInternalTypeNames.Contains(typeName))
            {
                reason = $"local type '{typeName}'";
                return true;
            }
            if (local.VariableType is GenericInstanceType git
                && ClrInternalTypeNames.Contains(git.ElementType.FullName))
            {
                reason = $"local generic type '{git.ElementType.FullName}'";
                return true;
            }
        }

        // Check parameter types
        foreach (var param in cecilMethod.Parameters)
        {
            if (ClrInternalTypeNames.Contains(param.ParameterType.FullName))
            {
                reason = $"parameter type '{param.ParameterType.FullName}'";
                return true;
            }
        }

        // Check for field/method references to BCL compiler-generated generic types
        // (display classes like System.Enum.<>c__63<T>) whose specializations may not exist
        foreach (var instr in cecilMethod.Body.Instructions)
        {
            TypeReference? refType = null;
            if (instr.Operand is FieldReference fr) refType = fr.DeclaringType;
            else if (instr.Operand is MethodReference mr) refType = mr.DeclaringType;

            if (refType is GenericInstanceType refGit)
            {
                var elemType = refGit.ElementType;
                // Compiler-generated nested types (<>c__, __DisplayClass) from BCL
                if ((elemType.Name.StartsWith("<>") || elemType.Name.Contains("__DisplayClass"))
                    && elemType.DeclaringType != null)
                {
                    var ns = elemType.DeclaringType.Namespace;
                    if (!string.IsNullOrEmpty(ns) &&
                        (ns.StartsWith("System") || ns.StartsWith("Internal") || ns.StartsWith("Microsoft")))
                    {
                        reason = $"BCL compiler-generated generic '{elemType.FullName}'";
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Generate a minimal stub body for methods that cannot be compiled from IL.
    /// Returns the appropriate default value for the method's return type.
    /// </summary>
    private static void GenerateStubBody(IRMethod irMethod)
    {
        // Special-case: QCall bridge types — generate correct trivial bodies
        if (TryGenerateQCallHelperBody(irMethod))
            return;

        var block = new IRBasicBlock { Id = 0 };

        // Emit stub_called diagnostic (Debug builds print to stderr, Release is no-op)
        var escapedName = (irMethod.CppName ?? irMethod.Name ?? "unknown").Replace("\\", "\\\\").Replace("\"", "\\\"");
        block.Instructions.Add(new IRRawCpp { Code = $"cil2cpp::stub_called(\"{escapedName}\");" });

        string? retVal = null;
        if (irMethod.ReturnTypeCpp != "void")
        {
            retVal = irMethod.ReturnTypeCpp.EndsWith("*") ? "nullptr" : "{}";
        }
        block.Instructions.Add(new IRReturn { Value = retVal });
        irMethod.BasicBlocks.Add(block);
    }

    /// <summary>
    /// Generate correct bodies for trivial QCall/ObjectHandleOnStack wrapper methods.
    /// These are 1-line methods that just store or return a pointer.
    /// </summary>
    private static bool TryGenerateQCallHelperBody(IRMethod irMethod)
    {
        var declType = irMethod.DeclaringType?.ILFullName;
        var name = irMethod.Name;

        string? code = null;

        // QCallTypeHandle.ctor(RuntimeType** type) — store pointer
        if (declType == "System.Runtime.CompilerServices.QCallTypeHandle" && name == ".ctor")
        {
            // Instance method on value type: 'this' is separate, Parameters[0] is the real param
            var paramName = irMethod.Parameters.Count >= 1 ? irMethod.Parameters[0].CppName : "type";
            code = $"__this->f_ptr = (void*){paramName};";
        }
        // ObjectHandleOnStack.Create<T>(T** o) — wrap pointer in struct
        else if (declType == "System.Runtime.CompilerServices.ObjectHandleOnStack" && name == "Create")
        {
            var paramName = irMethod.Parameters.Count >= 1 ? irMethod.Parameters[0].CppName : "o";
            code = $"return {{ (void*){paramName} }};";
        }

        if (code == null) return false;

        var block = new IRBasicBlock { Id = 0 };
        block.Instructions.Add(new IRRawCpp { Code = code });
        if (!code.Contains("return"))
            block.Instructions.Add(new IRReturn());
        irMethod.BasicBlocks.Add(block);
        irMethod.IrStubReason = null; // Clear stub reason — this is a real implementation
        return true;
    }

    /// <summary>
    /// Check if a type derives from System.Diagnostics.Tracing.EventSource.
    /// Walks the base type chain via Cecil metadata.
    /// </summary>
    private static bool IsEventSourceDerived(Mono.Cecil.TypeDefinition? typeDef)
    {
        var current = typeDef?.BaseType;
        while (current != null)
        {
            if (current.FullName == "System.Diagnostics.Tracing.EventSource")
                return true;
            try { current = current.Resolve()?.BaseType; }
            catch { break; }
        }
        return false;
    }

    /// <summary>
    /// Check if a type is a non-generic SIMD helper type (Vector128, Vector256, etc.).
    /// These static classes have methods that access fields on SIMD generic structs (f_lower, f_upper)
    /// which are opaque since we skip all SIMD generic type instantiation (IsHardwareAccelerated=false).
    /// </summary>
    private static bool IsNonGenericSimdHelperType(string typeFullName)
    {
        // Non-generic static helper types in System.Runtime.Intrinsics namespace
        // e.g., "System.Runtime.Intrinsics.Vector256" (NOT "Vector256`1" which is the generic struct)
        if (!typeFullName.StartsWith("System.Runtime.Intrinsics.Vector")) return false;
        // Must NOT contain '`' (that indicates the generic struct version like Vector128`1)
        return !typeFullName.Contains('`');
    }

    /// <summary>
    /// Generate a no-op method body (just returns default).
    /// Unlike GenerateStubBody, this does NOT call stub_called — the method is intentionally
    /// a no-op (e.g., EventSource diagnostics) rather than a missing implementation.
    /// </summary>
    private static void GenerateNoOpBody(IRMethod irMethod)
    {
        var block = new IRBasicBlock { Id = 0 };
        string? retVal = null;
        if (irMethod.ReturnTypeCpp != "void")
            retVal = irMethod.ReturnTypeCpp.EndsWith("*") ? "nullptr" : "{}";
        block.Instructions.Add(new IRReturn { Value = retVal });
        irMethod.BasicBlocks.Add(block);
    }

    private readonly AssemblyReader _reader;
    private readonly IRModule _module;
    private readonly BuildConfiguration _config;
    private readonly FeatureSwitchResolver _featureSwitchResolver;
    private readonly Dictionary<string, IRType> _typeCache = new();

    // volatile. prefix flag — set by Code.Volatile, consumed by next field access
    private bool _pendingVolatile;

    // constrained. prefix type — set by Code.Constrained, consumed by next callvirt
    private TypeReference? _constrainedType;

    // Exception filter tracking — set during filter evaluation region (FilterStart → endfilter)
    private bool _inFilterRegion;
    private int _endfilterOffset = -1;

    // Leave-crossing tracking — per-method data for leave dispatch across protected regions.
    // Maps leave instruction offset → (targetOffset, innermostCrossedTryStart, innermostCrossedTryEnd, fromHandlerBody)
    // FromHandlerBody: true when leave is from catch/filter handler body (needs inline context restore)
    private Dictionary<int, (int TargetOffset, int InnermostTryStart, int InnermostTryEnd, bool FromHandlerBody)>? _leaveCrossingTargets;

    // Leave dispatch info per region — maps (TryStart,TryEnd) → list of (targetOffset, chainRegion?)
    private Dictionary<(int TryStart, int TryEnd),
        List<(int TargetOffset, (int TryStart, int TryEnd)? ChainRegion)>>? _regionLeaveDispatch;

    // Set by Build() before BuildInternal()
    private AssemblySet _assemblySet = null!;
    private ReachabilityResult _reachability = null!;
    private ReachabilityAnalyzer _reachabilityAnalyzer = null!;
    private List<TypeDefinitionInfo> _allTypes = null!;

    // Dispatched interfaces — generic interfaces actually used for dispatch (callvirt, castclass, etc.)
    private HashSet<string> _dispatchedInterfaces = new();

    // Generic type instantiation tracking
    private readonly Dictionary<string, GenericInstantiationInfo> _genericInstantiations = new();

    private record GenericInstantiationInfo(
        string OpenTypeName,
        List<string> TypeArguments,
        string MangledName,
        TypeDefinition? CecilOpenType
    );

    // Generic method instantiation tracking
    private readonly Dictionary<string, GenericMethodInstantiationInfo> _genericMethodInstantiations = new();
    // Keys already processed by CreateGenericMethodSpecializations — allows safe re-invocation
    private readonly HashSet<string> _processedMethodSpecKeys = new();
    // Keys already processed by the second pass of CreateGenericSpecializations (base types, interfaces)
    private readonly HashSet<string> _resolvedGenericTypeKeys = new();
    // Types already processed by DisambiguateOverloadedMethods — prevents re-disambiguation
    private readonly HashSet<IRType> _disambiguatedTypes = new();
    private readonly HashSet<IRType> _vtableBuilt = new();

    private record GenericMethodInstantiationInfo(
        string DeclaringTypeName,
        string MethodName,
        List<string> TypeArguments,
        string MangledName,
        MethodDefinition CecilMethod
    );

    public IRBuilder(AssemblyReader reader, BuildConfiguration? config = null)
    {
        _reader = reader;
        _config = config ?? BuildConfiguration.Release;
        _featureSwitchResolver = new FeatureSwitchResolver(_config.FeatureSwitches);
        _module = new IRModule { Name = reader.AssemblyName };
    }

    /// <summary>
    /// Build the IR module from assemblies, filtered by reachability analysis.
    /// All BCL types with IL bodies compile to C++ (Unity IL2CPP model).
    /// </summary>
    public IRModule Build(AssemblySet assemblySet, ReachabilityResult reachability,
        ReachabilityAnalyzer? analyzer = null)
    {
        _assemblySet = assemblySet;
        _reachability = reachability;
        _reachabilityAnalyzer = analyzer!;

        _module.Name = assemblySet.RootAssemblyName;

        // Pass reflection target types to IRModule for codegen filtering
        _module.ReflectionTargetTypes = reachability.ReflectionTargetTypes;

        // Pass constructed types to IRModule for TypeInfo tiering
        _module.ConstructedTypes = new HashSet<string>(reachability.ConstructedTypes.Select(t => t.FullName));

        // Pass dispatched interfaces for generic interface specialization filtering
        _dispatchedInterfaces = reachability.DispatchedInterfaces;

        // Collect all reachable types as TypeDefinitionInfo, with classification
        var types = new List<TypeDefinitionInfo>();
        foreach (var cecilType in reachability.ReachableTypes)
        {
            types.Add(new TypeDefinitionInfo(cecilType));
        }
        _allTypes = types;

        return BuildInternal();
    }

    /// <summary>
    /// Scan all loaded assemblies and register value types (structs, enums) in CppNameMapper.
    /// This ensures BCL value types used as locals/params in reachable methods are correctly
    /// treated as value types even if the type itself isn't in _allTypes.
    /// </summary>
    private void PreRegisterAllValueTypes()
    {
        foreach (var (_, assembly) in _assemblySet.LoadedAssemblies)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var typeDef in module.Types)
                    PreRegisterValueTypesRecursive(typeDef);
            }
        }
    }

    private static void PreRegisterValueTypesRecursive(Mono.Cecil.TypeDefinition typeDef)
    {
        if (typeDef.IsValueType && typeDef.FullName != "<Module>")
        {
            CppNameMapper.RegisterValueType(typeDef.FullName);
            CppNameMapper.RegisterValueType(CppNameMapper.MangleTypeName(typeDef.FullName));
        }
        if (typeDef.HasNestedTypes)
        {
            foreach (var nested in typeDef.NestedTypes)
                PreRegisterValueTypesRecursive(nested);
        }
    }

    private IRModule BuildInternal()
    {
        CppNameMapper.ClearValueTypes();

        // Pre-register value types from ALL loaded assemblies (not just reachable types).
        // BCL enums/structs (e.g. System.Number.ParsingStatus) may be used as locals/params
        // in reachable methods but not themselves marked reachable.
        PreRegisterAllValueTypes();

        // Pass 0: Scan for generic instantiations in all method bodies
        var pass0sw = System.Diagnostics.Stopwatch.StartNew();
        ScanGenericInstantiations();
        pass0sw.Stop();
        Console.Error.WriteLine($"[perf] Pass 0 ScanGenericInstantiations: {pass0sw.ElapsedMilliseconds}ms, " +
            $"types={_genericInstantiations.Count}, specMethodKeys={_calledSpecializedMethods.Count}");

        // Pass 1: Create all type shells (no fields/methods yet)
        // Skip open generic types — they are templates, not concrete types
        // Partial classes (e.g., Interop.Kernel32) may span multiple assemblies —
        // reuse the existing IRType so methods from all assemblies merge onto one type.
        foreach (var typeDef in _allTypes)
        {
            if (typeDef.HasGenericParameters)
                continue;

            // Partial class from another assembly — reuse existing IRType
            if (_typeCache.ContainsKey(typeDef.FullName))
                continue;

            var irType = CreateTypeShell(typeDef);

            // Classify type origin and runtime-provided status
            var assemblyName = typeDef.GetCecilType().Module.Assembly.Name.Name;
            irType.SourceKind = _assemblySet.ClassifyAssembly(assemblyName);
            irType.IsRuntimeProvided = RuntimeProvidedTypes.Contains(typeDef.FullName);
            irType.IsPrimitiveType = ReachabilityAnalyzer.PrimitiveTypeNames.Contains(typeDef.FullName);

            _module.Types.Add(irType);
            _typeCache[typeDef.FullName] = irType;
        }

        // Pass 0.5: REMOVED — demand-driven discovery replaces bulk pre-scanning.
        // Generic types are now discovered on-demand when method bodies are compiled
        // (via EnsureBodyReferencedTypesExist in ConvertDeferredGenericBodies fixpoint).
        // This avoids the exponential blowup from scanning all methods of all discovered types.

        // Pass 1.5: Create specialized types for each generic instantiation
        CreateGenericSpecializations();

        // Pass 2: Fill in fields, base types, interfaces
        foreach (var typeDef in _allTypes)
        {
            if (typeDef.HasGenericParameters) continue;
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType))
            {
                PopulateTypeDetails(typeDef, irType);
            }
        }

        // Pass 2.3: Create BCL interface proxies for closed generic BCL interfaces
        // Open generic types (e.g. IEquatable`1) are loaded from BCL IL, but closed forms
        // (e.g. IEquatable<int>) may not be in the type cache. Create minimal proxy shells.
        CreateBclInterfaceProxies();
        ResolveBclProxyInterfaces();

        // Pass 2.4: Re-resolve interfaces that were missed in Pass 2 because BCL proxies
        // didn't exist yet. Back-fill the Interfaces list for user types.
        foreach (var typeDef in _allTypes)
        {
            if (typeDef.HasGenericParameters) continue;
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType))
            {
                foreach (var ifaceName in typeDef.InterfaceNames)
                {
                    if (_typeCache.TryGetValue(ifaceName, out var iface)
                        && !irType.Interfaces.Contains(iface))
                    {
                        irType.Interfaces.Add(iface);
                    }
                }
            }
        }

        // Pass 2.5: Flag types with static constructors (before method conversion)
        foreach (var typeDef in _allTypes)
        {
            if (typeDef.HasGenericParameters) continue;
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType2))
            {
                // Flag HasCctor if the type has a .cctor with a body.
                // Use |= so partial classes spanning assemblies don't overwrite earlier true.
                // We check HasBody (not reachability) because SeedCctorFor() may not have
                // been triggered yet for all types, but if the cctor exists and the type is
                // reachable, the cctor will be compiled and needs ensure_cctor guards.
                // A missing guard causes silent bugs (static fields read as default zero);
                // an unnecessary guard is just a cheap bool check — correctness wins.
                irType2.HasCctor |= typeDef.Methods.Any(m => m.IsConstructor && m.IsStatic
                    && m.HasBody);
            }
        }

        // Pass 3: Create method shells (no body yet — needed for VTable)
        // Skip open generic methods — they are templates, specialized in Pass 3.5
        var methodBodies = new List<(IL.MethodInfo MethodDef, IRMethod IRMethod)>();
        foreach (var typeDef in _allTypes)
        {
            if (typeDef.HasGenericParameters) continue;
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType))
            {
                foreach (var methodDef in typeDef.Methods)
                {
                    // Skip open generic methods — they'll be monomorphized in Pass 3.5
                    if (methodDef.HasGenericParameters) continue;

                    var irMethod = ConvertMethod(methodDef, irType);
                    irType.Methods.Add(irMethod);

                    // Detect entry point (only from root assembly)
                    if (methodDef.Name == "Main" && methodDef.IsStatic
                        && typeDef.GetCecilType().Module.Assembly.Name.Name == _assemblySet.RootAssemblyName)
                    {
                        irMethod.IsEntryPoint = true;
                        _module.EntryPoint = irMethod;
                    }

                    // Track finalizer
                    if (irMethod.IsFinalizer)
                        irType.Finalizer = irMethod;

                    // Save for body conversion later (skip abstract and InternalCall)
                    if (methodDef.HasBody && !methodDef.IsAbstract && !irMethod.IsInternalCall)
                    {
                        if (_reachability.IsReachable(methodDef.GetCecilMethod()))
                            methodBodies.Add((methodDef, irMethod));
                    }

                }

                // Detect record types:
                // - reference record: has <Clone>$ method
                // - value record struct: value type with PrintMembers (unique to records)
                bool isRefRecord = irType.Methods.Any(m => m.Name == "<Clone>$");
                bool isValRecord = irType.IsValueType
                    && irType.Methods.Any(m => m.Name == "PrintMembers");
                if (isRefRecord || isValRecord)
                    irType.IsRecord = true;

                // Collect properties (for reflection metadata)
                var cecilType = typeDef.GetCecilType();
                if (cecilType.HasProperties)
                {
                    foreach (var prop in cecilType.Properties)
                    {
                        var irProp = new IRProperty
                        {
                            Name = prop.Name,
                            PropertyTypeName = prop.PropertyType.FullName,
                            Attributes = (uint)prop.Attributes,
                            DeclaringType = irType,
                        };
                        // Resolve getter/setter to IRMethod (methods just created above)
                        if (prop.GetMethod != null)
                            irProp.Getter = irType.Methods.FirstOrDefault(
                                m => m.Name == prop.GetMethod.Name);
                        if (prop.SetMethod != null)
                            irProp.Setter = irType.Methods.FirstOrDefault(
                                m => m.Name == prop.SetMethod.Name);
                        irType.Properties.Add(irProp);
                    }
                }
            }
        }

        // Pass 3.2: Scan method signatures for external enum types (BCL enums not in the IR module)
        // Must run after Pass 3 to have method signatures, then fixup the types.
        var newEnums1 = ScanExternalEnumTypes();
        FixupExternalEnumTypes(newEnums1);

        // Pass 3.3: Disambiguate overloaded methods whose C++ names collide
        // (e.g. different C# enum types collapse to same C++ type via using aliases)
        DisambiguateOverloadedMethods();

        // Pass 3.3b: Build vtables for types discovered so far.
        // This enables virtual dispatch resolution in Pass 3.4/3.5 method body compilation.
        // Without this, callvirt in generic method bodies (Pass 3.5) would find empty VTables
        // and fall back to direct calls, breaking polymorphic dispatch (e.g., Iterator_1.ToArray).
        // BuildVTable is idempotent (skips if already built), so Pass 4 safely handles
        // any new types created in Pass 3.6.
        foreach (var irType in _module.Types)
        {
            BuildVTableRecursive(irType, _vtableBuilt);
        }

        // Pass 3.4: Convert deferred generic specialization bodies.
        // Must happen AFTER disambiguation (Pass 3.3) so that call sites in generic method
        // bodies resolve to the correct disambiguated function names.
        ConvertDeferredGenericBodies();

        // Post-3.4: Re-evaluate HasCctor for GENERIC types whose cctor body was never compiled.
        // CreateGenericSpecializations sets HasCctor=true when the cctor is in _deferredGenericBodies,
        // but body compilation may have been skipped (not reachable, broken patterns, etc.).
        // Only check generic instances — non-generic types' cctor bodies are compiled in Pass 6.
        foreach (var irType in _module.Types)
        {
            if (irType.HasCctor && irType.IsGenericInstance)
            {
                var cctorMethod = irType.Methods.FirstOrDefault(m => m.IsStaticConstructor);
                if (cctorMethod != null && cctorMethod.BasicBlocks.Count == 0)
                    irType.HasCctor = false;
            }
        }

        // Pass 3.5: Create specialized methods for each generic method instantiation
        CreateGenericMethodSpecializations();

        // Pass 3.5b: Drain deferred generic bodies discovered during Pass 3.5.
        // CreateGenericMethodSpecializations calls EnsureBodyReferencedTypesExist which may
        // discover new generic types (e.g., AsyncStateMachineBox<TResult,TStateMachine>)
        // and add their methods to _deferredGenericBodies. These weren't in the queue during
        // Pass 3.4 because they're only referenced from generic method bodies compiled in Pass 3.5.
        if (_deferredGenericBodies.Count > 0)
        {
            foreach (var irType in _module.Types)
                BuildVTableRecursive(irType, _vtableBuilt);
            DisambiguateOverloadedMethods();
            ConvertDeferredGenericBodies();
        }

        // Pass 3.6: Post-specialization cleanup (demand-driven replaced bulk scanning).
        // Ensure disambiguation is up to date for types created during 3.4/3.5/3.5b.
        DisambiguateOverloadedMethods();

        // Pass 3.7: Fix up undisambiguated call sites in already-compiled method bodies.
        // Generic method specializations (Pass 3.5) may call methods on types that are only
        // discovered and disambiguated in Pass 3.6. Their IRCall.FunctionName was set before
        // disambiguation entries existed. Retroactively apply disambiguation lookups.
        FixupDisambiguatedCalls();

        // Pass 3.8: Mark AOT companion types (ObjectEqualityComparer<T>, ObjectComparer<T>)
        // as constructed so they get full TypeInfo with vtables and interface vtables.
        foreach (var (key, info) in _genericInstantiations)
        {
            if (info.OpenTypeName is "System.Collections.Generic.ObjectEqualityComparer`1"
                                  or "System.Collections.Generic.ObjectComparer`1")
            {
                _module.ConstructedTypes.Add(key);
            }
        }

        // Pass 4: Build vtables for new types from Pass 3.6 (re-discovery).
        // Types from Pass 3.3b already have VTables (BuildVTable is idempotent).
        foreach (var irType in _module.Types)
        {
            BuildVTableRecursive(irType, _vtableBuilt);
        }

        // Pass 5: Build interface implementation maps
        foreach (var irType in _module.Types)
        {
            if (!irType.IsInterface && !irType.IsValueType)
                BuildInterfaceImpls(irType);
        }

        // Pass 5.5: Collect custom attributes from Cecil metadata
        PopulateCustomAttributes();

        // Pass 6: Convert method bodies (vtables are now available for virtual dispatch)
        foreach (var (methodDef, irMethod) in methodBodies)
        {
            // Skip record compiler-generated methods — Pass 7 synthesizes replacements
            if (irMethod.DeclaringType?.IsRecord == true && IsRecordSynthesizedMethod(irMethod.Name))
                continue;

            // Skip ICall-mapped methods — dead code (callers redirected to ICall function)
            if (irMethod.HasICallMapping) continue;

            // Skip methods with CLR-internal dependencies (QCallTypeHandle, RuntimeType, etc.)
            // These cannot be compiled to C++ — generate a minimal stub body instead
            if (HasClrInternalDependencies(methodDef.GetCecilMethod(), out var clrReason))
            {
                irMethod.IrStubReason = clrReason;
                GenerateStubBody(irMethod);
                continue;
            }

            // EventSource-derived type methods: generate no-op bodies instead of compiling IL.
            // EventSource.IsEnabled ICall always returns false → all diagnostic write methods
            // are dead code at runtime. Compiling them from BCL IL introduces references to
            // EventData, WriteEventCore, ActivityTracker etc. which are in the excluded
            // System.Diagnostics.Tracing namespace, causing UBR/UF stubs.
            if (IsEventSourceDerived(methodDef.GetCecilMethod().DeclaringType))
            {
                GenerateNoOpBody(irMethod);
                continue;
            }

            // Non-generic SIMD helper types (Vector128, Vector256, Vector512, Vector64):
            // Their static methods access fields on SIMD generic structs (f_lower, f_upper)
            // which are opaque since we skip all SIMD generic type instantiation.
            // Generate no-op bodies to avoid MSVC field access errors on empty structs.
            if (IsNonGenericSimdHelperType(methodDef.GetCecilMethod().DeclaringType.FullName))
            {
                GenerateNoOpBody(irMethod);
                continue;
            }

            ConvertMethodBody(methodDef, irMethod);

        }

        // Pass 6.1: Compile generic method specializations discovered during Pass 6.
        // E.g., DateTimeFormatInfo.GetAbbreviatedDayName calls ThrowHelper.ThrowArgumentOutOfRange_Range<DayOfWeek>
        // which is only discovered when the non-generic method body is compiled in Pass 6.
        CreateGenericMethodSpecializations();

        // Pass 6.1b: Drain deferred generic bodies discovered during Pass 6.1.
        if (_deferredGenericBodies.Count > 0)
        {
            foreach (var irType in _module.Types)
                BuildVTableRecursive(irType, _vtableBuilt);
            DisambiguateOverloadedMethods();
            ConvertDeferredGenericBodies();
        }

        // Pass 6.5: Discover types referenced by compiled method bodies but not yet in the module
        // This can happen when BCL methods reference types only as parameters/locals/fields
        DiscoverMissingReferencedTypes();

        // Pass 6.6: Re-scan for external enum types in generic specialization and newly
        // compiled method bodies (their locals weren't available during Pass 3.2).
        // Only fixup NEWLY discovered enums — types resolved after Pass 3.2 registration
        // already have correct pointer levels (ref enum → EnumType*).
        var newEnums2 = ScanExternalEnumTypes();
        FixupExternalEnumTypes(newEnums2);

        // Pass 7: Synthesize record method bodies (replace compiler-generated bodies
        // that reference unsupported BCL types like StringBuilder, EqualityComparer<T>)
        foreach (var irType in _module.Types)
        {
            if (irType.IsRecord)
                SynthesizeRecordMethods(irType);
        }

        return _module;
    }

    /// <summary>
    /// Methods that are compiler-generated for records and need synthesized replacements.
    /// </summary>
    private static bool IsRecordSynthesizedMethod(string name) => name is
        "ToString" or "GetHashCode" or "Equals" or "PrintMembers"
        or "<Clone>$" or "op_Equality" or "op_Inequality" or "get_EqualityContract";

    /// <summary>
    /// Check if a type reference is System.Nullable`1 (any instantiation).
    /// Used by boxing logic (ECMA-335 III.4.1).
    /// </summary>
    internal static bool IsNullableType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName == "System.Nullable`1";
    }
}
