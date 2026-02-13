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
    Int32 length;

    // UTF-16 character data (flexible array member)
    Char chars[1];

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

/**
 * Compare two strings for equality.
 */
Boolean string_equals(String* a, String* b);

/**
 * Get hash code for a string.
 */
Int32 string_get_hash_code(String* str);

/**
 * Check if string is null or empty.
 */
Boolean string_is_null_or_empty(String* str);

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

} // namespace cil2cpp
