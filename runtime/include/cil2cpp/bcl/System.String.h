/**
 * CIL2CPP Runtime - System.String
 */

#pragma once

#include "../string.h"
#include "../type_info.h"

namespace cil2cpp {
namespace System {

// System.String type info (defined in System.String.cpp)
extern TypeInfo String_TypeInfo;

// String static methods
inline String* String_Concat(String* a, String* b) {
    return string_concat(a, b);
}

inline Boolean String_IsNullOrEmpty(String* str) {
    return string_is_null_or_empty(str);
}

inline Boolean String_Equals(String* a, String* b) {
    return string_equals(a, b);
}

// String instance methods (called as static with this pointer)
inline Int32 String_get_Length(String* str) {
    return string_length(str);
}

inline Char String_get_Chars(String* str, Int32 index) {
    return str->get_char(index);
}

inline String* String_Substring(String* str, Int32 startIndex) {
    return string_substring(str, startIndex, str->length - startIndex);
}

inline String* String_Substring(String* str, Int32 startIndex, Int32 length) {
    return string_substring(str, startIndex, length);
}

} // namespace System
} // namespace cil2cpp
