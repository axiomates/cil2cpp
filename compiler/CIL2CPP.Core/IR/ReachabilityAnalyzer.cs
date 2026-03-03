using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Result of reachability analysis: the sets of reachable types and methods.
/// RTA (Rapid Type Analysis) model: ReachableTypes need struct layout / forward declarations,
/// ConstructedTypes are actually instantiated (newobj/newarr) and participate in virtual dispatch.
/// </summary>
public class ReachabilityResult
{
    public HashSet<TypeDefinition> ReachableTypes { get; } = new();
    public HashSet<MethodDefinition> ReachableMethods { get; } = new();

    /// <summary>
    /// Types that are actually instantiated (newobj, newarr, Activator.CreateInstance, DAM).
    /// Only constructed types participate in virtual method dispatch (RTA).
    /// Always a subset of ReachableTypes.
    /// </summary>
    public HashSet<TypeDefinition> ConstructedTypes { get; } = new();

    public bool IsReachable(TypeDefinition type) => ReachableTypes.Contains(type);
    public bool IsReachable(MethodDefinition method) => ReachableMethods.Contains(method);
    public bool IsConstructed(TypeDefinition type) => ConstructedTypes.Contains(type);
}

/// <summary>
/// Performs reachability analysis (tree shaking) starting from entry point(s).
/// Uses method-level granularity: only methods that are actually called are marked reachable.
/// All types (user and BCL) use the same worklist-driven analysis — no blanket seeding.
/// BCL types in deep internal namespaces are filtered at the boundary.
/// </summary>
public class ReachabilityAnalyzer
{
    private readonly AssemblySet _assemblySet;
    private readonly ReachabilityResult _result = new();
    private readonly Queue<MethodDefinition> _worklist = new();
    private readonly HashSet<string> _processedMethods = new();

    // Track dispatched virtual method slots for deferred override resolution.
    // Each slot records the declaring type so we only match overrides in subtypes/implementors.
    private readonly List<(string MethodName, int ParamCount, TypeDefinition DeclaringType)> _dispatchedVirtualSlots = new();
    private readonly HashSet<string> _dispatchedSlotKeys = new(); // dedup by "DeclaringType::Name/Count"

    // D.2: rd.xml preservation rules
    private List<RdXmlParser.PreservationRule>? _preservationRules;

    // Performance: cache InheritsFromOrImplements results to avoid repeated hierarchy walks
    private readonly Dictionary<(string TypeName, string TargetName), bool> _inheritsCache = new();

    // Performance: index from ancestor/interface FullName → subtypes.
    // Replaces O(V×T) full scans in DispatchVirtualSlot / MarkDispatchedOverrides
    // with O(V × subtypes(declaringType)) lookups.
    private readonly Dictionary<string, List<TypeDefinition>> _subtypeIndex = new();

    // Performance: cache TryResolve results to avoid repeated Cecil Resolve() calls
    private readonly Dictionary<string, TypeDefinition?> _resolveCache = new();

    // Separate guard for full reachability processing vs layout-only.
    // MarkTypeForLayout adds to ReachableTypes (struct emission) but not here.
    // MarkTypeReachable checks this set so a layout-only type can be upgraded later.
    private readonly HashSet<TypeDefinition> _fullyProcessedTypes = new();

    public ReachabilityAnalyzer(AssemblySet assemblySet)
    {
        _assemblySet = assemblySet;
    }

    /// <summary>
    /// Apply rd.xml preservation rules before running analysis.
    /// Call this before Analyze() to inject external preservation directives.
    /// </summary>
    public void SetPreservationRules(List<RdXmlParser.PreservationRule> rules)
    {
        _preservationRules = rules;
    }

    /// <summary>
    /// Run the reachability analysis and return the result.
    /// When forceLibraryMode is true, seeds from all types/methods regardless of entry point.
    /// </summary>
    public ReachabilityResult Analyze(bool forceLibraryMode = false)
    {
        // Seed: find the entry point
        var entryPoint = _assemblySet.RootAssembly.EntryPoint;
        if (forceLibraryMode)
        {
            // Forced library mode: seed ALL types and methods (used by test fixture)
            SeedAllTypes(_assemblySet.RootAssembly);
        }
        else if (entryPoint != null)
        {
            SeedMethod(entryPoint);
        }
        else
        {
            // Library mode: seed all public types/methods
            SeedAllPublicTypes(_assemblySet.RootAssembly);
        }

        // RTA: mark types that are implicitly constructed by the runtime.
        // These types are created by runtime internals (string literals, boxing,
        // array creation, exception handling) without explicit newobj in user code.
        SeedImplicitlyConstructedTypes();

        // D.2: Apply rd.xml preservation rules after initial seeding
        if (_preservationRules != null)
            ApplyPreservationRules();

        // Worklist fixpoint
        while (_worklist.Count > 0)
        {
            var method = _worklist.Dequeue();
            ProcessMethod(method);
        }

        return _result;
    }

    private void SeedMethod(MethodDefinition method)
    {
        if (!_result.ReachableMethods.Add(method))
            return;

        MarkTypeReachable(method.DeclaringType);
        _worklist.Enqueue(method);

        // If this is a virtual method, track it as a dispatched slot
        // and mark overrides in all already-reachable types
        if (method.IsVirtual)
            DispatchVirtualSlot(method);
    }

    private void SeedAllTypes(AssemblyDefinition assembly)
    {
        foreach (var type in assembly.MainModule.Types)
        {
            if (type.Name == "<Module>") continue;

            // Root assembly types in forceLibraryMode: mark constructed
            // (external callers may instantiate any type)
            MarkTypeConstructed(type);
            foreach (var method in type.Methods)
                SeedMethod(method);

            foreach (var nested in type.NestedTypes)
            {
                MarkTypeConstructed(nested);
                foreach (var method in nested.Methods)
                    SeedMethod(method);
            }
        }
    }

