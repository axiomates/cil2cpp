/**
 * CIL2CPP Runtime - System.IO.Directory ICalls
 */

#include <cil2cpp/io.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/gc.h>

#include "io_utils.h"

#include <filesystem>

namespace fs = std::filesystem;

namespace cil2cpp {
namespace icall {

bool Directory_Exists(String* path) {
    if (!path) return false;
    std::error_code ec;
    return fs::is_directory(io::managed_string_to_path(path), ec);
}

Object* Directory_CreateDirectory(String* path) {
    auto p = io::managed_string_to_path(path);
    std::error_code ec;
    fs::create_directories(p, ec);
    if (ec) {
        throw_io_exception(("Could not create directory: " + ec.message()).c_str());
    }
    // TODO: return a proper DirectoryInfo object once the BCL type is compiled from IL.
    // For now, return a minimal opaque object to satisfy non-null checks.
    return static_cast<Object*>(gc::alloc(sizeof(Object), nullptr));
}

} // namespace icall
} // namespace cil2cpp
