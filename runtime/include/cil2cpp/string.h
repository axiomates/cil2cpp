/**
 * CIL2CPP Runtime - String Type
 * Corresponds to System.String in .NET.
 */

#pragma once

#include "object.h"

namespace cil2cpp {

/**
 * Immutable string type using UTF-16 encoding.
 */
struct String : Object {
    // Length of the string (number of UTF-16 code units)
    // IL field name: _stringLength → f_stringLength
    union {
        Int32 length;
        Int32 f_stringLength;
    };

    // UTF-16 character data (flexible array member)
    // IL field name: _firstChar → f_firstChar
    union {
        Char chars[1];
        Char f_firstChar;
    };

    // Get the length of the string
    Int32 get_length() const { return length; }

    // Get a character at index
    Char get_char(Int32 index) const { return chars[index]; }
};

/**
 * Create a new string from UTF-8 data.
 */
String* string_create_utf8(const char* utf8);

/**
 * Create a new string from UTF-16 data.
 */
String* string_create_utf16(const Char* utf16, Int32 length);

/**
 * Create a string literal (interned).
 */
String* string_literal(const char* utf8);

/**
 * Concatenate two strings.
 */
String* string_concat(String* a, String* b);
String* string_concat(String* a, String* b, String* c);

/**
 * Compare two strings for equality.
 */
Boolean string_equals(String* a, String* b);

/**
 * Compare two strings for inequality.
 */
Boolean string_not_equals(String* a, String* b);

/**
 * Get hash code for a string.
 */
Int32 string_get_hash_code(String* str);

/**
 * Check if string is null or empty.
 */
Boolean string_is_null_or_empty(String* str);
Boolean string_is_null_or_whitespace(String* str);

/**
 * Get substring.
 */
String* string_substring(String* str, Int32 start, Int32 length);

/**
 * Convert string to UTF-8 (caller must free).
 */
char* string_to_utf8(String* str);

/**
 * Get string length.
 */
inline Int32 string_length(String* str) {
    return str ? str->length : 0;
}

/**
 * Convert an int32 to a string.
 */
String* string_from_int32(Int32 value);

/**
 * Convert a double to a string.
 */
String* string_from_double(Double value);

/**
 * Convert an int64 to a string.
 */
String* string_from_int64(Int64 value);
String* string_from_bool(Boolean value);
String* string_from_char(Char value);
String* string_fast_allocate(Int32 length);

inline Char string_get_chars(String* str, Int32 index) {
    return str->chars[index];
}

// ── String search / comparison ────────────────────────────
Int32 string_index_of(String* str, Char value);
Int32 string_index_of(String* str, Char value, Int32 startIndex);
Int32 string_index_of_string(String* str, String* value);
Int32 string_last_index_of(String* str, Char value);
Boolean string_contains(String* str, Char value);
Boolean string_contains_string(String* str, String* value);
Boolean string_starts_with(String* str, String* value);
Boolean string_ends_with(String* str, String* value);
Int32 string_compare_ordinal(String* a, String* b);

// ── String transformation ─────────────────────────────────
String* string_to_upper(String* str);
String* string_to_lower(String* str);
String* string_trim(String* str);
String* string_trim_start(String* str);
String* string_trim_end(String* str);
String* string_replace(String* str, Char oldChar, Char newChar);
String* string_replace_string(String* str, String* oldValue, String* newValue);
String* string_remove(String* str, Int32 startIndex);
String* string_remove(String* str, Int32 startIndex, Int32 count);
String* string_insert(String* str, Int32 startIndex, String* value);
String* string_pad_left(String* str, Int32 totalWidth);
String* string_pad_right(String* str, Int32 totalWidth);

// ── Concat with Object ────────────────────────────────────
String* string_concat_obj(Object* a, Object* b);
String* string_concat_obj3(Object* a, Object* b, Object* c);

// ── Format / Join / Split ─────────────────────────────────
String* string_format(String* format, Array* args);
String* string_join(String* separator, Array* values);
Array* string_split(String* str, Char separator);

} // namespace cil2cpp
