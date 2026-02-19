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

    /// <summary>
    /// Set of IL type full names whose struct definitions come from the C++ runtime.
    /// These types get IsRuntimeProvided = true; their struct is defined in runtime headers,
    /// but their method bodies still compile from IL (Unity IL2CPP model).
    /// </summary>
    internal static readonly HashSet<string> RuntimeProvidedTypes = new()
    {
        // Core types — struct layout defined in runtime headers (object.h, string.h, etc.)
        "System.Object",
        "System.ValueType",
        "System.Enum",
        "System.String",
        "System.Array",
        "System.Exception",
        "System.Delegate",
        "System.MulticastDelegate",
        "System.Type",

        // Async non-generic types — struct defined in task.h / async_enumerable.h
        // NOTE: generic open types (Task`1, TaskAwaiter`1, etc.) are NOT here —
        // their specializations get struct definitions generated from BCL IL.
        "System.Threading.Tasks.Task",
        "System.Runtime.CompilerServices.TaskAwaiter",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder",
        "System.Runtime.CompilerServices.IAsyncStateMachine",
        "System.Threading.Tasks.ValueTask",
        "System.Runtime.CompilerServices.ValueTaskAwaiter",
        "System.Runtime.CompilerServices.AsyncIteratorMethodBuilder",

        // Reflection types — struct defined in memberinfo.h
        "System.Reflection.MemberInfo",
        "System.Reflection.MethodBase",
        "System.Reflection.MethodInfo",
        "System.Reflection.FieldInfo",
        "System.Reflection.ParameterInfo",

        // Threading types — struct defined in threading.h / cancellation.h
        "System.Threading.Thread",
        "System.Threading.CancellationToken",
        "System.Threading.CancellationTokenSource",

        // TypedReference + ArgIterator — struct defined in typed_reference.h
        "System.TypedReference",
        "System.ArgIterator",
    };

    /// <summary>
    /// Core runtime types where instance methods should NOT be emitted from IL.
    /// These types have their methods fully provided by the C++ runtime.
    /// Non-core RuntimeProvidedTypes (Task, Thread, etc.) DO emit instance methods.
    /// </summary>
    internal static readonly HashSet<string> CoreRuntimeTypes = new()
    {
        "System.Object",
        "System.ValueType",
        "System.Enum",
        // System.String: NOT CoreRuntime — BCL IL compiles (struct layout still RuntimeProvided).
        // Only FastAllocateString/get_Length/get_Chars/GetRawStringData remain as icalls.
        "System.Array",
        "System.Exception",
        "System.Delegate",
        "System.MulticastDelegate",
        "System.Type",
        // Reflection types: instance methods provided by runtime C++ (memberinfo.h)
        "System.Reflection.MemberInfo",
        "System.Reflection.MethodBase",
        "System.Reflection.MethodInfo",
        "System.Reflection.FieldInfo",
        "System.Reflection.ParameterInfo",
        // TypedReference + ArgIterator: all methods handled by runtime / icall
        "System.TypedReference",
        "System.ArgIterator",
    };

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
        // CLR-internal runtime type system
        "System.RuntimeType",
        "System.RuntimeTypeHandle",
        "System.RuntimeMethodHandle",
        "System.RuntimeFieldHandle",
        // CLR-internal reflection types
        "System.Reflection.RuntimeMethodInfo",
        "System.Reflection.RuntimeFieldInfo",
        "System.Reflection.RuntimePropertyInfo",
        "System.Reflection.RuntimeConstructorInfo",
        "System.Reflection.MetadataImport",
        "System.Reflection.RuntimeCustomAttributeData",
        // CLR-internal binder/dispatch
        "System.DefaultBinder",
        "System.DBNull",
        "System.Signature",
        // Deep BCL types with runtime struct layout mismatches
        "System.AggregateException",        // Runtime struct lacks BCL internal fields
        "System.Reflection.TypeInfo",       // Abstract base for RuntimeType — compiled methods can't cast properly
        "System.Reflection.Assembly",       // GetExecutingAssembly uses CLR-internal StackCrawlMark
        "System.Reflection.RuntimeAssembly", // CLR internal assembly type
        "System.Threading.WaitHandle",      // Uses CLR-internal SafeWaitHandle patterns
        "System.Runtime.InteropServices.PosixSignalRegistration", // Uses CLR-internal interop
        // CLR-internal threading types
        "System.Threading.ThreadInt64PersistentCounter",
        "System.Threading.IAsyncLocal",
        // Globalization internal types
        "System.Globalization.CalendarId",
        "System.Globalization.EraInfo",
    };

    /// <summary>
    /// Check if a Cecil method's body references CLR-internal types that cannot be compiled.
    /// When true, the method body should be replaced with a stub.
    /// Detects: CLR-internal locals/params, BCL compiler-generated generic display classes.
    /// </summary>
    internal static bool HasClrInternalDependencies(Mono.Cecil.MethodDefinition cecilMethod)
    {
        // Check if declaring type is CLR-internal (all methods on these types are unstubable)
        var declType = cecilMethod.DeclaringType;
        if (ClrInternalTypeNames.Contains(declType.FullName))
            return true;
        // Nested types within CLR-internal types
        if (declType.DeclaringType != null && ClrInternalTypeNames.Contains(declType.DeclaringType.FullName))
            return true;

        if (!cecilMethod.HasBody) return false;

        // Check local variable types
        foreach (var local in cecilMethod.Body.Variables)
        {
            var typeName = local.VariableType.FullName;
            if (ClrInternalTypeNames.Contains(typeName))
                return true;
            if (local.VariableType is GenericInstanceType git
                && ClrInternalTypeNames.Contains(git.ElementType.FullName))
                return true;
        }

        // Check parameter types
        foreach (var param in cecilMethod.Parameters)
        {
            if (ClrInternalTypeNames.Contains(param.ParameterType.FullName))
                return true;
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
                        return true;
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
        var block = new IRBasicBlock { Id = 0 };
        string? retVal = null;
        if (irMethod.ReturnTypeCpp != "void")
        {
            retVal = irMethod.ReturnTypeCpp.EndsWith("*") ? "nullptr" : "{}";
        }
        block.Instructions.Add(new IRReturn { Value = retVal });
        irMethod.BasicBlocks.Add(block);
    }

    private readonly AssemblyReader _reader;
    private readonly IRModule _module;
    private readonly BuildConfiguration _config;
    private readonly Dictionary<string, IRType> _typeCache = new();

    // volatile. prefix flag — set by Code.Volatile, consumed by next field access
    private bool _pendingVolatile;

    // Track typed pointer types for temp variables (for byte-level pointer arithmetic).
    // Maps temp var name (e.g. "__t6") to its C++ pointer type (e.g. "char16_t*").
    private readonly Dictionary<string, string> _tempPtrTypes = new();

    // constrained. prefix type — set by Code.Constrained, consumed by next callvirt
    private TypeReference? _constrainedType;

    // Exception filter tracking — set during filter evaluation region (FilterStart → endfilter)
    private bool _inFilterRegion;
    private int _endfilterOffset = -1;

    // Set by Build() before BuildInternal()
    private AssemblySet _assemblySet = null!;
    private ReachabilityResult _reachability = null!;
    private List<TypeDefinitionInfo> _allTypes = null!;

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
        _module = new IRModule { Name = reader.AssemblyName };
    }

    /// <summary>
    /// Build the IR module from assemblies, filtered by reachability analysis.
    /// All BCL types with IL bodies compile to C++ (Unity IL2CPP model).
    /// </summary>
    public IRModule Build(AssemblySet assemblySet, ReachabilityResult reachability)
    {
        _assemblySet = assemblySet;
        _reachability = reachability;

        _module.Name = assemblySet.RootAssemblyName;

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
        ScanGenericInstantiations();

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
                irType2.HasCctor = typeDef.Methods.Any(m => m.IsConstructor && m.IsStatic);
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
                    if (methodDef.HasBody && !methodDef.IsAbstract && !irMethod.IsInternalCall
                        && _reachability.IsReachable(methodDef.GetCecilMethod()))
                    {
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
            }
        }

        // Pass 3.2: Scan method signatures for external enum types (BCL enums not in the IR module)
        // Must run after Pass 3 to have method signatures, then fixup the types.
        var newEnums1 = ScanExternalEnumTypes();
        FixupExternalEnumTypes(newEnums1);

        // Pass 3.3: Disambiguate overloaded methods whose C++ names collide
        // (e.g. different C# enum types collapse to same C++ type via using aliases)
        DisambiguateOverloadedMethods();

        // Pass 3.5: Create specialized methods for each generic method instantiation
        CreateGenericMethodSpecializations();

        // Pass 4: Build vtables (needs method shells with IsVirtual)
        // Use recursive helper to ensure base types are built before derived types
        var vtableBuilt = new HashSet<IRType>();
        foreach (var irType in _module.Types)
        {
            BuildVTableRecursive(irType, vtableBuilt);
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

            // Skip methods with CLR-internal dependencies (QCallTypeHandle, RuntimeType, etc.)
            // These cannot be compiled to C++ — generate a minimal stub body instead
            if (HasClrInternalDependencies(methodDef.GetCecilMethod()))
            {
                GenerateStubBody(irMethod);
                continue;
            }

            ConvertMethodBody(methodDef, irMethod);
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
