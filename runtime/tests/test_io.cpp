/**
 * CIL2CPP Runtime Tests - System.IO ICalls
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>
#include <cil2cpp/io.h>
#include <cil2cpp/string.h>
#include <cil2cpp/array.h>

#include <filesystem>
#include <fstream>
#include <cstring>

namespace fs = std::filesystem;

class IOTest : public ::testing::Test {
protected:
    static void SetUpTestSuite() {
        cil2cpp::runtime_init();
    }
    static void TearDownTestSuite() {
        cil2cpp::runtime_shutdown();
    }

    void SetUp() override {
        test_dir_ = fs::temp_directory_path() / "cil2cpp_io_test";
        fs::create_directories(test_dir_);
    }

    void TearDown() override {
        std::error_code ec;
        fs::remove_all(test_dir_, ec);
    }

    fs::path test_dir_;

    // Helper: create a file with raw bytes
    void write_raw(const fs::path& p, const void* data, size_t len) {
        std::ofstream f(p, std::ios::binary);
        f.write(static_cast<const char*>(data), static_cast<std::streamsize>(len));
    }

    // Helper: create a managed String from a UTF-8 C string
    cil2cpp::String* str(const char* utf8) {
        return cil2cpp::string_create_utf8(utf8);
    }

    // Helper: get managed string as UTF-8
    std::string to_utf8(cil2cpp::String* s) {
        if (!s) return {};
        char* raw = cil2cpp::string_to_utf8(s);
        if (!raw) return {};
        std::string result(raw);
        std::free(raw);
        return result;
    }

    // Helper: create managed String from filesystem path
    cil2cpp::String* path_str(const fs::path& p) {
#ifdef CIL2CPP_WINDOWS
        const auto& ws = p.native();
        return cil2cpp::string_create_utf16(
            reinterpret_cast<const cil2cpp::Char*>(ws.data()),
            static_cast<int32_t>(ws.size()));
#else
        return str(reinterpret_cast<const char*>(p.u8string().c_str()));
#endif
    }
};

// ===== File_ReadAllText =====

TEST_F(IOTest, ReadAllText_NoBom) {
    auto path = test_dir_ / "no_bom.txt";
    write_raw(path, "Hello World", 11);

    auto* result = cil2cpp::icall::File_ReadAllText(path_str(path));
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(to_utf8(result), "Hello World");
}

TEST_F(IOTest, ReadAllText_Utf8Bom) {
    auto path = test_dir_ / "utf8_bom.txt";
    // EF BB BF + "Hello"
    const uint8_t data[] = { 0xEF, 0xBB, 0xBF, 'H', 'e', 'l', 'l', 'o' };
    write_raw(path, data, sizeof(data));

    auto* result = cil2cpp::icall::File_ReadAllText(path_str(path));
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(to_utf8(result), "Hello");
}

TEST_F(IOTest, ReadAllText_Utf16LeBom) {
    auto path = test_dir_ / "utf16le_bom.txt";
    // FF FE + "Hi" in UTF-16 LE
    const uint8_t data[] = { 0xFF, 0xFE, 'H', 0x00, 'i', 0x00 };
    write_raw(path, data, sizeof(data));

    auto* result = cil2cpp::icall::File_ReadAllText(path_str(path));
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(to_utf8(result), "Hi");
}

TEST_F(IOTest, ReadAllText_EmptyFile) {
    auto path = test_dir_ / "empty.txt";
    write_raw(path, "", 0);

    auto* result = cil2cpp::icall::File_ReadAllText(path_str(path));
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(cil2cpp::string_length(result), 0);
}

// ===== File_ReadAllLines =====

TEST_F(IOTest, ReadAllLines_Simple) {
    auto path = test_dir_ / "lines.txt";
    write_raw(path, "line1\nline2\nline3", 17);

    auto* result = cil2cpp::icall::File_ReadAllLines(path_str(path));
    ASSERT_NE(result, nullptr);
    auto* arr = reinterpret_cast<cil2cpp::Array*>(result);
    ASSERT_EQ(cil2cpp::array_length(arr), 3);

    auto** elems = reinterpret_cast<cil2cpp::String**>(cil2cpp::array_data(arr));
    EXPECT_EQ(to_utf8(elems[0]), "line1");
    EXPECT_EQ(to_utf8(elems[1]), "line2");
    EXPECT_EQ(to_utf8(elems[2]), "line3");
}

TEST_F(IOTest, ReadAllLines_Utf8BomStripped) {
    auto path = test_dir_ / "bom_lines.txt";
    // UTF-8 BOM + "first\nsecond"
    const uint8_t data[] = { 0xEF, 0xBB, 0xBF, 'f', 'i', 'r', 's', 't', '\n', 's', 'e', 'c', 'o', 'n', 'd' };
    write_raw(path, data, sizeof(data));

    auto* result = cil2cpp::icall::File_ReadAllLines(path_str(path));
    ASSERT_NE(result, nullptr);
    auto* arr = reinterpret_cast<cil2cpp::Array*>(result);
    ASSERT_EQ(cil2cpp::array_length(arr), 2);

    auto** elems = reinterpret_cast<cil2cpp::String**>(cil2cpp::array_data(arr));
    // BOM must be stripped from first line
    EXPECT_EQ(to_utf8(elems[0]), "first");
    EXPECT_EQ(to_utf8(elems[1]), "second");
}

TEST_F(IOTest, ReadAllLines_CrLf) {
    auto path = test_dir_ / "crlf.txt";
    write_raw(path, "a\r\nb\r\nc", 7);

    auto* result = cil2cpp::icall::File_ReadAllLines(path_str(path));
    auto* arr = reinterpret_cast<cil2cpp::Array*>(result);
    ASSERT_EQ(cil2cpp::array_length(arr), 3);

    auto** elems = reinterpret_cast<cil2cpp::String**>(cil2cpp::array_data(arr));
    EXPECT_EQ(to_utf8(elems[0]), "a");
    EXPECT_EQ(to_utf8(elems[1]), "b");
    EXPECT_EQ(to_utf8(elems[2]), "c");
}

// ===== File_ReadAllBytes / File_WriteAllBytes =====

TEST_F(IOTest, ReadWriteAllBytes_Roundtrip) {
    auto path = test_dir_ / "binary.dat";
    const uint8_t original[] = { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x80 };
    write_raw(path, original, sizeof(original));

    auto* bytes_obj = cil2cpp::icall::File_ReadAllBytes(path_str(path));
    ASSERT_NE(bytes_obj, nullptr);
    auto* arr = reinterpret_cast<cil2cpp::Array*>(bytes_obj);
    ASSERT_EQ(cil2cpp::array_length(arr), static_cast<int32_t>(sizeof(original)));
    EXPECT_EQ(std::memcmp(cil2cpp::array_data(arr), original, sizeof(original)), 0);

    // Write to new file and verify
    auto path2 = test_dir_ / "binary2.dat";
    cil2cpp::icall::File_WriteAllBytes(path_str(path2), bytes_obj);

    auto* bytes2 = cil2cpp::icall::File_ReadAllBytes(path_str(path2));
    auto* arr2 = reinterpret_cast<cil2cpp::Array*>(bytes2);
    ASSERT_EQ(cil2cpp::array_length(arr2), static_cast<int32_t>(sizeof(original)));
    EXPECT_EQ(std::memcmp(cil2cpp::array_data(arr2), original, sizeof(original)), 0);
}

// ===== File_WriteAllText / File_ReadAllText =====

TEST_F(IOTest, WriteReadAllText_Roundtrip) {
    auto path = test_dir_ / "roundtrip.txt";
    auto* text = str("Hello, CIL2CPP!");
    cil2cpp::icall::File_WriteAllText(path_str(path), text);

    auto* result = cil2cpp::icall::File_ReadAllText(path_str(path));
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(to_utf8(result), "Hello, CIL2CPP!");
}

// ===== File_Exists =====

TEST_F(IOTest, FileExists_True) {
    auto path = test_dir_ / "exists.txt";
    write_raw(path, "x", 1);
    EXPECT_TRUE(cil2cpp::icall::File_Exists(path_str(path)));
}

TEST_F(IOTest, FileExists_False) {
    auto path = test_dir_ / "nonexistent.txt";
    EXPECT_FALSE(cil2cpp::icall::File_Exists(path_str(path)));
}

TEST_F(IOTest, FileExists_Null) {
    EXPECT_FALSE(cil2cpp::icall::File_Exists(nullptr));
}

// ===== File_Delete =====

TEST_F(IOTest, FileDelete_Exists) {
    auto path = test_dir_ / "to_delete.txt";
    write_raw(path, "x", 1);
    EXPECT_TRUE(fs::exists(path));
    cil2cpp::icall::File_Delete(path_str(path));
    EXPECT_FALSE(fs::exists(path));
}

TEST_F(IOTest, FileDelete_NotExist_NoThrow) {
    // .NET File.Delete does not throw if file doesn't exist
    auto path = test_dir_ / "no_such_file.txt";
    EXPECT_NO_THROW(cil2cpp::icall::File_Delete(path_str(path)));
}

// ===== File_Copy =====

TEST_F(IOTest, FileCopy) {
    auto src = test_dir_ / "src.txt";
    auto dst = test_dir_ / "dst.txt";
    write_raw(src, "copy me", 7);

    cil2cpp::icall::File_Copy(path_str(src), path_str(dst), false);

    auto* result = cil2cpp::icall::File_ReadAllText(path_str(dst));
    EXPECT_EQ(to_utf8(result), "copy me");
}

// ===== File_AppendAllText =====

TEST_F(IOTest, AppendAllText) {
    auto path = test_dir_ / "append.txt";
    cil2cpp::icall::File_WriteAllText(path_str(path), str("Hello"));
    cil2cpp::icall::File_AppendAllText(path_str(path), str(" World"));

    auto* result = cil2cpp::icall::File_ReadAllText(path_str(path));
    EXPECT_EQ(to_utf8(result), "Hello World");
}

// ===== Directory_Exists =====

TEST_F(IOTest, DirectoryExists_True) {
    EXPECT_TRUE(cil2cpp::icall::Directory_Exists(path_str(test_dir_)));
}

TEST_F(IOTest, DirectoryExists_False) {
    auto path = test_dir_ / "no_such_dir";
    EXPECT_FALSE(cil2cpp::icall::Directory_Exists(path_str(path)));
}

// ===== Directory_CreateDirectory =====

TEST_F(IOTest, CreateDirectory) {
    auto path = test_dir_ / "sub" / "dir";
    EXPECT_FALSE(fs::exists(path));
    cil2cpp::icall::Directory_CreateDirectory(path_str(path));
    EXPECT_TRUE(fs::is_directory(path));
}

// ===== Path operations =====

TEST_F(IOTest, PathGetFileName) {
    auto* result = cil2cpp::icall::Path_GetFileName(str("C:\\dir\\file.txt"));
    EXPECT_EQ(to_utf8(result), "file.txt");
}

TEST_F(IOTest, PathGetExtension) {
    auto* result = cil2cpp::icall::Path_GetExtension(str("file.cs"));
    EXPECT_EQ(to_utf8(result), ".cs");
}

TEST_F(IOTest, PathGetFileNameWithoutExtension) {
    auto* result = cil2cpp::icall::Path_GetFileNameWithoutExtension(str("myfile.txt"));
    EXPECT_EQ(to_utf8(result), "myfile");
}

TEST_F(IOTest, PathCombine2) {
    auto* result = cil2cpp::icall::Path_Combine2(str("dir"), str("file.txt"));
    auto text = to_utf8(result);
    // Should contain separator between "dir" and "file.txt"
    EXPECT_NE(text.find("file.txt"), std::string::npos);
    EXPECT_NE(text.find("dir"), std::string::npos);
}

TEST_F(IOTest, PathGetTempPath) {
    auto* result = cil2cpp::icall::Path_GetTempPath();
    ASSERT_NE(result, nullptr);
    EXPECT_GT(cil2cpp::string_length(result), 0);
}
