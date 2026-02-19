/**
 * CIL2CPP Runtime - System.IO.File ICalls
 *
 * Platform-independent file operations using C++ standard library.
 */

#include <cil2cpp/io.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/type_info.h>

#include "io_utils.h"

#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <filesystem>

namespace fs = std::filesystem;

namespace cil2cpp {

// Helper: read entire file as bytes
static std::vector<char> read_file_bytes(const fs::path& p) {
    std::ifstream file(p, std::ios::binary | std::ios::ate);
    if (!file.is_open()) {
        throw_file_not_found(("Could not find file '" + p.string() + "'.").c_str());
    }
    auto size = file.tellg();
    file.seekg(0, std::ios::beg);
    std::vector<char> buffer(static_cast<size_t>(size));
    if (size > 0) {
        file.read(buffer.data(), size);
    }
    return buffer;
}

// Helper: write bytes to file
static void write_file_bytes(const fs::path& p, const char* data, size_t len) {
    std::ofstream file(p, std::ios::binary | std::ios::trunc);
    if (!file.is_open()) {
        throw_io_exception(("Could not open file '" + p.string() + "' for writing.").c_str());
    }
    if (len > 0) {
        file.write(data, static_cast<std::streamsize>(len));
    }
}

// TypeInfos for array creation
static TypeInfo& get_byte_type() {
    static TypeInfo s_byte = {
        .name = "Byte", .namespace_name = "System", .full_name = "System.Byte",
        .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
        .instance_size = sizeof(uint8_t), .element_size = sizeof(uint8_t),
        .flags = TypeFlags::ValueType | TypeFlags::Primitive,
    };
    return s_byte;
}

static TypeInfo& get_string_array_type() {
    static TypeInfo s_string_arr = {
        .name = "String[]", .namespace_name = "System", .full_name = "System.String[]",
        .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
        .instance_size = sizeof(void*), .element_size = sizeof(void*),
        .flags = TypeFlags::None,
    };
    return s_string_arr;
}

namespace icall {

bool File_Exists(String* path) {
    if (!path) return false;
    std::error_code ec;
    return fs::is_regular_file(io::managed_string_to_path(path), ec);
}

String* File_ReadAllText(String* path) {
    auto bytes = read_file_bytes(io::managed_string_to_path(path));
    if (bytes.empty()) return string_create_utf8("");

    // Detect BOM and decode
    const uint8_t* data = reinterpret_cast<const uint8_t*>(bytes.data());
    size_t len = bytes.size();

    // UTF-8 BOM
    if (len >= io::kUtf8BomSize
        && data[0] == io::kUtf8Bom[0]
        && data[1] == io::kUtf8Bom[1]
        && data[2] == io::kUtf8Bom[2]) {
        // Copy to null-terminated string (read_file_bytes is NOT null-terminated)
        std::string utf8(reinterpret_cast<const char*>(data + io::kUtf8BomSize),
                         len - io::kUtf8BomSize);
        return string_create_utf8(utf8.c_str());
    }
    // UTF-16 LE BOM
    if (len >= io::kUtf16LeBomSize
        && data[0] == io::kUtf16LeBom[0]
        && data[1] == io::kUtf16LeBom[1]) {
        auto* chars = reinterpret_cast<const Char*>(data + io::kUtf16LeBomSize);
        int32_t charCount = static_cast<int32_t>((len - io::kUtf16LeBomSize) / 2);
        return string_create_utf16(chars, charCount);
    }
    // Default: UTF-8 (no BOM) — copy to null-terminated string
    std::string utf8(reinterpret_cast<const char*>(data), len);
    return string_create_utf8(utf8.c_str());
}

String* File_ReadAllText2(String* path, void* /*encoding*/) {
    // FIXME: ignoring Encoding parameter, always reading as UTF-8
    return File_ReadAllText(path);
}

void File_WriteAllText(String* path, String* contents) {
    auto p = io::managed_string_to_path(path);
    if (!contents) {
        write_file_bytes(p, nullptr, 0);
        return;
    }

    // Write as UTF-8 (no BOM, matching .NET default behavior)
    std::string utf8 = io::managed_string_to_utf8(contents);
    write_file_bytes(p, utf8.data(), utf8.size());
}

void File_WriteAllText2(String* path, String* contents, void* /*encoding*/) {
    // FIXME: ignoring Encoding parameter, always writing as UTF-8
    File_WriteAllText(path, contents);
}

Object* File_ReadAllBytes(String* path) {
    auto bytes = read_file_bytes(io::managed_string_to_path(path));
    int32_t len = static_cast<int32_t>(bytes.size());
    auto* arr = array_create(&get_byte_type(), len);
    if (len > 0) {
        std::memcpy(array_data(arr), bytes.data(), static_cast<size_t>(len));
    }
    return reinterpret_cast<Object*>(arr);
}

void File_WriteAllBytes(String* path, Object* bytes) {
    if (!bytes) throw_null_reference();
    auto* arr = reinterpret_cast<Array*>(bytes);
    auto* data = reinterpret_cast<const char*>(array_data(arr));
    int32_t len = array_length(arr);
    write_file_bytes(io::managed_string_to_path(path), data, static_cast<size_t>(len));
}

void File_Delete(String* path) {
    std::error_code ec;
    fs::remove(io::managed_string_to_path(path), ec);
    // .NET File.Delete does not throw if file doesn't exist
}

void File_Copy(String* srcPath, String* destPath, bool overwrite) {
    auto src = io::managed_string_to_path(srcPath);
    auto dest = io::managed_string_to_path(destPath);
    auto options = overwrite ? fs::copy_options::overwrite_existing : fs::copy_options::none;
    std::error_code ec;
    if (!fs::copy_file(src, dest, options, ec)) {
        if (ec) {
            throw_io_exception(("Could not copy file: " + ec.message()).c_str());
        }
    }
}

void File_Move(String* srcPath, String* destPath, bool overwrite) {
    auto src = io::managed_string_to_path(srcPath);
    auto dest = io::managed_string_to_path(destPath);
    if (!overwrite && fs::exists(dest)) {
        throw_io_exception("Cannot create a file when that file already exists.");
    }
    std::error_code ec;
    fs::rename(src, dest, ec);
    if (ec) {
        throw_io_exception(("Could not move file: " + ec.message()).c_str());
    }
}

Object* File_ReadAllLines(String* path) {
    auto bytes = read_file_bytes(io::managed_string_to_path(path));

    // Detect and skip UTF-8 BOM
    size_t offset = 0;
    if (bytes.size() >= io::kUtf8BomSize
        && static_cast<uint8_t>(bytes[0]) == io::kUtf8Bom[0]
        && static_cast<uint8_t>(bytes[1]) == io::kUtf8Bom[1]
        && static_cast<uint8_t>(bytes[2]) == io::kUtf8Bom[2]) {
        offset = io::kUtf8BomSize;
    }
    // FIXME: UTF-16 BOM not handled in ReadAllLines

    std::string content(bytes.begin() + static_cast<ptrdiff_t>(offset), bytes.end());

    // Split by lines
    std::vector<std::string> lines;
    std::istringstream stream(content);
    std::string line;
    while (std::getline(stream, line)) {
        // Remove trailing \r if present
        if (!line.empty() && line.back() == '\r') line.pop_back();
        lines.push_back(std::move(line));
    }

    // Create string array
    int32_t count = static_cast<int32_t>(lines.size());
    auto* arr = array_create(&get_string_array_type(), count);
    auto** elements = static_cast<String**>(array_data(arr));
    for (int32_t i = 0; i < count; ++i) {
        elements[i] = string_create_utf8(lines[static_cast<size_t>(i)].c_str());
    }
    return reinterpret_cast<Object*>(arr);
}

void File_AppendAllText(String* path, String* contents) {
    if (!contents || string_length(contents) == 0) return;

    auto p = io::managed_string_to_path(path);
    std::ofstream file(p, std::ios::binary | std::ios::app);
    if (!file.is_open()) {
        throw_io_exception(("Could not open file '" + p.string() + "' for appending.").c_str());
    }

    // Convert UTF-16 to UTF-8 using ICU
    std::string utf8 = io::managed_string_to_utf8(contents);
    file.write(utf8.data(), static_cast<std::streamsize>(utf8.size()));
}

} // namespace icall
} // namespace cil2cpp
