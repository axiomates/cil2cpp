namespace CIL2CPP.Core.IR;

/// <summary>
/// Classification flags for runtime-provided types.
/// A type can have multiple flags (e.g., RuntimeProvided | CoreRuntime | ReflectionAliased).
/// </summary>
[Flags]
public enum RuntimeTypeFlags
{
    None = 0,
    /// <summary>Struct layout from C++ runtime headers (skip struct emission).</summary>
    RuntimeProvided = 1 << 0,
    /// <summary>Instance methods blocked from IL compilation (provided by core_methods.cpp).</summary>
    CoreRuntime = 1 << 1,
    /// <summary>Aliased to different C++ struct with mismatched field layout (no field-access casts).</summary>
    ReflectionAliased = 1 << 2,
    /// <summary>ALL methods (instance AND static) blocked — QCall/CLR-internal wrappers.</summary>
    SkipAllMethods = 1 << 3,
    /// <summary>Using alias already defined in runtime header (skip both struct AND alias emission).</summary>
    HeaderAliased = 1 << 4,
    /// <summary>Exception type with runtime-provided TypeInfo.</summary>
    ExceptionType = 1 << 5,
    /// <summary>Stub TypeInfo for base_type references (ValueType, Enum, Delegate, MulticastDelegate).</summary>
    BaseTypeStub = 1 << 6,
    /// <summary>ALL instance methods unconditionally blocked (fundamental types + aliased-layout types).</summary>
    BlanketGated = 1 << 7,
}

/// <summary>
/// Descriptor for a runtime-known type. Each type is registered exactly once.
/// </summary>
public sealed class RuntimeTypeDescriptor
{
    /// <summary>IL full name (e.g., "System.Object").</summary>
    public required string ILFullName { get; init; }
    /// <summary>C++ using alias (e.g., "cil2cpp::Object"). Null if no alias needed.</summary>
    public string? CppAlias { get; init; }
    /// <summary>Runtime TypeInfo name (e.g., "cil2cpp::Exception_TypeInfo"). Null if no TypeInfo alias.</summary>
    public string? RuntimeTypeInfoName { get; init; }
    /// <summary>Classification flags.</summary>
    public RuntimeTypeFlags Flags { get; init; }

    /// <summary>Mangled C++ name (e.g., "System_Object").</summary>
    public string MangledName => CppNameMapper.MangleTypeName(ILFullName);

    public bool Has(RuntimeTypeFlags flag) => (Flags & flag) == flag;
}

/// <summary>
/// Centralized registry for all runtime-known type classifications.
/// Single source of truth — replaces scattered HashSets and yield-return methods
/// in IRBuilder.cs, CppCodeGenerator.Header.cs, and CppCodeGenerator.Source.cs.
/// </summary>
public static class RuntimeTypeRegistry
{
    private static readonly Dictionary<string, RuntimeTypeDescriptor> _byILName = new();
    private static readonly List<RuntimeTypeDescriptor> _all = new(); // preserves registration order
    private static readonly HashSet<string> _headerAliasedMangled = new(); // pre-built for mangled-name lookup

