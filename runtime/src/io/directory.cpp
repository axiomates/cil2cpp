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
    // Allocate a DirectoryInfo object with the correct TypeInfo if available.
    // DirectoryInfo is compiled from BCL IL — look it up at runtime.
    auto* ti = type_get_by_name("System.IO.DirectoryInfo");
    auto* obj = static_cast<Object*>(gc::alloc(
        ti ? ti->instance_size : sizeof(Object), ti));
    return obj;
}

} // namespace icall
} // namespace cil2cpp