    private void SeedAllPublicTypes(AssemblyDefinition assembly)
    {
        foreach (var type in assembly.MainModule.Types)
        {
            if (type.Name == "<Module>") continue;
            if (!type.IsPublic) continue;

            // Root assembly public types in library mode: mark constructed
            // (external callers may instantiate public types)
            MarkTypeConstructed(type);
            foreach (var method in type.Methods)
            {
                if (method.IsPublic || method.IsFamily)
                    SeedMethod(method);
            }
        }
    }

    /// <summary>
    /// RTA: Mark types that are implicitly constructed by runtime internals.
    /// These types are created without explicit newobj in user/BCL IL code
    /// (string literals, primitive boxing, array runtime, exception handling).
    /// Without this, their virtual dispatch overrides (ToString, Equals, GetHashCode)
    /// would not be seeded, causing UndeclaredFunction stubs.
    /// </summary>
    private void SeedImplicitlyConstructedTypes()
    {
        // Well-known BCL types created by runtime internals
        var implicitTypes = new[]
        {
            "System.String", "System.Object", "System.Type", "System.RuntimeType",
            "System.Exception", "System.NullReferenceException", "System.InvalidCastException",
            "System.IndexOutOfRangeException", "System.ArrayTypeMismatchException",
            "System.StackOverflowException", "System.OutOfMemoryException",
            "System.OverflowException", "System.InvalidOperationException",
            "System.ArgumentException", "System.ArgumentNullException",
            "System.ArgumentOutOfRangeException", "System.NotSupportedException",
            "System.NotImplementedException", "System.FormatException",
            "System.TypeInitializationException", "System.PlatformNotSupportedException",
            // Primitive types (implicitly constructed via boxing, literals, arithmetic)
            "System.Boolean", "System.Byte", "System.SByte",
            "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
            "System.Int64", "System.UInt64", "System.Single", "System.Double",
            "System.Char", "System.IntPtr", "System.UIntPtr", "System.Decimal",
            // Commonly constructed by runtime without explicit newobj
            "System.Text.StringBuilder",
        };

        foreach (var typeName in implicitTypes)
        {
            foreach (var (_, asm) in _assemblySet.LoadedAssemblies)
            {
                var typeDef = asm.MainModule.GetType(typeName);
                if (typeDef != null)
                {
                    MarkTypeConstructed(typeDef);
                    break;
                }
            }
        }
    }

    private void MarkTypeReachable(TypeDefinition type)
    {
        // BCL boundary filtering — deep internal types are not useful to compile
        if (IsBclBoundaryType(type))
            return;

        // Always add to reachable set (may already be there from MarkTypeForLayout)
        _result.ReachableTypes.Add(type);

        // Full processing guard: allows layout-only types to be upgraded later
        // when discovered through method bodies or construction sites.
        if (!_fullyProcessedTypes.Add(type))
            return;

        // Mark base types reachable (needed for struct layout / vtable)
        if (type.BaseType != null)
        {
            var baseTypeDef = TryResolve(type.BaseType);
            if (baseTypeDef != null)
                MarkTypeReachable(baseTypeDef);
        }

        // Mark interface types reachable
        foreach (var iface in type.Interfaces)
        {
            var ifaceDef = TryResolve(iface.InterfaceType);
            if (ifaceDef != null)
                MarkTypeReachable(ifaceDef);
        }

        // NOTE: Static constructor (cctor) is NOT seeded here.
        // Per ECMA-335 §II.10.5.3.3, cctor triggers only on:
        //   1. First static field access (ldsfld/stsfld/ldsflda)
        //   2. First static method invocation (call/callvirt on static method)
        //   3. First object construction (newobj → MarkTypeConstructed)
        // Seeding cctor unconditionally here caused exponential cascade explosion
        // (e.g., NuGetSimpleTest: 2 JSON calls → 30K methods → 21GB OOM).

        // Mark finalizer reachable
        var finalizer = type.Methods.FirstOrDefault(m => m.Name == "Finalize" && m.IsVirtual);
        if (finalizer != null)
            SeedMethod(finalizer);

        // Field type cascade: lightweight — only value types need struct layout.
        // Reference-type fields are pointers in C++ (8 bytes, forward-declared).
        // SanitizeFieldType() handles unknown pointer types as void*.
        // This cuts off the exponential cascade through reference-type field graphs.
        foreach (var field in type.Fields)
        {
            var fieldTypeDef = TryResolve(field.FieldType);
            if (fieldTypeDef == null) continue;

            if (fieldTypeDef.IsValueType || fieldTypeDef.IsEnum)
                MarkTypeForLayout(fieldTypeDef);
            // Reference types: skip — discovered through method bodies when actually used
        }

        // D.1: [DynamicallyAccessedMembers] on the type itself — seed members per flags
        var damFlags = GetDynamicallyAccessedMemberTypes(type.CustomAttributes);
        if (damFlags != 0)
            SeedDynamicallyAccessedMembers(type, damFlags);

        // D.1: [DynamicallyAccessedMembers] on fields of Type — seed the field's type
        foreach (var field in type.Fields)
        {
            if (field.FieldType.FullName == "System.Type")
            {
                var fieldDam = GetDynamicallyAccessedMemberTypes(field.CustomAttributes);
                if (fieldDam != 0)
                {
                    // The actual target type is unknown statically; mark this type's members
                    // as a conservative approximation (real flow analysis would track assignments)
                    SeedDynamicallyAccessedMembers(type, fieldDam);
                }
            }
        }

        // Populate subtype index: register this type under all ancestors + interfaces
        RegisterInSubtypeIndex(type);

        // RTA: virtual dispatch only for CONSTRUCTED types (not merely reachable).
        // MarkDispatchedOverrides is called from MarkTypeConstructed, not here.
    }

