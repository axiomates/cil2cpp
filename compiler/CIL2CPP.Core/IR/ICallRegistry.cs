using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Unified registry for all BCL method → C++ runtime mappings.
/// Covers both [InternalCall] methods and managed BCL methods that are
/// intercepted and routed to C++ runtime implementations.
/// </summary>
public static class ICallRegistry
{
    // Standard lookup: type::method/paramCount
    private static readonly Dictionary<string, string> _registry = new();
    // Wildcard lookup: type::method (matches any param count)
    private static readonly Dictionary<string, string> _wildcardRegistry = new();
    // Type-dispatched lookup: type::method/paramCount/firstParamType
    private static readonly Dictionary<string, string> _typedRegistry = new();

    static ICallRegistry()
    {
        // ===== System.Object =====
        Register("System.Object", "ToString", 0, "cil2cpp::object_to_string");
        Register("System.Object", "GetHashCode", 0, "cil2cpp::object_get_hash_code");
        Register("System.Object", "Equals", 1, "cil2cpp::object_equals");
        Register("System.Object", "GetType", 0, "cil2cpp::object_get_type_managed");
        Register("System.Object", "ReferenceEquals", 2, "cil2cpp::object_reference_equals");
        Register("System.Object", "MemberwiseClone", 0, "cil2cpp::object_memberwise_clone");

        // ===== System.String =====
        Register("System.String", "FastAllocateString", 1, "cil2cpp::string_fast_allocate");
        Register("System.String", "get_Length", 0, "cil2cpp::string_length");
        Register("System.String", "get_Chars", 1, "cil2cpp::string_get_chars");
        RegisterWildcard("System.String", "Concat", "cil2cpp::string_concat");
        Register("System.String", "IsNullOrEmpty", 1, "cil2cpp::string_is_null_or_empty");
        RegisterWildcard("System.String", "Substring", "cil2cpp::string_substring");

        // ===== System.Console =====
        RegisterWildcard("System.Console", "WriteLine", "cil2cpp::System::Console_WriteLine");
        RegisterWildcard("System.Console", "Write", "cil2cpp::System::Console_Write");
        Register("System.Console", "ReadLine", 0, "cil2cpp::System::Console_ReadLine");

        // ===== Primitive ToString =====
        Register("System.Int32", "ToString", 0, "cil2cpp::string_from_int32");
        Register("System.Int64", "ToString", 0, "cil2cpp::string_from_int64");
        Register("System.Double", "ToString", 0, "cil2cpp::string_from_double");
        Register("System.Single", "ToString", 0, "cil2cpp::string_from_double");
        Register("System.Boolean", "ToString", 0, "cil2cpp::object_to_string");

        // ===== System.Array =====
        Register("System.Array", "get_Length", 0, "cil2cpp::array_get_length");
        Register("System.Array", "get_Rank", 0, "cil2cpp::array_get_rank");
        Register("System.Array", "Copy", 5, "cil2cpp::array_copy");
        Register("System.Array", "Clear", 3, "cil2cpp::array_clear");
        Register("System.Array", "GetLength", 1, "cil2cpp::array_get_length_dim");

        // ===== System.Delegate / System.MulticastDelegate =====
        Register("System.Delegate", "Combine", 2, "cil2cpp::delegate_combine");
        Register("System.Delegate", "Remove", 2, "cil2cpp::delegate_remove");
        Register("System.MulticastDelegate", "Combine", 2, "cil2cpp::delegate_combine");
        Register("System.MulticastDelegate", "Remove", 2, "cil2cpp::delegate_remove");

        // ===== System.Attribute =====
        Register("System.Attribute", ".ctor", 0, "System_Object__ctor");

        // ===== System.Math =====
        // Abs dispatched by parameter type
        RegisterTyped("System.Math", "Abs", 1, "System.Single", "std::fabsf");
        RegisterTyped("System.Math", "Abs", 1, "System.Double", "std::fabs");
        Register("System.Math", "Abs", 1, "std::abs"); // fallback for int/long
        Register("System.Math", "Max", 2, "std::max");
        Register("System.Math", "Min", 2, "std::min");
        Register("System.Math", "Sqrt", 1, "std::sqrt");
        Register("System.Math", "Floor", 1, "std::floor");
        Register("System.Math", "Ceiling", 1, "std::ceil");
        Register("System.Math", "Round", 1, "std::round");
        Register("System.Math", "Pow", 2, "std::pow");
        Register("System.Math", "Sin", 1, "std::sin");
        Register("System.Math", "Cos", 1, "std::cos");
        Register("System.Math", "Tan", 1, "std::tan");
        Register("System.Math", "Asin", 1, "std::asin");
        Register("System.Math", "Acos", 1, "std::acos");
        Register("System.Math", "Atan", 1, "std::atan");
        Register("System.Math", "Atan2", 2, "std::atan2");
        Register("System.Math", "Log", 1, "std::log");
        Register("System.Math", "Log10", 1, "std::log10");
        Register("System.Math", "Exp", 1, "std::exp");

        // ===== System.Threading.Monitor =====
        Register("System.Threading.Monitor", "Enter", 1, "cil2cpp::icall::Monitor_Enter");
        Register("System.Threading.Monitor", "Enter", 2, "cil2cpp::icall::Monitor_Enter2");
        Register("System.Threading.Monitor", "Exit", 1, "cil2cpp::icall::Monitor_Exit");
        Register("System.Threading.Monitor", "ReliableEnter", 2, "cil2cpp::icall::Monitor_ReliableEnter");
        Register("System.Threading.Monitor", "Wait", 2, "cil2cpp::icall::Monitor_Wait");
        Register("System.Threading.Monitor", "Pulse", 1, "cil2cpp::icall::Monitor_Pulse");
        Register("System.Threading.Monitor", "PulseAll", 1, "cil2cpp::icall::Monitor_PulseAll");

        // ===== System.Threading.Interlocked =====
        // Typed overloads — dispatched by first parameter's element type (ref int vs ref long vs ref object)
        RegisterTyped("System.Threading.Interlocked", "Increment", 1, "System.Int32&", "cil2cpp::icall::Interlocked_Increment_i32");
        RegisterTyped("System.Threading.Interlocked", "Increment", 1, "System.Int64&", "cil2cpp::icall::Interlocked_Increment_i64");
        RegisterTyped("System.Threading.Interlocked", "Decrement", 1, "System.Int32&", "cil2cpp::icall::Interlocked_Decrement_i32");
        RegisterTyped("System.Threading.Interlocked", "Decrement", 1, "System.Int64&", "cil2cpp::icall::Interlocked_Decrement_i64");
        RegisterTyped("System.Threading.Interlocked", "Exchange", 2, "System.Int32&", "cil2cpp::icall::Interlocked_Exchange_i32");
        RegisterTyped("System.Threading.Interlocked", "Exchange", 2, "System.Int64&", "cil2cpp::icall::Interlocked_Exchange_i64");
        RegisterTyped("System.Threading.Interlocked", "Exchange", 2, "System.Object&", "cil2cpp::icall::Interlocked_Exchange_obj");
        RegisterTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Int32&", "cil2cpp::icall::Interlocked_CompareExchange_i32");
        RegisterTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Int64&", "cil2cpp::icall::Interlocked_CompareExchange_i64");
        RegisterTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Object&", "cil2cpp::icall::Interlocked_CompareExchange_obj");
        Register("System.Threading.Interlocked", "Add", 2, "cil2cpp::icall::Interlocked_Add_i32");

        // ===== System.Threading.Thread =====
        Register("System.Threading.Thread", "Sleep", 1, "cil2cpp::icall::Thread_Sleep");

        // ===== System.Environment =====
        Register("System.Environment", "get_NewLine", 0, "cil2cpp::icall::Environment_get_NewLine");
        Register("System.Environment", "get_TickCount", 0, "cil2cpp::icall::Environment_get_TickCount");
        Register("System.Environment", "get_TickCount64", 0, "cil2cpp::icall::Environment_get_TickCount64");
        Register("System.Environment", "get_ProcessorCount", 0, "cil2cpp::icall::Environment_get_ProcessorCount");
        Register("System.Environment", "get_CurrentManagedThreadId", 0, "cil2cpp::icall::Environment_get_CurrentManagedThreadId");

        // ===== System.GC =====
        Register("System.GC", "Collect", 0, "cil2cpp::gc_collect");
        Register("System.GC", "SuppressFinalize", 1, "cil2cpp::gc_suppress_finalize");
        Register("System.GC", "KeepAlive", 1, "cil2cpp::gc_keep_alive");
        Register("System.GC", "_Collect", 2, "cil2cpp::gc_collect");

        // ===== System.Buffer =====
        Register("System.Buffer", "Memmove", 3, "cil2cpp::icall::Buffer_Memmove");
        Register("System.Buffer", "BlockCopy", 5, "cil2cpp::icall::Buffer_BlockCopy");

        // ===== System.Type =====
        Register("System.Type", "GetTypeFromHandle", 1, "cil2cpp::icall::Type_GetTypeFromHandle");

        // ===== System.ArgumentNullException =====
        Register("System.ArgumentNullException", "ThrowIfNull", 2, "cil2cpp::icall::ArgumentNullException_ThrowIfNull");

        // ===== System.ThrowHelper (BCL internal) =====
        Register("System.ThrowHelper", "ThrowArgumentException", 1, "cil2cpp::icall::ThrowHelper_ThrowArgumentException");

        // ===== System.Runtime.CompilerServices.RuntimeHelpers =====
        Register("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", 2,
            "cil2cpp::icall::RuntimeHelpers_InitializeArray");
        Register("System.Runtime.CompilerServices.RuntimeHelpers", "IsReferenceOrContainsReferences", 0,
            "cil2cpp::icall::RuntimeHelpers_IsReferenceOrContainsReferences");
    }

