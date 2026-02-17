/**
 * CIL2CPP Runtime Tests - TypedReference & ArgIterator
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

// TypeInfo for test types
static TypeInfo Int32_TypeInfo_Test = {
    .name = "Int32",
    .namespace_name = "System",
    .full_name = "System.Int32",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(int32_t),
    .element_size = 0,
    .flags = TypeFlags::ValueType,
};

static TypeInfo Float_TypeInfo_Test = {
    .name = "Single",
    .namespace_name = "System",
    .full_name = "System.Single",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(float),
    .element_size = 0,
    .flags = TypeFlags::ValueType,
};

class TypedReferenceTest : public ::testing::Test {
protected:
    void SetUp() override {
        gc::init();
    }
};

// ===== TypedReference =====

TEST_F(TypedReferenceTest, CreateAndRead) {
    int32_t x = 42;
    TypedReference tr = {&x, &Int32_TypeInfo_Test};

    EXPECT_EQ(tr.value, &x);
    EXPECT_EQ(tr.type, &Int32_TypeInfo_Test);
    EXPECT_EQ(*static_cast<int32_t*>(tr.value), 42);
}

TEST_F(TypedReferenceTest, WriteBack) {
    // Mirrors __refvalue(tr, int) = 100
    int32_t x = 42;
    TypedReference tr = {&x, &Int32_TypeInfo_Test};

    *static_cast<int32_t*>(tr.value) = 100;
    EXPECT_EQ(x, 100);
}

TEST_F(TypedReferenceTest, TypeField) {
    float f = 3.14f;
    TypedReference tr = {&f, &Float_TypeInfo_Test};

    EXPECT_EQ(tr.type, &Float_TypeInfo_Test);
    EXPECT_STREQ(tr.type->name, "Single");
}

// ===== ArgIterator =====

TEST_F(TypedReferenceTest, ArgIterator_Init) {
    int32_t a = 10, b = 20;
    VarArgEntry entries[] = {
        {&a, &Int32_TypeInfo_Test},
        {&b, &Int32_TypeInfo_Test},
    };
    VarArgHandle handle = {entries, 2};

    ArgIterator iter;
    argiterator_init(&iter, reinterpret_cast<intptr_t>(&handle));

    EXPECT_EQ(iter.count, 2);
    EXPECT_EQ(iter.index, 0);
    EXPECT_EQ(iter.entries, entries);
}

TEST_F(TypedReferenceTest, ArgIterator_InitNull) {
    ArgIterator iter;
    argiterator_init(&iter, 0);

    EXPECT_EQ(iter.count, 0);
    EXPECT_EQ(iter.index, 0);
    EXPECT_EQ(iter.entries, nullptr);
}

TEST_F(TypedReferenceTest, ArgIterator_GetRemainingCount) {
    int32_t a = 1, b = 2, c = 3;
    VarArgEntry entries[] = {
        {&a, &Int32_TypeInfo_Test},
        {&b, &Int32_TypeInfo_Test},
        {&c, &Int32_TypeInfo_Test},
    };
    VarArgHandle handle = {entries, 3};

    ArgIterator iter;
    argiterator_init(&iter, reinterpret_cast<intptr_t>(&handle));

    EXPECT_EQ(argiterator_get_remaining_count(&iter), 3);
    argiterator_get_next_arg(&iter);
    EXPECT_EQ(argiterator_get_remaining_count(&iter), 2);
    argiterator_get_next_arg(&iter);
    EXPECT_EQ(argiterator_get_remaining_count(&iter), 1);
    argiterator_get_next_arg(&iter);
    EXPECT_EQ(argiterator_get_remaining_count(&iter), 0);
}

TEST_F(TypedReferenceTest, ArgIterator_GetNextArg) {
    int32_t a = 10;
    float b = 3.14f;
    VarArgEntry entries[] = {
        {&a, &Int32_TypeInfo_Test},
        {&b, &Float_TypeInfo_Test},
    };
    VarArgHandle handle = {entries, 2};

    ArgIterator iter;
    argiterator_init(&iter, reinterpret_cast<intptr_t>(&handle));

    TypedReference tr1 = argiterator_get_next_arg(&iter);
    EXPECT_EQ(tr1.type, &Int32_TypeInfo_Test);
    EXPECT_EQ(*static_cast<int32_t*>(tr1.value), 10);

    TypedReference tr2 = argiterator_get_next_arg(&iter);
    EXPECT_EQ(tr2.type, &Float_TypeInfo_Test);
    EXPECT_FLOAT_EQ(*static_cast<float*>(tr2.value), 3.14f);
}

TEST_F(TypedReferenceTest, ArgIterator_Overflow_ThrowsInvalidOperation) {
    VarArgEntry entries[] = {{nullptr, nullptr}};
    VarArgHandle handle = {entries, 1};

    ArgIterator iter;
    argiterator_init(&iter, reinterpret_cast<intptr_t>(&handle));
    argiterator_get_next_arg(&iter);  // consume the one entry

    // Attempting to get another arg should throw InvalidOperationException
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        argiterator_get_next_arg(&iter);  // should throw
        FAIL() << "Expected InvalidOperationException";
    } else {
        ASSERT_NE(ctx.current_exception, nullptr);
        EXPECT_TRUE(object_is_instance_of(
            reinterpret_cast<Object*>(ctx.current_exception),
            &InvalidOperationException_TypeInfo));
    }
    g_exception_context = ctx.previous;
}

TEST_F(TypedReferenceTest, ArgIterator_ZeroArgs) {
    VarArgHandle handle = {nullptr, 0};

    ArgIterator iter;
    argiterator_init(&iter, reinterpret_cast<intptr_t>(&handle));

    EXPECT_EQ(argiterator_get_remaining_count(&iter), 0);
}

TEST_F(TypedReferenceTest, ArgIterator_End_IsNoOp) {
    ArgIterator iter = {};
    argiterator_end(&iter);  // should not crash
}

TEST_F(TypedReferenceTest, ArgIterator_SumInts) {
    // Mirrors the SumInts test from ArglistTest sample
    int32_t a = 10, b = 20, c = 30;
    VarArgEntry entries[] = {
        {&a, &Int32_TypeInfo_Test},
        {&b, &Int32_TypeInfo_Test},
        {&c, &Int32_TypeInfo_Test},
    };
    VarArgHandle handle = {entries, 3};

    ArgIterator iter;
    argiterator_init(&iter, reinterpret_cast<intptr_t>(&handle));

    int32_t sum = 0;
    while (argiterator_get_remaining_count(&iter) > 0) {
        TypedReference tr = argiterator_get_next_arg(&iter);
        sum += *static_cast<int32_t*>(tr.value);
    }
    EXPECT_EQ(sum, 60);
}