    /// <summary>
    /// Lightweight reachability: only adds the type to ReachableTypes for struct emission.
    /// Does NOT seed cctor/finalizer, does NOT cascade reference-type fields,
    /// does NOT process interfaces/DAM/subtype index.
    /// Used for field-type cascade: C++ only needs the struct layout for value-type fields
    /// (embedded in parent struct). Reference-type fields are pointers (forward-declared).
    /// Types initially layout-only can be upgraded to fully-reachable via MarkTypeReachable
    /// when discovered through method bodies or construction sites.
    /// </summary>
    private void MarkTypeForLayout(TypeDefinition type)
    {
        if (IsBclBoundaryType(type))
            return;

        if (!_result.ReachableTypes.Add(type))
            return;

        // Base types needed for inherited field layout
        if (type.BaseType != null)
        {
            var baseTypeDef = TryResolve(type.BaseType);
            if (baseTypeDef != null)
                MarkTypeForLayout(baseTypeDef);
        }

        // Only value-type fields need layout cascade (embedded in struct).
        // Reference-type fields are pointers — forward declaration sufficient.
        foreach (var field in type.Fields)
        {
            var fieldTypeDef = TryResolve(field.FieldType);
            if (fieldTypeDef != null && (fieldTypeDef.IsValueType || fieldTypeDef.IsEnum))
                MarkTypeForLayout(fieldTypeDef);
        }
    }

    /// <summary>
    /// Mark a type as constructed (actually instantiated via newobj/newarr/Activator/DAM).
    /// Only constructed types participate in virtual method dispatch (RTA model).
    /// This is the key distinction from CHA: merely reachable types don't dispatch.
    /// Also marks base types as constructed (a Derived instance IS-A Base instance,
    /// so Base virtual dispatch targets must include Derived overrides).
    /// </summary>
    private void MarkTypeConstructed(TypeDefinition type)
    {
        // Ensure reachable first (struct layout needed for any constructed type)
        MarkTypeReachable(type);

        if (!_result.ConstructedTypes.Add(type))
            return;

        // ECMA-335: object construction triggers cctor
        SeedCctorFor(type);

        // Only NOW dispatch virtual overrides — the core RTA distinction.
        // Types that are merely reachable (field types, cast targets) don't dispatch.
        MarkDispatchedOverrides(type);

        // Mark base types as constructed too: a Derived instance IS-A Base instance.
        // Without this, virtual dispatch on Base references wouldn't find Derived overrides.
        if (type.BaseType != null)
        {
            var baseTypeDef = TryResolve(type.BaseType);
            if (baseTypeDef != null)
                MarkTypeConstructed(baseTypeDef);
        }
    }

