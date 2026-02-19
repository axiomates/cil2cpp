/**
 * CIL2CPP Runtime - ICU Bridge Implementation
 *
 * Wraps ICU4C C APIs for character classification, case conversion,
 * and UTF-8 ↔ UTF-16 conversion. Replaces hand-rolled implementations.
 */

#include <cil2cpp/unicode.h>

#include <unicode/uchar.h>    // u_isWhitespace, u_isdigit, u_isalpha, u_toupper, u_tolower, etc.
#include <unicode/ustring.h>  // u_strFromUTF8, u_strToUTF8, u_strToUpper, u_strToLower
#include <unicode/utypes.h>   // UErrorCode, UChar
#include <unicode/uversion.h> // u_getVersion
#include <unicode/uloc.h>     // uloc_getDefault

#include <cstring>

namespace cil2cpp {
namespace unicode {

void init() {
    // ICU initializes lazily on first API call. Nothing required here.
    // Kept as a hook for future initialization needs (e.g., custom data paths).
}

// ===== Character Classification =====

bool is_whitespace(Char c) {
    // u_isUWhiteSpace matches .NET Char.IsWhiteSpace behavior:
    // Unicode White_Space property — includes NBSP (U+00A0), NNBSP (U+202F), etc.
    // Note: u_isWhitespace() excludes no-break spaces, so we use u_isUWhiteSpace().
    return u_isUWhiteSpace(static_cast<UChar32>(c)) != 0;
}

bool is_digit(Char c) {
    return u_isdigit(static_cast<UChar32>(c)) != 0;
}

bool is_letter(Char c) {
    return u_isalpha(static_cast<UChar32>(c)) != 0;
}

bool is_letter_or_digit(Char c) {
    UChar32 cp = static_cast<UChar32>(c);
    return u_isalpha(cp) != 0 || u_isdigit(cp) != 0;
}

bool is_upper(Char c) {
    return u_isupper(static_cast<UChar32>(c)) != 0;
}

bool is_lower(Char c) {
    return u_islower(static_cast<UChar32>(c)) != 0;
}

bool is_punctuation(Char c) {
    return u_ispunct(static_cast<UChar32>(c)) != 0;
}

bool is_separator(Char c) {
    int32_t type = u_charType(static_cast<UChar32>(c));
    return type == U_SPACE_SEPARATOR || type == U_LINE_SEPARATOR || type == U_PARAGRAPH_SEPARATOR;
}

bool is_control(Char c) {
    return u_iscntrl(static_cast<UChar32>(c)) != 0;
}

bool is_surrogate(Char c) {
    return (c >= 0xD800 && c <= 0xDFFF);
}

bool is_high_surrogate(Char c) {
    return (c >= 0xD800 && c <= 0xDBFF);
}

bool is_low_surrogate(Char c) {
    return (c >= 0xDC00 && c <= 0xDFFF);
}

// ===== Case Conversion =====

Char to_upper(Char c) {
    UChar32 result = u_toupper(static_cast<UChar32>(c));
    // If result is outside BMP, return original (single Char can't represent supplementary)
    if (result > 0xFFFF) return c;
    return static_cast<Char>(result);
}

Char to_lower(Char c) {
    UChar32 result = u_tolower(static_cast<UChar32>(c));
    if (result > 0xFFFF) return c;
    return static_cast<Char>(result);
}

Char to_upper_locale(Char c) {
    UChar src[2] = { static_cast<UChar>(c), 0 };
    UChar dest[3] = {};
    UErrorCode err = U_ZERO_ERROR;
    int32_t len = u_strToUpper(dest, 3, src, 1, uloc_getDefault(), &err);
    // If result is not single char (e.g. ß → SS), return original — .NET Char.ToUpper behavior
    if (U_FAILURE(err) || len != 1) return c;
    return static_cast<Char>(dest[0]);
}

Char to_lower_locale(Char c) {
    UChar src[2] = { static_cast<UChar>(c), 0 };
    UChar dest[3] = {};
    UErrorCode err = U_ZERO_ERROR;
    int32_t len = u_strToLower(dest, 3, src, 1, uloc_getDefault(), &err);
    if (U_FAILURE(err) || len != 1) return c;
    return static_cast<Char>(dest[0]);
}

// ===== UTF-8 ↔ UTF-16 Conversion =====

Int32 utf8_to_utf16(const char* utf8, Char* out, Int32 outCapacity) {
    if (!utf8) return 0;

    UErrorCode err = U_ZERO_ERROR;
    int32_t resultLen = 0;

    u_strFromUTF8(
        reinterpret_cast<UChar*>(out),
        static_cast<int32_t>(outCapacity),
        &resultLen,
        utf8,
        -1,  // null-terminated
        &err
    );

    // U_BUFFER_OVERFLOW_ERROR means output was truncated but resultLen is correct
    if (U_FAILURE(err) && err != U_BUFFER_OVERFLOW_ERROR) {
        return 0;
    }

    return static_cast<Int32>(resultLen);
}

Int32 utf16_to_utf8(const Char* utf16, Int32 utf16Len, char* out, Int32 outCapacity) {
    if (!utf16 || utf16Len <= 0) return 0;

    UErrorCode err = U_ZERO_ERROR;
    int32_t resultLen = 0;

    u_strToUTF8(
        out,
        static_cast<int32_t>(outCapacity),
        &resultLen,
        reinterpret_cast<const UChar*>(utf16),
        static_cast<int32_t>(utf16Len),
        &err
    );

    if (U_FAILURE(err) && err != U_BUFFER_OVERFLOW_ERROR) {
        return 0;
    }

    return static_cast<Int32>(resultLen);
}

Int32 utf8_to_utf16_length(const char* utf8) {
    if (!utf8) return 0;

    UErrorCode err = U_ZERO_ERROR;
    int32_t resultLen = 0;

    // Preflight: pass nullptr output to get required length
    u_strFromUTF8(
        nullptr,
        0,
        &resultLen,
        utf8,
        -1,  // null-terminated
        &err
    );

    // U_BUFFER_OVERFLOW_ERROR is expected in preflight mode
    if (err != U_BUFFER_OVERFLOW_ERROR && U_FAILURE(err)) {
        return 0;
    }

    return static_cast<Int32>(resultLen);
}

Int32 utf16_to_utf8_length(const Char* utf16, Int32 utf16Len) {
    if (!utf16 || utf16Len <= 0) return 0;

    UErrorCode err = U_ZERO_ERROR;
    int32_t resultLen = 0;

    // Preflight: pass nullptr output to get required length
    u_strToUTF8(
        nullptr,
        0,
        &resultLen,
        reinterpret_cast<const UChar*>(utf16),
        static_cast<int32_t>(utf16Len),
        &err
    );

    if (err != U_BUFFER_OVERFLOW_ERROR && U_FAILURE(err)) {
        return 0;
    }

    return static_cast<Int32>(resultLen);
}

// ===== ICalls (wrappers for generated code) =====

Boolean char_is_whitespace(Char c) { return is_whitespace(c) ? 1 : 0; }
Boolean char_is_digit(Char c) { return is_digit(c) ? 1 : 0; }
Boolean char_is_letter(Char c) { return is_letter(c) ? 1 : 0; }
Boolean char_is_letter_or_digit(Char c) { return is_letter_or_digit(c) ? 1 : 0; }
Boolean char_is_upper(Char c) { return is_upper(c) ? 1 : 0; }
Boolean char_is_lower(Char c) { return is_lower(c) ? 1 : 0; }
Boolean char_is_punctuation(Char c) { return is_punctuation(c) ? 1 : 0; }
Boolean char_is_separator(Char c) { return is_separator(c) ? 1 : 0; }
Boolean char_is_control(Char c) { return is_control(c) ? 1 : 0; }
Boolean char_is_surrogate(Char c) { return is_surrogate(c) ? 1 : 0; }
Boolean char_is_high_surrogate(Char c) { return is_high_surrogate(c) ? 1 : 0; }
Boolean char_is_low_surrogate(Char c) { return is_low_surrogate(c) ? 1 : 0; }
Char char_to_upper(Char c) { return to_upper_locale(c); }
Char char_to_lower(Char c) { return to_lower_locale(c); }
Char char_to_upper_invariant(Char c) { return to_upper(c); }
Char char_to_lower_invariant(Char c) { return to_lower(c); }

// ===== CharUnicodeInfo ICalls =====

/// Map ICU UCharCategory → .NET System.Globalization.UnicodeCategory enum.
/// ICU uses UCharCategory (unicode/uchar.h), .NET uses UnicodeCategory (0-29).
Int32 char_get_unicode_category(Char c) {
    int32_t type = u_charType(static_cast<UChar32>(c));
    // ICU UCharCategory → .NET UnicodeCategory mapping
    // Both follow Unicode General Category, but with different enum values.
    switch (type) {
        case U_UPPERCASE_LETTER:        return 0;  // UppercaseLetter
        case U_LOWERCASE_LETTER:        return 1;  // LowercaseLetter
        case U_TITLECASE_LETTER:        return 2;  // TitlecaseLetter
        case U_MODIFIER_LETTER:         return 3;  // ModifierLetter
        case U_OTHER_LETTER:            return 4;  // OtherLetter
        case U_NON_SPACING_MARK:        return 5;  // NonSpacingMark
        case U_COMBINING_SPACING_MARK:  return 6;  // SpacingCombiningMark
        case U_ENCLOSING_MARK:          return 7;  // EnclosingMark
        case U_DECIMAL_DIGIT_NUMBER:    return 8;  // DecimalDigitNumber
        case U_LETTER_NUMBER:           return 9;  // LetterNumber
        case U_OTHER_NUMBER:            return 10; // OtherNumber
        case U_SPACE_SEPARATOR:         return 11; // SpaceSeparator
        case U_LINE_SEPARATOR:          return 12; // LineSeparator
        case U_PARAGRAPH_SEPARATOR:     return 13; // ParagraphSeparator
        case U_CONTROL_CHAR:            return 14; // Control
        case U_FORMAT_CHAR:             return 15; // Format
        case U_SURROGATE:               return 16; // Surrogate
        case U_PRIVATE_USE_CHAR:        return 17; // PrivateUse
        case U_CONNECTOR_PUNCTUATION:   return 18; // ConnectorPunctuation
        case U_DASH_PUNCTUATION:        return 19; // DashPunctuation
        case U_START_PUNCTUATION:       return 20; // OpenPunctuation
        case U_END_PUNCTUATION:         return 21; // ClosePunctuation
        case U_INITIAL_PUNCTUATION:     return 22; // InitialQuotePunctuation
        case U_FINAL_PUNCTUATION:       return 23; // FinalQuotePunctuation
        case U_OTHER_PUNCTUATION:       return 24; // OtherPunctuation
        case U_MATH_SYMBOL:             return 25; // MathSymbol
        case U_CURRENCY_SYMBOL:         return 26; // CurrencySymbol
        case U_MODIFIER_SYMBOL:         return 27; // ModifierSymbol
        case U_OTHER_SYMBOL:            return 28; // OtherSymbol
        default:                        return 29; // OtherNotAssigned
    }
}

} // namespace unicode
} // namespace cil2cpp
