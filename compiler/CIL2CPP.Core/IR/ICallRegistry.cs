using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Registry for method → C++ runtime mappings.
/// Primary purpose: [InternalCall] methods (no IL body, implemented in C++ runtime).
/// Secondary purpose: BCL methods whose IL chains are impractical for AOT compilation:
///   - JIT intrinsics (Unsafe.As, hardware intrinsics)
///   - SIMD-dependent paths (SSE2/AVX2 in Utf8Utility)
///   - Deep globalization data tables (CharUnicodeInfo → Char classification)
/// All other BCL methods compile from IL (Unity IL2CPP architecture).
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

        // Exception.GetType() hides Object.GetType() with 'new' keyword.
        // System.Exception is a CoreRuntimeType so its instance methods are skipped during
        // normal emission — register as ICall to avoid stub.
        RegisterICall("System.Exception", "GetType", 0, "cil2cpp::object_get_type_managed");

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
        // NOT registered as icalls — constrained resolution handles the redirection.
        // The BCL Number formatting chain uses generic methods with IUtfChar<TChar>
        // static abstract interface methods that can't compile in AOT.

        // ===== System.Array =====
        RegisterICall("System.Array", "get_Length", 0, "cil2cpp::array_get_length");
        RegisterICall("System.Array", "get_Rank", 0, "cil2cpp::array_get_rank");
        RegisterICall("System.Array", "Clear", 3, "cil2cpp::array_clear");
        RegisterICall("System.Array", "Clear", 1, "cil2cpp::array_clear_all");
        RegisterICall("System.Array", "GetLength", 1, "cil2cpp::array_get_length_dim");
        RegisterICall("System.Array", "Copy", 5, "cil2cpp::array_copy");
        RegisterICall("System.Array", "Copy", 3, "cil2cpp::array_copy_simple");
        RegisterICall("System.Array", "CopyImpl", 5, "cil2cpp::array_copy_impl");
        RegisterICall("System.Array", "Clone", 0, "cil2cpp::array_clone");
        RegisterICall("System.Array", "Reverse", 3, "cil2cpp::array_reverse");
        RegisterICall("System.Array", "get_NativeLength", 0, "cil2cpp::array_get_native_length");
        RegisterICall("System.Array", "GetValue", 1, "cil2cpp::array_get_value");
        RegisterICall("System.Array", "GetCorElementTypeOfElementType", 0, "cil2cpp::array_get_cor_element_type");

        // ===== System.Delegate / System.MulticastDelegate =====
        RegisterICall("System.Delegate", "Combine", 2, "cil2cpp::delegate_combine");
        RegisterICall("System.Delegate", "Remove", 2, "cil2cpp::delegate_remove");
        RegisterICall("System.MulticastDelegate", "Combine", 2, "cil2cpp::delegate_combine");
        RegisterICall("System.MulticastDelegate", "Remove", 2, "cil2cpp::delegate_remove");
        RegisterICall("System.Delegate", "InternalAlloc", 1, "cil2cpp::icall::Delegate_InternalAlloc");

        // ===== System.Enum =====
        RegisterICall("System.Enum", "InternalBoxEnum", 2, "cil2cpp::icall::Enum_InternalBoxEnum");
        RegisterICall("System.Enum", "InternalGetCorElementType", 1, "cil2cpp::icall::Enum_InternalGetCorElementType");

        // ===== System.IntPtr / System.UIntPtr =====
        // IntPtr/UIntPtr are aliased to intptr_t/uintptr_t (scalars).
        // IL methods access f_value field which doesn't exist on a scalar alias.
        RegisterICallTyped("System.IntPtr", ".ctor", 1, "System.Int32", "cil2cpp::icall::IntPtr_ctor_i32");
        RegisterICallTyped("System.IntPtr", ".ctor", 1, "System.Int64", "cil2cpp::icall::IntPtr_ctor_i64");
        RegisterICall("System.IntPtr", "ToPointer", 0, "cil2cpp::icall::IntPtr_ToPointer");
        RegisterICallTyped("System.UIntPtr", ".ctor", 1, "System.UInt32", "cil2cpp::icall::UIntPtr_ctor_u32");
        RegisterICallTyped("System.UIntPtr", ".ctor", 1, "System.UInt64", "cil2cpp::icall::UIntPtr_ctor_u64");
        RegisterICall("System.UIntPtr", "ToPointer", 0, "cil2cpp::icall::UIntPtr_ToPointer");

        // ===== System.Char (ICU-backed classification + case conversion) =====
        // BCL Char methods have IL bodies, but their chain goes through CharUnicodeInfo →
        // System.Globalization → large Unicode data tables that are impractical for AOT.
        // Redirect to ICU4C-backed runtime implementations instead.
        RegisterICall("System.Char", "IsWhiteSpace", 1, "cil2cpp::unicode::char_is_whitespace");
        RegisterICall("System.Char", "IsDigit", 1, "cil2cpp::unicode::char_is_digit");
        RegisterICall("System.Char", "IsLetter", 1, "cil2cpp::unicode::char_is_letter");
        RegisterICall("System.Char", "IsLetterOrDigit", 1, "cil2cpp::unicode::char_is_letter_or_digit");
        RegisterICall("System.Char", "IsUpper", 1, "cil2cpp::unicode::char_is_upper");
        RegisterICall("System.Char", "IsLower", 1, "cil2cpp::unicode::char_is_lower");
        RegisterICall("System.Char", "IsPunctuation", 1, "cil2cpp::unicode::char_is_punctuation");
        RegisterICall("System.Char", "IsSeparator", 1, "cil2cpp::unicode::char_is_separator");
        RegisterICall("System.Char", "IsControl", 1, "cil2cpp::unicode::char_is_control");
        RegisterICall("System.Char", "IsSurrogate", 1, "cil2cpp::unicode::char_is_surrogate");
        RegisterICall("System.Char", "IsHighSurrogate", 1, "cil2cpp::unicode::char_is_high_surrogate");
        RegisterICall("System.Char", "IsLowSurrogate", 1, "cil2cpp::unicode::char_is_low_surrogate");
        // ToUpper/ToLower: locale-aware (CurrentCulture via ICU uloc_getDefault + u_strToUpper/u_strToLower)
        RegisterICall("System.Char", "ToUpper", 1, "cil2cpp::unicode::char_to_upper");
        RegisterICall("System.Char", "ToLower", 1, "cil2cpp::unicode::char_to_lower");
        // ToUpperInvariant/ToLowerInvariant: culture-independent (ICU u_toupper/u_tolower)
        RegisterICall("System.Char", "ToUpperInvariant", 1, "cil2cpp::unicode::char_to_upper_invariant");
        RegisterICall("System.Char", "ToLowerInvariant", 1, "cil2cpp::unicode::char_to_lower_invariant");

        // ===== System.Globalization.CharUnicodeInfo (ICU-backed) =====
        // BCL implementation reads large static Unicode category tables — impractical for AOT.
        RegisterICall("System.Globalization.CharUnicodeInfo", "GetUnicodeCategory", 1,
            "cil2cpp::unicode::char_get_unicode_category");
        // Internal variant used by CompareInfo and other globalization code
        RegisterICall("System.Globalization.CharUnicodeInfo", "GetUnicodeCategoryNoBoundsChecks", 1,
            "cil2cpp::unicode::char_get_unicode_category");

        // ===== System.Globalization.CompareInfo (ICU4C collation) =====
        // CompareInfo methods P/Invoke to System.Globalization.Native (excluded from codegen).
        // Intercept at managed API level → ICU4C ucol_strcoll / usearch.
        RegisterICall("System.Globalization.CompareInfo", "Compare", 3,
            "cil2cpp::globalization::compareinfo_compare_string_string");
        RegisterICall("System.Globalization.CompareInfo", "Compare", 2,
            "cil2cpp::globalization::compareinfo_compare_string_string_2");
        RegisterICall("System.Globalization.CompareInfo", "Compare", 7,
            "cil2cpp::globalization::compareinfo_compare_substring");
        RegisterICall("System.Globalization.CompareInfo", "IndexOf", 5,
            "cil2cpp::globalization::compareinfo_index_of");
        RegisterICall("System.Globalization.CompareInfo", "IsPrefix", 3,
            "cil2cpp::globalization::compareinfo_is_prefix");
        RegisterICall("System.Globalization.CompareInfo", "IsSuffix", 3,
            "cil2cpp::globalization::compareinfo_is_suffix");
        RegisterICall("System.Globalization.CompareInfo", "CanUseAsciiOrdinalForOptions", 1,
            "cil2cpp::globalization::compareinfo_can_use_ascii_ordinal");
        RegisterICall("System.Globalization.CompareInfo", "CheckCompareOptionsForCompare", 1,
            "cil2cpp::globalization::compareinfo_check_options");
        RegisterICall("System.Globalization.CompareInfo", "GetNativeCompareFlags", 1,
            "cil2cpp::globalization::compareinfo_get_native_flags");
        RegisterICall("System.Globalization.CompareInfo", "ThrowCompareOptionsCheckFailed", 1,
            "cil2cpp::globalization::compareinfo_throw_options_failed");
        // SortHandleCache is a nested type, but Cecil resolves the declaring type
        RegisterICall("System.Globalization.CompareInfo", "GetCachedSortHandle", 1,
            "cil2cpp::globalization::compareinfo_get_sort_handle");

        // ===== System.String comparison ICalls (bypass ReadOnlySpan<char> chains) =====
        // String.Compare → CultureInfo → CompareInfo → P/Invoke chain is too deep for AOT.
        // Intercept at String.Compare level to bypass the entire chain.
        RegisterICall("System.String", "Compare", 3,
            "cil2cpp::globalization::string_compare_3");
        RegisterICall("System.String", "Compare", 6,
            "cil2cpp::globalization::string_compare_6");
        RegisterICall("System.String", "GetCaseCompareOfComparisonCulture", 1,
            "cil2cpp::globalization::string_get_case_compare");
        RegisterICall("System.String", "CheckStringComparison", 1,
            "cil2cpp::globalization::string_check_comparison");
        // NOTE: String.Equals/2 NOT registered — conflicts with static Equals(String, String)
        // which also has paramCount=2. The instance Equals(String, StringComparison) and
        // static Equals(String, String) can't be disambiguated by our ICall system.
        // EndsWith/StartsWith are safe — no static overloads with same paramCount.
        RegisterICall("System.String", "EndsWith", 2,
            "cil2cpp::globalization::string_ends_with");
        RegisterICall("System.String", "StartsWith", 2,
            "cil2cpp::globalization::string_starts_with");

        // ===== System.Globalization.Ordinal (ICU4C backed) =====
        // Ordinal methods use SIMD intrinsics (Vector128) — impractical for AOT.
        RegisterICall("System.Globalization.Ordinal", "EqualsIgnoreCase", 3,
            "cil2cpp::globalization::ordinal_equals_ignore_case");
        RegisterICall("System.Globalization.Ordinal", "CompareStringIgnoreCase", 4,
            "cil2cpp::globalization::ordinal_compare_ignore_case");

        // ===== System.Globalization.OrdinalCasing =====
        RegisterICall("System.Globalization.OrdinalCasing", "ToUpper", 1,
            "cil2cpp::globalization::ordinal_casing_to_upper");
        RegisterICall("System.Globalization.OrdinalCasing", "InitCasingTable", 0,
            "cil2cpp::globalization::ordinal_casing_init_table");
        RegisterICall("System.Globalization.OrdinalCasing", "InitOrdinalCasingPage", 1,
            "cil2cpp::globalization::ordinal_casing_init_page");

        // ===== System.Globalization.TextInfo (ICU4C case conversion) =====
        RegisterICall("System.Globalization.TextInfo", "ChangeCaseCore", 5,
            "cil2cpp::globalization::textinfo_change_case_core");
        RegisterICall("System.Globalization.TextInfo", "IcuChangeCase", 5,
            "cil2cpp::globalization::textinfo_icu_change_case");

        // ===== System.Globalization.GlobalizationMode =====
        // Force ICU path — return false for UseNls.
        RegisterICall("System.Globalization.GlobalizationMode", "get_UseNls", 0,
            "cil2cpp::globalization::globalization_mode_get_use_nls");

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
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.Byte&", "cil2cpp::icall::Interlocked_Exchange_u8");
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.UInt16&", "cil2cpp::icall::Interlocked_Exchange_u16");
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.Int32&", "cil2cpp::icall::Interlocked_Exchange_i32");
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.Int64&", "cil2cpp::icall::Interlocked_Exchange_i64");
        RegisterICallTyped("System.Threading.Interlocked", "Exchange", 2, "System.Object&", "cil2cpp::icall::Interlocked_Exchange_obj");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Byte&", "cil2cpp::icall::Interlocked_CompareExchange_u8");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.UInt16&", "cil2cpp::icall::Interlocked_CompareExchange_u16");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Int32&", "cil2cpp::icall::Interlocked_CompareExchange_i32");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Int64&", "cil2cpp::icall::Interlocked_CompareExchange_i64");
        RegisterICallTyped("System.Threading.Interlocked", "CompareExchange", 3, "System.Object&", "cil2cpp::icall::Interlocked_CompareExchange_obj");
        RegisterICallTyped("System.Threading.Interlocked", "Add", 2, "System.Int32&", "cil2cpp::icall::Interlocked_Add_i32");
        RegisterICallTyped("System.Threading.Interlocked", "Add", 2, "System.Int64&", "cil2cpp::icall::Interlocked_Add_i64");
        RegisterICall("System.Threading.Interlocked", "MemoryBarrier", 0, "cil2cpp::icall::Interlocked_MemoryBarrier");
        RegisterICall("System.Threading.Interlocked", "MemoryBarrierProcessWide", 0, "cil2cpp::icall::Interlocked_MemoryBarrier");
        RegisterICall("System.Threading.Interlocked", "ReadMemoryBarrier", 0, "cil2cpp::icall::Interlocked_ReadMemoryBarrier");

        // ===== System.Threading.Volatile =====
        // JIT intrinsics for volatile memory access. Implemented as template functions
        // in the runtime (cil2cpp::volatile_read<T> / cil2cpp::volatile_write<T>).
        RegisterICallWildcard("System.Threading.Volatile", "Read", "cil2cpp::volatile_read");
        RegisterICallWildcard("System.Threading.Volatile", "Write", "cil2cpp::volatile_write");

        // ===== System.Threading.Thread =====
        RegisterICall("System.Threading.Thread", "Sleep", 1, "cil2cpp::icall::Thread_Sleep");
        RegisterICall("System.Threading.Thread", "SpinWait", 1, "cil2cpp::icall::Thread_SpinWait");
        RegisterICall("System.Threading.Thread", "YieldInternal", 0, "cil2cpp::icall::Thread_Yield");
        RegisterICall("System.Threading.Thread", "get_OptimalMaxSpinWaitsPerSpinIteration", 0,
            "cil2cpp::icall::Thread_get_OptimalMaxSpinWaitsPerSpinIteration");
        RegisterICall("System.Threading.Thread", "get_CurrentThread", 0,
            "cil2cpp::icall::Thread_get_CurrentThread");
        RegisterICall("System.Threading.Thread", "GetCurrentOSThreadId", 0,
            "cil2cpp::icall::Thread_GetCurrentOSThreadId");
        RegisterICall("System.Threading.Thread", "Initialize", 0,
            "cil2cpp::icall::Thread_Initialize");
        RegisterICall("System.Threading.Thread", "GetCurrentThreadNative", 0,
            "cil2cpp::icall::Thread_GetCurrentThreadNative");
        RegisterICall("System.Threading.Thread", "IsBackgroundNative", 0,
            "cil2cpp::icall::Thread_IsBackgroundNative");
        RegisterICall("System.Threading.Thread", "SetBackgroundNative", 1,
            "cil2cpp::icall::Thread_SetBackgroundNative");
        RegisterICall("System.Threading.Thread", "GetPriorityNative", 0,
            "cil2cpp::icall::Thread_GetPriorityNative");
        RegisterICall("System.Threading.Thread", "SetPriorityNative", 1,
            "cil2cpp::icall::Thread_SetPriorityNative");
        RegisterICall("System.Threading.Thread", "get_ManagedThreadId", 0,
            "cil2cpp::icall::Thread_get_ManagedThreadId");
        RegisterICall("System.Threading.Thread", "InternalFinalize", 0,
            "cil2cpp::icall::Thread_InternalFinalize");

        // ===== System.Environment =====
        RegisterICall("System.Environment", "get_NewLine", 0, "cil2cpp::icall::Environment_get_NewLine");
        RegisterICall("System.Environment", "get_TickCount", 0, "cil2cpp::icall::Environment_get_TickCount");
        RegisterICall("System.Environment", "get_TickCount64", 0, "cil2cpp::icall::Environment_get_TickCount64");
        RegisterICall("System.Environment", "get_ProcessorCount", 0, "cil2cpp::icall::Environment_get_ProcessorCount");
        RegisterICall("System.Environment", "get_CurrentManagedThreadId", 0, "cil2cpp::icall::Environment_get_CurrentManagedThreadId");
        RegisterICall("System.Environment", "Exit", 1, "cil2cpp::icall::Environment_Exit");
        RegisterICall("System.Environment", "GetCommandLineArgs", 0, "cil2cpp::icall::Environment_GetCommandLineArgs");
        RegisterICall("System.Environment", "GetEnvironmentVariable", 1, "cil2cpp::icall::Environment_GetEnvironmentVariable");

        // ===== System.GC =====
        RegisterICall("System.GC", "Collect", 0, "cil2cpp::gc_collect");
        RegisterICall("System.GC", "SuppressFinalize", 1, "cil2cpp::gc_suppress_finalize");
        RegisterICall("System.GC", "_SuppressFinalize", 1, "cil2cpp::gc_suppress_finalize");
        RegisterICall("System.GC", "KeepAlive", 1, "cil2cpp::gc_keep_alive");
        RegisterICall("System.GC", "_Collect", 2, "cil2cpp::gc_collect");
        RegisterICall("System.GC", "_WaitForPendingFinalizers", 0, "cil2cpp::gc_noop"); // no-op with BoehmGC
        RegisterICall("System.GC", "_ReRegisterForFinalize", 1, "cil2cpp::gc_noop"); // no-op
        RegisterICall("System.GC", "GetTotalMemory", 1, "cil2cpp::gc_get_total_memory");
        // FIXME: GetGCMemoryInfo returns GCMemoryInfo struct, not void — don't map to gc_noop
        // Let it be stubbed until proper implementation exists
        RegisterICall("System.GC", "AllocateUninitializedArray", 2, "cil2cpp::gc_allocate_uninitialized_array");
        RegisterICall("System.GC", "GetAllocatedBytesForCurrentThread", 0, "cil2cpp::gc_get_total_memory_simple");

        // ===== System.Buffer =====
        RegisterICall("System.Buffer", "Memmove", 3, "cil2cpp::icall::Buffer_Memmove");
        RegisterICall("System.Buffer", "__Memmove", 3, "cil2cpp::icall::Buffer_Memmove");
        RegisterICall("System.Buffer", "__ZeroMemory", 2, "cil2cpp::icall::Buffer_ZeroMemory");
        RegisterICall("System.Buffer", "__BulkMoveWithWriteBarrier", 3, "cil2cpp::icall::Buffer_BulkMoveWithWriteBarrier");
        RegisterICall("System.Buffer", "BlockCopy", 5, "cil2cpp::icall::Buffer_BlockCopy");

        // ===== System.Runtime.InteropServices.GCHandle =====
        RegisterICall("System.Runtime.InteropServices.GCHandle", "InternalAlloc", 2, "cil2cpp::icall::GCHandle_InternalAlloc");
        RegisterICall("System.Runtime.InteropServices.GCHandle", "InternalFree", 1, "cil2cpp::icall::GCHandle_InternalFree");
        RegisterICall("System.Runtime.InteropServices.GCHandle", "InternalSet", 2, "cil2cpp::icall::GCHandle_InternalSet");
        RegisterICall("System.Runtime.InteropServices.GCHandle", "InternalGet", 1, "cil2cpp::icall::GCHandle_InternalGet");

        // ===== System.Runtime.InteropServices.Marshal =====
        RegisterICall("System.Runtime.InteropServices.Marshal", "AllocHGlobal", 1, "cil2cpp::icall::Marshal_AllocHGlobal");
        RegisterICall("System.Runtime.InteropServices.Marshal", "FreeHGlobal", 1, "cil2cpp::icall::Marshal_FreeHGlobal");
        RegisterICall("System.Runtime.InteropServices.Marshal", "AllocCoTaskMem", 1, "cil2cpp::icall::Marshal_AllocCoTaskMem");
        RegisterICall("System.Runtime.InteropServices.Marshal", "FreeCoTaskMem", 1, "cil2cpp::icall::Marshal_FreeCoTaskMem");
        RegisterICall("System.Runtime.InteropServices.Marshal", "GetLastPInvokeError", 0, "cil2cpp::get_last_pinvoke_error");
        RegisterICall("System.Runtime.InteropServices.Marshal", "SetLastPInvokeError", 1, "cil2cpp::set_last_pinvoke_error");

        // ===== SafeHandle =====
        RegisterICall("System.Runtime.InteropServices.SafeHandle", ".ctor", 2, "cil2cpp::icall::SafeHandle__ctor");

        // ===== SafeFileHandle =====
        // Default ctor: initializes _fileType = -1 (field initializer) + base SafeHandle fields.
        // BCL ctor body can't compile from IL because Activator.CreateInstance<T>() in the generic
        // SafeHandleMarshaller prevents reachability from discovering the concrete ctor.
        RegisterICall("Microsoft.Win32.SafeHandles.SafeFileHandle", ".ctor", 0, "cil2cpp::icall::SafeFileHandle__ctor");
        RegisterICall("System.Runtime.InteropServices.SafeHandle", "DangerousGetHandle", 0, "cil2cpp::icall::SafeHandle_DangerousGetHandle");
        RegisterICall("System.Runtime.InteropServices.SafeHandle", "SetHandle", 1, "cil2cpp::icall::SafeHandle_SetHandle");
        RegisterICall("System.Runtime.InteropServices.SafeHandle", "DangerousAddRef", 1, "cil2cpp::icall::SafeHandle_DangerousAddRef");
        RegisterICall("System.Runtime.InteropServices.SafeHandle", "DangerousRelease", 0, "cil2cpp::icall::SafeHandle_DangerousRelease");
        RegisterICall("System.Runtime.InteropServices.SafeHandle", "get_IsClosed", 0, "cil2cpp::icall::SafeHandle_get_IsClosed");
        RegisterICall("System.Runtime.InteropServices.SafeHandle", "SetHandleAsInvalid", 0, "cil2cpp::icall::SafeHandle_SetHandleAsInvalid");
        RegisterICall("System.Runtime.InteropServices.SafeHandle", "Dispose", 1, "cil2cpp::icall::SafeHandle_Dispose");

        // ===== WaitHandle =====
        RegisterICall("System.Threading.WaitHandle", "WaitOneCore", 2, "cil2cpp::icall::WaitHandle_WaitOneCore");

        // ===== System.IO =====
        // File operations — intercept at public API level, bypassing BCL FileStream chain.
        // HACK: B.6 partial removal attempted — File.ReadAllText works through BCL IL,
        // but File.ReadAllBytes hangs (FileStream.Read(byte[],int,int) code path has a bug).
        // TODO: Investigate FileStream.Read byte[] path, then remove these ICalls.
        RegisterICall("System.IO.File", "Exists", 1, "cil2cpp::icall::File_Exists");
        RegisterICall("System.IO.File", "ReadAllText", 1, "cil2cpp::icall::File_ReadAllText");
        RegisterICall("System.IO.File", "ReadAllText", 2, "cil2cpp::icall::File_ReadAllText2");
        RegisterICall("System.IO.File", "WriteAllText", 2, "cil2cpp::icall::File_WriteAllText");
        RegisterICall("System.IO.File", "WriteAllText", 3, "cil2cpp::icall::File_WriteAllText2");
        RegisterICall("System.IO.File", "ReadAllBytes", 1, "cil2cpp::icall::File_ReadAllBytes");
        RegisterICall("System.IO.File", "WriteAllBytes", 2, "cil2cpp::icall::File_WriteAllBytes");
        RegisterICall("System.IO.File", "Delete", 1, "cil2cpp::icall::File_Delete");
        RegisterICall("System.IO.File", "Copy", 3, "cil2cpp::icall::File_Copy");
        RegisterICall("System.IO.File", "Move", 3, "cil2cpp::icall::File_Move");
        RegisterICall("System.IO.File", "ReadAllLines", 1, "cil2cpp::icall::File_ReadAllLines");
        RegisterICall("System.IO.File", "AppendAllText", 2, "cil2cpp::icall::File_AppendAllText");
        // Path operations
        RegisterICall("System.IO.Path", "GetFullPath", 1, "cil2cpp::icall::Path_GetFullPath");
        RegisterICall("System.IO.Path", "GetDirectoryName", 1, "cil2cpp::icall::Path_GetDirectoryName");
        RegisterICall("System.IO.Path", "GetFileName", 1, "cil2cpp::icall::Path_GetFileName");
        RegisterICall("System.IO.Path", "GetFileNameWithoutExtension", 1, "cil2cpp::icall::Path_GetFileNameWithoutExtension");
        RegisterICall("System.IO.Path", "GetExtension", 1, "cil2cpp::icall::Path_GetExtension");
        RegisterICall("System.IO.Path", "GetTempPath", 0, "cil2cpp::icall::Path_GetTempPath");
        RegisterICall("System.IO.Path", "Combine", 2, "cil2cpp::icall::Path_Combine2");
        RegisterICall("System.IO.Path", "Combine", 3, "cil2cpp::icall::Path_Combine3");
        // Directory operations
        RegisterICall("System.IO.Directory", "Exists", 1, "cil2cpp::icall::Directory_Exists");
        RegisterICall("System.IO.Directory", "CreateDirectory", 1, "cil2cpp::icall::Directory_CreateDirectory");

        // ===== System.Type =====
        RegisterICall("System.Type", "GetTypeFromHandle", 1, "cil2cpp::icall::Type_GetTypeFromHandle");

        // NOTE: ArgumentNullException.ThrowIfNull is NOT an icall — it compiles from IL.
        // It has a void* overload in BCL that would cause casting issues with the Object* icall.

        // ThrowHelper methods — compile from BCL IL (create exceptions and throw).

        // ===== System.Runtime.CompilerServices.RuntimeHelpers =====
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", 2,
            "cil2cpp::icall::RuntimeHelpers_InitializeArray");
        // RuntimeHelpers.IsReferenceOrContainsReferences<T>() is resolved at compile time
        // in IRBuilder.Emit.cs (compile-time generic type evaluation).
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "TryEnsureSufficientExecutionStack", 0,
            "cil2cpp::icall::RuntimeHelpers_TryEnsureSufficientExecutionStack");
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "EnsureSufficientExecutionStack", 0,
            "cil2cpp::icall::RuntimeHelpers_EnsureSufficientExecutionStack");
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "GetObjectMethodTablePointer", 1,
            "cil2cpp::icall::RuntimeHelpers_GetObjectMethodTablePointer");
        // ObjectHasComponentSize: CLR checks MethodTable which we don't have.
        // Our ICall checks TypeInfo flags for Array/String (both have "component size").
        RegisterICall("System.Runtime.CompilerServices.RuntimeHelpers", "ObjectHasComponentSize", 1,
            "cil2cpp::icall::RuntimeHelpers_ObjectHasComponentSize");

        // ===== System.Text.Unicode.Utf8Utility =====
        // These BCL methods use SIMD intrinsics (SSE2/AVX2) which our codegen can't compile.
        // Scalar C++ implementations provided in runtime/src/icall/unicode_utility.cpp.
        RegisterICall("System.Text.Unicode.Utf8Utility", "TranscodeToUtf8", 6,
            "cil2cpp::utf8_utility_transcode_to_utf8");
        RegisterICall("System.Text.Unicode.Utf8Utility", "GetPointerToFirstInvalidByte", 4,
            "cil2cpp::utf8_utility_get_pointer_to_first_invalid_byte");

        // ===== System.Text.Ascii =====
        // BCL uses SIMD (Vector128/SSE2/AVX2) for all ASCII processing.
        // Scalar C++ implementations in runtime/src/icall/ascii.cpp.
        RegisterICall("System.Text.Ascii", "AllBytesInUInt32AreAscii", 1,
            "cil2cpp::icall::Ascii_AllBytesInUInt32AreAscii");
        RegisterICall("System.Text.Ascii", "AllBytesInUInt64AreAscii", 1,
            "cil2cpp::icall::Ascii_AllBytesInUInt64AreAscii");
        RegisterICall("System.Text.Ascii", "AllCharsInUInt32AreAscii", 1,
            "cil2cpp::icall::Ascii_AllCharsInUInt32AreAscii");
        RegisterICall("System.Text.Ascii", "AllCharsInUInt64AreAscii", 1,
            "cil2cpp::icall::Ascii_AllCharsInUInt64AreAscii");
        RegisterICall("System.Text.Ascii", "FirstCharInUInt32IsAscii", 1,
            "cil2cpp::icall::Ascii_FirstCharInUInt32IsAscii");
        RegisterICallTyped("System.Text.Ascii", "IsValid", 1, "System.Byte",
            "cil2cpp::icall::Ascii_IsValid_byte");
        RegisterICallTyped("System.Text.Ascii", "IsValid", 1, "System.Char",
            "cil2cpp::icall::Ascii_IsValid_char");
        RegisterICall("System.Text.Ascii", "WidenAsciiToUtf16", 3,
            "cil2cpp::icall::Ascii_WidenAsciiToUtf16");
        RegisterICall("System.Text.Ascii", "WidenFourAsciiBytesToUtf16AndWriteToBuffer", 2,
            "cil2cpp::icall::Ascii_WidenFourAsciiBytesToUtf16AndWriteToBuffer");
        RegisterICall("System.Text.Ascii", "NarrowUtf16ToAscii", 3,
            "cil2cpp::icall::Ascii_NarrowUtf16ToAscii");
        RegisterICall("System.Text.Ascii", "NarrowFourUtf16CharsToAsciiAndWriteToBuffer", 2,
            "cil2cpp::icall::Ascii_NarrowFourUtf16CharsToAsciiAndWriteToBuffer");
        RegisterICall("System.Text.Ascii", "NarrowTwoUtf16CharsToAsciiAndWriteToBuffer", 2,
            "cil2cpp::icall::Ascii_NarrowTwoUtf16CharsToAsciiAndWriteToBuffer");
        RegisterICall("System.Text.Ascii", "CountNumberOfLeadingAsciiBytesFromUInt32WithSomeNonAsciiData", 1,
            "cil2cpp::icall::Ascii_CountNumberOfLeadingAsciiBytesFromUInt32WithSomeNonAsciiData");
        RegisterICall("System.Text.Ascii", "GetIndexOfFirstNonAsciiByte", 2,
            "cil2cpp::icall::Ascii_GetIndexOfFirstNonAsciiByte");
        RegisterICall("System.Text.Ascii", "GetIndexOfFirstNonAsciiByte_Intrinsified", 2,
            "cil2cpp::icall::Ascii_GetIndexOfFirstNonAsciiByte");
        RegisterICall("System.Text.Ascii", "GetIndexOfFirstNonAsciiByte_Vector", 2,
            "cil2cpp::icall::Ascii_GetIndexOfFirstNonAsciiByte");
        RegisterICall("System.Text.Ascii", "GetIndexOfFirstNonAsciiChar", 2,
            "cil2cpp::icall::Ascii_GetIndexOfFirstNonAsciiChar");
        RegisterICall("System.Text.Ascii", "GetIndexOfFirstNonAsciiChar_Intrinsified", 2,
            "cil2cpp::icall::Ascii_GetIndexOfFirstNonAsciiChar");
        RegisterICall("System.Text.Ascii", "GetIndexOfFirstNonAsciiChar_Vector", 2,
            "cil2cpp::icall::Ascii_GetIndexOfFirstNonAsciiChar");
        RegisterICall("System.Text.Ascii", "ContainsNonAsciiByte_Sse2", 1,
            "cil2cpp::icall::Ascii_ContainsNonAsciiByte_Sse2");
        // Narrowing _Intrinsified variants all map to the same scalar impl
        RegisterICall("System.Text.Ascii", "NarrowUtf16ToAscii_Intrinsified", 3,
            "cil2cpp::icall::Ascii_NarrowUtf16ToAscii");
        RegisterICall("System.Text.Ascii", "NarrowUtf16ToAscii_Intrinsified_256", 3,
            "cil2cpp::icall::Ascii_NarrowUtf16ToAscii");
        RegisterICall("System.Text.Ascii", "NarrowUtf16ToAscii_Intrinsified_512", 3,
            "cil2cpp::icall::Ascii_NarrowUtf16ToAscii");

        // ===== SpanHelpers.DontNegate/Negate =====
        // Generic structs with trivial methods used by SpanHelpers.IndexOfAny etc.
        // Stubbed due to "undeclared TypeInfo" gate; ICalls bypass that check.
        // DontNegate.NegateIfNeeded(bool) = identity, Negate.NegateIfNeeded(bool) = !value
        // Using open generic type names with wildcard — covers all T specializations.
        RegisterICallWildcard("System.SpanHelpers/DontNegate`1", "NegateIfNeeded",
            "cil2cpp::icall::SpanHelpers_DontNegate_NegateIfNeeded");
        RegisterICallWildcard("System.SpanHelpers/Negate`1", "NegateIfNeeded",
            "cil2cpp::icall::SpanHelpers_Negate_NegateIfNeeded");

        // ===== System.Math (double) =====
        // .NET 8: Math methods are [InternalCall] with no IL body (JIT replaces with CPU instructions).
        // AOT: map to <cmath> functions via icall.
        RegisterICallTyped("System.Math", "Abs", 1, "System.Double", "cil2cpp::icall::Math_Abs_double");
        RegisterICallTyped("System.Math", "Abs", 1, "System.Single", "cil2cpp::icall::Math_Abs_float");
        RegisterICallTyped("System.Math", "Abs", 1, "System.Int32", "cil2cpp::icall::Math_Abs_int");
        RegisterICallTyped("System.Math", "Abs", 1, "System.Int64", "cil2cpp::icall::Math_Abs_long");
        RegisterICall("System.Math", "Sqrt", 1, "cil2cpp::icall::Math_Sqrt");
        RegisterICall("System.Math", "Sin", 1, "cil2cpp::icall::Math_Sin");
        RegisterICall("System.Math", "Cos", 1, "cil2cpp::icall::Math_Cos");
        RegisterICall("System.Math", "Tan", 1, "cil2cpp::icall::Math_Tan");
        RegisterICall("System.Math", "Asin", 1, "cil2cpp::icall::Math_Asin");
        RegisterICall("System.Math", "Acos", 1, "cil2cpp::icall::Math_Acos");
        RegisterICall("System.Math", "Atan", 1, "cil2cpp::icall::Math_Atan");
        RegisterICall("System.Math", "Atan2", 2, "cil2cpp::icall::Math_Atan2");
        RegisterICall("System.Math", "Pow", 2, "cil2cpp::icall::Math_Pow");
        RegisterICall("System.Math", "Exp", 1, "cil2cpp::icall::Math_Exp");
        RegisterICall("System.Math", "Log", 1, "cil2cpp::icall::Math_Log");
        RegisterICall("System.Math", "Log10", 1, "cil2cpp::icall::Math_Log10");
        RegisterICall("System.Math", "Log2", 1, "cil2cpp::icall::Math_Log2");
        RegisterICall("System.Math", "Floor", 1, "cil2cpp::icall::Math_Floor");
        RegisterICall("System.Math", "Ceiling", 1, "cil2cpp::icall::Math_Ceiling");
        RegisterICall("System.Math", "Round", 1, "cil2cpp::icall::Math_Round");
        RegisterICall("System.Math", "Truncate", 1, "cil2cpp::icall::Math_Truncate");
        RegisterICallTyped("System.Math", "Max", 2, "System.Double", "cil2cpp::icall::Math_Max_double");
        RegisterICallTyped("System.Math", "Min", 2, "System.Double", "cil2cpp::icall::Math_Min_double");
        RegisterICallTyped("System.Math", "Max", 2, "System.Int32", "cil2cpp::icall::Math_Max_int");
        RegisterICallTyped("System.Math", "Min", 2, "System.Int32", "cil2cpp::icall::Math_Min_int");
        RegisterICall("System.Math", "Cbrt", 1, "cil2cpp::icall::Math_Cbrt");
        RegisterICall("System.Math", "IEEERemainder", 2, "cil2cpp::icall::Math_IEEERemainder");
        RegisterICall("System.Math", "FusedMultiplyAdd", 3, "cil2cpp::icall::Math_FusedMultiplyAdd");
        RegisterICall("System.Math", "CopySign", 2, "cil2cpp::icall::Math_CopySign");
        RegisterICall("System.Math", "BitDecrement", 1, "cil2cpp::icall::Math_BitDecrement");
        RegisterICall("System.Math", "BitIncrement", 1, "cil2cpp::icall::Math_BitIncrement");

        // ===== System.MathF (float) =====
        RegisterICall("System.MathF", "Sqrt", 1, "cil2cpp::icall::MathF_Sqrt");
        RegisterICall("System.MathF", "Sin", 1, "cil2cpp::icall::MathF_Sin");
        RegisterICall("System.MathF", "Cos", 1, "cil2cpp::icall::MathF_Cos");
        RegisterICall("System.MathF", "Tan", 1, "cil2cpp::icall::MathF_Tan");
        RegisterICall("System.MathF", "Asin", 1, "cil2cpp::icall::MathF_Asin");
        RegisterICall("System.MathF", "Acos", 1, "cil2cpp::icall::MathF_Acos");
        RegisterICall("System.MathF", "Atan", 1, "cil2cpp::icall::MathF_Atan");
        RegisterICall("System.MathF", "Atan2", 2, "cil2cpp::icall::MathF_Atan2");
        RegisterICall("System.MathF", "Pow", 2, "cil2cpp::icall::MathF_Pow");
        RegisterICall("System.MathF", "Exp", 1, "cil2cpp::icall::MathF_Exp");
        RegisterICall("System.MathF", "Log", 1, "cil2cpp::icall::MathF_Log");
        RegisterICall("System.MathF", "Log10", 1, "cil2cpp::icall::MathF_Log10");
        RegisterICall("System.MathF", "Log2", 1, "cil2cpp::icall::MathF_Log2");
        RegisterICall("System.MathF", "Floor", 1, "cil2cpp::icall::MathF_Floor");
        RegisterICall("System.MathF", "Ceiling", 1, "cil2cpp::icall::MathF_Ceiling");
        RegisterICall("System.MathF", "Round", 1, "cil2cpp::icall::MathF_Round");
        RegisterICall("System.MathF", "Truncate", 1, "cil2cpp::icall::MathF_Truncate");
        RegisterICall("System.MathF", "Max", 2, "cil2cpp::icall::MathF_Max");
        RegisterICall("System.MathF", "Min", 2, "cil2cpp::icall::MathF_Min");
        RegisterICall("System.MathF", "Cbrt", 1, "cil2cpp::icall::MathF_Cbrt");
        RegisterICall("System.MathF", "FusedMultiplyAdd", 3, "cil2cpp::icall::MathF_FusedMultiplyAdd");

        // (Array ICalls moved to main section above)

        // ===== System.ThrowHelper =====
        // BCL's centralized exception throwers — they depend on SR resource strings
        // which need P/Invoke to native resources. Map to our runtime throw functions.
        RegisterICall("System.ThrowHelper", "ThrowArgumentOutOfRangeException", 1,
            "cil2cpp::icall::ThrowHelper_ThrowArgumentOutOfRangeException");
        RegisterICall("System.ThrowHelper", "ThrowArgumentOutOfRangeException", 2,
            "cil2cpp::icall::ThrowHelper_ThrowArgumentOutOfRangeException2");
        RegisterICall("System.ThrowHelper", "ThrowArgumentNullException", 1,
            "cil2cpp::icall::ThrowHelper_ThrowArgumentNullException");
        RegisterICallTyped("System.ThrowHelper", "ThrowArgumentNullException", 1,
            "System.String", "cil2cpp::icall::ThrowHelper_ThrowArgumentNullException2");
        RegisterICall("System.ThrowHelper", "ThrowArgumentException", 1,
            "cil2cpp::icall::ThrowHelper_ThrowArgumentException");
        RegisterICall("System.ThrowHelper", "ThrowArgumentException", 2,
            "cil2cpp::icall::ThrowHelper_ThrowArgumentException2");
        RegisterICall("System.ThrowHelper", "ThrowInvalidOperationException", 1,
            "cil2cpp::icall::ThrowHelper_ThrowInvalidOperationException");
        RegisterICall("System.ThrowHelper", "ThrowInvalidOperationException", 0,
            "cil2cpp::icall::ThrowHelper_ThrowInvalidOperationException0");
        RegisterICall("System.ThrowHelper", "ThrowNotSupportedException", 1,
            "cil2cpp::icall::ThrowHelper_ThrowNotSupportedException");
        RegisterICall("System.ThrowHelper", "ThrowNotSupportedException", 0,
            "cil2cpp::icall::ThrowHelper_ThrowNotSupportedException0");
        RegisterICall("System.ThrowHelper", "ThrowFormatInvalidString", 2,
            "cil2cpp::icall::ThrowHelper_ThrowFormatInvalidString");
        RegisterICall("System.ThrowHelper", "ThrowUnexpectedStateForKnownCallback", 1,
            "cil2cpp::icall::ThrowHelper_ThrowUnexpectedStateForKnownCallback");
        // GetXxx factory pattern (return exception objects for caller to throw)
        RegisterICall("System.ThrowHelper", "GetArgumentException", 1,
            "cil2cpp::icall::ThrowHelper_GetArgumentException");
        RegisterICall("System.ThrowHelper", "GetArgumentException", 2,
            "cil2cpp::icall::ThrowHelper_GetArgumentException2");
        RegisterICall("System.ThrowHelper", "GetArgumentOutOfRangeException", 1,
            "cil2cpp::icall::ThrowHelper_GetArgumentOutOfRangeException");
        RegisterICall("System.ThrowHelper", "GetInvalidOperationException", 1,
            "cil2cpp::icall::ThrowHelper_GetInvalidOperationException");
        RegisterICall("System.ThrowHelper", "GetResourceString", 1,
            "cil2cpp::icall::ThrowHelper_GetResourceString");
        RegisterICall("System.ThrowHelper", "GetArgumentName", 1,
            "cil2cpp::icall::ThrowHelper_GetArgumentName");

        // ===== System.SR (resource string resolution) =====
        // AOT: return the resource key directly instead of going through ResourceManager.
        // The full SR.InternalGetResourceString path triggers CultureInfo initialization which
        // creates an infinite recursion cycle (CultureNotSupported → SR.GetResourceString → CultureInfo → ...).
        RegisterICall("System.SR", "InternalGetResourceString", 1,
            "cil2cpp::icall::SR_GetResourceString");
        RegisterICall("System.SR", "GetResourceString", 1,
            "cil2cpp::icall::SR_GetResourceString");

        // ===== System.Diagnostics.Tracing.EventSource (no-op — ETW tracing disabled in AOT) =====
        // EventSource is excluded by ReachabilityAnalyzer (System.Diagnostics.Tracing namespace),
        // but derived types TplEventSource (System.Threading.Tasks) and ArrayPoolEventSource
        // (System.Buffers) ARE reachable and call base EventSource methods.
        // Returning false from IsEnabled() makes all tracing methods early-return (no-op at runtime).
        RegisterICall("System.Diagnostics.Tracing.EventSource", ".ctor", 0, "cil2cpp::eventsource_ctor");
        RegisterICall("System.Diagnostics.Tracing.EventSource", "IsEnabled", 0, "cil2cpp::eventsource_is_enabled");
        RegisterICall("System.Diagnostics.Tracing.EventSource", "IsEnabled", 2, "cil2cpp::eventsource_is_enabled_level");
        RegisterICall("System.Diagnostics.Tracing.EventSource", "get_IsSupported", 0, "cil2cpp::eventsource_get_is_supported");
        RegisterICallWildcard("System.Diagnostics.Tracing.EventSource", "WriteEvent", "cil2cpp::eventsource_write_event");

        // ===== Interop.GetRandomBytes — platform-specific RNG =====
        // BCL uses Interop.GetRandomBytes (→ BCrypt.GenRandom on Windows, /dev/urandom on Linux)
        // for seeding hash tables, PRNG, etc. Provide C++ implementation.
        RegisterICall("System.HashCode", "GenerateGlobalSeed", 0, "cil2cpp::icall::HashCode_GenerateGlobalSeed");
        RegisterICall("System.Marvin", "GenerateSeed", 0, "cil2cpp::icall::Marvin_GenerateSeed");

        // ===== System.Diagnostics.Tracing (additional no-ops) =====
        RegisterICall("System.Diagnostics.Tracing.EventSource", "SetCurrentThreadActivityId", 1,
            "cil2cpp::eventsource_write_event"); // no-op
        RegisterICall("System.Diagnostics.Tracing.ActivityTracker", "get_Instance", 0,
            "cil2cpp::icall::ActivityTracker_get_Instance");

        // ===== System.Type (reflection introspection) =====
        RegisterICall("System.Type", "GetEnumUnderlyingType", 0, "cil2cpp::icall::Type_GetEnumUnderlyingType");
        RegisterICall("System.Type", "get_IsPublic", 0, "cil2cpp::icall::Type_get_IsPublic");
        RegisterICall("System.Type", "get_IsValueType", 0, "cil2cpp::icall::Type_get_IsValueType");
        RegisterICall("System.Type", "get_IsAbstract", 0, "cil2cpp::icall::Type_get_IsAbstract");
        RegisterICall("System.Type", "get_IsNestedPublic", 0, "cil2cpp::icall::Type_get_IsNestedPublic");
        RegisterICall("System.Type", "IsArrayImpl", 0, "cil2cpp::icall::Type_IsArrayImpl");
        RegisterICall("System.Type", "IsEnumDefined", 1, "cil2cpp::icall::Type_IsEnumDefined");
        RegisterICall("System.Type", "IsEquivalentTo", 1, "cil2cpp::icall::Type_IsEquivalentTo");
        RegisterICall("System.Type", "GetTypeCodeImpl", 0, "cil2cpp::icall::Type_GetTypeCodeImpl");
        RegisterICall("System.Type", "get_GenericParameterAttributes", 0,
            "cil2cpp::icall::Type_get_GenericParameterAttributes");

        // ===== System.RuntimeTypeHandle =====
        // .ctor is NOT [InternalCall] — it's a regular ctor (m_type = type), compiles from IL
        RegisterICall("System.RuntimeTypeHandle", "GetElementType", 1,
            "cil2cpp::icall::RuntimeTypeHandle_GetElementType");
        RegisterICall("System.RuntimeTypeHandle", "IsEquivalentTo", 1,
            "cil2cpp::icall::RuntimeTypeHandle_IsEquivalentTo");
        RegisterICall("System.RuntimeTypeHandle", "GetAssembly", 1,
            "cil2cpp::icall::RuntimeTypeHandle_GetAssembly");
        RegisterICall("System.RuntimeTypeHandle", "IsByRefLike", 1,
            "cil2cpp::icall::RuntimeTypeHandle_IsByRefLike");
        RegisterICall("System.RuntimeTypeHandle", "GetToken", 1,
            "cil2cpp::icall::RuntimeTypeHandle_GetToken");
        RegisterICall("System.RuntimeTypeHandle", "IsInstanceOfType", 2,
            "cil2cpp::icall::RuntimeTypeHandle_IsInstanceOfType");
        RegisterICall("System.RuntimeTypeHandle", "GetDeclaringMethod", 1,
            "cil2cpp::icall::RuntimeTypeHandle_GetDeclaringMethod");

        // ===== System.RuntimeMethodHandle =====
        RegisterICall("System.RuntimeMethodHandle", "IsDynamicMethod", 1,
            "cil2cpp::icall::RuntimeMethodHandle_IsDynamicMethod");
        RegisterICall("System.RuntimeMethodHandle", "ReboxToNullable", 2,
            "cil2cpp::icall::RuntimeMethodHandle_ReboxToNullable");

        // ===== System.RuntimeType (internal helpers) =====
        RegisterICall("System.RuntimeType", "CanValueSpecialCast", 0,
            "cil2cpp::icall::RuntimeType_CanValueSpecialCast");
        RegisterICall("System.RuntimeType", "_CreateEnum", 2,
            "cil2cpp::icall::RuntimeType_CreateEnum");

        // ===== System.Reflection (binding flags and introspection) =====
        RegisterICall("System.Reflection.TypeInfo", "AsType", 0, "cil2cpp::icall::TypeInfo_AsType");
        RegisterICall("System.Reflection.MethodBase", "get_IsVirtual", 0,
            "cil2cpp::icall::MethodBase_get_IsVirtual");
        RegisterICall("System.Reflection.RuntimeMethodInfo", "get_BindingFlags", 0,
            "cil2cpp::icall::RuntimeMethodInfo_get_BindingFlags");
        RegisterICall("System.Reflection.RuntimeMethodInfo", "GetGenericArgumentsInternal", 0,
            "cil2cpp::icall::RuntimeMethodInfo_GetGenericArgumentsInternal");
        RegisterICall("System.Reflection.RuntimeMethodInfo", "GetDeclaringTypeInternal", 0,
            "cil2cpp::icall::RuntimeMethodInfo_GetDeclaringTypeInternal");
        RegisterICall("System.Reflection.RuntimeConstructorInfo", "get_BindingFlags", 0,
            "cil2cpp::icall::RuntimeConstructorInfo_get_BindingFlags");
        RegisterICall("System.Reflection.RuntimeFieldInfo", "get_BindingFlags", 0,
            "cil2cpp::icall::RuntimeFieldInfo_get_BindingFlags");

        // ===== System.Delegate (reflection) =====
        RegisterICall("System.Delegate", "get_Method", 0, "cil2cpp::icall::Delegate_get_Method");

        // ===== System.Runtime.InteropServices.GCHandle =====
        RegisterICall("System.Runtime.InteropServices.GCHandle", "InternalCompareExchange", 3,
            "cil2cpp::icall::GCHandle_InternalCompareExchange");

        // ===== System.Diagnostics (stack traces) =====
        RegisterICall("System.Diagnostics.StackFrameHelper", "GetMethodBase", 2,
            "cil2cpp::icall::StackFrameHelper_GetMethodBase");
        RegisterICall("System.Diagnostics.StackFrame", "GetMethod", 0,
            "cil2cpp::icall::StackFrame_GetMethod");

        // ===== System.Runtime.Loader =====
        RegisterICall("System.Runtime.Loader.AssemblyLoadContext", "OnTypeResolve", 1,
            "cil2cpp::icall::AssemblyLoadContext_OnTypeResolve");

        // ===== System.Runtime.InteropServices.Marshal (additional) =====
        // StringToCoTaskMemUni — IL body does void* arithmetic (C++ C2036). ICall avoids this.
        RegisterICall("System.Runtime.InteropServices.Marshal", "StringToCoTaskMemUni", 1,
            "cil2cpp::icall::Marshal_StringToCoTaskMemUni");

        // ===== System.Runtime.InteropServices.NativeLibrary =====
        RegisterICall("System.Runtime.InteropServices.NativeLibrary", "GetSymbol", 2,
            "cil2cpp::icall::NativeLibrary_GetSymbol");

        // ===== System.Threading.ThreadPool (CIL2CPP has its own thread pool) =====
        // BCL ThreadPool methods are [InternalCall] — map to our runtime thread pool.
        // Most are no-ops (CIL2CPP thread pool manages its own workers/metrics).
        RegisterICall("System.Threading.ThreadPool", "GetNextConfigUInt32Value", 4,
            "cil2cpp::icall::ThreadPool_GetNextConfigUInt32Value");
        RegisterICall("System.Threading.ThreadPool", "GetOrCreateThreadLocalCompletionCountObject", 0,
            "cil2cpp::icall::ThreadPool_GetOrCreateThreadLocalCompletionCountObject");
        RegisterICall("System.Threading.ThreadPool", "NotifyWorkItemComplete", 2,
            "cil2cpp::icall::ThreadPool_NotifyWorkItemComplete");
        RegisterICall("System.Threading.ThreadPool", "NotifyWorkItemProgress", 0,
            "cil2cpp::icall::ThreadPool_NotifyWorkItemProgress");
        RegisterICall("System.Threading.ThreadPool", "ReportThreadStatus", 1,
            "cil2cpp::icall::ThreadPool_ReportThreadStatus");
        RegisterICall("System.Threading.ThreadPool", "RequestWorkerThread", 0,
            "cil2cpp::icall::ThreadPool_RequestWorkerThread");
        RegisterICall("System.Threading.ThreadPoolWorkQueue", "Dispatch", 0,
            "cil2cpp::icall::ThreadPoolWorkQueue_Dispatch");
        RegisterICall("System.Threading.ThreadPoolWorkQueue", "Enqueue", 2,
            "cil2cpp::icall::ThreadPoolWorkQueue_Enqueue");
        RegisterICall("System.Threading.WindowsThreadPool", "RequestWorkerThread", 0,
            "cil2cpp::icall::ThreadPool_RequestWorkerThread"); // same impl

        // ===== System.Threading.Interlocked (additional) =====
        RegisterICallTyped("System.Threading.Interlocked", "ExchangeAdd", 2, "System.Int32&",
            "cil2cpp::icall::Interlocked_ExchangeAdd_i32");
        RegisterICallTyped("System.Threading.Interlocked", "ExchangeAdd", 2, "System.Int64&",
            "cil2cpp::icall::Interlocked_ExchangeAdd_i64");

        // ===== System.ArgIterator =====
        // ArgIterator is 100% [InternalCall] in BCL. Our runtime uses VarArgHandle metadata
        // constructed at call sites and passed as intptr_t to varargs methods.
        RegisterICall("System.ArgIterator", ".ctor", 1, "cil2cpp::argiterator_init");
        RegisterICall("System.ArgIterator", "GetRemainingCount", 0, "cil2cpp::argiterator_get_remaining_count");
        RegisterICall("System.ArgIterator", "GetNextArg", 0, "cil2cpp::argiterator_get_next_arg");
        RegisterICall("System.ArgIterator", "End", 0, "cil2cpp::argiterator_end");

        // ===== Interop.Globalization P/Invoke stubs =====
        // Low-level ICU P/Invoke wrappers called from CultureData, CultureInfo, CalendarData.
        // Higher-level operations (CompareInfo, TextInfo) are handled by dedicated ICalls above.
        // TODO: Replace stubs with full ICU implementations for locale data correctness.
        RegisterICallWildcard("Interop/Globalization", "IndexOf", "cil2cpp::interop_globalization_return_neg");
        RegisterICallWildcard("Interop/Globalization", "StartsWith", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "EndsWith", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetSortHandle", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "IanaIdToWindowsId", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "CompareString", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetLocaleName", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetLocaleInfoString", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetLocaleInfoInt", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetLocaleInfoGroupingSizes", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetLocaleTimeFormat", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "IsPredefinedLocale", "cil2cpp::interop_globalization_return_one");
        RegisterICallWildcard("Interop/Globalization", "GetCalendars", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetLatestJapaneseEra", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetJapaneseEraStartDate", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "GetCalendarInfo", "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("Interop/Globalization", "LoadICU", "cil2cpp::interop_globalization_return_one");
        RegisterICallWildcard("Interop/Globalization", "InitICUFunctions", "cil2cpp::interop_globalization_return_one");

        // ===== Internal.Win32.RegistryKey stubs =====
        // Windows registry access — not meaningful in AOT binaries. Return failure codes.
        RegisterICallWildcard("Internal.Win32.RegistryKey", "OpenSubKey", "cil2cpp::win32_registry_stub");
        RegisterICallWildcard("Internal.Win32.RegistryKey", "GetValue", "cil2cpp::win32_registry_stub");

        // ===== Interop.NtDll stubs =====
        RegisterICallWildcard("Interop/NtDll", "RtlGetVersionEx", "cil2cpp::interop_ntdll_stub");
        RegisterICallWildcard("Interop/NtDll", "NtQuerySystemInformation", "cil2cpp::interop_ntdll_stub");

        // ===== Interop.User32 stubs =====
        RegisterICallWildcard("Interop/User32", "LoadString", "cil2cpp::interop_user32_stub");

        // ===== Interop.Ucrtbase stubs =====
        RegisterICallWildcard("Interop/Ucrtbase", "malloc", "cil2cpp::interop_ucrtbase_malloc");
        RegisterICallWildcard("Interop/Ucrtbase", "free", "cil2cpp::interop_ucrtbase_free");

        // ===== Interop.BCrypt stubs =====
        RegisterICallWildcard("Interop/BCrypt", "BCryptGenRandom", "cil2cpp::interop_bcrypt_stub");

        // ===== System.Array (additional) =====
        RegisterICallWildcard("System.Array", "InternalCreate", "cil2cpp::array_internal_create");

        // ===== System.Delegate (additional) =====
        RegisterICallWildcard("System.Delegate", "BindToMethodInfo", "cil2cpp::delegate_bind_to_method_info");

        // ===== System.RuntimeTypeHandle (additional) =====
        RegisterICall("System.RuntimeTypeHandle", "GetArrayRank", 1,
            "cil2cpp::interop_globalization_return_one"); // default rank 1

        // ===== System.Reflection.MethodBase / System.Type (additional) =====
        RegisterICall("System.Reflection.MethodBase", "get_IsAbstract", 0,
            "cil2cpp::interop_globalization_return_zero");
        RegisterICall("System.Type", "get_IsNestedAssembly", 0,
            "cil2cpp::interop_globalization_return_zero");
        RegisterICall("System.Type", "get_IsNotPublic", 0,
            "cil2cpp::interop_globalization_return_zero");

        // ===== System.Diagnostics (additional stack trace stubs) =====
        RegisterICallWildcard("System.Diagnostics.StackFrameHelper", "GetMethodBase",
            "cil2cpp::stackframehelper_get_method_base");
        RegisterICall("System.Diagnostics.StackFrame", "GetILOffset", 0,
            "cil2cpp::stackframe_get_il_offset");
        RegisterICall("System.Diagnostics.StackFrame", "GetFileName", 0,
            "cil2cpp::stackframehelper_get_method_base"); // returns nullptr (void*)

        // ===== Additional Interop.Globalization stubs =====
        RegisterICallWildcard("Interop/Globalization", "LastIndexOf", "cil2cpp::interop_globalization_return_neg");
        RegisterICallWildcard("Interop/Globalization", "CloseSortHandle", "cil2cpp::interop_globalization_return_zero");

        // ===== Internal.Win32.RegistryKey additional stubs =====
        RegisterICallWildcard("Internal.Win32.RegistryKey", "GetSubKeyNames", "cil2cpp::win32_registry_stub");
        RegisterICallWildcard("Internal.Win32.RegistryKey", "GetValueNames", "cil2cpp::win32_registry_stub");

        // ===== System.RuntimeMethodHandle (additional) =====
        RegisterICall("System.RuntimeMethodHandle", "GetResolver", 1,
            "cil2cpp::stackframehelper_get_method_base"); // returns nullptr
        RegisterICall("System.RuntimeTypeHandle", "IsEquivalentTo", 2,
            "cil2cpp::interop_globalization_return_zero"); // returns false

        // ===== System.RuntimeType (additional) =====
        RegisterICall("System.RuntimeType", "CanValueSpecialCast", 0,
            "cil2cpp::interop_globalization_return_zero"); // returns false

        // ===== System.Diagnostics.Tracing.EventSource (additional) =====
        RegisterICallWildcard("System.Diagnostics.Tracing.EventSource", "SetCurrentThreadActivityId",
            "cil2cpp::interop_globalization_return_zero");

        // ===== System.Threading.Tasks interface stubs =====
        RegisterICallWildcard("System.Threading.Tasks.TaskContinuation", "Run",
            "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("System.Threading.Tasks.ITaskCompletionAction", "get_InvokeMayRunArbitraryCode",
            "cil2cpp::interop_globalization_return_one");

        // ===== System.Reflection.Binder =====
        RegisterICallWildcard("System.Reflection.Binder", "ChangeType",
            "cil2cpp::stackframehelper_get_method_base"); // returns nullptr

        // ===== System.Reflection.Emit stubs (dynamic code gen — no-op in AOT) =====
        RegisterICallWildcard("System.Reflection.Emit.ILGenerator", "Emit",
            "cil2cpp::interop_globalization_return_zero");
        RegisterICallWildcard("System.Runtime.Loader.AssemblyLoadContext", "OnTypeResolve",
            "cil2cpp::stackframehelper_get_method_base"); // returns nullptr
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

        // Fallback: for closed generic types (e.g., DontNegate`1<Int16>),
        // also try the open generic type name (e.g., DontNegate`1).
        if (methodRef.DeclaringType is Mono.Cecil.GenericInstanceType git)
        {
            var openResult = Lookup(git.ElementType.FullName, methodName, paramCount, firstParamType);
            if (openResult != null)
                return openResult;
        }

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
