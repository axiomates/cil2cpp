/**
 * CIL2CPP Runtime - Globalization Support (ICU4C backed)
 *
 * Provides CompareInfo, Ordinal, OrdinalCasing, TextInfo, and CultureData
 * functionality via ICU4C. Replaces BCL managed code that depends on
 * System.Globalization.Native P/Invoke or NLS (kernel32).
 */

#pragma once

#include "types.h"
#include "string.h"

namespace cil2cpp {
namespace globalization {

/// Initialize globalization subsystem. Called from runtime_init().
void init();

/// Shutdown globalization subsystem. Called from runtime_shutdown().
void shutdown();

// ===== CompareInfo ICalls =====
// CompareOptions enum values (System.Globalization.CompareOptions):
//   None=0, IgnoreCase=1, IgnoreNonSpace=2, IgnoreSymbols=4,
//   IgnoreKanaType=8, IgnoreWidth=16, OrdinalIgnoreCase=0x10000000, Ordinal=0x40000000

// StringComparison enum values (System.StringComparison):
//   CurrentCulture=0, CurrentCultureIgnoreCase=1,
//   InvariantCulture=2, InvariantCultureIgnoreCase=3,
//   Ordinal=4, OrdinalIgnoreCase=5

/// CompareInfo.Compare(string, string, CompareOptions) — instance, 3 params
Int32 compareinfo_compare_string_string(void* __this,
    String* string1, String* string2, Int32 options);

/// CompareInfo.Compare(string, string) — instance, 2 params
Int32 compareinfo_compare_string_string_2(void* __this,
    String* string1, String* string2);

/// CompareInfo.Compare(string, int, int, string, int, int, CompareOptions) — instance, 7 params
Int32 compareinfo_compare_substring(void* __this,
    String* string1, Int32 offset1, Int32 length1,
    String* string2, Int32 offset2, Int32 length2, Int32 options);

/// CompareInfo.IndexOf(string, string, int, int, CompareOptions) — instance, 5 params
Int32 compareinfo_index_of(void* __this,
    String* source, String* value, Int32 startIndex, Int32 count, Int32 options);

/// CompareInfo.IsPrefix(string, string, CompareOptions) — instance, 3 params
Boolean compareinfo_is_prefix(void* __this,
    String* source, String* prefix, Int32 options);

/// CompareInfo.IsSuffix(string, string, CompareOptions) — instance, 3 params
Boolean compareinfo_is_suffix(void* __this,
    String* source, String* suffix, Int32 options);

/// CompareInfo.CanUseAsciiOrdinalForOptions — static, 1 param
Boolean compareinfo_can_use_ascii_ordinal(Int32 options);

/// CompareInfo.CheckCompareOptionsForCompare — static, 1 param (void return, throws on invalid)
void compareinfo_check_options(Int32 options);

/// CompareInfo.GetNativeCompareFlags — static, 1 param
Int32 compareinfo_get_native_flags(Int32 options);

/// CompareInfo.ThrowCompareOptionsCheckFailed — static, 1 param (always throws)
void compareinfo_throw_options_failed(Int32 options);

/// CompareInfo/SortHandleCache.GetCachedSortHandle — static, 1 param
intptr_t compareinfo_get_sort_handle(String* sortName);

// ===== String ICalls =====

/// String.Compare(string, string, StringComparison) — static, 3 params
Int32 string_compare_3(String* strA, String* strB, Int32 comparisonType);

/// String.Compare(string, int, string, int, int, StringComparison) — static, 6 params
Int32 string_compare_6(String* strA, Int32 indexA, String* strB, Int32 indexB,
    Int32 length, Int32 comparisonType);

/// String.Equals(string, StringComparison) — instance, 2 params
Boolean string_equals_comparison(String* __this, String* value, Int32 comparisonType);

/// String.EndsWith(string, StringComparison) — instance, 2 params
Boolean string_ends_with(String* __this, String* value, Int32 comparisonType);

/// String.StartsWith(string, StringComparison) — instance, 2 params
Boolean string_starts_with(String* __this, String* value, Int32 comparisonType);

/// String.IndexOf(string, int, StringComparison) — instance, 3 params
Int32 string_index_of_3(String* __this, String* value, Int32 startIndex, Int32 comparisonType);

/// String.IndexOf(string, int, int, StringComparison) — instance, 4 params
Int32 string_index_of_4(String* __this, String* value, Int32 startIndex,
    Int32 count, Int32 comparisonType);

/// String.GetCaseCompareOfComparisonCulture — static, 1 param
Int32 string_get_case_compare(Int32 comparisonType);

/// String.CheckStringComparison — static, 1 param (throws on invalid)
void string_check_comparison(Int32 comparisonType);

// ===== Ordinal ICalls =====

/// Ordinal.EqualsIgnoreCase(ref char, ref char, int) — static, 3 params
Boolean ordinal_equals_ignore_case(Char* charA, Char* charB, Int32 length);

/// Ordinal.CompareStringIgnoreCase(ref char, int, ref char, int) — static, 4 params
Int32 ordinal_compare_ignore_case(Char* strA, Int32 lengthA, Char* strB, Int32 lengthB);

// ===== OrdinalCasing ICalls =====

/// OrdinalCasing.ToUpper(char) — static, 1 param
Char ordinal_casing_to_upper(Char c);

// ===== TextInfo ICalls =====

/// TextInfo.ChangeCaseCore(char*, int, char*, int, bool) — instance, 5 params
void textinfo_change_case_core(void* __this,
    Char* src, Int32 srcLen, Char* dstBuffer, Int32 dstBufferCapacity, Boolean bToUpper);

/// TextInfo.IcuChangeCase(char*, int, char*, int, bool) — instance, 5 params
void textinfo_icu_change_case(void* __this,
    Char* src, Int32 srcLen, Char* dstBuffer, Int32 dstBufferCapacity, Boolean bToUpper);

// ===== CultureData ICalls =====

/// GlobalizationMode.get_UseNls — static property getter
Boolean globalization_mode_get_use_nls();

// ===== Noop stubs for initialization methods that use data tables =====

/// OrdinalCasing.InitCasingTable() — returns null (we use ICU instead)
void* ordinal_casing_init_table();

/// OrdinalCasing.InitOrdinalCasingPage(int) — returns null (we use ICU instead)
void* ordinal_casing_init_page(Int32 pageNumber);

} // namespace globalization
} // namespace cil2cpp
