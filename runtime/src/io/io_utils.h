/**
 * CIL2CPP Runtime - IO Utilities (internal)
 *
 * Shared path conversion and BOM constants for System.IO ICalls.
 * Uses ICU-backed unicode:: functions for proper surrogate pair handling.
 */

#pragma once

#include <cil2cpp/string.h>
#include <cil2cpp/unicode.h>
#include <cil2cpp/exception.h>

#include <cstdint>
#include <filesystem>

namespace fs = std::filesystem;

namespace cil2cpp {
namespace io {

// ===== BOM Constants =====

inline constexpr uint8_t kUtf8Bom[] = { 0xEF, 0xBB, 0xBF };
inline constexpr size_t kUtf8BomSize = sizeof(kUtf8Bom);

inline constexpr uint8_t kUtf16LeBom[] = { 0xFF, 0xFE };
inline constexpr size_t kUtf16LeBomSize = sizeof(kUtf16LeBom);

// ===== Path Conversion =====

/// Convert managed String* to std::filesystem::path.
/// Windows: zero-copy via wchar_t reinterpret (UTF-16 native).
/// Non-Windows: ICU-backed UTF-16 → UTF-8 conversion (handles surrogates).
inline fs::path managed_string_to_path(String* str) {
    if (!str) throw_null_reference();
    auto* chars = string_get_raw_data(str);
    int32_t len = string_length(str);
#ifdef CIL2CPP_WINDOWS
    return fs::path(reinterpret_cast<const wchar_t*>(chars),
                    reinterpret_cast<const wchar_t*>(chars + len));
#else
    // Use ICU for correct surrogate pair handling
    Int32 utf8Len = unicode::utf16_to_utf8_length(chars, len);
    std::string utf8(static_cast<size_t>(utf8Len), '\0');
    unicode::utf16_to_utf8(chars, len, utf8.data(), utf8Len);
    return fs::path(utf8);
#endif
}

/// Convert std::filesystem::path to managed String*.
/// Windows: zero-copy from native UTF-16.
/// Non-Windows: ICU-backed UTF-8 → UTF-16 conversion (handles surrogates).
inline String* path_to_managed_string(const fs::path& p) {
#ifdef CIL2CPP_WINDOWS
    const auto& ws = p.native();
    return string_create_utf16(reinterpret_cast<const Char*>(ws.data()),
                               static_cast<int32_t>(ws.size()));
#else
    auto u8 = p.u8string();
    auto* utf8 = reinterpret_cast<const char*>(u8.c_str());
    Int32 utf16Len = unicode::utf8_to_utf16_length(utf8);
    std::vector<Char> buf(static_cast<size_t>(utf16Len));
    unicode::utf8_to_utf16(utf8, buf.data(), utf16Len);
    return string_create_utf16(buf.data(), utf16Len);
#endif
}

/// Convert managed String* UTF-16 content to std::string UTF-8.
/// Uses ICU for correct surrogate pair handling.
inline std::string managed_string_to_utf8(String* str) {
    if (!str) return {};
    auto* chars = string_get_raw_data(str);
    int32_t len = string_length(str);
    Int32 utf8Len = unicode::utf16_to_utf8_length(chars, len);
    std::string utf8(static_cast<size_t>(utf8Len), '\0');
    unicode::utf16_to_utf8(chars, len, utf8.data(), utf8Len);
    return utf8;
}

} // namespace io
} // namespace cil2cpp