    /// <summary>
    /// Seed a type's static constructor (.cctor) if it exists.
    /// Called only at ECMA-335 §II.10.5.3.3 trigger points:
    ///   1. First static field access (ldsfld/stsfld/ldsflda)
    ///   2. First static method invocation
    ///   3. First object construction (MarkTypeConstructed)
    /// This lazy seeding prevents exponential cascade through cctor chains
    /// (the primary explosion source for NuGet-dependent assemblies).
    /// </summary>
    private void SeedCctorFor(TypeDefinition? type)
    {
        if (type == null) return;
        var cctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic);
        if (cctor != null)
            SeedMethod(cctor);
    }

    /// <summary>
    /// Register a type in the subtype index under all its ancestors and interfaces.
    /// This enables O(1) lookup of subtypes when dispatching virtual slots.
    /// </summary>
    private void RegisterInSubtypeIndex(TypeDefinition type)
    {
        // Register under own FullName (a type is its own subtype for dispatch purposes)
        AddToSubtypeIndex(type.FullName, type);

        // Walk base type chain
        var current = type.BaseType;
        while (current != null)
        {
            var baseName = current is GenericInstanceType git
                ? git.ElementType.FullName
                : current.FullName;
            AddToSubtypeIndex(baseName, type);

            var baseDef = TryResolve(current);
            current = baseDef?.BaseType;
        }

        // Register under all interfaces (recursive)
        RegisterInterfacesInIndex(type, type);
    }

    private void RegisterInterfacesInIndex(TypeDefinition rootType, TypeDefinition type)
    {
        foreach (var iface in type.Interfaces)
        {
            var ifaceName = iface.InterfaceType is GenericInstanceType git
                ? git.ElementType.FullName
                : iface.InterfaceType.FullName;
            AddToSubtypeIndex(ifaceName, rootType);

            var ifaceDef = TryResolve(iface.InterfaceType);
            if (ifaceDef != null)
                RegisterInterfacesInIndex(rootType, ifaceDef);
        }
        // Also check base type's interfaces
        if (type.BaseType != null)
        {
            var baseDef = TryResolve(type.BaseType);
            if (baseDef != null)
                RegisterInterfacesInIndex(rootType, baseDef);
        }
    }

    private void AddToSubtypeIndex(string ancestorFullName, TypeDefinition subtype)
    {
        if (!_subtypeIndex.TryGetValue(ancestorFullName, out var list))
        {
            list = new List<TypeDefinition>();
            _subtypeIndex[ancestorFullName] = list;
        }
        // Avoid duplicates (same type registered multiple times via different interface paths)
        if (!list.Contains(subtype))
            list.Add(subtype);
    }

    /// <summary>
    /// Track a virtual method as dispatched and mark overrides in CONSTRUCTED subtypes only.
    /// RTA model: only types that are actually instantiated can be runtime dispatch targets.
    /// Uses subtype index for O(subtypes) instead of O(all reachable types).
    /// </summary>
    private void DispatchVirtualSlot(MethodDefinition method)
    {
        var key = $"{method.DeclaringType.FullName}::{method.Name}/{method.Parameters.Count}";
        if (!_dispatchedSlotKeys.Add(key))
            return;

        _dispatchedVirtualSlots.Add((method.Name, method.Parameters.Count, method.DeclaringType));

        // RTA: only check CONSTRUCTED subtypes for virtual dispatch
        if (_subtypeIndex.TryGetValue(method.DeclaringType.FullName, out var subtypes))
        {
            foreach (var type in subtypes.ToArray())
            {
                if (_result.ConstructedTypes.Contains(type))
                    MarkOverrideIfExists(type, method.Name, method.Parameters.Count);
            }
        }
    }

    /// <summary>
    /// When a type becomes CONSTRUCTED, check if it overrides any dispatched virtual slot.
    /// Called from MarkTypeConstructed — NOT from MarkTypeReachable (key RTA distinction).
    /// </summary>
    private void MarkDispatchedOverrides(TypeDefinition type)
    {
        // Snapshot: MarkOverrideIfExists → SeedMethod → DispatchVirtualSlot can add entries
        var snapshot = _dispatchedVirtualSlots.ToArray();
        foreach (var (methodName, paramCount, declaringType) in snapshot)
        {
            // Check if this type is a subtype of the declaring type via the index
            if (_subtypeIndex.TryGetValue(declaringType.FullName, out var subtypes)
                && subtypes.Contains(type))
            {
                MarkOverrideIfExists(type, methodName, paramCount);
            }
        }
    }

    /// <summary>
    /// Check if <paramref name="type"/> inherits from or implements <paramref name="target"/>.
    /// Used to narrow virtual dispatch to only relevant subtypes.
    /// </summary>
    private bool InheritsFromOrImplements(TypeDefinition type, TypeDefinition target)
    {
        var key = (type.FullName, target.FullName);
        if (_inheritsCache.TryGetValue(key, out var cached))
            return cached;

        // Walk base type chain
        var current = type;
        while (current != null)
        {
            if (current.FullName == target.FullName)
            {
                _inheritsCache[key] = true;
                return true;
            }
            if (current.BaseType == null) break;
            current = TryResolve(current.BaseType);
        }

        // Check interfaces (recursive — interfaces can extend interfaces)
        var result = ImplementsInterface(type, target.FullName);
        _inheritsCache[key] = result;
        return result;
    }

    private bool ImplementsInterface(TypeDefinition type, string targetFullName)
    {
        foreach (var iface in type.Interfaces)
        {
            var ifaceRef = iface.InterfaceType;
            // Handle generic instances: compare element type name
            var ifaceName = ifaceRef is GenericInstanceType git
                ? git.ElementType.FullName
                : ifaceRef.FullName;
            if (ifaceName == targetFullName) return true;

            var ifaceDef = TryResolve(ifaceRef);
            if (ifaceDef != null && ImplementsInterface(ifaceDef, targetFullName))
                return true;
        }
        // Also check base type's interfaces
        if (type.BaseType != null)
        {
            var baseDef = TryResolve(type.BaseType);
            if (baseDef != null && ImplementsInterface(baseDef, targetFullName))
                return true;
        }
        return false;
    }

    private void MarkOverrideIfExists(TypeDefinition type, string methodName, int paramCount)
    {
        // Mark ALL matching overrides, not just the first.
        // Multiple overloads can share (name, paramCount) with different param types
        // (e.g. DecoderNLS.GetChars(byte[],int,int,char[],int) vs GetChars(byte*,int,char*,int,bool)).
        // Using FirstOrDefault would only mark one, leaving the other unreachable
        // and causing vtable fallback to the base class — potential infinite recursion.
        foreach (var overrideMethod in type.Methods.Where(m =>
            m.IsVirtual && m.Name == methodName && m.Parameters.Count == paramCount))
        {
            SeedMethod(overrideMethod);
        }
    }

    private bool IsUserAssembly(TypeDefinition type)
    {
        return type.Module.Assembly.Name.Name == _assemblySet.RootAssemblyName;
    }

    /// <summary>
    /// Primitive types that map directly to C++ primitives.
    /// Their struct definitions are NOT emitted (they use C++ built-in types),
    /// but their methods ARE compiled from BCL IL (CompareTo, Equals, etc.).
    /// </summary>
    internal static readonly HashSet<string> PrimitiveTypeNames = new()
    {
        "System.Void", "System.Boolean", "System.Byte", "System.SByte",
        "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64", "System.Single", "System.Double",
        "System.Char", "System.IntPtr", "System.UIntPtr",
    };

    /// <summary>
    /// Filter out BCL types that should not be compiled to C++.
    /// Unity IL2CPP model: ALL BCL types with IL bodies compile to C++.
    /// Primitive types pass through (their methods compile, but struct is skipped).
    /// Only filter compiler internals and module-level types.
    /// </summary>
    private bool IsBclBoundaryType(TypeDefinition type)
    {
        // Non-BCL types always pass through
        var assemblyName = type.Module.Assembly.Name.Name;
        if (!IsBclAssembly(assemblyName))
            return false;

        // Primitive types pass through — methods compile from BCL IL,
        // struct emission is skipped by the code generator (IsPrimitiveType flag)
        // System.Void is the exception — no useful methods to compile
        if (type.FullName == "System.Void")
            return true;

        // Filter module-level types only
        var fullName = type.FullName;
        if (fullName == "<Module>")
            return true;
        // Note: <PrivateImplementationDetails> is NOT filtered — BCL code uses it for
        // static array initialization data, and we need its statics in generated code.

        // Exclude CLR-internal / AOT-incompatible namespaces.
        // These types require JIT or CLR-internal infrastructure that cannot work in AOT.
        // Excluding them prevents cascading generic specializations (e.g., List<EventSource>).
        if (IsAotExcludedNamespace(fullName))
            return true;

        // Allow all other BCL types through — they compile from IL
        return false;
    }

    /// <summary>
    /// Types/namespaces that are CLR-internal or require JIT — useless in AOT.
    /// Excluding these prevents cascading generic specializations (e.g., List&lt;EventSource&gt;).
    /// </summary>
    private static bool IsAotExcludedNamespace(string typeFullName)
    {
        // EventSource/EventPipe/ETW — CLR event tracing infrastructure, requires CLR runtime
        if (typeFullName.StartsWith("System.Diagnostics.Tracing."))
            return true;

        // Reflection.Emit — fundamentally requires JIT, incompatible with AOT
        if (typeFullName.StartsWith("System.Reflection.Emit."))
            return true;

        // AssemblyLoadContext — CLR-only dynamic assembly loading, no AOT equivalent
        if (typeFullName.StartsWith("System.Runtime.Loader."))
            return true;

        // PortableThreadPool — CLR managed thread pool internals; CIL2CPP has its own thread pool
        if (typeFullName.StartsWith("System.Threading.PortableThreadPool"))
            return true;

        // StackFrame/StackFrameHelper — CLR-internal stack walking; CIL2CPP runtime
        // provides its own stack trace via DbgHelp (Windows) / backtrace (Linux)
        if (typeFullName.StartsWith("System.Diagnostics.StackFrame"))
            return true;

        // Platform-specific SIMD intrinsics — require hardware feature detection
        // that doesn't exist in AOT. Vector128/256/512 types kept (used as fields by System.Buffers)
        if (typeFullName.StartsWith("System.Runtime.Intrinsics.X86.") ||
            typeFullName.StartsWith("System.Runtime.Intrinsics.Arm.") ||
            typeFullName.StartsWith("System.Runtime.Intrinsics.Wasm."))
            return true;

        // Interop P/Invoke wrappers for OS-specific subsystems
        // Note: Interop/Kernel32 NOT excluded (LowLevelMonitor embeds CRITICAL_SECTION)
        // Note: Top-level Interop types NOT excluded (INPUT_RECORD embedded in ConsolePal)
        if (typeFullName.StartsWith("Interop/Advapi32") ||
            typeFullName.StartsWith("Interop/NtDll") ||
            typeFullName.StartsWith("Interop/Ucrtbase") ||
            typeFullName.StartsWith("Interop/BCrypt") ||
            typeFullName.StartsWith("Interop/Ole32") ||
            typeFullName.StartsWith("Interop/Globalization") ||
            typeFullName.StartsWith("Interop/User32"))
            return true;

        // Internal.Win32 — Windows registry/interop, not useful in AOT
        if (typeFullName.StartsWith("Internal.Win32."))
            return true;

        // COM interop — requires COM runtime infrastructure
        if (typeFullName.StartsWith("System.ComAwareWeakReference"))
            return true;

        // Design-time infrastructure — TypeConverter, TypeDescriptor, etc.
        // Runtime type inspection/conversion used by designers, not AOT
        if (typeFullName.StartsWith("System.ComponentModel."))
            return true;

        // Legacy serialization — BinaryFormatter, IFormatter, DataContract
        // Deprecated, security risk, requires runtime reflection
        if (typeFullName.StartsWith("System.Runtime.Serialization."))
            return true;

        // Runtime code compilation — requires JIT, fundamentally AOT-incompatible
        if (typeFullName.StartsWith("System.CodeDom"))
            return true;

        // .resx resource loading — ResourceReader/ResourceManager require assembly metadata
        // CIL2CPP uses SR icall for resource strings instead
        if (typeFullName.StartsWith("System.Resources."))
            return true;

        // Regex symbolic engine — recursive generic types (BDD, DerivativeEffect, SymbolicRegexNode)
        // create infinitely nested generic instantiations. These are internal implementation details.
        if (typeFullName.StartsWith("System.Text.RegularExpressions.Symbolic."))
            return true;

        return false;
    }

    private static bool IsBclAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("System.") ||
               assemblyName == "System" ||
               assemblyName == "mscorlib" ||
               assemblyName == "netstandard" ||
               assemblyName.StartsWith("Microsoft.");
    }

    private void ProcessMethod(MethodDefinition method)
    {
        var key = GetMethodKey(method);
        if (!_processedMethods.Add(key))
            return;

        // D.1: Scan [DynamicallyAccessedMembers] on method parameters and return type.
        // This must run even for methods without bodies (abstract, extern, etc.)
        ProcessMethodDamAttributes(method);

        if (!method.HasBody)
            return;

        foreach (var instr in method.Body.Instructions)
        {
            switch (instr.OpCode.Code)
            {
                // Method calls: trigger cctor if calling a static method (ECMA-335 §II.10.5.3.3)
                case Code.Call:
                case Code.Callvirt:
                    ProcessMethodRef(instr.Operand as MethodReference);
                    if (instr.Operand is MethodReference callRef)
                    {
                        var callMethodDef = TryResolveMethod(callRef);
                        if (callMethodDef != null && callMethodDef.IsStatic)
                            SeedCctorFor(TryResolve(callRef.DeclaringType));
                    }
                    break;

                // Function pointer loads: no cctor trigger (just loads pointer, no invocation)
                case Code.Ldftn:
                case Code.Ldvirtftn:
                    ProcessMethodRef(instr.Operand as MethodReference);
                    break;

                // RTA: newobj = object construction site → mark type as CONSTRUCTED
                case Code.Newobj:
                    ProcessMethodRef(instr.Operand as MethodReference);
                    if (instr.Operand is MethodReference ctorRef)
                    {
                        var ctorTypeDef = TryResolve(ctorRef.DeclaringType);
                        if (ctorTypeDef != null)
                            MarkTypeConstructed(ctorTypeDef);
                    }
                    break;

                // RTA: newarr = array construction site → mark element type as CONSTRUCTED
                case Code.Newarr:
                    ProcessTypeRef(instr.Operand as TypeReference);
                    if (instr.Operand is TypeReference newarrElemType)
                    {
                        var elemDef = TryResolve(newarrElemType);
                        if (elemDef != null)
                            MarkTypeConstructed(elemDef);
                    }
                    break;

                // RTA: box = creates a boxed object on the heap → mark as CONSTRUCTED
                case Code.Box:
                    ProcessTypeRef(instr.Operand as TypeReference);
                    if (instr.Operand is TypeReference boxType)
                    {
                        var boxDef = TryResolve(boxType);
                        if (boxDef != null)
                            MarkTypeConstructed(boxDef);
                    }
                    break;

                // Type references (non-constructing)
                case Code.Castclass:
                case Code.Isinst:
                case Code.Unbox:
                case Code.Unbox_Any:
                case Code.Initobj:
                case Code.Ldobj:
                case Code.Stobj:
                case Code.Ldelem_Any:
                case Code.Stelem_Any:
                case Code.Ldelema:
                case Code.Sizeof:
                case Code.Constrained:
                    ProcessTypeRef(instr.Operand as TypeReference);
                    // Also resolve explicit interface implementations on the constrained type.
                    // The next instruction is call/callvirt with the interface method — we need
                    // to find and mark the concrete implementation on the constrained type.
                    ProcessConstrainedCall(instr.Operand as TypeReference, instr.Next);
                    break;

                // Instance field references: no cctor trigger
                case Code.Ldfld:
                case Code.Stfld:
                case Code.Ldflda:
                    ProcessFieldRef(instr.Operand as FieldReference);
                    break;

                // Static field access: triggers cctor (ECMA-335 §II.10.5.3.3)
                case Code.Ldsfld:
                case Code.Stsfld:
                case Code.Ldsflda:
                    ProcessFieldRef(instr.Operand as FieldReference);
                    if (instr.Operand is FieldReference sfldRef)
                        SeedCctorFor(TryResolve(sfldRef.DeclaringType));
                    break;

                // Ldtoken can be field, type, or method
                case Code.Ldtoken:
                    if (instr.Operand is MethodReference tokenMethod)
                        ProcessMethodRef(tokenMethod);
                    else if (instr.Operand is TypeReference tokenType)
                        ProcessTypeRef(tokenType);
                    else if (instr.Operand is FieldReference tokenField)
                        ProcessFieldRef(tokenField);
                    break;
            }
        }
    }

    private void ProcessMethodRef(MethodReference? methodRef)
    {
        if (methodRef == null) return;

        // Mark the declaring type reachable
        var declaringTypeDef = TryResolve(methodRef.DeclaringType);
        if (declaringTypeDef != null)
            MarkTypeReachable(declaringTypeDef);

        // Resolve and seed the target method
        var methodDef = TryResolveMethod(methodRef);
        if (methodDef != null)
            SeedMethod(methodDef);

        // Activator.CreateInstance<T>() — also seed T..ctor() for AOT.
        // The IRBuilder replaces this with gc::alloc + .ctor call, but the ctor
        // won't be compiled unless we discover it here. Without this, field
        // initializers (e.g., SafeFileHandle._fileType = -1) are lost.
        // RTA: Activator.CreateInstance = construction site → mark CONSTRUCTED.
        if (methodRef.DeclaringType.FullName == "System.Activator"
            && methodRef.Name == "CreateInstance"
            && methodRef.Parameters.Count == 0
            && methodRef is GenericInstanceMethod gim
            && gim.GenericArguments.Count == 1)
        {
            var typeArg = gim.GenericArguments[0];
            var typeDef = TryResolve(typeArg);
            if (typeDef != null)
            {
                MarkTypeConstructed(typeDef);
                var defaultCtor = typeDef.Methods
                    .FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
                if (defaultCtor != null)
                    SeedMethod(defaultCtor);
            }
        }
    }

    /// <summary>
    /// When a constrained. prefix is followed by call/callvirt, resolve the interface method
    /// to the concrete type's explicit interface implementation and mark it reachable.
    /// Without this, static abstract interface methods (e.g., IUtfChar&lt;Char&gt;.CastFrom)
    /// remain as stubs because only the abstract interface method (with no body) is discovered.
    /// </summary>
    private void ProcessConstrainedCall(TypeReference? constrainedType, Mono.Cecil.Cil.Instruction? nextInstr)
    {
        if (constrainedType == null || nextInstr == null) return;
        if (nextInstr.OpCode.Code is not (Code.Call or Code.Callvirt)) return;

        var methodRef = nextInstr.Operand as MethodReference;
        if (methodRef == null) return;

        var constrainedTypeDef = TryResolve(constrainedType);
        if (constrainedTypeDef == null) return;

        var interfaceMethodName = methodRef.Name;

        // Search for explicit interface implementations that match the method name
        foreach (var method in constrainedTypeDef.Methods)
        {
            if (!method.IsStatic) continue;

            // Exact match or explicit interface impl suffix match
            // e.g., "CastFrom" matches "System.IUtfChar<System.Char>.CastFrom"
            bool nameMatch = method.Name == interfaceMethodName;
            if (!nameMatch)
            {
                var lastDot = method.Name.LastIndexOf('.');
                if (lastDot >= 0 && method.Name.AsSpan(lastDot + 1).SequenceEqual(interfaceMethodName))
                    nameMatch = true;
            }

            if (nameMatch && method.Parameters.Count == methodRef.Parameters.Count)
                SeedMethod(method);
        }
    }

    private void ProcessTypeRef(TypeReference? typeRef)
    {
        if (typeRef == null) return;

        var typeDef = TryResolve(typeRef);
        if (typeDef != null)
            MarkTypeReachable(typeDef);
    }

    private void ProcessFieldRef(FieldReference? fieldRef)
    {
        if (fieldRef == null) return;

        var declaringTypeDef = TryResolve(fieldRef.DeclaringType);
        if (declaringTypeDef != null)
            MarkTypeReachable(declaringTypeDef);

        var fieldTypeDef = TryResolve(fieldRef.FieldType);
        if (fieldTypeDef != null)
            MarkTypeReachable(fieldTypeDef);
    }

    // ===== D.1: [DynamicallyAccessedMembers] support =====

    private const string DamAttributeName =
        "System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute";

    /// <summary>
    /// DynamicallyAccessedMemberTypes flags (System.Diagnostics.CodeAnalysis).
    /// </summary>
    [Flags]
    private enum DamFlags
    {
        None = 0,
        PublicParameterlessConstructor = 1,
        PublicConstructors = 3,
        NonPublicConstructors = 4,
        PublicMethods = 8,
        NonPublicMethods = 16,
        PublicFields = 32,
        NonPublicFields = 64,
        PublicNestedTypes = 128,
        NonPublicNestedTypes = 256,
        PublicProperties = 512,
        NonPublicProperties = 1024,
        PublicEvents = 2048,
        NonPublicEvents = 4096,
        Interfaces = 8192,
        All = -1,
    }

    /// <summary>
    /// Extract DynamicallyAccessedMemberTypes flags from a collection of custom attributes.
    /// Returns 0 if the attribute is not present.
    /// </summary>
    private static int GetDynamicallyAccessedMemberTypes(
        Mono.Collections.Generic.Collection<CustomAttribute>? attrs)
    {
        if (attrs == null || attrs.Count == 0) return 0;

        foreach (var attr in attrs)
        {
            if (attr.AttributeType.FullName != DamAttributeName) continue;
            if (attr.ConstructorArguments.Count > 0)
            {
                var arg = attr.ConstructorArguments[0];
                if (arg.Value is int intVal) return intVal;
                // Enum values may come as other integer types
                try { return Convert.ToInt32(arg.Value); } catch { return -1; }
            }
        }
        return 0;
    }

    /// <summary>
    /// Scan [DynamicallyAccessedMembers] on method parameters and return type.
    /// When DAM is found, resolve the parameter's type and seed the required members.
    /// </summary>
    private void ProcessMethodDamAttributes(MethodDefinition method)
    {
        // Check parameters
        foreach (var param in method.Parameters)
        {
            if (!param.HasCustomAttributes) continue;
            var flags = GetDynamicallyAccessedMemberTypes(param.CustomAttributes);
            if (flags == 0) continue;

            // The parameter should be of type System.Type (or derived).
            // Resolve the type constraint statically if possible.
            var paramTypeDef = TryResolve(param.ParameterType);
            if (paramTypeDef != null)
                SeedDynamicallyAccessedMembers(paramTypeDef, flags);
        }

        // Check return type
        if (method.MethodReturnType.HasCustomAttributes)
        {
            var flags = GetDynamicallyAccessedMemberTypes(method.MethodReturnType.CustomAttributes);
            if (flags != 0)
            {
                var retTypeDef = TryResolve(method.ReturnType);
                if (retTypeDef != null)
                    SeedDynamicallyAccessedMembers(retTypeDef, flags);
            }
        }

        // Check generic parameters
        if (method.HasGenericParameters)
        {
            foreach (var gp in method.GenericParameters)
            {
                if (!gp.HasCustomAttributes) continue;
                var flags = GetDynamicallyAccessedMemberTypes(gp.CustomAttributes);
                if (flags == 0) continue;

                // For generic parameters, seed constraints if available
                foreach (var constraint in gp.Constraints)
                {
                    var constraintDef = TryResolve(constraint.ConstraintType);
                    if (constraintDef != null)
                        SeedDynamicallyAccessedMembers(constraintDef, flags);
                }
            }
        }
    }

    /// <summary>
    /// Seed members of a type based on DynamicallyAccessedMemberTypes flags.
    /// This prevents tree-shaking from removing members needed by reflection or serialization.
    /// RTA: when constructors are seeded, mark the type as constructed (reflection may instantiate).
    /// </summary>
    private void SeedDynamicallyAccessedMembers(TypeDefinition type, int memberTypes)
    {
        var flags = (DamFlags)memberTypes;

        // All = seed everything
        if (flags == DamFlags.All)
            flags = (DamFlags)0x3FFF; // all individual bits

        // RTA: if DAM seeds constructors, mark type as constructed (reflection may call .ctor)
        bool seedsConstructors = flags.HasFlag(DamFlags.PublicParameterlessConstructor)
            || (flags & DamFlags.PublicConstructors) == DamFlags.PublicConstructors
            || flags.HasFlag(DamFlags.NonPublicConstructors);
        if (seedsConstructors)
            MarkTypeConstructed(type);

        // Constructors
        if (flags.HasFlag(DamFlags.PublicParameterlessConstructor))
        {
            var ctor = type.Methods.FirstOrDefault(m =>
                m.IsConstructor && !m.IsStatic && m.IsPublic && m.Parameters.Count == 0);
            if (ctor != null) SeedMethod(ctor);
        }
        if ((flags & DamFlags.PublicConstructors) == DamFlags.PublicConstructors)
        {
            foreach (var m in type.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.IsPublic))
                SeedMethod(m);
        }
        if (flags.HasFlag(DamFlags.NonPublicConstructors))
        {
            foreach (var m in type.Methods.Where(m => m.IsConstructor && !m.IsStatic && !m.IsPublic))
                SeedMethod(m);
        }

        // Methods
        if (flags.HasFlag(DamFlags.PublicMethods))
        {
            foreach (var m in type.Methods.Where(m => !m.IsConstructor && m.IsPublic))
                SeedMethod(m);
        }
        if (flags.HasFlag(DamFlags.NonPublicMethods))
        {
            foreach (var m in type.Methods.Where(m => !m.IsConstructor && !m.IsPublic))
                SeedMethod(m);
        }

        // Fields — mark field types reachable
        if (flags.HasFlag(DamFlags.PublicFields))
        {
            foreach (var f in type.Fields.Where(f => f.IsPublic))
            {
                var fTypeDef = TryResolve(f.FieldType);
                if (fTypeDef != null) MarkTypeReachable(fTypeDef);
            }
        }
        if (flags.HasFlag(DamFlags.NonPublicFields))
        {
            foreach (var f in type.Fields.Where(f => !f.IsPublic))
            {
                var fTypeDef = TryResolve(f.FieldType);
                if (fTypeDef != null) MarkTypeReachable(fTypeDef);
            }
        }

        // Nested types
        if (flags.HasFlag(DamFlags.PublicNestedTypes))
        {
            foreach (var nested in type.NestedTypes.Where(t => t.IsNestedPublic))
                MarkTypeReachable(nested);
        }
        if (flags.HasFlag(DamFlags.NonPublicNestedTypes))
        {
            foreach (var nested in type.NestedTypes.Where(t => !t.IsNestedPublic))
                MarkTypeReachable(nested);
        }

        // Properties → seed get_/set_ accessor methods
        if (flags.HasFlag(DamFlags.PublicProperties))
        {
            foreach (var prop in type.Properties.Where(p =>
                (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false)))
            {
                if (prop.GetMethod != null) SeedMethod(prop.GetMethod);
                if (prop.SetMethod != null) SeedMethod(prop.SetMethod);
            }
        }
        if (flags.HasFlag(DamFlags.NonPublicProperties))
        {
            foreach (var prop in type.Properties.Where(p =>
                !(p.GetMethod?.IsPublic ?? false) && !(p.SetMethod?.IsPublic ?? false)))
            {
                if (prop.GetMethod != null) SeedMethod(prop.GetMethod);
                if (prop.SetMethod != null) SeedMethod(prop.SetMethod);
            }
        }

        // Events → seed add_/remove_ methods
        if (flags.HasFlag(DamFlags.PublicEvents))
        {
            foreach (var evt in type.Events.Where(e => e.AddMethod?.IsPublic ?? false))
            {
                if (evt.AddMethod != null) SeedMethod(evt.AddMethod);
                if (evt.RemoveMethod != null) SeedMethod(evt.RemoveMethod);
            }
        }
        if (flags.HasFlag(DamFlags.NonPublicEvents))
        {
            foreach (var evt in type.Events.Where(e => !(e.AddMethod?.IsPublic ?? false)))
            {
                if (evt.AddMethod != null) SeedMethod(evt.AddMethod);
                if (evt.RemoveMethod != null) SeedMethod(evt.RemoveMethod);
            }
        }

        // Interfaces
        if (flags.HasFlag(DamFlags.Interfaces))
        {
            foreach (var iface in type.Interfaces)
            {
                var ifaceDef = TryResolve(iface.InterfaceType);
                if (ifaceDef != null) MarkTypeReachable(ifaceDef);
            }
        }
    }

    /// <summary>
    /// Apply rd.xml preservation rules — find matching types/methods and seed them.
    /// Reuses the DynamicallyAccessedMembers infrastructure for member-level seeding.
    /// </summary>
    private void ApplyPreservationRules()
    {
        if (_preservationRules == null) return;

        foreach (var rule in _preservationRules)
        {
            // Find the matching type across all loaded assemblies
            TypeDefinition? typeDef = null;
            foreach (var (_, asm) in _assemblySet.LoadedAssemblies)
            {
                if (rule.AssemblyName != null && asm.Name.Name != rule.AssemblyName)
                    continue;

                typeDef = asm.MainModule.GetType(rule.TypeName);
                if (typeDef != null) break;
            }

            if (typeDef == null) continue;

            MarkTypeReachable(typeDef);

            if (rule.MethodName != null)
            {
                // Seed a specific method
                foreach (var m in typeDef.Methods.Where(m => m.Name == rule.MethodName))
                    SeedMethod(m);
            }
            else if (rule.MemberTypes != 0)
            {
                // Seed members by DAM flags
                SeedDynamicallyAccessedMembers(typeDef, rule.MemberTypes);
            }
        }
    }

    private TypeDefinition? TryResolve(TypeReference typeRef)
    {
        try
        {
            // Handle generic instances
            if (typeRef is GenericInstanceType git)
            {
                // Resolve the element type
                var elemDef = TryResolve(git.ElementType);
                // Mark generic argument types as reachable
                foreach (var arg in git.GenericArguments)
                {
                    var argDef = TryResolve(arg);
                    if (argDef != null)
                        MarkTypeReachable(argDef);
                }
                return elemDef;
            }

            // Handle array types
            if (typeRef is ArrayType arrayType)
            {
                var elemDef = TryResolve(arrayType.ElementType);
                if (elemDef != null)
                    MarkTypeReachable(elemDef);
                return null; // Arrays are handled by the runtime
            }

            // Handle by-ref and pointer types
            if (typeRef is ByReferenceType byRef)
                return TryResolve(byRef.ElementType);
            if (typeRef is PointerType ptr)
                return TryResolve(ptr.ElementType);

            // Skip generic parameters (T, U, etc.)
            if (typeRef is GenericParameter)
                return null;

            // Cache Cecil Resolve() calls — these are expensive for cross-assembly references
            var cacheKey = typeRef.FullName;
            if (_resolveCache.TryGetValue(cacheKey, out var cachedResult))
                return cachedResult;

            var resolved = typeRef.Resolve();
            if (resolved != null)
            {
                // Ensure the assembly is loaded in our set
                _assemblySet.LoadAssembly(resolved.Module.Assembly.Name.Name);
            }
            _resolveCache[cacheKey] = resolved;
            return resolved;
        }
        catch
        {
            return null;
        }
    }

    private MethodDefinition? TryResolveMethod(MethodReference methodRef)
    {
        try
        {
            // Handle generic instance methods
            if (methodRef is GenericInstanceMethod gim)
            {
                foreach (var arg in gim.GenericArguments)
                    TryResolve(arg);
                return TryResolveMethod(gim.ElementMethod);
            }

            return methodRef.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static string GetMethodKey(MethodDefinition method)
    {
        return $"{method.DeclaringType.FullName}::{method.FullName}";
    }
}
