/**
 * CIL2CPP Runtime Tests - System.Console
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;
using namespace cil2cpp::System;

class ConsoleTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== Console_WriteLine() overloads =====

TEST_F(ConsoleTest, WriteLineEmpty_NoThrow) {
    testing::internal::CaptureStdout();
    Console_WriteLine();
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "\n");
}

TEST_F(ConsoleTest, WriteLineString_PrintsValue) {
    String* str = string_create_utf8("Hello, World!");
    testing::internal::CaptureStdout();
    Console_WriteLine(str);
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "Hello, World!\n");
}

TEST_F(ConsoleTest, WriteLineString_Null_PrintsNewline) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<String*>(nullptr));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "\n");
}

TEST_F(ConsoleTest, WriteLineInt32_Positive) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Int32>(42));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "42\n");
}

TEST_F(ConsoleTest, WriteLineInt32_Negative) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Int32>(-100));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "-100\n");
}

TEST_F(ConsoleTest, WriteLineInt32_Zero) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Int32>(0));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "0\n");
}

TEST_F(ConsoleTest, WriteLineInt64_LargeValue) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Int64>(123456789012345LL));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "123456789012345\n");
}

TEST_F(ConsoleTest, WriteLineInt64_Negative) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Int64>(-1LL));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "-1\n");
}

TEST_F(ConsoleTest, WriteLineSingle_Value) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Single>(2.5f));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "2.5\n");
}

TEST_F(ConsoleTest, WriteLineDouble_Value) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Double>(3.14));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "3.14\n");
}

TEST_F(ConsoleTest, WriteLineDouble_Integer) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Double>(100.0));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "100\n");
}

TEST_F(ConsoleTest, WriteLineBoolTrue) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Boolean>(true));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "True\n");
}

TEST_F(ConsoleTest, WriteLineBoolFalse) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Boolean>(false));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "False\n");
}

TEST_F(ConsoleTest, WriteLineObject_DoesNotCrash) {
    // Avoid CaptureStdout for Object* overload: on Windows, SetConsoleOutputCP
    // inside init_console() can conflict with GTest's stdout capture.
    // object_to_string correctness is verified in test_object.cpp.
    static TypeInfo TestType = {
        .name = "TestObj",
        .namespace_name = "Tests",
        .full_name = "Tests.TestObj",
        .base_type = nullptr,
        .interfaces = nullptr,
        .interface_count = 0,
        .instance_size = sizeof(Object),
        .element_size = 0,
        .flags = TypeFlags::None,
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
    Object* obj = object_alloc(&TestType);
    Console_WriteLine(obj);
    SUCCEED();
}

TEST_F(ConsoleTest, WriteLineObject_Null_PrintsNewline) {
    testing::internal::CaptureStdout();
    Console_WriteLine(static_cast<Object*>(nullptr));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "\n");
}

// ===== Console_Write() overloads =====

TEST_F(ConsoleTest, WriteString_PrintsValue) {
    String* str = string_create_utf8("Hello");
    testing::internal::CaptureStdout();
    Console_Write(str);
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "Hello");
}

TEST_F(ConsoleTest, WriteString_Null_PrintsNothing) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<String*>(nullptr));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "");
}

TEST_F(ConsoleTest, WriteInt32) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Int32>(99));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "99");
}

TEST_F(ConsoleTest, WriteInt64) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Int64>(9999999999LL));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "9999999999");
}

TEST_F(ConsoleTest, WriteSingle) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Single>(1.5f));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "1.5");
}

TEST_F(ConsoleTest, WriteDouble) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Double>(2.718));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "2.718");
}

TEST_F(ConsoleTest, WriteBoolTrue) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Boolean>(true));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "True");
}

TEST_F(ConsoleTest, WriteBoolFalse) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Boolean>(false));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "False");
}

TEST_F(ConsoleTest, WriteObject_Null_PrintsNothing) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Object*>(nullptr));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "");
}

// ===== Multiple writes =====

TEST_F(ConsoleTest, MultipleWrites_Concatenated) {
    testing::internal::CaptureStdout();
    Console_Write(static_cast<Int32>(1));
    Console_Write(static_cast<Int32>(2));
    Console_Write(static_cast<Int32>(3));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "123");
}

TEST_F(ConsoleTest, WriteAndWriteLine_Combined) {
    testing::internal::CaptureStdout();
    Console_Write(string_create_utf8("Hello, "));
    Console_WriteLine(string_create_utf8("World!"));
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "Hello, World!\n");
}

// ===== UTF-8 output =====

TEST_F(ConsoleTest, WriteLineString_Utf8Content) {
    String* str = string_create_utf8("caf\xC3\xA9");  // café
    testing::internal::CaptureStdout();
    Console_WriteLine(str);
    std::string output = testing::internal::GetCapturedStdout();
    EXPECT_EQ(output, "caf\xC3\xA9\n");
}
