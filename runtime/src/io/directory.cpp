/**
 * CIL2CPP Runtime - System.IO.Directory ICalls
 */

#include <cil2cpp/io.h>
#include <cil2cpp/exception.h>

#include <filesystem>

namespace fs = std::filesystem;

namespace cil2cpp {

// Helper (same pattern as file.cpp/path.cpp)
static fs::path dir_to_path(String* str) {
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
        if (ch < 0x80) utf8.push_back(static_cast<char>(ch));
        else if (ch < 0x800) { utf8.push_back(static_cast<char>(0xC0 | (ch >> 6))); utf8.push_back(static_cast<char>(0x80 | (ch & 0x3F))); }
        else { utf8.push_back(static_cast<char>(0xE0 | (ch >> 12))); utf8.push_back(static_cast<char>(0x80 | ((ch >> 6) & 0x3F))); utf8.push_back(static_cast<char>(0x80 | (ch & 0x3F))); }
    }
    return fs::path(utf8);
#endif
}

namespace icall {

bool Directory_Exists(String* path) {
    if (!path) return false;
    std::error_code ec;
    return fs::is_directory(dir_to_path(path), ec);
}

Object* Directory_CreateDirectory(String* path) {
    auto p = dir_to_path(path);
    std::error_code ec;
    fs::create_directories(p, ec);
    if (ec) {
        throw_io_exception(("Could not create directory: " + ec.message()).c_str());
    }
    // FIXME: should return a DirectoryInfo object; currently returns nullptr
    return nullptr;
}

} // namespace icall
} // namespace cil2cpp
