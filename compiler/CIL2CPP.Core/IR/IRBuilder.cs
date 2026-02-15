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
    /// Set of IL type full names that have C++ runtime implementations.
    /// These types get IsRuntimeProvided = true and should not emit struct definitions.
    /// </summary>
    internal static readonly HashSet<string> RuntimeProvidedTypes = new()
    {
        "System.Object",
        "System.ValueType",
        "System.Enum",
        "System.String",
        "System.Array",
        "System.Exception",
        "System.Delegate",
        "System.MulticastDelegate",
        "System.Threading.Tasks.Task",
        "System.Runtime.CompilerServices.TaskAwaiter",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder",
        "System.Runtime.CompilerServices.IAsyncStateMachine",
        "System.Threading.Thread",
        "System.Type",
        "System.Span`1",
        "System.ReadOnlySpan`1",
    };

    private readonly AssemblyReader _reader;
    private readonly IRModule _module;
    private readonly BuildConfiguration _config;
    private readonly Dictionary<string, IRType> _typeCache = new();

    // volatile. prefix flag — set by Code.Volatile, consumed by next field access
    private bool _pendingVolatile;

    // constrained. prefix type — set by Code.Constrained, consumed by next callvirt
    private TypeReference? _constrainedType;

    // Exception filter tracking — set during filter evaluation region (FilterStart → endfilter)
    private bool _inFilterRegion;
    private int _endfilterOffset = -1;

    // Multi-assembly mode fields (null in single-assembly mode)
    private AssemblySet? _assemblySet;
    private ReachabilityResult? _reachability;
    // Pre-computed list of types to process (set by both Build overloads)
    private List<TypeDefinitionInfo>? _allTypes;

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
    /// Build the complete IR module from a single assembly (existing behavior).
    /// </summary>
    public IRModule Build()
    {
        _allTypes = _reader.GetAllTypes().ToList();
        return BuildInternal();
    }

    /// <summary>
    /// Build the IR module from multiple assemblies, filtered by reachability.
    /// Types are classified by SourceKind and RuntimeProvided status.
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

    private IRModule BuildInternal()
    {
        CppNameMapper.ClearValueTypes();

        // Register async BCL value types (awaiter + builder are value types)
        // Register both IL names and C++ mangled names for GetDefaultValue
        CppNameMapper.RegisterValueType("System.Runtime.CompilerServices.TaskAwaiter");
        CppNameMapper.RegisterValueType("System_Runtime_CompilerServices_TaskAwaiter");
        CppNameMapper.RegisterValueType("System.Runtime.CompilerServices.AsyncTaskMethodBuilder");
        CppNameMapper.RegisterValueType("System_Runtime_CompilerServices_AsyncTaskMethodBuilder");

        // Register Index/Range BCL value types
        CppNameMapper.RegisterValueType("System.Index");
        CppNameMapper.RegisterValueType("System_Index");
        CppNameMapper.RegisterValueType("System.Range");
        CppNameMapper.RegisterValueType("System_Range");

        // Pass 0: Scan for generic instantiations in all method bodies
        ScanGenericInstantiations();

        // Pass 1: Create all type shells (no fields/methods yet)
        // Skip open generic types — they are templates, not concrete types
        foreach (var typeDef in _allTypes!)
        {
            if (typeDef.HasGenericParameters)
                continue;
            var irType = CreateTypeShell(typeDef);

            // Classify type origin and runtime-provided status
            if (_assemblySet != null)
            {
                var assemblyName = typeDef.GetCecilType().Module.Assembly.Name.Name;
                irType.SourceKind = _assemblySet.ClassifyAssembly(assemblyName);
                irType.IsRuntimeProvided = RuntimeProvidedTypes.Contains(typeDef.FullName);
            }

            _module.Types.Add(irType);
            _typeCache[typeDef.FullName] = irType;
        }

        // Pass 1.5a: Create synthetic types for BCL value types (Index, Range)
        CreateIndexRangeSyntheticTypes();

        // Pass 1.5b: Create synthetic type for System.Threading.Thread (reference type)
        CreateThreadSyntheticType();

        // Pass 1.5c: Create proxy types for well-known BCL interfaces (IDisposable, IEnumerable, etc.)
        // In multi-assembly mode, real BCL interfaces are loaded from assemblies — no proxies needed.
        if (_assemblySet == null)
        {
            CreateBclInterfaceProxies();
            ResolveBclProxyInterfaces();
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

                    // Detect entry point (only from root assembly in multi-assembly mode)
                    if (methodDef.Name == "Main" && methodDef.IsStatic)
                    {
                        if (_assemblySet == null ||
                            typeDef.GetCecilType().Module.Assembly.Name.Name == _assemblySet.RootAssemblyName)
                        {
                            irMethod.IsEntryPoint = true;
                            _module.EntryPoint = irMethod;
                        }
                    }

                    // Track finalizer
                    if (irMethod.IsFinalizer)
                        irType.Finalizer = irMethod;

                    // Save for body conversion later (skip abstract and InternalCall)
                    // In multi-assembly mode, only convert bodies for reachable methods
                    if (methodDef.HasBody && !methodDef.IsAbstract && !irMethod.IsInternalCall)
                    {
                        if (_reachability == null || _reachability.IsReachable(methodDef.GetCecilMethod()))
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

        // Pass 3.5: Create specialized methods for each generic method instantiation
        CreateGenericMethodSpecializations();

        // Pass 4: Build vtables (needs method shells with IsVirtual)
        foreach (var irType in _module.Types)
        {
            BuildVTable(irType);
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

            ConvertMethodBody(methodDef, irMethod);
        }

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
}
