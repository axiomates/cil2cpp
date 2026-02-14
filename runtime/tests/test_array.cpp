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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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

// ===== Double arrays =====

static TypeInfo DoubleElementType = {
    .name = "Double",
    .namespace_name = "System",
    .full_name = "System.Double",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(double),
    .element_size = sizeof(double),
    .flags = TypeFlags::ValueType | TypeFlags::Primitive,
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

TEST_F(ArrayTest, DoubleArray_SetAndGet) {
    Array* arr = array_create(&DoubleElementType, 3);
    ASSERT_NE(arr, nullptr);

    array_set<double>(arr, 0, 1.5);
    array_set<double>(arr, 1, 2.718);
    array_set<double>(arr, 2, 3.14);

    EXPECT_DOUBLE_EQ(array_get<double>(arr, 0), 1.5);
    EXPECT_DOUBLE_EQ(array_get<double>(arr, 1), 2.718);
    EXPECT_DOUBLE_EQ(array_get<double>(arr, 2), 3.14);
}

// ===== Boolean arrays =====

static TypeInfo BoolElementType = {
    .name = "Boolean",
    .namespace_name = "System",
    .full_name = "System.Boolean",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(bool),
    .element_size = sizeof(bool),
    .flags = TypeFlags::ValueType | TypeFlags::Primitive,
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

TEST_F(ArrayTest, BoolArray_SetAndGet) {
    Array* arr = array_create(&BoolElementType, 4);
    ASSERT_NE(arr, nullptr);

    array_set<bool>(arr, 0, true);
    array_set<bool>(arr, 1, false);
    array_set<bool>(arr, 2, true);
    array_set<bool>(arr, 3, false);

    EXPECT_TRUE(array_get<bool>(arr, 0));
    EXPECT_FALSE(array_get<bool>(arr, 1));
    EXPECT_TRUE(array_get<bool>(arr, 2));
    EXPECT_FALSE(array_get<bool>(arr, 3));
}

// ===== Int64 arrays =====

static TypeInfo Int64ElementType = {
    .name = "Int64",
    .namespace_name = "System",
    .full_name = "System.Int64",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(int64_t),
    .element_size = sizeof(int64_t),
    .flags = TypeFlags::ValueType | TypeFlags::Primitive,
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

TEST_F(ArrayTest, Int64Array_SetAndGet) {
    Array* arr = array_create(&Int64ElementType, 2);
    ASSERT_NE(arr, nullptr);

    array_set<int64_t>(arr, 0, 123456789012345LL);
    array_set<int64_t>(arr, 1, -999999999999LL);

    EXPECT_EQ(array_get<int64_t>(arr, 0), 123456789012345LL);
    EXPECT_EQ(array_get<int64_t>(arr, 1), -999999999999LL);
}

// ===== Reference type (Object*) arrays =====

static TypeInfo ObjectElementType = {
    .name = "Object",
    .namespace_name = "System",
    .full_name = "System.Object",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object),
    .element_size = sizeof(Object*),
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

TEST_F(ArrayTest, ObjectArray_SetAndGet) {
    Array* arr = array_create(&ObjectElementType, 3);
    ASSERT_NE(arr, nullptr);

    Object* obj1 = object_alloc(&ObjectElementType);
    Object* obj2 = object_alloc(&ObjectElementType);

    array_set<Object*>(arr, 0, obj1);
    array_set<Object*>(arr, 1, obj2);
    array_set<Object*>(arr, 2, nullptr);

    EXPECT_EQ(array_get<Object*>(arr, 0), obj1);
    EXPECT_EQ(array_get<Object*>(arr, 1), obj2);
    EXPECT_EQ(array_get<Object*>(arr, 2), nullptr);
}

TEST_F(ArrayTest, ObjectArray_ZeroInitialized) {
    Array* arr = array_create(&ObjectElementType, 3);
    ASSERT_NE(arr, nullptr);

    // Reference type array should be zero-initialized (all nulls)
    EXPECT_EQ(array_get<Object*>(arr, 0), nullptr);
    EXPECT_EQ(array_get<Object*>(arr, 1), nullptr);
    EXPECT_EQ(array_get<Object*>(arr, 2), nullptr);
}

// ===== Array with single element =====

TEST_F(ArrayTest, SingleElement_SetAndGet) {
    Array* arr = array_create(&Int32ElementType, 1);
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(arr->length, 1);

    array_set<int32_t>(arr, 0, 42);
    EXPECT_EQ(array_get<int32_t>(arr, 0), 42);
}

// ===== Array element_type is set correctly =====

TEST_F(ArrayTest, Create_DifferentTypes_CorrectElementType) {
    Array* intArr = array_create(&Int32ElementType, 1);
    Array* dblArr = array_create(&DoubleElementType, 1);
    Array* objArr = array_create(&ObjectElementType, 1);

    EXPECT_EQ(intArr->element_type, &Int32ElementType);
    EXPECT_EQ(dblArr->element_type, &DoubleElementType);
    EXPECT_EQ(objArr->element_type, &ObjectElementType);
}