    static RuntimeTypeRegistry()
    {
        // ===== Core types =====
        // Registration order matches the old GetRuntimeProvidedTypeAliases() yield order
        // so that using-alias emission order is identical.
        Register("System.Object", "cil2cpp::Object",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated,
            typeInfo: "cil2cpp::System::Object_TypeInfo");
        Register("System.String", "cil2cpp::String",
            RuntimeTypeFlags.RuntimeProvided,
            typeInfo: "cil2cpp::System::String_TypeInfo");
        Register("System.Array", "cil2cpp::Array",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated);
        Register("System.Delegate", "cil2cpp::Delegate",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated | RuntimeTypeFlags.BaseTypeStub);
        Register("System.MulticastDelegate", "cil2cpp::Delegate",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated | RuntimeTypeFlags.BaseTypeStub);
        Register("System.Type", "cil2cpp::Object",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime);
        Register("System.RuntimeType", "cil2cpp::Type",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime);
        Register("System.Attribute", "cil2cpp::Object",
            RuntimeTypeFlags.None); // only needs CppAlias for using directive
        Register("System.Enum", "cil2cpp::Object",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BaseTypeStub);
        Register("System.ValueType", "cil2cpp::Object",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated | RuntimeTypeFlags.BaseTypeStub);

        // ===== Exception hierarchy =====
        Register("System.Exception", "cil2cpp::Exception",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated | RuntimeTypeFlags.ExceptionType,
            typeInfo: "cil2cpp::Exception_TypeInfo");
        RegisterException("System.NullReferenceException", "cil2cpp::NullReferenceException");
        RegisterException("System.IndexOutOfRangeException", "cil2cpp::IndexOutOfRangeException");
        RegisterException("System.InvalidCastException", "cil2cpp::InvalidCastException");
        RegisterException("System.InvalidOperationException", "cil2cpp::InvalidOperationException");
        RegisterException("System.ObjectDisposedException", "cil2cpp::ObjectDisposedException");
        RegisterException("System.NotSupportedException", "cil2cpp::NotSupportedException");
        RegisterException("System.PlatformNotSupportedException", "cil2cpp::PlatformNotSupportedException");
        RegisterException("System.NotImplementedException", "cil2cpp::NotImplementedException");
        RegisterException("System.ArgumentException", "cil2cpp::ArgumentException");
        RegisterException("System.ArgumentNullException", "cil2cpp::ArgumentNullException");
        RegisterException("System.ArgumentOutOfRangeException", "cil2cpp::ArgumentOutOfRangeException");
        RegisterException("System.ArithmeticException", "cil2cpp::ArithmeticException");
        RegisterException("System.OverflowException", "cil2cpp::OverflowException");
        RegisterException("System.DivideByZeroException", "cil2cpp::DivideByZeroException");
        RegisterException("System.FormatException", "cil2cpp::FormatException");
        RegisterException("System.RankException", "cil2cpp::RankException");
        RegisterException("System.ArrayTypeMismatchException", "cil2cpp::ArrayTypeMismatchException");
        RegisterException("System.TypeInitializationException", "cil2cpp::TypeInitializationException");
        RegisterException("System.TimeoutException", "cil2cpp::TimeoutException");
        RegisterException("System.AggregateException", "cil2cpp::AggregateException");
        RegisterException("System.OperationCanceledException", "cil2cpp::OperationCanceledException");
        RegisterException("System.Threading.Tasks.TaskCanceledException", "cil2cpp::TaskCanceledException");
        RegisterException("System.Collections.Generic.KeyNotFoundException", "cil2cpp::KeyNotFoundException");

        // ===== Async non-generic types (struct from runtime, methods compile from IL) =====
        Register("System.Threading.Tasks.Task", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.HeaderAliased);
        Register("System.Runtime.CompilerServices.TaskAwaiter", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.HeaderAliased);
        Register("System.Runtime.CompilerServices.AsyncTaskMethodBuilder", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.HeaderAliased);
        Register("System.Threading.Tasks.ValueTask", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.HeaderAliased);
        Register("System.Runtime.CompilerServices.ValueTaskAwaiter", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.HeaderAliased);
        Register("System.Runtime.CompilerServices.AsyncIteratorMethodBuilder", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.HeaderAliased);

        // ===== Reflection base types (aliased to runtime structs, all instance methods blocked) =====
        // CppAlias is null: the using aliases are defined in runtime headers (memberinfo.h),
        // NOT emitted by codegen. Only the Runtime* subtypes below get codegen-emitted aliases.
        Register("System.Reflection.MemberInfo", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.MethodBase", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.MethodInfo", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.FieldInfo", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.ParameterInfo", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);

        // ===== Reflection runtime subtypes (aliased to runtime structs) =====
        Register("System.Reflection.RuntimeMethodInfo", "cil2cpp::ManagedMethodInfo",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.RuntimeFieldInfo", "cil2cpp::ManagedFieldInfo",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.RuntimeConstructorInfo", "cil2cpp::ManagedMethodInfo",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.TypeInfo", "cil2cpp::Type",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);

        // ===== Reflection: Assembly + PropertyInfo (aliased to runtime structs) =====
        Register("System.Reflection.RuntimePropertyInfo", "cil2cpp::ManagedPropertyInfo",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.Assembly", "cil2cpp::ManagedAssembly",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);
        Register("System.Reflection.RuntimeAssembly", "cil2cpp::ManagedAssembly",
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.ReflectionAliased | RuntimeTypeFlags.HeaderAliased);

        // ===== Threading =====
        Register("System.Threading.Thread", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated | RuntimeTypeFlags.HeaderAliased);

        // ===== TypedReference + ArgIterator (all methods handled by runtime/icall) =====
        Register("System.TypedReference", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.SkipAllMethods | RuntimeTypeFlags.HeaderAliased);
        Register("System.ArgIterator", null,
            RuntimeTypeFlags.RuntimeProvided | RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.SkipAllMethods | RuntimeTypeFlags.HeaderAliased);

        // ===== RuntimeTypeHandle (all methods blocked — QCall wrappers) =====
        Register("System.RuntimeTypeHandle", null,
            RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.SkipAllMethods);

        // ===== MethodTable (Unsafe-intrinsic field reads — not compilable from IL) =====
        Register("System.Runtime.CompilerServices.MethodTable", null,
            RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.SkipAllMethods);

        // ===== DefaultBinder (array type mismatches in generic IL patterns) =====
        Register("System.DefaultBinder", null,
            RuntimeTypeFlags.CoreRuntime | RuntimeTypeFlags.BlanketGated);

        // ===== WaitHandle hierarchy (aliases in waithandle.h, NOT RuntimeProvided) =====
        Register("System.Threading.WaitHandle", "cil2cpp::ManagedWaitHandle",
            RuntimeTypeFlags.HeaderAliased);
        Register("System.Threading.EventWaitHandle", "cil2cpp::ManagedEventWaitHandle",
            RuntimeTypeFlags.HeaderAliased);
        Register("System.Threading.ManualResetEvent", "cil2cpp::ManagedEventWaitHandle",
            RuntimeTypeFlags.HeaderAliased);
        Register("System.Threading.AutoResetEvent", "cil2cpp::ManagedEventWaitHandle",
            RuntimeTypeFlags.HeaderAliased);
        Register("System.Threading.Mutex", "cil2cpp::ManagedMutex",
            RuntimeTypeFlags.HeaderAliased);
        Register("System.Threading.Semaphore", "cil2cpp::ManagedSemaphore",
            RuntimeTypeFlags.HeaderAliased);

        // ===== Cancellation (header-aliased only, NOT RuntimeProvided) =====
        Register("System.Threading.CancellationTokenSource", null,
            RuntimeTypeFlags.HeaderAliased);
        Register("System.Threading.CancellationToken", null,
            RuntimeTypeFlags.HeaderAliased);

        // Pre-build mangled name lookup for HeaderAliased
        foreach (var desc in _all)
        {
            if (desc.Has(RuntimeTypeFlags.HeaderAliased))
                _headerAliasedMangled.Add(desc.MangledName);
        }
    }

    // ===== Registration helpers =====

    private static void Register(string ilFullName, string? cppAlias, RuntimeTypeFlags flags, string? typeInfo = null)
    {
        var desc = new RuntimeTypeDescriptor
        {
            ILFullName = ilFullName,
            CppAlias = cppAlias,
            Flags = flags,
            RuntimeTypeInfoName = typeInfo,
        };
        _byILName[ilFullName] = desc;
        _all.Add(desc);
    }

    /// <summary>
    /// Register an exception type (ExceptionType flag + TypeInfo alias derived from CppAlias).
    /// Exception subtypes are NOT RuntimeProvided (their struct is in the runtime C++ hierarchy).
    /// </summary>
    private static void RegisterException(string ilFullName, string cppAlias)
    {
        Register(ilFullName, cppAlias, RuntimeTypeFlags.ExceptionType,
            typeInfo: cppAlias.Replace("::", "::") + "_TypeInfo");
        // TypeInfo name follows pattern: cil2cpp::NullReferenceException → cil2cpp::NullReferenceException_TypeInfo
    }

    // ===== Query methods (replace scattered HashSets) =====

    public static bool IsRuntimeProvided(string ilFullName)
        => _byILName.TryGetValue(ilFullName, out var d) && d.Has(RuntimeTypeFlags.RuntimeProvided);

    public static bool IsCoreRuntime(string ilFullName)
        => _byILName.TryGetValue(ilFullName, out var d) && d.Has(RuntimeTypeFlags.CoreRuntime);

    public static bool IsReflectionAliased(string ilFullName)
        => _byILName.TryGetValue(ilFullName, out var d) && d.Has(RuntimeTypeFlags.ReflectionAliased);

    public static bool IsSkipAllMethods(string ilFullName)
        => _byILName.TryGetValue(ilFullName, out var d) && d.Has(RuntimeTypeFlags.SkipAllMethods);

    public static bool IsBlanketGated(string ilFullName)
        => _byILName.TryGetValue(ilFullName, out var d) && d.Has(RuntimeTypeFlags.BlanketGated);

    public static bool IsHeaderAliased(string mangledName)
        => _headerAliasedMangled.Contains(mangledName);

    /// <summary>
    /// Get all IL full names with the given flag (for iteration/set-building).
    /// </summary>
    public static IEnumerable<string> GetILNames(RuntimeTypeFlags flag)
        => _all.Where(d => d.Has(flag)).Select(d => d.ILFullName);

    /// <summary>
    /// Get runtime TypeInfo alias for a type (e.g., "System.String" → "cil2cpp::System::String_TypeInfo").
    /// Returns null for types without runtime TypeInfo.
    /// </summary>
    public static string? GetRuntimeTypeInfoName(string ilFullName)
        => _byILName.TryGetValue(ilFullName, out var d) ? d.RuntimeTypeInfoName : null;

    // ===== Enumeration methods (replace yield-return methods in Header.cs) =====

    /// <summary>
    /// Get (mangled, cppAlias) pairs for types with CppAlias set.
    /// Replaces Header.cs GetRuntimeProvidedTypeAliases().
    /// </summary>
    public static IEnumerable<(string Mangled, string CppAlias)> GetTypeAliases()
        => _all.Where(d => d.CppAlias != null).Select(d => (d.MangledName, d.CppAlias!));

    /// <summary>
    /// Get (mangled, runtimeTypeInfoName) pairs for exception types.
    /// Replaces Header.cs GetExceptionTypeInfoAliases().
    /// </summary>
    public static IEnumerable<(string MangledName, string RuntimeTypeInfoName)> GetExceptionTypeInfoAliases()
        => _all.Where(d => d.Has(RuntimeTypeFlags.ExceptionType) && d.RuntimeTypeInfoName != null)
               .Select(d => (d.MangledName, d.RuntimeTypeInfoName!));

    /// <summary>
    /// Get (mangled, ilFullName) pairs for base type stubs.
    /// Order matches the original GetRuntimeBaseTypeInfoStubs(): ValueType, Enum, MulticastDelegate, Delegate.
    /// </summary>
    public static IEnumerable<(string MangledName, string ILFullName)> GetBaseTypeStubs()
    {
        // Explicit order for backward compatibility (TypeInfo stub emission order in data.cpp)
        yield return ("System_ValueType", "System.ValueType");
        yield return ("System_Enum", "System.Enum");
        yield return ("System_MulticastDelegate", "System.MulticastDelegate");
        yield return ("System_Delegate", "System.Delegate");
    }

    /// <summary>
    /// Get mangled names of all header-aliased types.
    /// Replaces Header.cs RuntimeHeaderAliasedTypes iteration.
    /// </summary>
    public static IEnumerable<string> GetHeaderAliasedMangledNames()
        => _headerAliasedMangled;

    /// <summary>
    /// Determine whether an instance method on a CoreRuntime type should be blocked from IL emission.
    /// Replaces Source.cs ShouldKeepCoreRuntimeMethodGate().
    /// </summary>
    public static bool ShouldBlockInstanceMethod(string ilTypeName, IRMethod method)
    {
        if (!_byILName.TryGetValue(ilTypeName, out var desc))
            return method.IrStubReason != null;

        // ReflectionAliased: all instance methods blocked (field layout mismatch)
        if (desc.Has(RuntimeTypeFlags.ReflectionAliased))
            return true;

        // BlanketGated: fundamental types fully provided by runtime
        if (desc.Has(RuntimeTypeFlags.BlanketGated))
            return true;

        // SkipAllMethods: types where all methods are blocked (e.g. RuntimeTypeHandle — QCall wrappers)
        if (desc.Has(RuntimeTypeFlags.SkipAllMethods))
            return true;

        // System.Type: specific virtual methods implemented in core_methods.cpp
        // get_IsEnum and IsValueTypeImpl use IsSubclassOf(typeof(Enum/ValueType)) which breaks
        // because RuntimeType's GetBaseType reads MethodTable layout (offset mismatch with TypeInfo).
        if (ilTypeName == "System.Type" && method.Name is "get_IsEnum" or "IsValueTypeImpl"
            or "get_IsByRefLike" or "GetArrayRank"
            or "GetGenericTypeDefinition" or "GetGenericArguments" or "MakeGenericType")
            return true;

        // System.RuntimeType: methods that read MethodTable.f_ParentMethodTable directly are broken
        // because our TypeInfo struct layout differs from CoreCLR's MethodTable. These methods compare
        // ParentMethodTable against known types (Enum, MulticastDelegate) but read TypeInfo.full_name
        // (offset 16) instead of TypeInfo.base_type (offset 24). core_methods.cpp provides correct
        // implementations using TypeInfo.base_type.
        if (ilTypeName == "System.RuntimeType" && method.Name is "get_IsEnum" or "get_IsActualEnum"
            or "IsDelegate" or "MakeGenericType")
            return true;

        // Other CoreRuntimeTypes: allow methods with real compiled bodies.
        // Methods that couldn't compile (CLR-internal deps) have IrStubReason set.
        return method.IrStubReason != null;
    }
}
