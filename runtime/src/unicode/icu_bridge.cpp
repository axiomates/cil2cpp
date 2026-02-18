/**
 * CIL2CPP Runtime - ICU Bridge Implementation
 *
 * Wraps ICU4C C APIs for character classification, case conversion,
 * and UTF-8 ↔ UTF-16 conversion. Replaces hand-rolled implementations.
 */

#include <cil2cpp/unicode.h>

#include <unicode/uchar.h>    // u_isWhitespace, u_isdigit, u_isalpha, u_toupper, u_tolower, etc.
#include <unicode/ustring.h>  // u_strFromUTF8, u_strToUTF8
#include <unicode/utypes.h>   // UErrorCode, UChar
#include <unicode/uversion.h> // u_getVersion

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
Char char_to_upper(Char c) { return to_upper(c); }
Char char_to_lower(Char c) { return to_lower(c); }

} // namespace unicode
} // namespace cil2cpp
