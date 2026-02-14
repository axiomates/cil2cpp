/**
 * CIL2CPP Runtime Tests - Boxing/Unboxing
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

static TypeInfo Int32Type = {
    .name = "Int32",
    .namespace_name = "System",
    .full_name = "System.Int32",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + sizeof(Int32),
    .element_size = 0,
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

static TypeInfo Int64Type = {
    .name = "Int64",
    .namespace_name = "System",
    .full_name = "System.Int64",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + sizeof(Int64),
    .element_size = 0,
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

static TypeInfo DoubleType = {
    .name = "Double",
    .namespace_name = "System",
    .full_name = "System.Double",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + sizeof(Double),
    .element_size = 0,
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

static TypeInfo SingleType = {
    .name = "Single",
    .namespace_name = "System",
    .full_name = "System.Single",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + sizeof(Single),
    .element_size = 0,
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

static TypeInfo BooleanType = {
    .name = "Boolean",
    .namespace_name = "System",
    .full_name = "System.Boolean",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + sizeof(Boolean),
    .element_size = 0,
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

class BoxingTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== box<Int32> =====

TEST_F(BoxingTest, BoxInt32_ReturnsNonNull) {
    Object* boxed = box<Int32>(42, &Int32Type);
    ASSERT_NE(boxed, nullptr);
}

TEST_F(BoxingTest, BoxInt32_SetsTypeInfo) {
    Object* boxed = box<Int32>(42, &Int32Type);
    EXPECT_EQ(boxed->__type_info, &Int32Type);
}

TEST_F(BoxingTest, BoxInt32_UnboxRoundTrip) {
    Object* boxed = box<Int32>(42, &Int32Type);
    Int32 value = unbox<Int32>(boxed);
    EXPECT_EQ(value, 42);
}

TEST_F(BoxingTest, BoxInt32_NegativeValue) {
    Object* boxed = box<Int32>(-100, &Int32Type);
    EXPECT_EQ(unbox<Int32>(boxed), -100);
}

TEST_F(BoxingTest, BoxInt32_Zero) {
    Object* boxed = box<Int32>(0, &Int32Type);
    EXPECT_EQ(unbox<Int32>(boxed), 0);
}

TEST_F(BoxingTest, BoxInt32_MaxValue) {
    Object* boxed = box<Int32>(INT32_MAX, &Int32Type);
    EXPECT_EQ(unbox<Int32>(boxed), INT32_MAX);
}

TEST_F(BoxingTest, BoxInt32_MinValue) {
    Object* boxed = box<Int32>(INT32_MIN, &Int32Type);
    EXPECT_EQ(unbox<Int32>(boxed), INT32_MIN);
}

// ===== box<Int64> =====

TEST_F(BoxingTest, BoxInt64_RoundTrip) {
    Object* boxed = box<Int64>(123456789012345LL, &Int64Type);
    EXPECT_EQ(unbox<Int64>(boxed), 123456789012345LL);
}

TEST_F(BoxingTest, BoxInt64_SetsTypeInfo) {
    Object* boxed = box<Int64>(0, &Int64Type);
    EXPECT_EQ(boxed->__type_info, &Int64Type);
}

TEST_F(BoxingTest, BoxInt64_MaxValue) {
    Object* boxed = box<Int64>(INT64_MAX, &Int64Type);
    EXPECT_EQ(unbox<Int64>(boxed), INT64_MAX);
}

// ===== box<Double> =====

TEST_F(BoxingTest, BoxDouble_RoundTrip) {
    Object* boxed = box<Double>(3.14159, &DoubleType);
    EXPECT_DOUBLE_EQ(unbox<Double>(boxed), 3.14159);
}

TEST_F(BoxingTest, BoxDouble_Zero) {
    Object* boxed = box<Double>(0.0, &DoubleType);
    EXPECT_DOUBLE_EQ(unbox<Double>(boxed), 0.0);
}

TEST_F(BoxingTest, BoxDouble_Negative) {
    Object* boxed = box<Double>(-1.5, &DoubleType);
    EXPECT_DOUBLE_EQ(unbox<Double>(boxed), -1.5);
}

TEST_F(BoxingTest, BoxDouble_SetsTypeInfo) {
    Object* boxed = box<Double>(1.0, &DoubleType);
    EXPECT_EQ(boxed->__type_info, &DoubleType);
}

// ===== box<Single> =====

TEST_F(BoxingTest, BoxSingle_RoundTrip) {
    Object* boxed = box<Single>(2.5f, &SingleType);
    EXPECT_FLOAT_EQ(unbox<Single>(boxed), 2.5f);
}

TEST_F(BoxingTest, BoxSingle_SetsTypeInfo) {
    Object* boxed = box<Single>(0.0f, &SingleType);
    EXPECT_EQ(boxed->__type_info, &SingleType);
}

// ===== box<Boolean> =====

TEST_F(BoxingTest, BoxBoolTrue_RoundTrip) {
    Object* boxed = box<Boolean>(true, &BooleanType);
    EXPECT_EQ(unbox<Boolean>(boxed), true);
}

TEST_F(BoxingTest, BoxBoolFalse_RoundTrip) {
    Object* boxed = box<Boolean>(false, &BooleanType);
    EXPECT_EQ(unbox<Boolean>(boxed), false);
}

TEST_F(BoxingTest, BoxBool_SetsTypeInfo) {
    Object* boxed = box<Boolean>(true, &BooleanType);
    EXPECT_EQ(boxed->__type_info, &BooleanType);
}

// ===== unbox null =====

TEST_F(BoxingTest, UnboxNull_ThrowsNullReference) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        unbox<Int32>(nullptr);
        g_exception_context = ctx.previous;
        FAIL() << "Expected NullReferenceException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}

// ===== unbox_ptr =====

TEST_F(BoxingTest, UnboxPtr_ReturnsValidPointer) {
    Object* boxed = box<Int32>(42, &Int32Type);
    Int32* ptr = unbox_ptr<Int32>(boxed);
    ASSERT_NE(ptr, nullptr);
    EXPECT_EQ(*ptr, 42);
}

TEST_F(BoxingTest, UnboxPtr_CanModifyValue) {
    Object* boxed = box<Int32>(42, &Int32Type);
    Int32* ptr = unbox_ptr<Int32>(boxed);
    *ptr = 100;
    EXPECT_EQ(unbox<Int32>(boxed), 100);
}

TEST_F(BoxingTest, UnboxPtr_PointsAfterObjectHeader) {
    Object* boxed = box<Int32>(42, &Int32Type);
    Int32* ptr = unbox_ptr<Int32>(boxed);
    // unbox_ptr should point to data after Object header
    EXPECT_EQ(reinterpret_cast<char*>(ptr),
              reinterpret_cast<char*>(boxed) + sizeof(Object));
}

TEST_F(BoxingTest, UnboxPtrNull_ThrowsNullReference) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        unbox_ptr<Int32>(nullptr);
        g_exception_context = ctx.previous;
        FAIL() << "Expected NullReferenceException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}

// ===== Multiple box/unbox =====

TEST_F(BoxingTest, MultipleBoxes_IndependentValues) {
    Object* a = box<Int32>(10, &Int32Type);
    Object* b = box<Int32>(20, &Int32Type);
    Object* c = box<Int32>(30, &Int32Type);

    EXPECT_EQ(unbox<Int32>(a), 10);
    EXPECT_EQ(unbox<Int32>(b), 20);
    EXPECT_EQ(unbox<Int32>(c), 30);
}

TEST_F(BoxingTest, BoxDifferentTypes_IndependentTypeInfo) {
    Object* intBox = box<Int32>(42, &Int32Type);
    Object* dblBox = box<Double>(3.14, &DoubleType);
    Object* boolBox = box<Boolean>(true, &BooleanType);

    EXPECT_EQ(intBox->__type_info, &Int32Type);
    EXPECT_EQ(dblBox->__type_info, &DoubleType);
    EXPECT_EQ(boolBox->__type_info, &BooleanType);
}
