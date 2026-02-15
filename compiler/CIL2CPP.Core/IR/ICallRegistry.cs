using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Unified registry for all BCL method → C++ runtime mappings.
/// Split into two categories:
///   - True ICalls: always active (no compilable IL exists — GC, threading, type system, native math)
///   - Managed Shortcuts: C++ implementations for BCL methods that have IL bodies
///     (active by default; skippable via skipManaged=true if BCL IL compilation is enabled)
/// </summary>
public static class ICallRegistry
{
    // True ICalls — always active (no compilable IL body)
    private static readonly Dictionary<string, string> _icallRegistry = new();
    private static readonly Dictionary<string, string> _icallWildcardRegistry = new();
    private static readonly Dictionary<string, string> _icallTypedRegistry = new();

    // Managed Shortcuts — can be skipped in MA mode when BCL IL compiles
    private static readonly Dictionary<string, string> _managedRegistry = new();
    private static readonly Dictionary<string, string> _managedWildcardRegistry = new();
    private static readonly Dictionary<string, string> _managedTypedRegistry = new();

    static ICallRegistry()
    {
        // =====================================================================
        //  TRUE ICALLS — always active, no compilable IL exists
        // =====================================================================

        // ===== System.Object (runtime type system) =====
        RegisterICall("System.Object", "GetType", 0, "cil2cpp::object_get_type_managed");
        RegisterICall("System.Object", "MemberwiseClone", 0, "cil2cpp::object_memberwise_clone");

        // ===== System.String (allocation) =====
        RegisterICall("System.String", "FastAllocateString", 1, "cil2cpp::string_fast_allocate");

        // ===== System.Threading.Monitor =====
        RegisterICall("System.Threading.Monitor", "Enter", 1, "cil2cpp::icall::Monitor_Enter");
        RegisterICall("System.Threading.Monitor", "Enter", 2, "cil2cpp::icall::Monitor_Enter2");
        RegisterICall("System.Threading.Monitor", "Exit", 1, "cil2cpp::icall::Monitor_Exit");
        RegisterICall("System.Threading.Monitor", "ReliableEnter", 2, "cil2cpp::icall::Monitor_ReliableEnter");
        RegisterICall("System.Threading.Monitor", "Wait", 1, "cil2cpp::icall::Monitor_Wait");
        RegisterICall("System.Threading.Monitor", "Wait", 2, "cil2cpp::icall::Monitor_Wait");
        RegisterICall("System.Threading.Monitor", "Pulse", 1, "cil2cpp::icall::Monitor_Pulse");
        RegisterICall("System.Threading.Monitor", "PulseAll", 1, "cil2cpp::icall::Monitor_PulseAll");

        // ===== System.Threading.Interlocked =====
        RegisterICallTyped("System.Threading.Interlocked", "Increment", 1, "System.Int32&", "cil2cpp::icall::Interlocked_Increment_i32");
        RegisterICallTyped("System.Threading.Interlocked", "Increment", 1, "System.Int64&", "cil2cpp::icall::Interlocked_Increment_i64");
        RegisterICallTyped("System.Threading.Interlocked", "Decrement", 1, "System.Int32&", "cil2cpp::icall::Interlocked_Decrement_i32");
        RegisterICallTyped("System.Threading.Interlocked", "Decrement", 1, "System.Int64&", "cil2cpp::icall::Interlocked_Decrement_i64");
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.Int32&", "cil2cpp::icall::Interlocked_Exchange_i32");
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.Int64&", "cil2cpp::icall::Interlocked_Exchange_i64");
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.Object&", "cil2cpp::icall::Interlocked_Exchange_obj");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Int32&", "cil2cpp::icall::Interlocked_CompareExchange_i32");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Int64&", "cil2cpp::icall::Interlocked_CompareExchange_i64");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Object&", "cil2cpp::icall::Interlocked_CompareExchange_obj");
        RegisterICall("System.Threading.Interlocked", "Add", 2, "cil2cpp::icall::Interlocked_Add_i32");

        // ===== System.Threading.Thread =====
        RegisterICall("System.Threading.Thread", "Sleep", 1, "cil2cpp::icall::Thread_Sleep");

        // ===== System.Environment =====
        RegisterICall("System.Environment", "get_NewLine", 0, "cil2cpp::icall::Environment_get_NewLine");
        RegisterICall("System.Environment", "get_TickCount", 0, "cil2cpp::icall::Environment_get_TickCount");
        RegisterICall("System.Environment", "get_TickCount64", 0, "cil2cpp::icall::Environment_get_TickCount64");
        RegisterICall("System.Environment", "get_ProcessorCount", 0, "cil2cpp::icall::Environment_get_ProcessorCount");
        RegisterICall("System.Environment", "get_CurrentManagedThreadId", 0, "cil2cpp::icall::Environment_get_CurrentManagedThreadId");

        // ===== System.GC =====
        RegisterICall("System.GC", "Collect", 0, "cil2cpp::gc_collect");
        RegisterICall("System.GC", "SuppressFinalize", 1, "cil2cpp::gc_suppress_finalize");
        RegisterICall("System.GC", "KeepAlive", 1, "cil2cpp::gc_keep_alive");
        RegisterICall("System.GC", "_Collect", 2, "cil2cpp::gc_collect");

        // ===== System.Buffer =====
        RegisterICall("System.Buffer", "Memmove", 3, "cil2cpp::icall::Buffer_Memmove");
        RegisterICall("System.Buffer", "BlockCopy", 5, "cil2cpp::icall::Buffer_BlockCopy");

        // ===== System.Type =====
        RegisterICall("System.Type", "GetTypeFromHandle", 1, "cil2cpp::icall::Type_GetTypeFromHandle");

        // ===== System.ArgumentNullException =====
        RegisterICall("System.ArgumentNullException", "ThrowIfNull", 2, "cil2cpp::icall::ArgumentNullException_ThrowIfNull");

        // ===== System.ThrowHelper (BCL internal) =====
        RegisterICall("System.ThrowHelper", "ThrowArgumentException", 1, "cil2cpp::icall::ThrowHelper_ThrowArgumentException");
        RegisterICall("System.ThrowHelper", "ThrowInvalidOperationException_InvalidOperation_NoValue", 0,
            "cil2cpp::throw_invalid_operation");
        RegisterICall("System.ThrowHelper", "ThrowValueArgumentOutOfRange_NeedNonNegNumException", 0,
            "cil2cpp::throw_argument_out_of_range");
        RegisterICall("System.ThrowHelper", "ThrowArgumentOutOfRangeException", 0,
            "cil2cpp::throw_argument_out_of_range");

        // ===== System.Runtime.CompilerServices.RuntimeHelpers =====
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", 2,
            "cil2cpp::icall::RuntimeHelpers_InitializeArray");
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "IsReferenceOrContainsReferences", 0,
            "cil2cpp::icall::RuntimeHelpers_IsReferenceOrContainsReferences");

        // ===== System.Math (native math — BCL IL also calls [InternalCall] at bottom) =====
        RegisterICallTyped("System.Math", "Abs", 1, "System.Single", "std::fabsf");
        RegisterICallTyped("System.Math", "Abs", 1, "System.Double", "std::fabs");
        RegisterICall("System.Math", "Abs", 1, "std::abs"); // fallback for int/long
        RegisterICall("System.Math", "Max", 2, "std::max");
        RegisterICall("System.Math", "Min", 2, "std::min");
        RegisterICall("System.Math", "Sqrt", 1, "std::sqrt");
        RegisterICall("System.Math", "Floor", 1, "std::floor");
        RegisterICall("System.Math", "Ceiling", 1, "std::ceil");
        RegisterICall("System.Math", "Round", 1, "std::round");
        RegisterICall("System.Math", "Pow", 2, "std::pow");
        RegisterICall("System.Math", "Sin", 1, "std::sin");
        RegisterICall("System.Math", "Cos", 1, "std::cos");
        RegisterICall("System.Math", "Tan", 1, "std::tan");
        RegisterICall("System.Math", "Asin", 1, "std::asin");
        RegisterICall("System.Math", "Acos", 1, "std::acos");
        RegisterICall("System.Math", "Atan", 1, "std::atan");
        RegisterICall("System.Math", "Atan2", 2, "std::atan2");
        RegisterICall("System.Math", "Log", 1, "std::log");
        RegisterICall("System.Math", "Log10", 1, "std::log10");
        RegisterICall("System.Math", "Log2", 1, "std::log2");
        RegisterICall("System.Math", "Exp", 1, "std::exp");
        RegisterICall("System.Math", "Truncate", 1, "std::trunc");
        RegisterICall("System.Math", "Sinh", 1, "std::sinh");
        RegisterICall("System.Math", "Cosh", 1, "std::cosh");
        RegisterICall("System.Math", "Tanh", 1, "std::tanh");
        RegisterICallTyped("System.Math", "Sign", 1, "System.Int32", "cil2cpp::math_sign_i32");
        RegisterICallTyped("System.Math", "Sign", 1, "System.Int64", "cil2cpp::math_sign_i64");
        RegisterICallTyped("System.Math", "Sign", 1, "System.Double", "cil2cpp::math_sign_f64");
        RegisterICall("System.Math", "Clamp", 3, "std::clamp");

        // =====================================================================
        //  MANAGED SHORTCUTS — C++ implementations for BCL methods with IL bodies
        //  These have BCL IL but we map them to C++ for simplicity/performance.
        //  In MA mode with skipManaged=true, these would be skipped.
        // =====================================================================

        // ===== System.Object (virtual methods — default runtime impls) =====
        RegisterManaged("System.Object", "ToString", 0, "cil2cpp::object_to_string");
        RegisterManaged("System.Object", "GetHashCode", 0, "cil2cpp::object_get_hash_code");
        RegisterManaged("System.Object", "Equals", 1, "cil2cpp::object_equals");
        RegisterManaged("System.Object", "ReferenceEquals", 2, "cil2cpp::object_reference_equals");

        // ===== System.String (managed string operations) =====
        RegisterManaged("System.String", "get_Length", 0, "cil2cpp::string_length");
        RegisterManaged("System.String", "get_Chars", 1, "cil2cpp::string_get_chars");
        RegisterManagedWildcard("System.String", "Concat", "cil2cpp::string_concat");
        RegisterManaged("System.String", "IsNullOrEmpty", 1, "cil2cpp::string_is_null_or_empty");
        RegisterManaged("System.String", "IsNullOrWhiteSpace", 1, "cil2cpp::string_is_null_or_empty");
        RegisterManagedWildcard("System.String", "Substring", "cil2cpp::string_substring");
        RegisterManaged("System.String", "op_Equality", 2, "cil2cpp::string_equals");
        RegisterManaged("System.String", "op_Inequality", 2, "cil2cpp::string_not_equals");
        RegisterManaged("System.String", "GetHashCode", 0, "cil2cpp::string_get_hash_code");
        RegisterManaged("System.String", "Equals", 1, "cil2cpp::string_equals");
        RegisterManaged("System.String", "IndexOf", 1, "cil2cpp::string_index_of");
        RegisterManaged("System.String", "IndexOf", 2, "cil2cpp::string_index_of");
        RegisterManaged("System.String", "LastIndexOf", 1, "cil2cpp::string_last_index_of");
        RegisterManaged("System.String", "Contains", 1, "cil2cpp::string_contains");
        RegisterManaged("System.String", "StartsWith", 1, "cil2cpp::string_starts_with");
        RegisterManaged("System.String", "EndsWith", 1, "cil2cpp::string_ends_with");
        RegisterManaged("System.String", "ToUpper", 0, "cil2cpp::string_to_upper");
        RegisterManaged("System.String", "ToLower", 0, "cil2cpp::string_to_lower");
        RegisterManaged("System.String", "Trim", 0, "cil2cpp::string_trim");
        RegisterManaged("System.String", "TrimStart", 0, "cil2cpp::string_trim_start");
        RegisterManaged("System.String", "TrimEnd", 0, "cil2cpp::string_trim_end");
        RegisterManaged("System.String", "Replace", 2, "cil2cpp::string_replace");
        RegisterManaged("System.String", "Remove", 1, "cil2cpp::string_remove");
        RegisterManaged("System.String", "Remove", 2, "cil2cpp::string_remove");
        RegisterManaged("System.String", "Insert", 2, "cil2cpp::string_insert");
        RegisterManaged("System.String", "PadLeft", 1, "cil2cpp::string_pad_left");
        RegisterManaged("System.String", "PadRight", 1, "cil2cpp::string_pad_right");
        // String.Format handled by TryEmitStringCall interception (packs args into array)
        RegisterManaged("System.String", "Join", 2, "cil2cpp::string_join");
        RegisterManaged("System.String", "Split", 1, "cil2cpp::string_split");
        RegisterManaged("System.String", "CompareTo", 1, "cil2cpp::string_compare_ordinal");

        // ===== System.Console =====
        RegisterManagedWildcard("System.Console", "WriteLine", "cil2cpp::System::Console_WriteLine");
        RegisterManagedWildcard("System.Console", "Write", "cil2cpp::System::Console_Write");
        RegisterManaged("System.Console", "ReadLine", 0, "cil2cpp::System::Console_ReadLine");
        RegisterManaged("System.Console", "Read", 0, "cil2cpp::System::Console_Read");

        // ===== System.IO.File =====
        RegisterManaged("System.IO.File", "ReadAllText", 1, "cil2cpp::System::IO::File_ReadAllText");
        RegisterManaged("System.IO.File", "WriteAllText", 2, "cil2cpp::System::IO::File_WriteAllText");
        RegisterManaged("System.IO.File", "ReadAllLines", 1, "cil2cpp::System::IO::File_ReadAllLines");
        RegisterManaged("System.IO.File", "WriteAllLines", 2, "cil2cpp::System::IO::File_WriteAllLines");
        RegisterManaged("System.IO.File", "AppendAllText", 2, "cil2cpp::System::IO::File_AppendAllText");
        RegisterManaged("System.IO.File", "Exists", 1, "cil2cpp::System::IO::File_Exists");
        RegisterManaged("System.IO.File", "Delete", 1, "cil2cpp::System::IO::File_Delete");
        RegisterManaged("System.IO.File", "Copy", 2, "cil2cpp::System::IO::File_Copy");
        RegisterManaged("System.IO.File", "Copy", 3, "cil2cpp::System::IO::File_Copy");
        RegisterManaged("System.IO.File", "ReadAllBytes", 1, "cil2cpp::System::IO::File_ReadAllBytes_Array");
        RegisterManaged("System.IO.File", "WriteAllBytes", 2, "cil2cpp::System::IO::File_WriteAllBytes");

        // ===== System.IO.Directory =====
        RegisterManaged("System.IO.Directory", "Exists", 1, "cil2cpp::System::IO::Directory_Exists");
        RegisterManaged("System.IO.Directory", "CreateDirectory", 1, "cil2cpp::System::IO::Directory_CreateDirectory");
        RegisterManaged("System.IO.Directory", "Delete", 1, "cil2cpp::System::IO::Directory_Delete");

        // ===== System.IO.Path =====
        RegisterManaged("System.IO.Path", "Combine", 2, "cil2cpp::System::IO::Path_Combine");
        RegisterManaged("System.IO.Path", "Combine", 3, "cil2cpp::System::IO::Path_Combine");
        RegisterManaged("System.IO.Path", "GetFileName", 1, "cil2cpp::System::IO::Path_GetFileName");
        RegisterManaged("System.IO.Path", "GetDirectoryName", 1, "cil2cpp::System::IO::Path_GetDirectoryName");
        RegisterManaged("System.IO.Path", "GetExtension", 1, "cil2cpp::System::IO::Path_GetExtension");
        RegisterManaged("System.IO.Path", "GetFileNameWithoutExtension", 1, "cil2cpp::System::IO::Path_GetFileNameWithoutExtension");
        RegisterManaged("System.IO.Path", "IsPathRooted", 1, "cil2cpp::System::IO::Path_IsPathRooted");
        RegisterManaged("System.IO.Path", "GetFullPath", 1, "cil2cpp::System::IO::Path_GetFullPath");

        // ===== Primitive ToString =====
        RegisterManaged("System.Int32", "ToString", 0, "cil2cpp::string_from_int32");
        RegisterManaged("System.Int64", "ToString", 0, "cil2cpp::string_from_int64");
        RegisterManaged("System.Double", "ToString", 0, "cil2cpp::string_from_double");
        RegisterManaged("System.Single", "ToString", 0, "cil2cpp::string_from_double");
        RegisterManaged("System.Boolean", "ToString", 0, "cil2cpp::string_from_bool");
        RegisterManaged("System.Char", "ToString", 0, "cil2cpp::string_from_char");

        // ===== System.Array =====
        RegisterManaged("System.Array", "get_Length", 0, "cil2cpp::array_get_length");
        RegisterManaged("System.Array", "get_Rank", 0, "cil2cpp::array_get_rank");
        RegisterManaged("System.Array", "Copy", 5, "cil2cpp::array_copy");
        RegisterManaged("System.Array", "Clear", 3, "cil2cpp::array_clear");
        RegisterManaged("System.Array", "GetLength", 1, "cil2cpp::array_get_length_dim");

        // ===== System.Delegate / System.MulticastDelegate =====
        RegisterManaged("System.Delegate", "Combine", 2, "cil2cpp::delegate_combine");
        RegisterManaged("System.Delegate", "Remove", 2, "cil2cpp::delegate_remove");
        RegisterManaged("System.MulticastDelegate", "Combine", 2, "cil2cpp::delegate_combine");
        RegisterManaged("System.MulticastDelegate", "Remove", 2, "cil2cpp::delegate_remove");

        // ===== System.Attribute =====
        RegisterManaged("System.Attribute", ".ctor", 0, "System_Object__ctor");

    }

    // ===== Registration methods =====

    /// <summary>Register a true [InternalCall] mapping — always active.</summary>
    public static void RegisterICall(string typeFullName, string methodName, int paramCount, string cppFunctionName)
    {
        _icallRegistry[MakeKey(typeFullName, methodName, paramCount)] = cppFunctionName;
    }

    /// <summary>Register a true [InternalCall] wildcard mapping — always active.</summary>
    public static void RegisterICallWildcard(string typeFullName, string methodName, string cppFunctionName)
    {
        _icallWildcardRegistry[$"{typeFullName}::{methodName}"] = cppFunctionName;
    }

    /// <summary>Register a true [InternalCall] typed overload — always active.</summary>
    public static void RegisterICallTyped(string typeFullName, string methodName, int paramCount,
        string firstParamType, string cppFunctionName)
    {
        _icallTypedRegistry[$"{typeFullName}::{methodName}/{paramCount}/{firstParamType}"] = cppFunctionName;
    }

    /// <summary>Register a managed shortcut — active in SA mode, skippable in MA mode.</summary>
    public static void RegisterManaged(string typeFullName, string methodName, int paramCount, string cppFunctionName)
    {
        _managedRegistry[MakeKey(typeFullName, methodName, paramCount)] = cppFunctionName;
    }

    /// <summary>Register a managed shortcut wildcard — active in SA mode, skippable in MA mode.</summary>
    public static void RegisterManagedWildcard(string typeFullName, string methodName, string cppFunctionName)
    {
        _managedWildcardRegistry[$"{typeFullName}::{methodName}"] = cppFunctionName;
    }

    /// <summary>Register a managed shortcut typed overload — active in SA mode, skippable in MA mode.</summary>
    public static void RegisterManagedTyped(string typeFullName, string methodName, int paramCount,
        string firstParamType, string cppFunctionName)
    {
        _managedTypedRegistry[$"{typeFullName}::{methodName}/{paramCount}/{firstParamType}"] = cppFunctionName;
    }

    // ===== Legacy API (backwards compatibility) =====

    /// <summary>Register a mapping (defaults to true ICall).</summary>
    public static void Register(string typeFullName, string methodName, int paramCount, string cppFunctionName)
        => RegisterICall(typeFullName, methodName, paramCount, cppFunctionName);

    /// <summary>Register a wildcard mapping (defaults to true ICall).</summary>
    public static void RegisterWildcard(string typeFullName, string methodName, string cppFunctionName)
        => RegisterICallWildcard(typeFullName, methodName, cppFunctionName);

    /// <summary>Register a typed overload (defaults to true ICall).</summary>
    public static void RegisterTyped(string typeFullName, string methodName, int paramCount,
        string firstParamType, string cppFunctionName)
        => RegisterICallTyped(typeFullName, methodName, paramCount, firstParamType, cppFunctionName);

    // ===== Lookup =====

    /// <summary>
    /// Look up the C++ function name for a BCL method.
    /// When skipManaged is true, only true icall entries are returned.
    /// </summary>
    public static string? Lookup(string typeFullName, string methodName, int paramCount,
        string? firstParamType = null, bool skipManaged = false)
    {
        // 1. Type-dispatched overloads (icall first, then managed)
        if (firstParamType != null)
        {
            var typedKey = $"{typeFullName}::{methodName}/{paramCount}/{firstParamType}";
            if (_icallTypedRegistry.TryGetValue(typedKey, out var icallTyped))
                return icallTyped;
            if (!skipManaged && _managedTypedRegistry.TryGetValue(typedKey, out var managedTyped))
                return managedTyped;
        }

        // 2. Exact param count match
        var key = MakeKey(typeFullName, methodName, paramCount);
        if (_icallRegistry.TryGetValue(key, out var icallResult))
            return icallResult;
        if (!skipManaged && _managedRegistry.TryGetValue(key, out var managedResult))
            return managedResult;

        // 3. Wildcard (any param count)
        var wildcardKey = $"{typeFullName}::{methodName}";
        if (_icallWildcardRegistry.TryGetValue(wildcardKey, out var icallWildcard))
            return icallWildcard;
        if (!skipManaged && _managedWildcardRegistry.TryGetValue(wildcardKey, out var managedWildcard))
            return managedWildcard;

        return null;
    }

    /// <summary>
    /// Look up with MethodReference for automatic first-param-type extraction.
    /// Also handles generic Interlocked methods (CompareExchange&lt;T&gt;).
    /// </summary>
    public static string? Lookup(MethodReference methodRef, bool skipManaged = false)
    {
        var typeFullName = methodRef.DeclaringType.FullName;
        var methodName = methodRef.Name;
        var paramCount = methodRef.Parameters.Count;

        // Extract first parameter type for type-dispatched overloads
        string? firstParamType = null;
        if (paramCount > 0)
            firstParamType = methodRef.Parameters[0].ParameterType.FullName;

        var result = Lookup(typeFullName, methodName, paramCount, firstParamType, skipManaged);
        if (result != null)
            return result;

        // Generic Interlocked methods (e.g., CompareExchange<T>) — resolve to _obj overload
        if (typeFullName == "System.Threading.Interlocked" && methodRef is GenericInstanceMethod gim
            && gim.GenericArguments.Count > 0)
        {
            var typeArg = gim.GenericArguments[0];
            bool isValueType = false;
            try { isValueType = typeArg.Resolve()?.IsValueType == true; } catch { }
            if (!isValueType)
            {
                // Reference type → use _obj overload
                return Lookup(typeFullName, methodName, paramCount, "System.Object&", skipManaged);
            }
        }

        return null;
    }

    private static string MakeKey(string typeFullName, string methodName, int paramCount)
        => $"{typeFullName}::{methodName}/{paramCount}";
}
