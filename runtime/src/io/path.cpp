/**
 * CIL2CPP Runtime - System.IO.Path ICalls
 *
 * Path manipulation using std::filesystem.
 */

#include <cil2cpp/io.h>
#include <cil2cpp/exception.h>

#include <filesystem>
#include <string>

#ifdef _WIN32
#include <windows.h>
#endif

namespace fs = std::filesystem;

namespace cil2cpp {

// Forward declaration (defined in file.cpp)
// We reuse the same helper pattern here
static fs::path str_to_path(String* str) {
    if (!str) throw_null_reference();
    auto* chars = string_get_raw_data(str);
    int32_t len = string_length(str);
#ifdef _WIN32
    return fs::path(reinterpret_cast<const wchar_t*>(chars), reinterpret_cast<const wchar_t*>(chars + len));
#else
    std::string utf8;
    utf8.reserve(static_cast<size_t>(len));
    for (int32_t i = 0; i < len; ++i) {
        char16_t ch = chars[i];
        if (ch < 0x80) {
            utf8.push_back(static_cast<char>(ch));
        } else if (ch < 0x800) {
            utf8.push_back(static_cast<char>(0xC0 | (ch >> 6)));
            utf8.push_back(static_cast<char>(0x80 | (ch & 0x3F)));
        } else {
            utf8.push_back(static_cast<char>(0xE0 | (ch >> 12)));
            utf8.push_back(static_cast<char>(0x80 | ((ch >> 6) & 0x3F)));
            utf8.push_back(static_cast<char>(0x80 | (ch & 0x3F)));
        }
    }
    return fs::path(utf8);
#endif
}

static String* path_to_str(const fs::path& p) {
#ifdef _WIN32
    const auto& ws = p.native();
    return string_create_utf16(reinterpret_cast<const Char*>(ws.data()), static_cast<int32_t>(ws.size()));
#else
    auto u8 = p.u8string();
    // Simple UTF-8 to UTF-16 conversion for common case
    std::u16string u16;
    u16.reserve(u8.size());
    for (size_t i = 0; i < u8.size(); ) {
        uint32_t cp;
        auto ch = static_cast<uint8_t>(u8[i]);
        if (ch < 0x80) { cp = ch; i += 1; }
        else if (ch < 0xE0) { cp = (ch & 0x1F) << 6 | (static_cast<uint8_t>(u8[i+1]) & 0x3F); i += 2; }
        else if (ch < 0xF0) { cp = (ch & 0x0F) << 12 | (static_cast<uint8_t>(u8[i+1]) & 0x3F) << 6 | (static_cast<uint8_t>(u8[i+2]) & 0x3F); i += 3; }
        else { cp = (ch & 0x07) << 18 | (static_cast<uint8_t>(u8[i+1]) & 0x3F) << 12 | (static_cast<uint8_t>(u8[i+2]) & 0x3F) << 6 | (static_cast<uint8_t>(u8[i+3]) & 0x3F); i += 4; }
        if (cp <= 0xFFFF) u16.push_back(static_cast<char16_t>(cp));
        else { cp -= 0x10000; u16.push_back(static_cast<char16_t>(0xD800 | (cp >> 10))); u16.push_back(static_cast<char16_t>(0xDC00 | (cp & 0x3FF))); }
    }
    return string_create(reinterpret_cast<const Char*>(u16.data()), static_cast<int32_t>(u16.size()));
#endif
}

namespace icall {

String* Path_GetFullPath(String* path) {
    auto p = str_to_path(path);
    std::error_code ec;
    auto full = fs::absolute(p, ec);
    if (ec) return path; // fallback to original
    return path_to_str(full);
}

String* Path_GetDirectoryName(String* path) {
    if (!path) return nullptr;
    auto p = str_to_path(path);
    auto parent = p.parent_path();
    if (parent.empty()) return nullptr;
    return path_to_str(parent);
}

String* Path_GetFileName(String* path) {
    if (!path) return nullptr;
    auto p = str_to_path(path);
    auto fname = p.filename();
    return path_to_str(fname);
}

String* Path_GetFileNameWithoutExtension(String* path) {
    if (!path) return nullptr;
    auto p = str_to_path(path);
    auto stem = p.stem();
    return path_to_str(stem);
}

String* Path_GetExtension(String* path) {
    if (!path) return nullptr;
    auto p = str_to_path(path);
    auto ext = p.extension();
    return path_to_str(ext);
}

String* Path_GetTempPath() {
    std::error_code ec;
    auto p = fs::temp_directory_path(ec);
    if (ec) {
#ifdef _WIN32
        return string_create_utf8("C:\\Temp\\");
#else
        return string_create_utf8("/tmp/");
#endif
    }
    // Ensure trailing separator
    auto s = p.native();
#ifdef _WIN32
    if (!s.empty() && s.back() != L'\\' && s.back() != L'/') s.push_back(L'\\');
    return string_create_utf16(reinterpret_cast<const Char*>(s.data()), static_cast<int32_t>(s.size()));
#else
    auto u8 = p.u8string();
    if (!u8.empty() && u8.back() != '/') u8.push_back('/');
    return string_create_utf8(reinterpret_cast<const char*>(u8.c_str()));
#endif
}

String* Path_Combine2(String* path1, String* path2) {
    if (!path1 || string_length(path1) == 0) return path2;
    if (!path2 || string_length(path2) == 0) return path1;

    auto p1 = str_to_path(path1);
    auto p2 = str_to_path(path2);

    // If path2 is rooted, return path2 (matches .NET behavior)
    if (p2.is_absolute()) return path2;

    return path_to_str(p1 / p2);
}

String* Path_Combine3(String* path1, String* path2, String* path3) {
    auto* combined = Path_Combine2(path1, path2);
    return Path_Combine2(combined, path3);
}

} // namespace icall
} // namespace cil2cpp
