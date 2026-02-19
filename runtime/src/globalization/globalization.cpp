/**
 * CIL2CPP Runtime - Globalization (ICU4C backed)
 *
 * Provides CompareInfo, Ordinal, OrdinalCasing, TextInfo, and CultureData
 * functionality via ICU4C collation, string search, and locale APIs.
 */

#include <cil2cpp/globalization.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/unicode.h>

#include <unicode/ucol.h>     // Collation: ucol_open, ucol_strcoll, ucol_setStrength
#include <unicode/usearch.h>  // String search: usearch_open, usearch_first, usearch_last
#include <unicode/uloc.h>     // Locale: uloc_getDefault
#include <unicode/uchar.h>    // Character: u_toupper
#include <unicode/ustring.h>  // String: u_strToUpper, u_strToLower
#include <unicode/utypes.h>   // UErrorCode

#include <cstring>
#include <mutex>

namespace cil2cpp {
namespace globalization {

// ===== Collator Cache =====
// Cache a collator per sort handle to avoid repeated ucol_open() calls.
// For simplicity, we cache the invariant (root) collator only.
// Thread-safe via mutex.

static UCollator* g_invariant_collator = nullptr;
static std::mutex g_collator_mutex;

void init() {
    UErrorCode err = U_ZERO_ERROR;
    g_invariant_collator = ucol_open("", &err); // root = invariant culture
    if (U_FAILURE(err)) {
        g_invariant_collator = nullptr;
    }
}

void shutdown() {
    if (g_invariant_collator) {
        ucol_close(g_invariant_collator);
        g_invariant_collator = nullptr;
    }
}

// ===== CompareOptions → ICU Strength Mapping =====
// CompareOptions: None=0, IgnoreCase=1, IgnoreNonSpace=2, IgnoreSymbols=4,
//   IgnoreKanaType=8, IgnoreWidth=16, OrdinalIgnoreCase=0x10000000, Ordinal=0x40000000

enum CompareOptions : int32_t {
    CO_None = 0,
    CO_IgnoreCase = 1,
    CO_IgnoreNonSpace = 2,
    CO_IgnoreSymbols = 4,
    CO_OrdinalIgnoreCase = 0x10000000,
    CO_Ordinal = 0x40000000,
};

enum StringComparison : int32_t {
    SC_CurrentCulture = 0,
    SC_CurrentCultureIgnoreCase = 1,
    SC_InvariantCulture = 2,
    SC_InvariantCultureIgnoreCase = 3,
    SC_Ordinal = 4,
    SC_OrdinalIgnoreCase = 5,
};

// ===== Internal Helpers =====

/// Ordinal compare: raw memcmp-style comparison of UTF-16 code units.
static Int32 ordinal_compare(const Char* s1, Int32 len1, const Char* s2, Int32 len2) {
    Int32 minLen = len1 < len2 ? len1 : len2;
    for (Int32 i = 0; i < minLen; i++) {
        if (s1[i] != s2[i])
            return static_cast<Int32>(s1[i]) - static_cast<Int32>(s2[i]);
    }
    return len1 - len2;
}

/// Ordinal compare ignore case: per-char u_toupper comparison.
static Int32 ordinal_compare_ic(const Char* s1, Int32 len1, const Char* s2, Int32 len2) {
    Int32 minLen = len1 < len2 ? len1 : len2;
    for (Int32 i = 0; i < minLen; i++) {
        UChar32 c1 = u_toupper(static_cast<UChar32>(s1[i]));
        UChar32 c2 = u_toupper(static_cast<UChar32>(s2[i]));
        if (c1 != c2)
            return static_cast<Int32>(c1) - static_cast<Int32>(c2);
    }
    return len1 - len2;
}

/// Culture-sensitive compare using ICU collator.
static Int32 icu_compare(const Char* s1, Int32 len1, const Char* s2, Int32 len2, Int32 options) {
    std::lock_guard<std::mutex> lock(g_collator_mutex);
    if (!g_invariant_collator) {
        // Fallback to ordinal if ICU not initialized
        return ordinal_compare(s1, len1, s2, len2);
    }

    // Set collator strength based on options
    UCollationStrength strength = UCOL_TERTIARY; // default: case-sensitive
    if (options & CO_IgnoreCase)
        strength = UCOL_SECONDARY; // ignore case
    if (options & CO_IgnoreNonSpace)
        strength = UCOL_PRIMARY; // ignore accents too

    ucol_setStrength(g_invariant_collator, strength);

    UCollationResult result = ucol_strcoll(
        g_invariant_collator,
        reinterpret_cast<const UChar*>(s1), static_cast<int32_t>(len1),
        reinterpret_cast<const UChar*>(s2), static_cast<int32_t>(len2)
    );

    switch (result) {
        case UCOL_LESS:    return -1;
        case UCOL_GREATER: return 1;
        default:           return 0;
    }
}

/// Compare two strings with CompareOptions.
static Int32 compare_with_options(const Char* s1, Int32 len1, const Char* s2, Int32 len2, Int32 options) {
    if (options & CO_Ordinal)
        return ordinal_compare(s1, len1, s2, len2);
    if (options & CO_OrdinalIgnoreCase)
        return ordinal_compare_ic(s1, len1, s2, len2);
    return icu_compare(s1, len1, s2, len2, options);
}

/// Compare two strings with StringComparison.
static Int32 compare_with_string_comparison(String* a, String* b, Int32 comparisonType) {
    if (!a && !b) return 0;
    if (!a) return -1;
    if (!b) return 1;

    switch (comparisonType) {
        case SC_Ordinal:
            return ordinal_compare(a->chars, a->length, b->chars, b->length);
        case SC_OrdinalIgnoreCase:
            return ordinal_compare_ic(a->chars, a->length, b->chars, b->length);
        case SC_InvariantCulture:
            return icu_compare(a->chars, a->length, b->chars, b->length, CO_None);
        case SC_InvariantCultureIgnoreCase:
            return icu_compare(a->chars, a->length, b->chars, b->length, CO_IgnoreCase);
        case SC_CurrentCulture:
            return icu_compare(a->chars, a->length, b->chars, b->length, CO_None);
        case SC_CurrentCultureIgnoreCase:
            return icu_compare(a->chars, a->length, b->chars, b->length, CO_IgnoreCase);
        default:
            return ordinal_compare(a->chars, a->length, b->chars, b->length);
    }
}

/// Find index of value in source with CompareOptions.
static Int32 index_of_with_options(const Char* source, Int32 sourceLen,
    const Char* value, Int32 valueLen, Int32 startIndex, Int32 count, Int32 options)
{
    if (valueLen == 0) return startIndex;
    if (sourceLen == 0 || count == 0) return -1;
    if (startIndex + count > sourceLen) count = sourceLen - startIndex;

    // Ordinal path: simple substring search
    if ((options & CO_Ordinal) || (options & CO_OrdinalIgnoreCase) || options == CO_None) {
        bool ignoreCase = (options & CO_IgnoreCase) || (options & CO_OrdinalIgnoreCase);
        for (Int32 i = startIndex; i <= startIndex + count - valueLen; i++) {
            bool match = true;
            for (Int32 j = 0; j < valueLen; j++) {
                Char c1 = source[i + j];
                Char c2 = value[j];
                if (ignoreCase) {
                    c1 = static_cast<Char>(u_toupper(static_cast<UChar32>(c1)));
                    c2 = static_cast<Char>(u_toupper(static_cast<UChar32>(c2)));
                }
                if (c1 != c2) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    // Culture-sensitive: use ICU string search
    std::lock_guard<std::mutex> lock(g_collator_mutex);
    if (!g_invariant_collator) {
        // Fallback to ordinal
        return index_of_with_options(source, sourceLen, value, valueLen,
            startIndex, count, CO_Ordinal);
    }

    UErrorCode err = U_ZERO_ERROR;
    UStringSearch* search = usearch_open(
        reinterpret_cast<const UChar*>(value), static_cast<int32_t>(valueLen),
        reinterpret_cast<const UChar*>(source + startIndex), static_cast<int32_t>(count),
        uloc_getDefault(), nullptr, &err
    );

    if (U_FAILURE(err) || !search) return -1;

    // Set strength for options
    UCollator* searchCollator = usearch_getCollator(search);
    if (options & CO_IgnoreCase)
        ucol_setStrength(searchCollator, UCOL_SECONDARY);

    usearch_reset(search);
    int32_t pos = usearch_first(search, &err);
    usearch_close(search);

    if (pos == USEARCH_DONE || U_FAILURE(err)) return -1;
    return startIndex + pos;
}

// ===== CompareInfo ICalls =====

Int32 compareinfo_compare_string_string(void* /*__this*/,
    String* string1, String* string2, Int32 options)
{
    if (!string1 && !string2) return 0;
    if (!string1) return -1;
    if (!string2) return 1;
    return compare_with_options(string1->chars, string1->length,
        string2->chars, string2->length, options);
}

Int32 compareinfo_compare_string_string_2(void* /*__this*/,
    String* string1, String* string2)
{
    return compareinfo_compare_string_string(nullptr, string1, string2, CO_None);
}

Int32 compareinfo_compare_substring(void* /*__this*/,
    String* string1, Int32 offset1, Int32 length1,
    String* string2, Int32 offset2, Int32 length2, Int32 options)
{
    const Char* s1 = string1 ? string1->chars + offset1 : nullptr;
    Int32 len1 = string1 ? length1 : 0;
    const Char* s2 = string2 ? string2->chars + offset2 : nullptr;
    Int32 len2 = string2 ? length2 : 0;

    if (!s1 && !s2) return 0;
    if (!s1) return -1;
    if (!s2) return 1;

    return compare_with_options(s1, len1, s2, len2, options);
}

Int32 compareinfo_index_of(void* /*__this*/,
    String* source, String* value, Int32 startIndex, Int32 count, Int32 options)
{
    if (!source || !value) return -1;
    return index_of_with_options(source->chars, source->length,
        value->chars, value->length, startIndex, count, options);
}

Boolean compareinfo_is_prefix(void* /*__this*/,
    String* source, String* prefix, Int32 options)
{
    if (!source || !prefix) return false;
    if (prefix->length == 0) return true;
    if (source->length < prefix->length) return false;

    return compare_with_options(source->chars, prefix->length,
        prefix->chars, prefix->length, options) == 0;
}

Boolean compareinfo_is_suffix(void* /*__this*/,
    String* source, String* suffix, Int32 options)
{
    if (!source || !suffix) return false;
    if (suffix->length == 0) return true;
    if (source->length < suffix->length) return false;

    Int32 offset = source->length - suffix->length;
    return compare_with_options(source->chars + offset, suffix->length,
        suffix->chars, suffix->length, options) == 0;
}

Boolean compareinfo_can_use_ascii_ordinal(Int32 options) {
    // BCL: options <= CompareOptions.IgnoreCase (i.e., None=0 or IgnoreCase=1)
    return options <= CO_IgnoreCase;
}

void compareinfo_check_options(Int32 options) {
    // Valid options: individual flags or Ordinal/OrdinalIgnoreCase (mutually exclusive with others)
    if ((options & CO_Ordinal) && (options & ~CO_Ordinal))
        throw_argument();
    if ((options & CO_OrdinalIgnoreCase) && (options & ~CO_OrdinalIgnoreCase))
        throw_argument();
}

Int32 compareinfo_get_native_flags(Int32 options) {
    // Return options as-is — our ICU implementation interprets them directly
    return options;
}

void compareinfo_throw_options_failed(Int32 /*options*/) {
    throw_argument();
}

intptr_t compareinfo_get_sort_handle(String* /*sortName*/) {
    // Return a non-null sentinel — we use the global collator, not per-handle collators
    // TODO: proper per-locale collator cache
    return reinterpret_cast<intptr_t>(g_invariant_collator);
}

// ===== String ICalls =====

Int32 string_compare_3(String* strA, String* strB, Int32 comparisonType) {
    return compare_with_string_comparison(strA, strB, comparisonType);
}

Int32 string_compare_6(String* strA, Int32 indexA, String* strB, Int32 indexB,
    Int32 length, Int32 comparisonType)
{
    // Extract substrings and compare
    const Char* s1 = strA ? strA->chars + indexA : nullptr;
    Int32 len1 = strA ? (length < strA->length - indexA ? length : strA->length - indexA) : 0;
    const Char* s2 = strB ? strB->chars + indexB : nullptr;
    Int32 len2 = strB ? (length < strB->length - indexB ? length : strB->length - indexB) : 0;

    if (!s1 && !s2) return 0;
    if (!s1) return -1;
    if (!s2) return 1;

    // Convert StringComparison to CompareOptions for the internal compare
    Int32 options = CO_None;
    switch (comparisonType) {
        case SC_Ordinal:               options = CO_Ordinal; break;
        case SC_OrdinalIgnoreCase:     options = CO_OrdinalIgnoreCase; break;
        case SC_InvariantCultureIgnoreCase:
        case SC_CurrentCultureIgnoreCase: options = CO_IgnoreCase; break;
        default: break;
    }
    return compare_with_options(s1, len1, s2, len2, options);
}

Boolean string_equals_comparison(String* __this, String* value, Int32 comparisonType) {
    return compare_with_string_comparison(__this, value, comparisonType) == 0 ? 1 : 0;
}

Boolean string_ends_with(String* __this, String* value, Int32 comparisonType) {
    if (!__this || !value) return false;
    if (value->length == 0) return true;
    if (__this->length < value->length) return false;

    Int32 offset = __this->length - value->length;
    String* sub = string_create_utf16(__this->chars + offset, value->length);
    return compare_with_string_comparison(sub, value, comparisonType) == 0 ? 1 : 0;
}

Boolean string_starts_with(String* __this, String* value, Int32 comparisonType) {
    if (!__this || !value) return false;
    if (value->length == 0) return true;
    if (__this->length < value->length) return false;

    String* sub = string_create_utf16(__this->chars, value->length);
    return compare_with_string_comparison(sub, value, comparisonType) == 0 ? 1 : 0;
}

Int32 string_index_of_3(String* __this, String* value, Int32 startIndex, Int32 comparisonType) {
    if (!__this || !value) return -1;
    Int32 options = CO_None;
    switch (comparisonType) {
        case SC_Ordinal:               options = CO_Ordinal; break;
        case SC_OrdinalIgnoreCase:     options = CO_OrdinalIgnoreCase; break;
        case SC_InvariantCultureIgnoreCase:
        case SC_CurrentCultureIgnoreCase: options = CO_IgnoreCase; break;
        default: break;
    }
    return index_of_with_options(__this->chars, __this->length,
        value->chars, value->length, startIndex, __this->length - startIndex, options);
}

Int32 string_index_of_4(String* __this, String* value, Int32 startIndex,
    Int32 count, Int32 comparisonType)
{
    if (!__this || !value) return -1;
    Int32 options = CO_None;
    switch (comparisonType) {
        case SC_Ordinal:               options = CO_Ordinal; break;
        case SC_OrdinalIgnoreCase:     options = CO_OrdinalIgnoreCase; break;
        case SC_InvariantCultureIgnoreCase:
        case SC_CurrentCultureIgnoreCase: options = CO_IgnoreCase; break;
        default: break;
    }
    return index_of_with_options(__this->chars, __this->length,
        value->chars, value->length, startIndex, count, options);
}

Int32 string_get_case_compare(Int32 comparisonType) {
    switch (comparisonType) {
        case SC_CurrentCultureIgnoreCase:
        case SC_InvariantCultureIgnoreCase:
        case SC_OrdinalIgnoreCase:
            return CO_IgnoreCase;
        default:
            return CO_None;
    }
}

void string_check_comparison(Int32 comparisonType) {
    if (comparisonType < SC_CurrentCulture || comparisonType > SC_OrdinalIgnoreCase)
        throw_argument_out_of_range();
}

// ===== Ordinal ICalls =====

Boolean ordinal_equals_ignore_case(Char* charA, Char* charB, Int32 length) {
    if (!charA || !charB) return false;
    for (Int32 i = 0; i < length; i++) {
        UChar32 c1 = u_toupper(static_cast<UChar32>(charA[i]));
        UChar32 c2 = u_toupper(static_cast<UChar32>(charB[i]));
        if (c1 != c2) return false;
    }
    return true;
}

Int32 ordinal_compare_ignore_case(Char* strA, Int32 lengthA, Char* strB, Int32 lengthB) {
    return ordinal_compare_ic(strA, lengthA, strB, lengthB);
}

// ===== OrdinalCasing ICalls =====

Char ordinal_casing_to_upper(Char c) {
    UChar32 result = u_toupper(static_cast<UChar32>(c));
    if (result > 0xFFFF) return c;
    return static_cast<Char>(result);
}

void* ordinal_casing_init_table() {
    return nullptr; // We use ICU instead of static tables
}

void* ordinal_casing_init_page(Int32 /*pageNumber*/) {
    return nullptr; // We use ICU instead of static tables
}

// ===== TextInfo ICalls =====

void textinfo_change_case_core(void* /*__this*/,
    Char* src, Int32 srcLen, Char* dstBuffer, Int32 dstBufferCapacity, Boolean bToUpper)
{
    UErrorCode err = U_ZERO_ERROR;
    if (bToUpper) {
        u_strToUpper(
            reinterpret_cast<UChar*>(dstBuffer), static_cast<int32_t>(dstBufferCapacity),
            reinterpret_cast<const UChar*>(src), static_cast<int32_t>(srcLen),
            uloc_getDefault(), &err
        );
    } else {
        u_strToLower(
            reinterpret_cast<UChar*>(dstBuffer), static_cast<int32_t>(dstBufferCapacity),
            reinterpret_cast<const UChar*>(src), static_cast<int32_t>(srcLen),
            uloc_getDefault(), &err
        );
    }
}

void textinfo_icu_change_case(void* __this,
    Char* src, Int32 srcLen, Char* dstBuffer, Int32 dstBufferCapacity, Boolean bToUpper)
{
    textinfo_change_case_core(__this, src, srcLen, dstBuffer, dstBufferCapacity, bToUpper);
}

// ===== CultureData ICalls =====

Boolean globalization_mode_get_use_nls() {
    return false; // Force ICU path — we have ICU4C, not NLS
}

} // namespace globalization
} // namespace cil2cpp
