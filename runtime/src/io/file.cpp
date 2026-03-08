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

#include <cil2cpp/object.h>

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

// Encoding type detection from managed System.Text.Encoding object
enum class EncodingType { UTF8, UTF16LE, UTF32, ASCII, Latin1 };

static EncodingType get_encoding_type(void* encoding) {
    if (!encoding) return EncodingType::UTF8;
    auto* obj = static_cast<Object*>(encoding);
    auto* ti = obj->__type_info;
    if (!ti || !ti->full_name) return EncodingType::UTF8;

    const char* name = ti->full_name;
    // Check concrete encoding type names from BCL
    if (std::strstr(name, "UnicodeEncoding")) return EncodingType::UTF16LE;
    if (std::strstr(name, "UTF32Encoding")) return EncodingType::UTF32;
    if (std::strstr(name, "ASCIIEncoding")) return EncodingType::ASCII;
    if (std::strstr(name, "Latin1Encoding")) return EncodingType::Latin1;
    // UTF8Encoding, UTF8EncodingSealed, or anything else → UTF-8
    return EncodingType::UTF8;
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

String* File_ReadAllText2(String* path, void* encoding) {
    auto enc = get_encoding_type(encoding);

    // For UTF-8 (most common), delegate to the BOM-aware implementation
    if (enc == EncodingType::UTF8) return File_ReadAllText(path);

    auto bytes = read_file_bytes(io::managed_string_to_path(path));
    if (bytes.empty()) return string_create_utf8("");

    const uint8_t* data = reinterpret_cast<const uint8_t*>(bytes.data());
    size_t len = bytes.size();

    switch (enc) {
        case EncodingType::UTF16LE: {
            // Skip UTF-16 LE BOM if present
            size_t offset = 0;
            if (len >= io::kUtf16LeBomSize
                && data[0] == io::kUtf16LeBom[0]
                && data[1] == io::kUtf16LeBom[1]) {
                offset = io::kUtf16LeBomSize;
            }
            auto* chars = reinterpret_cast<const Char*>(data + offset);
            int32_t charCount = static_cast<int32_t>((len - offset) / 2);
            return string_create_utf16(chars, charCount);
        }
        case EncodingType::ASCII:
        case EncodingType::Latin1: {
            // ASCII/Latin-1: each byte maps to a single UTF-16 code unit
            int32_t charCount = static_cast<int32_t>(len);
            auto* str = string_fast_allocate(charCount);
            auto* dst = const_cast<Char*>(string_get_raw_data(str));
            for (int32_t i = 0; i < charCount; i++) {
                dst[i] = static_cast<Char>(data[i]);
            }
            return str;
        }
        case EncodingType::UTF32: {
            // UTF-32 LE: each 4 bytes = one code point → convert to UTF-16
            size_t offset = 0;
            // Skip UTF-32 LE BOM (FF FE 00 00)
            if (len >= 4 && data[0] == 0xFF && data[1] == 0xFE
                && data[2] == 0x00 && data[3] == 0x00) {
                offset = 4;
            }
            auto* codepoints = reinterpret_cast<const uint32_t*>(data + offset);
            size_t cpCount = (len - offset) / 4;
            // First pass: count UTF-16 code units
            int32_t charCount = 0;
            for (size_t i = 0; i < cpCount; i++) {
                charCount += (codepoints[i] > 0xFFFF) ? 2 : 1;
            }
            auto* str = string_fast_allocate(charCount);
            auto* dst = const_cast<Char*>(string_get_raw_data(str));
            int32_t pos = 0;
            for (size_t i = 0; i < cpCount; i++) {
                uint32_t cp = codepoints[i];
                if (cp <= 0xFFFF) {
                    dst[pos++] = static_cast<Char>(cp);
                } else {
                    // Surrogate pair
                    cp -= 0x10000;
                    dst[pos++] = static_cast<Char>(0xD800 + (cp >> 10));
                    dst[pos++] = static_cast<Char>(0xDC00 + (cp & 0x3FF));
                }
            }
            return str;
        }
        default:
            return File_ReadAllText(path);
    }
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

void File_WriteAllText2(String* path, String* contents, void* encoding) {
    auto enc = get_encoding_type(encoding);

    // For UTF-8 (most common), delegate to the default implementation
    if (enc == EncodingType::UTF8) {
        File_WriteAllText(path, contents);
        return;
    }

    auto p = io::managed_string_to_path(path);
    if (!contents) {
        write_file_bytes(p, nullptr, 0);
        return;
    }

    auto* chars = string_get_raw_data(contents);
    int32_t charLen = string_length(contents);

    switch (enc) {
        case EncodingType::UTF16LE: {
            // Write UTF-16 LE BOM + raw UTF-16 data
            size_t dataLen = io::kUtf16LeBomSize + static_cast<size_t>(charLen) * 2;
            std::vector<char> buf(dataLen);
            std::memcpy(buf.data(), io::kUtf16LeBom, io::kUtf16LeBomSize);
            std::memcpy(buf.data() + io::kUtf16LeBomSize, chars,
                        static_cast<size_t>(charLen) * 2);
            write_file_bytes(p, buf.data(), buf.size());
            break;
        }
        case EncodingType::ASCII: {
            // Truncate each UTF-16 code unit to 7-bit ASCII
            std::vector<char> buf(static_cast<size_t>(charLen));
            for (int32_t i = 0; i < charLen; i++) {
                buf[static_cast<size_t>(i)] = static_cast<char>(
                    chars[i] > 127 ? '?' : chars[i]);
            }
            write_file_bytes(p, buf.data(), buf.size());
            break;
        }
        case EncodingType::Latin1: {
            // Each UTF-16 code unit → single byte (truncate to 8-bit)
            std::vector<char> buf(static_cast<size_t>(charLen));
            for (int32_t i = 0; i < charLen; i++) {
                buf[static_cast<size_t>(i)] = static_cast<char>(
                    chars[i] > 255 ? '?' : chars[i]);
            }
            write_file_bytes(p, buf.data(), buf.size());
            break;
        }
        case EncodingType::UTF32: {
            // Convert UTF-16 → UTF-32 LE with BOM
            // First pass: count code points
            size_t cpCount = 0;
            for (int32_t i = 0; i < charLen; i++) {
                cpCount++;
                if (chars[i] >= 0xD800 && chars[i] <= 0xDBFF && i + 1 < charLen)
                    i++; // skip low surrogate
            }
            size_t bomSize = 4;
            std::vector<char> buf(bomSize + cpCount * 4);
            // UTF-32 LE BOM: FF FE 00 00
            buf[0] = '\xFF'; buf[1] = '\xFE'; buf[2] = '\0'; buf[3] = '\0';
            auto* out = reinterpret_cast<uint32_t*>(buf.data() + bomSize);
            size_t pos = 0;
            for (int32_t i = 0; i < charLen; i++) {
                uint32_t cp = chars[i];
                if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < charLen) {
                    uint32_t lo = chars[++i];
                    cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00);
                }
                out[pos++] = cp;
            }
            write_file_bytes(p, buf.data(), bomSize + pos * 4);
            break;
        }
        default:
            File_WriteAllText(path, contents);
            break;
    }
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
    // UTF-16 LE BOM: decode UTF-16 to UTF-8 first
    if (offset == 0 && bytes.size() >= io::kUtf16LeBomSize
        && static_cast<uint8_t>(bytes[0]) == io::kUtf16LeBom[0]
        && static_cast<uint8_t>(bytes[1]) == io::kUtf16LeBom[1]) {
        auto* chars = reinterpret_cast<const Char*>(bytes.data() + io::kUtf16LeBomSize);
        int32_t charCount = static_cast<int32_t>((bytes.size() - io::kUtf16LeBomSize) / 2);
        // Convert to UTF-8 for line splitting
        Int32 utf8Len = unicode::utf16_to_utf8_length(chars, charCount);
        std::string utf8(static_cast<size_t>(utf8Len), '\0');
        unicode::utf16_to_utf8(chars, charCount, utf8.data(), utf8Len);

        std::vector<std::string> lines;
        std::istringstream stream(utf8);
        std::string line;
        while (std::getline(stream, line)) {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            lines.push_back(std::move(line));
        }
        int32_t count = static_cast<int32_t>(lines.size());
        auto* arr = array_create(&get_string_array_type(), count);
        auto** elements = static_cast<String**>(array_data(arr));
        for (int32_t i = 0; i < count; ++i) {
            elements[i] = string_create_utf8(lines[static_cast<size_t>(i)].c_str());
        }
        return reinterpret_cast<Object*>(arr);
    }

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
