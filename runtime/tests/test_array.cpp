/**
 * CIL2CPP Runtime Tests - Array
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

static TypeInfo Int32ElementType = {
    .name = "Int32",
    .namespace_name = "System",
    .full_name = "System.Int32",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(int32_t),
    .element_size = sizeof(int32_t),
    .flags = TypeFlags::ValueType | TypeFlags::Primitive,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
};

class ArrayTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

TEST_F(ArrayTest, Create_ReturnsNonNull) {
    Array* arr = array_create(&Int32ElementType, 10);
    ASSERT_NE(arr, nullptr);
}

TEST_F(ArrayTest, Create_SetsLength) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(arr->length, 5);
}

TEST_F(ArrayTest, Create_SetsElementType) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(arr->element_type, &Int32ElementType);
}

TEST_F(ArrayTest, Create_ZeroLength) {
    Array* arr = array_create(&Int32ElementType, 0);
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(arr->length, 0);
}

TEST_F(ArrayTest, Create_NegativeLength_ReturnsNull) {
    Array* arr = array_create(&Int32ElementType, -1);
    EXPECT_EQ(arr, nullptr);
}

TEST_F(ArrayTest, Length_Helper) {
    Array* arr = array_create(&Int32ElementType, 7);
    EXPECT_EQ(array_length(arr), 7);
}

TEST_F(ArrayTest, Length_Null_ReturnsZero) {
    EXPECT_EQ(array_length(nullptr), 0);
}

TEST_F(ArrayTest, Data_ReturnsPointerAfterHeader) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);

    void* data = array_data(arr);
    // Data should be right after the Array header
    EXPECT_EQ(data, reinterpret_cast<char*>(arr) + sizeof(Array));
}

TEST_F(ArrayTest, SetAndGet_Int32) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);

    // Set values
    array_set<int32_t>(arr, 0, 10);
    array_set<int32_t>(arr, 1, 20);
    array_set<int32_t>(arr, 2, 30);
    array_set<int32_t>(arr, 4, 50);

    // Get and verify
    EXPECT_EQ(array_get<int32_t>(arr, 0), 10);
    EXPECT_EQ(array_get<int32_t>(arr, 1), 20);
    EXPECT_EQ(array_get<int32_t>(arr, 2), 30);
    EXPECT_EQ(array_get<int32_t>(arr, 3), 0);  // Zero-initialized
    EXPECT_EQ(array_get<int32_t>(arr, 4), 50);
}

TEST_F(ArrayTest, BoundsCheck_ValidIndex_NoThrow) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);

    // Set up exception context to catch potential throws
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        array_bounds_check(arr, 0);
        array_bounds_check(arr, 4);
        // If we get here, no exception was thrown - good!
        g_exception_context = ctx.previous;
        SUCCEED();
    } else {
        g_exception_context = ctx.previous;
        FAIL() << "Unexpected exception thrown for valid index";
    }
}

TEST_F(ArrayTest, BoundsCheck_NegativeIndex_Throws) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);

    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        array_bounds_check(arr, -1);
        g_exception_context = ctx.previous;
        FAIL() << "Expected IndexOutOfRangeException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}

TEST_F(ArrayTest, BoundsCheck_OverflowIndex_Throws) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);

    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        array_bounds_check(arr, 5);
        g_exception_context = ctx.previous;
        FAIL() << "Expected IndexOutOfRangeException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}

TEST_F(ArrayTest, BoundsCheck_NullArray_Throws) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        array_bounds_check(nullptr, 0);
        g_exception_context = ctx.previous;
        FAIL() << "Expected NullReferenceException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}

TEST_F(ArrayTest, GetElementPtr_ReturnsCorrectOffset) {
    Array* arr = array_create(&Int32ElementType, 5);
    ASSERT_NE(arr, nullptr);

    // Element 0 should be at array_data
    void* elem0 = array_get_element_ptr(arr, 0);
    EXPECT_EQ(elem0, array_data(arr));

    // Element 1 should be element_size bytes after element 0
    void* elem1 = array_get_element_ptr(arr, 1);
    EXPECT_EQ(elem1, static_cast<char*>(elem0) + sizeof(int32_t));
}
