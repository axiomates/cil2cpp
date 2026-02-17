/**
 * CIL2CPP Runtime - System.String Implementation
 */

#include <cil2cpp/bcl/System.String.h>
#include <cil2cpp/bcl/System.Object.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/array.h>
#include <cil2cpp/type_info.h>

#include <unordered_map>
#include <string>
#include <cstring>

namespace cil2cpp {

// String interning pool
static std::unordered_map<std::string, String*> g_string_pool;

namespace System {

TypeInfo String_TypeInfo = {
    .name = "String",
    .namespace_name = "System",
    .full_name = "System.String",
    .base_type = &Object_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(String),
    .element_size = 0,  // Reference type: arrays store pointers (sizeof(void*) fallback in alloc_array)
    .flags = TypeFlags::Sealed,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

} // namespace System

// ---------- UTF-8 ↔ UTF-16 conversion (RFC 3629 / Unicode 15.0) ----------

// Convert UTF-8 to UTF-16 length
static Int32 utf8_to_utf16_length(const char* utf8) {
    Int32 len = 0;
    while (*utf8) {
        unsigned char c = static_cast<unsigned char>(*utf8);
        if (c < 0x80) {
            utf8 += 1;
        } else if ((c & 0xE0) == 0xC0) {
            utf8 += 2;
        } else if ((c & 0xF0) == 0xE0) {
            utf8 += 3;
        } else if ((c & 0xF8) == 0xF0) {
            utf8 += 4;
            len++;  // Surrogate pair
        } else {
            utf8 += 1;  // Invalid, skip
        }
        len++;
    }
    return len;
}

// Convert UTF-8 to UTF-16
static void utf8_to_utf16(const char* utf8, Char* utf16) {
    while (*utf8) {
        unsigned char c = static_cast<unsigned char>(*utf8);

        if (c < 0x80) {
            *utf16++ = static_cast<Char>(c);
            utf8 += 1;
        } else if ((c & 0xE0) == 0xC0) {
            UInt32 codepoint = (c & 0x1F) << 6;
            codepoint |= (utf8[1] & 0x3F);
            *utf16++ = static_cast<Char>(codepoint);
            utf8 += 2;
        } else if ((c & 0xF0) == 0xE0) {
            UInt32 codepoint = (c & 0x0F) << 12;
            codepoint |= (utf8[1] & 0x3F) << 6;
            codepoint |= (utf8[2] & 0x3F);
            *utf16++ = static_cast<Char>(codepoint);
            utf8 += 3;
        } else if ((c & 0xF8) == 0xF0) {
            UInt32 codepoint = (c & 0x07) << 18;
            codepoint |= (utf8[1] & 0x3F) << 12;
            codepoint |= (utf8[2] & 0x3F) << 6;
            codepoint |= (utf8[3] & 0x3F);
            // Surrogate pair
            codepoint -= 0x10000;
            *utf16++ = static_cast<Char>(0xD800 | (codepoint >> 10));
            *utf16++ = static_cast<Char>(0xDC00 | (codepoint & 0x3FF));
            utf8 += 4;
        } else {
            utf8 += 1;  // Skip invalid
        }
    }
}

String* string_create_utf8(const char* utf8) {
    if (!utf8) {
        return nullptr;
    }

    Int32 len = utf8_to_utf16_length(utf8);
    size_t size = sizeof(String) + (len * sizeof(Char));

    String* str = static_cast<String*>(gc::alloc(size, &System::String_TypeInfo));
    str->length = len;
    utf8_to_utf16(utf8, str->chars);

    return str;
}

String* string_create_utf16(const Char* utf16, Int32 length) {
    if (!utf16 || length < 0) {
        return nullptr;
    }

    size_t size = sizeof(String) + (length * sizeof(Char));
    String* str = static_cast<String*>(gc::alloc(size, &System::String_TypeInfo));
    str->length = length;
    std::memcpy(str->chars, utf16, length * sizeof(Char));

    return str;
}

String* string_literal(const char* utf8) {
    if (!utf8) {
        return nullptr;
    }

    // Check intern pool
    auto it = g_string_pool.find(utf8);
    if (it != g_string_pool.end()) {
        return it->second;
    }

    // Create and intern (BoehmGC auto-scans g_string_pool as a global root)
    String* str = string_create_utf8(utf8);
    g_string_pool[utf8] = str;

    return str;
}

String* string_concat(String* a, String* b) {
    if (!a) return b;
    if (!b) return a;

    Int32 new_length = a->length + b->length;
    size_t size = sizeof(String) + (new_length * sizeof(Char));

    String* result = static_cast<String*>(gc::alloc(size, &System::String_TypeInfo));
    result->length = new_length;

    std::memcpy(result->chars, a->chars, a->length * sizeof(Char));
    std::memcpy(result->chars + a->length, b->chars, b->length * sizeof(Char));

    return result;
}

String* string_concat(String* a, String* b, String* c) {
    return string_concat(string_concat(a, b), c);
}

Boolean string_equals(String* a, String* b) {
    if (a == b) return true;
    if (!a || !b) return false;
    if (a->length != b->length) return false;

    return std::memcmp(a->chars, b->chars, a->length * sizeof(Char)) == 0;
}

Boolean string_not_equals(String* a, String* b) {
    return !string_equals(a, b);
}

// FNV-1a hash constants (http://www.isthe.com/chongo/tech/comp/fnv/)
static constexpr UInt32 FNV1A_OFFSET_BASIS = 2166136261u;
static constexpr UInt32 FNV1A_PRIME        = 16777619u;

Int32 string_get_hash_code(String* str) {
    if (!str) return 0;

    UInt32 hash = FNV1A_OFFSET_BASIS;
    for (Int32 i = 0; i < str->length; i++) {
        hash ^= static_cast<UInt32>(str->chars[i]);
        hash *= FNV1A_PRIME;
    }
    return static_cast<Int32>(hash);
}

Boolean string_is_null_or_empty(String* str) {
    return !str || str->length == 0;
}

Boolean string_is_null_or_whitespace(String* str) {
    if (!str || str->length == 0) return true;
    for (Int32 i = 0; i < str->length; i++) {
        Char c = str->chars[i];
        if (c != u' ' && c != u'\t' && c != u'\n' && c != u'\r' &&
            c != u'\v' && c != u'\f') return false;
    }
    return true;
}

String* string_substring(String* str, Int32 start, Int32 length) {
    if (!str || start < 0 || length < 0 || start + length > str->length) {
        return nullptr;  // TODO: throw ArgumentOutOfRangeException
    }

    return string_create_utf16(str->chars + start, length);
}

char* string_to_utf8(String* str) {
    if (!str) {
        return nullptr;
    }

    // Calculate UTF-8 length
    size_t utf8_len = 0;
    for (Int32 i = 0; i < str->length; i++) {
        Char c = str->chars[i];
        if (c < 0x80) {
            utf8_len += 1;
        } else if (c < 0x800) {
            utf8_len += 2;
        } else {
            utf8_len += 3;
        }
    }

    char* utf8 = static_cast<char*>(std::malloc(utf8_len + 1));
    char* p = utf8;

    for (Int32 i = 0; i < str->length; i++) {
        Char c = str->chars[i];
        if (c < 0x80) {
            *p++ = static_cast<char>(c);
        } else if (c < 0x800) {
            *p++ = static_cast<char>(0xC0 | (c >> 6));
            *p++ = static_cast<char>(0x80 | (c & 0x3F));
        } else {
            *p++ = static_cast<char>(0xE0 | (c >> 12));
            *p++ = static_cast<char>(0x80 | ((c >> 6) & 0x3F));
            *p++ = static_cast<char>(0x80 | (c & 0x3F));
        }
    }

    *p = '\0';
    return utf8;
}

String* string_from_int32(Int32 value) {
    char buf[16];
    snprintf(buf, sizeof(buf), "%d", value);
    return string_create_utf8(buf);
}

String* string_from_double(Double value) {
    char buf[32];
    snprintf(buf, sizeof(buf), "%g", value);
    return string_create_utf8(buf);
}

String* string_from_int64(Int64 value) {
    char buf[24];
    snprintf(buf, sizeof(buf), "%lld", static_cast<long long>(value));
    return string_create_utf8(buf);
}

String* string_fast_allocate(Int32 length) {
    size_t size = sizeof(String) + (length * sizeof(Char));
    String* str = static_cast<String*>(gc::alloc(size, &System::String_TypeInfo));
    str->length = length;
    return str;
}

Array* string_to_char_array(String* str) {
    if (!str) return nullptr;
    Int32 len = str->length;
    if (len == 0) {
        // Return empty array (equivalent to Array.Empty<char>())
        return static_cast<Array*>(array_create(&System::String_TypeInfo, 0));
    }
    // Char TypeInfo uses element_size = sizeof(Char) = 2
    // We need a TypeInfo for Char elements — use a static local
    static TypeInfo Char_TypeInfo = {
        .name = "Char", .namespace_name = "System", .full_name = "System.Char",
        .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
        .instance_size = sizeof(Char), .element_size = sizeof(Char),
        .flags = TypeFlags::ValueType | TypeFlags::Primitive, .vtable = nullptr,
        .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
        .interface_vtables = nullptr, .interface_vtable_count = 0,
    };
    auto* arr = static_cast<Array*>(array_create(&Char_TypeInfo, len));
    std::memcpy(array_data(arr), str->chars, len * sizeof(Char));
    return arr;
}

} // namespace cil2cpp
