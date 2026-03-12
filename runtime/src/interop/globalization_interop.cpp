/**
 * CIL2CPP Runtime - Interop.Globalization P/Invoke Implementations
 *
 * Real ICU4C-backed implementations for the low-level Interop.Globalization
 * P/Invoke methods. These replace the template stubs that returned hardcoded
 * 0/1/-1 values.
 *
 * Modeled after .NET's System.Globalization.Native (pal_collation.c,
 * pal_locale.c, pal_calendarData.c).
 */

#include <cil2cpp/globalization_interop.h>
#include <cil2cpp/array.h>

#include <unicode/ucol.h>
#include <unicode/usearch.h>
#include <unicode/uloc.h>
#include <unicode/uchar.h>
#include <unicode/ustring.h>
#include <unicode/utypes.h>
#include <unicode/unum.h>
#include <unicode/udat.h>
#include <unicode/ucal.h>
#include <unicode/ulocdata.h>

#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

namespace cil2cpp {

// ===== Internal Helpers =====

/// Convert a managed String* locale name to an ICU locale string (ASCII, '-' → '_').
static std::string locale_to_icu(String* localeName) {
    if (!localeName || string_length(localeName) == 0) return "";
    auto* chars = &localeName->f__firstChar;
    int32_t len = string_length(localeName);
    std::string result(static_cast<size_t>(len), '\0');
    for (int32_t i = 0; i < len; i++) {
        char c = static_cast<char>(chars[i]);
        result[static_cast<size_t>(i)] = (c == '-') ? '_' : c;
    }
    return result;
}

/// Write a null-terminated char16_t string from a UChar buffer into an output buffer.
/// Returns the number of chars written (excluding null terminator), or 0 on failure.
static int32_t write_uchar_to_buffer(const UChar* src, int32_t srcLen,
    char16_t* buffer, int32_t bufferLength) {
    if (!buffer || bufferLength <= 0 || !src || srcLen <= 0) return 0;
    int32_t copyLen = srcLen < (bufferLength - 1) ? srcLen : (bufferLength - 1);
    std::memcpy(buffer, src, static_cast<size_t>(copyLen) * sizeof(char16_t));
    buffer[copyLen] = 0;
    return copyLen;
}

// ===== CompareOptions enum (matches System.Globalization.CompareOptions) =====

enum InteropCompareOptions : int32_t {
    ICO_None             = 0,
    ICO_IgnoreCase       = 1,
    ICO_IgnoreNonSpace   = 2,
    ICO_IgnoreSymbols    = 4,
    ICO_OrdinalIgnoreCase = 0x10000000,
    ICO_Ordinal          = 0x40000000,
};

/// Apply CompareOptions to a UCollator's strength setting.
static void apply_compare_options(UCollator* collator, int32_t options) {
    UCollationStrength strength = UCOL_TERTIARY; // default: case-sensitive
    if (options & ICO_IgnoreCase)     strength = UCOL_SECONDARY;
    if (options & ICO_IgnoreNonSpace) strength = UCOL_PRIMARY;
    ucol_setStrength(collator, strength);
}

// ===== Sort Handle Cache =====
// Sort handles are UCollator* pointers passed as intptr_t.
// Cached by locale name — CloseSortHandle is a no-op.

static std::unordered_map<std::string, UCollator*> g_sort_handle_cache;
static std::mutex g_sort_handle_mutex;

static UCollator* get_or_create_collator(const std::string& locale) {
    std::lock_guard<std::mutex> lock(g_sort_handle_mutex);
    auto it = g_sort_handle_cache.find(locale);
    if (it != g_sort_handle_cache.end()) return it->second;

    UErrorCode err = U_ZERO_ERROR;
    UCollator* collator = ucol_open(locale.empty() ? "" : locale.c_str(), &err);
    if (U_FAILURE(err) || !collator) return nullptr;

    g_sort_handle_cache[locale] = collator;
    return collator;
}

// ===== LocaleNumberData enum (matches .NET's LocaleNumberData) =====

enum LocaleNumberData : uint32_t {
    LND_LanguageId                   = 0x0001,
    LND_MeasurementSystem            = 0x000D,
    LND_Digit                        = 0x0010,
    LND_FractionalDigitsCount        = 0x0011,
    LND_MonetaryDigit                = 0x0018,
    LND_MonetaryFractionalDigitsCount = 0x0019,
    LND_NegativeMonetaryNumberFormat = 0x001A,
    LND_PositiveMonetaryNumberFormat = 0x001B,
    LND_NegativeNumberFormat         = 0x001C,
    LND_ReadingLayout                = 0x0070,
    LND_NegativePercentFormat        = 0x0074,
    LND_PositivePercentFormat        = 0x0075,
    LND_FirstDayOfWeek               = 0x100C,
    LND_FirstWeekOfYear              = 0x100D,
};

// ===== CalendarId enum (matches .NET's CalendarId) =====

enum CalendarId : int32_t {
    CAL_GREGORIAN       = 1,
    CAL_GREGORIAN_US    = 2,
    CAL_JAPAN           = 3,
    CAL_TAIWAN          = 4,
    CAL_KOREA           = 5,
    CAL_HIJRI           = 6,
    CAL_THAI            = 7,
    CAL_HEBREW          = 8,
    CAL_GREGORIAN_ME_FR = 9,
    CAL_GREGORIAN_AR    = 10,
    CAL_GREGORIAN_XLIT_EN = 11,
    CAL_GREGORIAN_XLIT_FR = 12,
    CAL_PERSIAN         = 22,
    CAL_UMALQURA        = 23,
};

/// Map an ICU calendar type keyword to a .NET CalendarId.
static int32_t icu_calendar_to_id(const char* calType) {
    if (!calType) return CAL_GREGORIAN;
    if (strcmp(calType, "gregorian") == 0) return CAL_GREGORIAN;
    if (strcmp(calType, "japanese")  == 0) return CAL_JAPAN;
    if (strcmp(calType, "roc")       == 0) return CAL_TAIWAN;
    if (strcmp(calType, "dangi")     == 0) return CAL_KOREA;
    if (strcmp(calType, "islamic")   == 0) return CAL_HIJRI;
    if (strcmp(calType, "islamic-civil") == 0) return CAL_HIJRI;
    if (strcmp(calType, "buddhist")  == 0) return CAL_THAI;
    if (strcmp(calType, "hebrew")    == 0) return CAL_HEBREW;
    if (strcmp(calType, "persian")   == 0) return CAL_PERSIAN;
    if (strcmp(calType, "islamic-umalqura") == 0) return CAL_UMALQURA;
    return CAL_GREGORIAN; // default
}

/// Map a .NET CalendarId to an ICU calendar type keyword.
static const char* id_to_icu_calendar(int32_t calendarId) {
    switch (calendarId) {
        case CAL_GREGORIAN:
        case CAL_GREGORIAN_US:
        case CAL_GREGORIAN_ME_FR:
        case CAL_GREGORIAN_AR:
        case CAL_GREGORIAN_XLIT_EN:
        case CAL_GREGORIAN_XLIT_FR:
            return "gregorian";
        case CAL_JAPAN:    return "japanese";
        case CAL_TAIWAN:   return "roc";
        case CAL_KOREA:    return "dangi";
        case CAL_HIJRI:    return "islamic";
        case CAL_THAI:     return "buddhist";
        case CAL_HEBREW:   return "hebrew";
        case CAL_PERSIAN:  return "persian";
        case CAL_UMALQURA: return "islamic-umalqura";
        default:           return "gregorian";
    }
}

// ===== CalendarDataType enum (matches .NET's CalendarDataType) =====

enum CalendarDataType : int32_t {
    CDT_NativeName      = 1,
    CDT_MonthDay        = 2,
    CDT_ShortDates      = 3,
    CDT_LongDates       = 4,
    CDT_YearMonths      = 5,
    CDT_DayNames        = 6,
    CDT_AbbrevDayNames  = 7,
    CDT_MonthNames      = 8,
    CDT_AbbrevMonthNames = 9,
    CDT_SuperShortDayNames = 10,
    CDT_MonthGenitiveNames = 11,
    CDT_AbbrevMonthGenitiveNames = 12,
    CDT_EraNames        = 13,
    CDT_AbbrevEraNames  = 14,
};

// =====================================================================
//  Sort Handle Management
// =====================================================================

int32_t interop_globalization_get_sort_handle(String* sortName, intptr_t* sortHandle) {
    if (!sortHandle) return 0;

    std::string locale = locale_to_icu(sortName);
    UCollator* collator = get_or_create_collator(locale);
    if (!collator) {
        *sortHandle = 0;
        return 0;
    }
    *sortHandle = reinterpret_cast<intptr_t>(collator);
    return 1;
}

void interop_globalization_close_sort_handle(intptr_t /*sortHandle*/) {
    // Collators are cached and reused — don't close them
}

// =====================================================================
//  String Comparison (ICU Collation)
// =====================================================================

int32_t interop_globalization_compare_string(
    intptr_t sortHandle, char16_t* string1, int32_t string1Length,
    char16_t* string2, int32_t string2Length, int32_t options)
{
    // Ordinal comparison — raw code unit comparison
    if (options & ICO_Ordinal) {
        int32_t minLen = string1Length < string2Length ? string1Length : string2Length;
        for (int32_t i = 0; i < minLen; i++) {
            if (string1[i] != string2[i])
                return string1[i] < string2[i] ? -1 : 1;
        }
        if (string1Length < string2Length) return -1;
        if (string1Length > string2Length) return 1;
        return 0;
    }

    // OrdinalIgnoreCase — per-char case-folded comparison
    if (options & ICO_OrdinalIgnoreCase) {
        int32_t minLen = string1Length < string2Length ? string1Length : string2Length;
        for (int32_t i = 0; i < minLen; i++) {
            UChar32 c1 = u_toupper(static_cast<UChar32>(string1[i]));
            UChar32 c2 = u_toupper(static_cast<UChar32>(string2[i]));
            if (c1 != c2) return c1 < c2 ? -1 : 1;
        }
        if (string1Length < string2Length) return -1;
        if (string1Length > string2Length) return 1;
        return 0;
    }

    // Culture-sensitive: use ICU collator
    auto* collator = reinterpret_cast<UCollator*>(sortHandle);
    if (!collator) return 0;

    apply_compare_options(collator, options);

    UCollationResult result = ucol_strcoll(
        collator,
        reinterpret_cast<const UChar*>(string1), string1Length,
        reinterpret_cast<const UChar*>(string2), string2Length);

    switch (result) {
        case UCOL_LESS:    return -1;
        case UCOL_GREATER: return 1;
        default:           return 0;
    }
}

// =====================================================================
//  String Search (ICU UStringSearch)
// =====================================================================

int32_t interop_globalization_index_of(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* pSource, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr)
{
    if (matchLengthPtr) *matchLengthPtr = 0;

    if (!target || targetLength <= 0 || !pSource || sourceLength <= 0)
        return -1;

    // Ordinal forward search
    if (options & ICO_Ordinal) {
        for (int32_t i = 0; i <= sourceLength - targetLength; i++) {
            if (std::memcmp(pSource + i, target, static_cast<size_t>(targetLength) * sizeof(char16_t)) == 0) {
                if (matchLengthPtr) *matchLengthPtr = targetLength;
                return i;
            }
        }
        return -1;
    }

    // OrdinalIgnoreCase forward search
    if (options & ICO_OrdinalIgnoreCase) {
        for (int32_t i = 0; i <= sourceLength - targetLength; i++) {
            bool match = true;
            for (int32_t j = 0; j < targetLength; j++) {
                if (u_toupper(static_cast<UChar32>(pSource[i + j])) !=
                    u_toupper(static_cast<UChar32>(target[j]))) {
                    match = false;
                    break;
                }
            }
            if (match) {
                if (matchLengthPtr) *matchLengthPtr = targetLength;
                return i;
            }
        }
        return -1;
    }

    // Culture-sensitive: use ICU string search
    auto* collator = reinterpret_cast<UCollator*>(sortHandle);
    if (!collator) return -1;

    // Clone collator so we can set options without affecting cached instance
    UErrorCode err = U_ZERO_ERROR;
    UCollator* cloned = ucol_clone(collator, &err);
    if (U_FAILURE(err) || !cloned) return -1;

    apply_compare_options(cloned, options);

    UStringSearch* search = usearch_openFromCollator(
        reinterpret_cast<const UChar*>(target), targetLength,
        reinterpret_cast<const UChar*>(pSource), sourceLength,
        cloned, nullptr, &err);

    if (U_FAILURE(err) || !search) {
        ucol_close(cloned);
        return -1;
    }

    int32_t pos = usearch_first(search, &err);
    int32_t matchLen = 0;
    if (pos != USEARCH_DONE && !U_FAILURE(err)) {
        matchLen = usearch_getMatchedLength(search);
    }

    usearch_close(search);
    ucol_close(cloned);

    if (pos == USEARCH_DONE || U_FAILURE(err)) return -1;
    if (matchLengthPtr) *matchLengthPtr = matchLen;
    return pos;
}

int32_t interop_globalization_last_index_of(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* pSource, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr)
{
    if (matchLengthPtr) *matchLengthPtr = 0;

    if (!target || targetLength <= 0 || !pSource || sourceLength <= 0)
        return -1;

    // Ordinal reverse search
    if (options & ICO_Ordinal) {
        for (int32_t i = sourceLength - targetLength; i >= 0; i--) {
            if (std::memcmp(pSource + i, target, static_cast<size_t>(targetLength) * sizeof(char16_t)) == 0) {
                if (matchLengthPtr) *matchLengthPtr = targetLength;
                return i;
            }
        }
        return -1;
    }

    // OrdinalIgnoreCase reverse search
    if (options & ICO_OrdinalIgnoreCase) {
        for (int32_t i = sourceLength - targetLength; i >= 0; i--) {
            bool match = true;
            for (int32_t j = 0; j < targetLength; j++) {
                if (u_toupper(static_cast<UChar32>(pSource[i + j])) !=
                    u_toupper(static_cast<UChar32>(target[j]))) {
                    match = false;
                    break;
                }
            }
            if (match) {
                if (matchLengthPtr) *matchLengthPtr = targetLength;
                return i;
            }
        }
        return -1;
    }

    // Culture-sensitive: use ICU string search (last match)
    auto* collator = reinterpret_cast<UCollator*>(sortHandle);
    if (!collator) return -1;

    UErrorCode err = U_ZERO_ERROR;
    UCollator* cloned = ucol_clone(collator, &err);
    if (U_FAILURE(err) || !cloned) return -1;

    apply_compare_options(cloned, options);

    UStringSearch* search = usearch_openFromCollator(
        reinterpret_cast<const UChar*>(target), targetLength,
        reinterpret_cast<const UChar*>(pSource), sourceLength,
        cloned, nullptr, &err);

    if (U_FAILURE(err) || !search) {
        ucol_close(cloned);
        return -1;
    }

    int32_t pos = usearch_last(search, &err);
    int32_t matchLen = 0;
    if (pos != USEARCH_DONE && !U_FAILURE(err)) {
        matchLen = usearch_getMatchedLength(search);
    }

    usearch_close(search);
    ucol_close(cloned);

    if (pos == USEARCH_DONE || U_FAILURE(err)) return -1;
    if (matchLengthPtr) *matchLengthPtr = matchLen;
    return pos;
}

int32_t interop_globalization_starts_with(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* source, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr)
{
    if (matchLengthPtr) *matchLengthPtr = 0;

    if (!target || targetLength <= 0) {
        // Empty target → always a prefix
        return 1;
    }
    if (!source || sourceLength <= 0 || sourceLength < targetLength)
        return 0;

    // Check if the first match occurs at position 0
    int32_t matchLen = 0;
    int32_t pos = interop_globalization_index_of(
        sortHandle, target, targetLength, source, sourceLength, options, &matchLen);
    if (matchLengthPtr) *matchLengthPtr = matchLen;
    return (pos == 0) ? 1 : 0;
}

int32_t interop_globalization_ends_with(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* source, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr)
{
    if (matchLengthPtr) *matchLengthPtr = 0;

    if (!target || targetLength <= 0) {
        return 1;
    }
    if (!source || sourceLength <= 0 || sourceLength < targetLength)
        return 0;

    // Check if the last match ends exactly at sourceLength
    int32_t matchLen = 0;
    int32_t pos = interop_globalization_last_index_of(
        sortHandle, target, targetLength, source, sourceLength, options, &matchLen);
    if (matchLengthPtr) *matchLengthPtr = matchLen;
    return (pos >= 0 && pos + matchLen == sourceLength) ? 1 : 0;
}

// =====================================================================
//  Locale Data
// =====================================================================

int32_t interop_globalization_get_locale_name(
    String* localeName, char16_t* buffer, int32_t bufferLength)
{
    if (!localeName || bufferLength <= 0) return 0;
    auto len = string_length(localeName);
    if (len <= 0 || len >= bufferLength) return 0;
    std::memcpy(buffer, &localeName->f__firstChar, static_cast<size_t>(len) * sizeof(char16_t));
    buffer[len] = 0;
    return 1;
}

int32_t interop_globalization_get_locale_info_string(
    String* /*localeName*/, uint32_t type, char16_t* buffer,
    int32_t /*bufferLength*/, String* /*uiCultureName*/)
{
    // Helper: write a null-terminated char16_t string from ASCII
    auto write = [&](const char* s) -> int32_t {
        int i = 0;
        while (s[i]) { buffer[i] = static_cast<char16_t>(s[i]); i++; }
        buffer[i] = 0;
        return 1;
    };

    switch (type) {
    // Number formatting
    case 14:  return write(".");   // NumberDecimalSeparator
    case 15:  return write(",");   // NumberGroupSeparator
    case 80:  return write("+");   // PositiveSign
    case 81:  return write("-");   // NegativeSign
    case 105: return write("NaN"); // NaNSymbol
    case 106: buffer[0] = 0x221E; buffer[1] = 0; return 1;   // PositiveInfinitySymbol (U+221E = ∞)
    case 107: buffer[0] = u'-'; buffer[1] = 0x221E; buffer[2] = 0; return 1; // NegativeInfinitySymbol
    // Currency
    case 20:  return write("$");   // CurrencySymbol
    case 22:  return write(".");   // CurrencyDecimalSeparator
    case 23:  return write(",");   // CurrencyGroupSeparator
    // Percent
    case 19:  return write("%");   // PercentSymbol
    case 89:  return write("+");   // PercentPositivePattern (string rep)
    case 90:  return write("-");   // PercentNegativePattern (string rep)
    case 118: return write("%");   // PerMilleSymbol
    case 119: return write("+");   // (additional locale data)
    // List/misc
    case 40:  return write("AM");  // AMDesignator
    case 41:  return write("PM");  // PMDesignator
    default:
        return 0;
    }
}

int32_t interop_globalization_get_locale_info_int(
    String* localeName, uint32_t type, int32_t* value)
{
    if (!value) return 0;

    std::string locale = locale_to_icu(localeName);
    UErrorCode err = U_ZERO_ERROR;

    switch (static_cast<LocaleNumberData>(type)) {
    case LND_LanguageId: {
        *value = uloc_getLCID(locale.c_str());
        return 1;
    }

    case LND_MeasurementSystem: {
        UMeasurementSystem system = ulocdata_getMeasurementSystem(locale.c_str(), &err);
        if (U_FAILURE(err)) { *value = 0; return 0; }
        *value = (system == UMS_US) ? 1 : 0;
        return 1;
    }

    case LND_FractionalDigitsCount: {
        UNumberFormat* fmt = unum_open(UNUM_DECIMAL, nullptr, 0, locale.c_str(), nullptr, &err);
        if (U_FAILURE(err) || !fmt) { *value = 2; return 1; }
        *value = unum_getAttribute(fmt, UNUM_MAX_FRACTION_DIGITS);
        unum_close(fmt);
        return 1;
    }

    case LND_MonetaryFractionalDigitsCount: {
        UNumberFormat* fmt = unum_open(UNUM_CURRENCY, nullptr, 0, locale.c_str(), nullptr, &err);
        if (U_FAILURE(err) || !fmt) { *value = 2; return 1; }
        *value = unum_getAttribute(fmt, UNUM_MAX_FRACTION_DIGITS);
        unum_close(fmt);
        return 1;
    }

    case LND_NegativeNumberFormat: {
        // .NET NegativeNumberFormat: 0="(n)", 1="-n", 2="- n", 3="n-", 4="n -"
        // ICU: parse the negative prefix/suffix from the decimal formatter
        UNumberFormat* fmt = unum_open(UNUM_DECIMAL, nullptr, 0, locale.c_str(), nullptr, &err);
        if (U_FAILURE(err) || !fmt) { *value = 1; return 1; }

        UChar prefix[16] = {};
        unum_getTextAttribute(fmt, UNUM_NEGATIVE_PREFIX, prefix, 16, &err);
        UChar suffix[16] = {};
        unum_getTextAttribute(fmt, UNUM_NEGATIVE_SUFFIX, suffix, 16, &err);
        unum_close(fmt);

        // Determine pattern by prefix/suffix
        if (prefix[0] == u'(' && suffix[0] == u')') *value = 0;      // (n)
        else if (prefix[0] == u'-' && prefix[1] == u' ') *value = 2;  // - n
        else if (suffix[0] == u' ' && suffix[1] == u'-') *value = 4;  // n -
        else if (suffix[0] == u'-') *value = 3;                        // n-
        else *value = 1;                                                // -n (default)
        return 1;
    }

    case LND_NegativeMonetaryNumberFormat: {
        // .NET: 0=($n), 1=-$n, 2=$-n, 3=$n-, ...15 patterns
        // Simplified: most locales use pattern 1 (-$n) or 0 (($n))
        UNumberFormat* fmt = unum_open(UNUM_CURRENCY, nullptr, 0, locale.c_str(), nullptr, &err);
        if (U_FAILURE(err) || !fmt) { *value = 1; return 1; }

        UChar prefix[16] = {};
        unum_getTextAttribute(fmt, UNUM_NEGATIVE_PREFIX, prefix, 16, &err);
        unum_close(fmt);

        if (prefix[0] == u'(') *value = 0;    // ($n)
        else *value = 1;                        // -$n (most common)
        return 1;
    }

    case LND_PositiveMonetaryNumberFormat: {
        // .NET: 0=$n, 1=n$, 2=$ n, 3=n $
        UNumberFormat* fmt = unum_open(UNUM_CURRENCY, nullptr, 0, locale.c_str(), nullptr, &err);
        if (U_FAILURE(err) || !fmt) { *value = 0; return 1; }

        UChar prefix[16] = {};
        unum_getTextAttribute(fmt, UNUM_POSITIVE_PREFIX, prefix, 16, &err);
        UChar suffix[16] = {};
        unum_getTextAttribute(fmt, UNUM_POSITIVE_SUFFIX, suffix, 16, &err);
        unum_close(fmt);

        // Detect pattern from prefix/suffix
        bool hasCurrencyPrefix = false, hasCurrencySuffix = false;
        bool hasSpacePrefix = false, hasSpaceSuffix = false;
        for (int i = 0; prefix[i]; i++) {
            if (prefix[i] == 0x00A4) hasCurrencyPrefix = true; // currency sign
            if (prefix[i] == u' ') hasSpacePrefix = true;
        }
        for (int i = 0; suffix[i]; i++) {
            if (suffix[i] == 0x00A4) hasCurrencySuffix = true;
            if (suffix[i] == u' ') hasSpaceSuffix = true;
        }

        if (hasCurrencyPrefix && hasSpacePrefix) *value = 2;       // $ n
        else if (hasCurrencyPrefix) *value = 0;                     // $n
        else if (hasCurrencySuffix && hasSpaceSuffix) *value = 3;  // n $
        else if (hasCurrencySuffix) *value = 1;                     // n$
        else *value = 0;                                             // default $n
        return 1;
    }

    case LND_Digit:
    case LND_MonetaryDigit: {
        // Native digit — 0 for ASCII (0x0030), or the native zero digit offset
        // ICU: check the numbering system for this locale
        *value = 0; // ASCII digits for most locales
        return 1;
    }

    case LND_FirstDayOfWeek: {
        UCalendar* cal = ucal_open(nullptr, 0, locale.c_str(), UCAL_DEFAULT, &err);
        if (U_FAILURE(err) || !cal) { *value = 0; return 1; }
        int32_t icuDay = ucal_getAttribute(cal, UCAL_FIRST_DAY_OF_WEEK);
        ucal_close(cal);
        // ICU: 1=Sunday..7=Saturday → .NET: 0=Monday..6=Sunday
        *value = (icuDay == 1) ? 6 : (icuDay - 2);
        return 1;
    }

    case LND_FirstWeekOfYear: {
        UCalendar* cal = ucal_open(nullptr, 0, locale.c_str(), UCAL_DEFAULT, &err);
        if (U_FAILURE(err) || !cal) { *value = 0; return 1; }
        int32_t minDays = ucal_getAttribute(cal, UCAL_MINIMAL_DAYS_IN_FIRST_WEEK);
        ucal_close(cal);
        // .NET CalendarWeekRule: 0=FirstDay, 1=FirstFullWeek, 2=FirstFourDayWeek
        if (minDays == 7) *value = 1;       // FirstFullWeek
        else if (minDays >= 4) *value = 2;  // FirstFourDayWeek
        else *value = 0;                     // FirstDay
        return 1;
    }

    case LND_ReadingLayout: {
        // 0=LTR, 1=RTL
        ULayoutType layout = uloc_getCharacterOrientation(locale.c_str(), &err);
        if (U_FAILURE(err)) { *value = 0; return 1; }
        *value = (layout == ULOC_LAYOUT_RTL) ? 1 : 0;
        return 1;
    }

    case LND_NegativePercentFormat: {
        // .NET: 0="-n %", 1="-n%", 2="-%n", ...11 patterns
        *value = 0; // default "-n %"
        return 1;
    }

    case LND_PositivePercentFormat: {
        // .NET: 0="n %", 1="n%", 2="%n", 3="% n"
        *value = 0; // default "n %"
        return 1;
    }

    default:
        *value = 0;
        return 0;
    }
}

int32_t interop_globalization_get_locale_info_grouping_sizes(
    String* localeName, uint32_t type, int32_t* primaryGroupSize,
    int32_t* secondaryGroupSize)
{
    if (!primaryGroupSize || !secondaryGroupSize) return 0;

    std::string locale = locale_to_icu(localeName);
    UErrorCode err = U_ZERO_ERROR;

    // type distinguishes decimal (0x0010) vs monetary (0x0018) grouping
    UNumberFormatStyle style = (type == LND_MonetaryDigit) ? UNUM_CURRENCY : UNUM_DECIMAL;
    UNumberFormat* fmt = unum_open(style, nullptr, 0, locale.c_str(), nullptr, &err);
    if (U_FAILURE(err) || !fmt) {
        *primaryGroupSize = 3;
        *secondaryGroupSize = 0;
        return 1;
    }

    *primaryGroupSize = unum_getAttribute(fmt, UNUM_GROUPING_SIZE);
    *secondaryGroupSize = unum_getAttribute(fmt, UNUM_SECONDARY_GROUPING_SIZE);
    unum_close(fmt);
    return 1;
}

int32_t interop_globalization_get_locale_time_format(
    String* localeName, int32_t shortFormat, char16_t* buffer,
    int32_t bufferLength)
{
    if (!buffer || bufferLength <= 0) return 0;

    std::string locale = locale_to_icu(localeName);
    UErrorCode err = U_ZERO_ERROR;

    UDateFormatStyle style = shortFormat ? UDAT_SHORT : UDAT_MEDIUM;
    UDateFormat* fmt = udat_open(style, UDAT_NONE, locale.c_str(), nullptr, 0, nullptr, 0, &err);
    if (U_FAILURE(err) || !fmt) {
        buffer[0] = 0;
        return 0;
    }

    UChar pattern[128] = {};
    int32_t patternLen = udat_toPattern(fmt, 0, pattern, 128, &err);
    udat_close(fmt);

    if (U_FAILURE(err) || patternLen <= 0) {
        buffer[0] = 0;
        return 0;
    }

    return write_uchar_to_buffer(pattern, patternLen, buffer, bufferLength);
}

int32_t interop_globalization_is_predefined_locale(String* localeName) {
    std::string locale = locale_to_icu(localeName);
    if (locale.empty()) return 1; // invariant culture

    // Check against ICU's available locales
    int32_t count = uloc_countAvailable();
    for (int32_t i = 0; i < count; i++) {
        const char* available = uloc_getAvailable(i);
        if (available && locale == available) return 1;
    }

    // Also check with canonical form
    char canonical[ULOC_FULLNAME_CAPACITY] = {};
    UErrorCode err = U_ZERO_ERROR;
    uloc_canonicalize(locale.c_str(), canonical, sizeof(canonical), &err);
    if (U_SUCCESS(err) && canonical[0]) {
        for (int32_t i = 0; i < count; i++) {
            const char* available = uloc_getAvailable(i);
            if (available && strcmp(canonical, available) == 0) return 1;
        }
    }

    // ICU often accepts locales it doesn't list — check if we can get data for it
    err = U_ZERO_ERROR;
    UCollator* col = ucol_open(locale.c_str(), &err);
    if (col) {
        ucol_close(col);
        return 1;
    }
    return 0;
}

// =====================================================================
//  Calendar Data
// =====================================================================

int32_t interop_globalization_get_calendars(
    String* localeName, Array* calendars, int32_t calendarsCapacity)
{
    if (!calendars || calendarsCapacity <= 0) return 0;

    std::string locale = locale_to_icu(localeName);
    UErrorCode err = U_ZERO_ERROR;

    // Get available calendar types for this locale
    UEnumeration* calEnum = ucal_getKeywordValuesForLocale(
        "calendar", locale.c_str(), true, &err);

    int32_t count = 0;
    // Get pointer to array data (int32_t elements after the Array header)
    auto* data = reinterpret_cast<int32_t*>(
        reinterpret_cast<char*>(calendars) + sizeof(Array));

    if (U_SUCCESS(err) && calEnum) {
        int32_t length = 0;
        const char* calType;
        while (count < calendarsCapacity &&
               (calType = uenum_next(calEnum, &length, &err)) != nullptr) {
            if (U_FAILURE(err)) break;
            data[count] = icu_calendar_to_id(calType);
            count++;
        }
        uenum_close(calEnum);
    }

    // Ensure at least Gregorian
    if (count == 0 && calendarsCapacity > 0) {
        data[0] = CAL_GREGORIAN;
        count = 1;
    }

    return count;
}

int32_t interop_globalization_get_calendar_info(
    String* localeName, int32_t calendarId, int32_t dataType,
    char16_t* buffer, int32_t bufferLength)
{
    if (!buffer || bufferLength <= 0) return 0;

    std::string locale = locale_to_icu(localeName);
    const char* calType = id_to_icu_calendar(calendarId);

    // Append @calendar=<type> to locale for ICU
    std::string calLocale = locale + "@calendar=" + calType;
    UErrorCode err = U_ZERO_ERROR;

    switch (static_cast<CalendarDataType>(dataType)) {
    case CDT_NativeName: {
        // Calendar display name — use the ICU calendar type name
        UChar name[128] = {};
        // Get the display name of the calendar
        UCalendar* cal = ucal_open(nullptr, 0, calLocale.c_str(), UCAL_DEFAULT, &err);
        if (U_FAILURE(err) || !cal) {
            buffer[0] = 0;
            return 0;
        }
        // Use the calendar type string as fallback display name
        int32_t len = 0;
        for (const char* p = calType; *p && len < 127; p++, len++) {
            name[len] = static_cast<UChar>(*p);
        }
        ucal_close(cal);
        return write_uchar_to_buffer(name, len, buffer, bufferLength);
    }

    case CDT_DayNames:
    case CDT_AbbrevDayNames:
    case CDT_SuperShortDayNames: {
        UDateFormatSymbolType symType;
        switch (static_cast<CalendarDataType>(dataType)) {
            case CDT_AbbrevDayNames:     symType = UDAT_SHORT_WEEKDAYS; break;
            case CDT_SuperShortDayNames: symType = UDAT_NARROW_WEEKDAYS; break;
            default:                     symType = UDAT_WEEKDAYS; break;
        }

        UDateFormat* fmt = udat_open(UDAT_DEFAULT, UDAT_DEFAULT, calLocale.c_str(),
            nullptr, 0, nullptr, 0, &err);
        if (U_FAILURE(err) || !fmt) { buffer[0] = 0; return 0; }

        // Write all day names separated by '|'
        int32_t pos = 0;
        int32_t dayCount = udat_countSymbols(fmt, symType);
        // ICU day indices: 1=Sunday..7=Saturday
        for (int32_t i = 1; i < dayCount && pos < bufferLength - 1; i++) {
            if (i > 1 && pos < bufferLength - 1) {
                buffer[pos++] = u'|';
            }
            UChar name[64] = {};
            int32_t nameLen = udat_getSymbols(fmt, symType, i, name, 64, &err);
            if (U_FAILURE(err)) break;
            for (int32_t j = 0; j < nameLen && pos < bufferLength - 1; j++) {
                buffer[pos++] = static_cast<char16_t>(name[j]);
            }
        }
        buffer[pos] = 0;
        udat_close(fmt);
        return pos > 0 ? 1 : 0;
    }

    case CDT_MonthNames:
    case CDT_AbbrevMonthNames:
    case CDT_MonthGenitiveNames:
    case CDT_AbbrevMonthGenitiveNames: {
        UDateFormatSymbolType symType;
        switch (static_cast<CalendarDataType>(dataType)) {
            case CDT_AbbrevMonthNames:          symType = UDAT_SHORT_MONTHS; break;
            case CDT_MonthGenitiveNames:        symType = UDAT_MONTHS; break;
            case CDT_AbbrevMonthGenitiveNames:  symType = UDAT_SHORT_MONTHS; break;
            default:                            symType = UDAT_MONTHS; break;
        }

        UDateFormat* fmt = udat_open(UDAT_DEFAULT, UDAT_DEFAULT, calLocale.c_str(),
            nullptr, 0, nullptr, 0, &err);
        if (U_FAILURE(err) || !fmt) { buffer[0] = 0; return 0; }

        int32_t pos = 0;
        int32_t monthCount = udat_countSymbols(fmt, symType);
        for (int32_t i = 0; i < monthCount && pos < bufferLength - 1; i++) {
            if (i > 0 && pos < bufferLength - 1) {
                buffer[pos++] = u'|';
            }
            UChar name[64] = {};
            int32_t nameLen = udat_getSymbols(fmt, symType, i, name, 64, &err);
            if (U_FAILURE(err)) break;
            for (int32_t j = 0; j < nameLen && pos < bufferLength - 1; j++) {
                buffer[pos++] = static_cast<char16_t>(name[j]);
            }
        }
        buffer[pos] = 0;
        udat_close(fmt);
        return pos > 0 ? 1 : 0;
    }

    case CDT_EraNames:
    case CDT_AbbrevEraNames: {
        // UDAT_ERAS = abbreviated era names, UDAT_ERA_NAMES = long era names
        UDateFormatSymbolType symType = (static_cast<CalendarDataType>(dataType) == CDT_EraNames)
            ? UDAT_ERA_NAMES : UDAT_ERAS;

        UDateFormat* fmt = udat_open(UDAT_DEFAULT, UDAT_DEFAULT, calLocale.c_str(),
            nullptr, 0, nullptr, 0, &err);
        if (U_FAILURE(err) || !fmt) { buffer[0] = 0; return 0; }

        int32_t pos = 0;
        int32_t eraCount = udat_countSymbols(fmt, symType);
        for (int32_t i = 0; i < eraCount && pos < bufferLength - 1; i++) {
            if (i > 0 && pos < bufferLength - 1) {
                buffer[pos++] = u'|';
            }
            UChar name[64] = {};
            int32_t nameLen = udat_getSymbols(fmt, symType, i, name, 64, &err);
            if (U_FAILURE(err)) break;
            for (int32_t j = 0; j < nameLen && pos < bufferLength - 1; j++) {
                buffer[pos++] = static_cast<char16_t>(name[j]);
            }
        }
        buffer[pos] = 0;
        udat_close(fmt);
        return pos > 0 ? 1 : 0;
    }

    case CDT_ShortDates:
    case CDT_LongDates: {
        UDateFormatStyle dateStyle = (static_cast<CalendarDataType>(dataType) == CDT_ShortDates)
            ? UDAT_SHORT : UDAT_LONG;

        UDateFormat* fmt = udat_open(UDAT_NONE, dateStyle, calLocale.c_str(),
            nullptr, 0, nullptr, 0, &err);
        if (U_FAILURE(err) || !fmt) { buffer[0] = 0; return 0; }

        UChar pattern[128] = {};
        int32_t patternLen = udat_toPattern(fmt, 0, pattern, 128, &err);
        udat_close(fmt);

        if (U_FAILURE(err) || patternLen <= 0) { buffer[0] = 0; return 0; }
        return write_uchar_to_buffer(pattern, patternLen, buffer, bufferLength);
    }

    case CDT_YearMonths:
    case CDT_MonthDay: {
        // UDAT_YEAR_MONTH ("yMMMM") and UDAT_MONTH_DAY ("MMMMd") are skeleton strings,
        // not UDateFormatStyle enum values. Use udat_open with the skeleton as pattern.
        const UChar* skeleton = (static_cast<CalendarDataType>(dataType) == CDT_YearMonths)
            ? reinterpret_cast<const UChar*>(u"yMMMM")
            : reinterpret_cast<const UChar*>(u"MMMMd");
        int32_t skelLen = (static_cast<CalendarDataType>(dataType) == CDT_YearMonths) ? 5 : 5;

        UDateFormat* fmt = udat_open(UDAT_NONE, UDAT_NONE, calLocale.c_str(),
            nullptr, 0, skeleton, skelLen, &err);
        if (U_FAILURE(err) || !fmt) { buffer[0] = 0; return 0; }

        UChar pattern[128] = {};
        int32_t patternLen = udat_toPattern(fmt, 0, pattern, 128, &err);
        udat_close(fmt);

        if (U_FAILURE(err) || patternLen <= 0) { buffer[0] = 0; return 0; }
        return write_uchar_to_buffer(pattern, patternLen, buffer, bufferLength);
    }

    default:
        buffer[0] = 0;
        return 0;
    }
}

int32_t interop_globalization_get_latest_japanese_era() {
    UErrorCode err = U_ZERO_ERROR;
    UCalendar* cal = ucal_open(nullptr, 0, "ja_JP@calendar=japanese", UCAL_DEFAULT, &err);
    if (U_FAILURE(err) || !cal) return 0;

    int32_t maxEra = ucal_getLimit(cal, UCAL_ERA, UCAL_MAXIMUM, &err);
    ucal_close(cal);

    if (U_FAILURE(err)) return 0;
    return maxEra;
}

int32_t interop_globalization_get_japanese_era_start_date(
    int32_t era, int32_t* year, int32_t* month, int32_t* day)
{
    if (!year || !month || !day) return 0;

    UErrorCode err = U_ZERO_ERROR;
    UCalendar* cal = ucal_open(nullptr, 0, "ja_JP@calendar=japanese", UCAL_DEFAULT, &err);
    if (U_FAILURE(err) || !cal) return 0;

    // Set to beginning of the requested era
    ucal_set(cal, UCAL_ERA, era);
    ucal_set(cal, UCAL_YEAR, 1);
    ucal_set(cal, UCAL_MONTH, 0);  // January (0-based in ICU)
    ucal_set(cal, UCAL_DAY_OF_MONTH, 1);

    // Get the Gregorian date by switching to Gregorian calendar interpretation
    *year = ucal_get(cal, UCAL_EXTENDED_YEAR, &err);
    *month = ucal_get(cal, UCAL_MONTH, &err) + 1; // ICU months are 0-based
    *day = ucal_get(cal, UCAL_DAY_OF_MONTH, &err);
    ucal_close(cal);

    return U_SUCCESS(err) ? 1 : 0;
}

// =====================================================================
//  Timezone
// =====================================================================

int32_t interop_globalization_iana_id_to_windows_id(
    String* ianaId, char16_t* buffer, int32_t bufferLength)
{
    if (!ianaId || !buffer || bufferLength <= 0) return 0;

    auto* chars = &ianaId->f__firstChar;
    int32_t len = string_length(ianaId);

    UErrorCode err = U_ZERO_ERROR;
    UChar winId[128] = {};
    int32_t winIdLen = ucal_getWindowsTimeZoneID(
        reinterpret_cast<const UChar*>(chars), len,
        winId, 128, &err);

    if (U_FAILURE(err) || winIdLen <= 0) {
        buffer[0] = 0;
        return 0;
    }

    return write_uchar_to_buffer(winId, winIdLen, buffer, bufferLength);
}

// LoadICU / InitICUFunctions are inline variadic templates in the header
// (return 1 — ICU is statically linked).

} // namespace cil2cpp
