/**
 * CIL2CPP Runtime - System.IO.Path ICalls
 *
 * Path manipulation using std::filesystem.
 */

#include <cil2cpp/io.h>
#include <cil2cpp/exception.h>

#include "io_utils.h"

#include <filesystem>
#include <string>

namespace fs = std::filesystem;

namespace cil2cpp {
namespace icall {

String* Path_GetFullPath(String* path) {
    auto p = io::managed_string_to_path(path);
    std::error_code ec;
    auto full = fs::absolute(p, ec);
    if (ec) return path; // fallback to original
    return io::path_to_managed_string(full);
}

String* Path_GetDirectoryName(String* path) {
    if (!path) return nullptr;
    auto p = io::managed_string_to_path(path);
    auto parent = p.parent_path();
    if (parent.empty()) return nullptr;
    return io::path_to_managed_string(parent);
}

String* Path_GetFileName(String* path) {
    if (!path) return nullptr;
    auto p = io::managed_string_to_path(path);
    auto fname = p.filename();
    return io::path_to_managed_string(fname);
}

String* Path_GetFileNameWithoutExtension(String* path) {
    if (!path) return nullptr;
    auto p = io::managed_string_to_path(path);
    auto stem = p.stem();
    return io::path_to_managed_string(stem);
}

String* Path_GetExtension(String* path) {
    if (!path) return nullptr;
    auto p = io::managed_string_to_path(path);
    auto ext = p.extension();
    return io::path_to_managed_string(ext);
}

String* Path_GetTempPath() {
    std::error_code ec;
    auto p = fs::temp_directory_path(ec);
    if (ec) {
#ifdef CIL2CPP_WINDOWS
        return string_create_utf8("C:\\Temp\\");
#else
        return string_create_utf8("/tmp/");
#endif
    }
    // Ensure trailing separator
#ifdef CIL2CPP_WINDOWS
    auto s = p.native();
    if (!s.empty() && s.back() != L'\\' && s.back() != L'/') s.push_back(L'\\');
    return string_create_utf16(reinterpret_cast<const Char*>(s.data()),
                               static_cast<int32_t>(s.size()));
#else
    auto u8 = p.u8string();
    if (!u8.empty() && u8.back() != '/') u8.push_back('/');
    return string_create_utf8(reinterpret_cast<const char*>(u8.c_str()));
#endif
}

String* Path_Combine2(String* path1, String* path2) {
    if (!path1 || string_length(path1) == 0) return path2;
    if (!path2 || string_length(path2) == 0) return path1;

    auto p1 = io::managed_string_to_path(path1);
    auto p2 = io::managed_string_to_path(path2);

    // If path2 is rooted, return path2 (matches .NET behavior)
    if (p2.is_absolute()) return path2;

    return io::path_to_managed_string(p1 / p2);
}

String* Path_Combine3(String* path1, String* path2, String* path3) {
    auto* combined = Path_Combine2(path1, path2);
    return Path_Combine2(combined, path3);
}

} // namespace icall
} // namespace cil2cpp
