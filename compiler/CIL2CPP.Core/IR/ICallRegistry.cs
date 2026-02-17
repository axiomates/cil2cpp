using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Registry for [InternalCall] method → C++ runtime mappings.
/// Unity IL2CPP architecture: only methods with MethodImplAttributes.InternalCall
/// are mapped to C++ implementations. All other BCL methods compile from IL.
/// </summary>
public static class ICallRegistry
{
    private static readonly Dictionary<string, string> _icallRegistry = new();
    private static readonly Dictionary<string, string> _icallWildcardRegistry = new();
    private static readonly Dictionary<string, string> _icallTypedRegistry = new();

    static ICallRegistry()
    {
        // =====================================================================
        //  [InternalCall] MAPPINGS — C++ implementations for native methods
        //  These methods have no IL body; they are implemented in C++ runtime.
        // =====================================================================

        // ===== System.Object (runtime type system) =====
        RegisterICall("System.Object", "GetType", 0, "cil2cpp::object_get_type_managed");
        RegisterICall("System.Object", "MemberwiseClone", 0, "cil2cpp::object_memberwise_clone");

        // ===== System.Object (virtual methods — runtime default implementations) =====
        // These have IL bodies in BCL but are called through vtable dispatch.
        // Runtime needs default implementations for types that don't override.
        RegisterICall("System.Object", "ToString", 0, "cil2cpp::object_to_string");
        RegisterICall("System.Object", "GetHashCode", 0, "cil2cpp::object_get_hash_code");
        RegisterICall("System.Object", "Equals", 1, "cil2cpp::object_equals");
        RegisterICall("System.Object", "ReferenceEquals", 2, "cil2cpp::object_reference_equals");

        // ===== System.String (true [InternalCall] only) =====
        // FastAllocateString is [InternalCall] — allocates raw string storage.
        // get_Length/get_Chars are kept as icall — they access internal String layout.
        // All other String methods (Concat, Substring, IndexOf, etc.) compile from BCL IL.
        RegisterICall("System.String", "FastAllocateString", 1, "cil2cpp::string_fast_allocate");
        RegisterICall("System.String", "get_Length", 0, "cil2cpp::string_length");
        RegisterICall("System.String", "get_Chars", 1, "cil2cpp::string_get_chars");
        RegisterICall("System.String", "GetRawStringData", 0, "cil2cpp::string_get_raw_data");
        RegisterICall("System.String", "GetPinnableReference", 0, "cil2cpp::string_get_raw_data");
        // ToCharArray — BCL uses Unsafe.As (JIT intrinsic) which our codegen can't compile
        RegisterICall("System.String", "ToCharArray", 0, "cil2cpp::string_to_char_array");

        // ===== Primitive ToString =====
        // These have IL bodies (Number formatting chain) — compile from BCL IL.
        // HasUnknownBodyReferences will filter if they reference unresolvable types.

        // ===== System.Array =====
        RegisterICall("System.Array", "get_Length", 0, "cil2cpp::array_get_length");
        RegisterICall("System.Array", "get_Rank", 0, "cil2cpp::array_get_rank");
        RegisterICall("System.Array", "Clear", 3, "cil2cpp::array_clear");
        RegisterICall("System.Array", "GetLength", 1, "cil2cpp::array_get_length_dim");

        // ===== System.Delegate / System.MulticastDelegate =====
        RegisterICall("System.Delegate", "Combine", 2, "cil2cpp::delegate_combine");
        RegisterICall("System.Delegate", "Remove", 2, "cil2cpp::delegate_remove");
        RegisterICall("System.MulticastDelegate", "Combine", 2, "cil2cpp::delegate_combine");
        RegisterICall("System.MulticastDelegate", "Remove", 2, "cil2cpp::delegate_remove");
        RegisterICall("System.Delegate", "InternalAlloc", 1, "cil2cpp::icall::Delegate_InternalAlloc");
        RegisterICall("System.Delegate", "BindToMethodInfo", 4, "cil2cpp::icall::Delegate_BindToMethodInfo");

        // ===== System.Enum =====
        RegisterICall("System.Enum", "InternalBoxEnum", 2, "cil2cpp::icall::Enum_InternalBoxEnum");
        RegisterICall("System.Enum", "InternalGetCorElementType", 1, "cil2cpp::icall::Enum_InternalGetCorElementType");

        // Char classification methods (IsWhiteSpace, IsDigit, etc.) — compile from BCL IL.
        // Attribute..ctor — compiles from BCL IL (just calls Object..ctor).

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
        RegisterICallTyped("System.Threading.Interlocked", "Add", 2, "System.Int32&", "cil2cpp::icall::Interlocked_Add_i32");
        RegisterICallTyped("System.Threading.Interlocked", "Add", 2, "System.Int64&", "cil2cpp::icall::Interlocked_Add_i64");

        // ===== System.Threading.Volatile =====
        // JIT intrinsics for volatile memory access. Implemented as template functions
        // in the runtime (cil2cpp::volatile_read<T> / cil2cpp::volatile_write<T>).
        RegisterICallWildcard("System.Threading.Volatile", "Read", "cil2cpp::volatile_read");
        RegisterICallWildcard("System.Threading.Volatile", "Write", "cil2cpp::volatile_write");

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

        // NOTE: ArgumentNullException.ThrowIfNull is NOT an icall — it compiles from IL.
        // It has a void* overload in BCL that would cause casting issues with the Object* icall.

        // ThrowHelper methods — compile from BCL IL (create exceptions and throw).

        // ===== System.Runtime.CompilerServices.RuntimeHelpers =====
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", 2,
            "cil2cpp::icall::RuntimeHelpers_InitializeArray");
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "IsReferenceOrContainsReferences", 0,
            "cil2cpp::icall::RuntimeHelpers_IsReferenceOrContainsReferences");

        // ===== System.Text.Unicode.Utf8Utility =====
        // These BCL methods use SIMD intrinsics (SSE2/AVX2) which our codegen can't compile.
        // Scalar C++ implementations provided in runtime/src/icall/unicode_utility.cpp.
        RegisterICall("System.Text.Unicode.Utf8Utility", "TranscodeToUtf8", 6,
            "cil2cpp::utf8_utility_transcode_to_utf8");
        RegisterICall("System.Text.Unicode.Utf8Utility", "GetPointerToFirstInvalidByte", 4,
            "cil2cpp::utf8_utility_get_pointer_to_first_invalid_byte");

        // Math methods — compile from BCL IL.
        // In .NET 8, most Math functions (Sqrt, Sin, Cos, etc.) are [InternalCall] extern methods.
        // Their icalls will be discovered via compile errors and added back as needed.

        // ===== System.ArgIterator =====
        // ArgIterator is 100% [InternalCall] in BCL. Our runtime uses VarArgHandle metadata
        // constructed at call sites and passed as intptr_t to varargs methods.
        RegisterICall("System.ArgIterator", ".ctor", 1, "cil2cpp::argiterator_init");
        RegisterICall("System.ArgIterator", "GetRemainingCount", 0, "cil2cpp::argiterator_get_remaining_count");
        RegisterICall("System.ArgIterator", "GetNextArg", 0, "cil2cpp::argiterator_get_next_arg");
        RegisterICall("System.ArgIterator", "End", 0, "cil2cpp::argiterator_end");
    }

    // ===== Registration methods =====

    public static void RegisterICall(string typeFullName, string methodName, int paramCount, string cppFunctionName)
    {
        _icallRegistry[MakeKey(typeFullName, methodName, paramCount)] = cppFunctionName;
    }

    public static void RegisterICallWildcard(string typeFullName, string methodName, string cppFunctionName)
    {
        _icallWildcardRegistry[$"{typeFullName}::{methodName}"] = cppFunctionName;
    }

    public static void RegisterICallTyped(string typeFullName, string methodName, int paramCount,
        string firstParamType, string cppFunctionName)
    {
        _icallTypedRegistry[$"{typeFullName}::{methodName}/{paramCount}/{firstParamType}"] = cppFunctionName;
    }

    // ===== Lookup =====

    /// <summary>
    /// Look up the C++ function name for a method in the icall registry.
    /// </summary>
    public static string? Lookup(string typeFullName, string methodName, int paramCount,
        string? firstParamType = null)
    {
        // 1. Type-dispatched overloads
        if (firstParamType != null)
        {
            var typedKey = $"{typeFullName}::{methodName}/{paramCount}/{firstParamType}";
            if (_icallTypedRegistry.TryGetValue(typedKey, out var icallTyped))
                return icallTyped;
        }

        // 2. Exact param count match
        var key = MakeKey(typeFullName, methodName, paramCount);
        if (_icallRegistry.TryGetValue(key, out var icallResult))
            return icallResult;

        // 3. Wildcard (any param count)
        var wildcardKey = $"{typeFullName}::{methodName}";
        if (_icallWildcardRegistry.TryGetValue(wildcardKey, out var icallWildcard))
            return icallWildcard;

        return null;
    }

    /// <summary>
    /// Look up with MethodReference for automatic first-param-type extraction.
    /// Also handles generic Interlocked methods (CompareExchange&lt;T&gt;).
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
                return Lookup(typeFullName, methodName, paramCount, "System.Object&");
            }
        }

        return null;
    }

    private static string MakeKey(string typeFullName, string methodName, int paramCount)
        => $"{typeFullName}::{methodName}/{paramCount}";
}