    /// <summary>
    /// Register a BCL method mapping with exact param count.
    /// </summary>
    public static void Register(string typeFullName, string methodName, int paramCount, string cppFunctionName)
    {
        var key = MakeKey(typeFullName, methodName, paramCount);
        _registry[key] = cppFunctionName;
    }

    /// <summary>
    /// Register a BCL method mapping that matches any param count.
    /// Useful for methods like Console.Write/WriteLine with many overloads all mapping to the same function.
    /// </summary>
    public static void RegisterWildcard(string typeFullName, string methodName, string cppFunctionName)
    {
        var key = $"{typeFullName}::{methodName}";
        _wildcardRegistry[key] = cppFunctionName;
    }

    /// <summary>
    /// Register a type-dispatched overload — matches when the first parameter type matches.
    /// Used for Math.Abs (float vs double) and Interlocked (int& vs long& vs object&).
    /// </summary>
    public static void RegisterTyped(string typeFullName, string methodName, int paramCount,
        string firstParamType, string cppFunctionName)
    {
        var key = $"{typeFullName}::{methodName}/{paramCount}/{firstParamType}";
        _typedRegistry[key] = cppFunctionName;
    }

    /// <summary>
    /// Look up the C++ function name for a BCL method.
    /// Tries typed dispatch first, then exact param count, then wildcard.
    /// </summary>
    public static string? Lookup(string typeFullName, string methodName, int paramCount,
        string? firstParamType = null)
    {
        // 1. Type-dispatched overload (e.g., Math.Abs(float) vs Math.Abs(double))
        if (firstParamType != null)
        {
            var typedKey = $"{typeFullName}::{methodName}/{paramCount}/{firstParamType}";
            if (_typedRegistry.TryGetValue(typedKey, out var typedResult))
                return typedResult;
        }

        // 2. Exact param count match
        var key = MakeKey(typeFullName, methodName, paramCount);
        if (_registry.TryGetValue(key, out var cppName))
            return cppName;

        // 3. Wildcard (any param count) — for Console.Write/WriteLine, String.Concat, etc.
        var wildcardKey = $"{typeFullName}::{methodName}";
        if (_wildcardRegistry.TryGetValue(wildcardKey, out var wildcardResult))
            return wildcardResult;

        return null;
    }

    /// <summary>
    /// Look up with MethodReference for automatic first-param-type extraction.
    /// Also handles generic Interlocked methods (CompareExchange&lt;T&gt;) by resolving
    /// the generic argument to determine if _obj overload should be used.
    /// </summary>
    public static string? Lookup(MethodReference methodRef)
    {
        var typeFullName = methodRef.DeclaringType.FullName;
        var methodName = methodRef.Name;
        var paramCount = methodRef.Parameters.Count;

        // Extract first parameter type for type-dispatched overloads
        string? firstParamType = null;
        if (paramCount > 0)
            firstParamType = methodRef.Parameters[0].ParameterType.FullName;

        var result = Lookup(typeFullName, methodName, paramCount, firstParamType);
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
                return Lookup(typeFullName, methodName, paramCount, "System.Object&");
            }
        }

        return null;
    }

    private static string MakeKey(string typeFullName, string methodName, int paramCount)
        => $"{typeFullName}::{methodName}/{paramCount}";
}
