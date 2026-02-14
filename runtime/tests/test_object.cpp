/**
 * CIL2CPP Runtime Tests - Object
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>
#include <cstdlib>

using namespace cil2cpp;

static TypeInfo TestObjType = {
    .name = "TestObj",
    .namespace_name = "Tests",
    .full_name = "Tests.TestObj",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + 8,
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

class ObjectTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== object_alloc =====

TEST_F(ObjectTest, Alloc_ReturnsNonNull) {
    Object* obj = object_alloc(&TestObjType);
    ASSERT_NE(obj, nullptr);
}

TEST_F(ObjectTest, Alloc_SetsTypeInfo) {
    Object* obj = object_alloc(&TestObjType);
    ASSERT_NE(obj, nullptr);
    EXPECT_EQ(obj->__type_info, &TestObjType);
}

TEST_F(ObjectTest, Alloc_NullType_ReturnsNull) {
    Object* obj = object_alloc(nullptr);
    EXPECT_EQ(obj, nullptr);
}

// ===== object_get_type =====

TEST_F(ObjectTest, GetType_ReturnsTypeInfo) {
    Object* obj = object_alloc(&TestObjType);
    ASSERT_NE(obj, nullptr);
    EXPECT_EQ(object_get_type(obj), &TestObjType);
}

TEST_F(ObjectTest, GetType_Null_ReturnsNull) {
    EXPECT_EQ(object_get_type(nullptr), nullptr);
}

// ===== object_get_hash_code =====

TEST_F(ObjectTest, GetHashCode_DifferentObjects_DifferentHash) {
    Object* a = object_alloc(&TestObjType);
    Object* b = object_alloc(&TestObjType);
    // Different objects should have different hash codes (address-based)
    EXPECT_NE(object_get_hash_code(a), object_get_hash_code(b));
}

// ===== object_equals =====

TEST_F(ObjectTest, Equals_SameObject_True) {
    Object* obj = object_alloc(&TestObjType);
    EXPECT_TRUE(object_equals(obj, obj));
}

TEST_F(ObjectTest, Equals_DifferentObjects_False) {
    Object* a = object_alloc(&TestObjType);
    Object* b = object_alloc(&TestObjType);
    EXPECT_FALSE(object_equals(a, b));
}

// ===== object_is_instance_of =====

static TypeInfo BaseType = {
    .name = "Base",
    .namespace_name = "Tests",
    .full_name = "Tests.Base",
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

static TypeInfo DerivedType = {
    .name = "Derived",
    .namespace_name = "Tests",
    .full_name = "Tests.Derived",
    .base_type = &BaseType,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + 8,
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

TEST_F(ObjectTest, IsInstanceOf_SameType_True) {
    Object* obj = object_alloc(&DerivedType);
    EXPECT_TRUE(object_is_instance_of(obj, &DerivedType));
}

TEST_F(ObjectTest, IsInstanceOf_BaseType_True) {
    Object* obj = object_alloc(&DerivedType);
    EXPECT_TRUE(object_is_instance_of(obj, &BaseType));
}

TEST_F(ObjectTest, IsInstanceOf_Unrelated_False) {
    Object* obj = object_alloc(&BaseType);
    EXPECT_FALSE(object_is_instance_of(obj, &DerivedType));
}

TEST_F(ObjectTest, IsInstanceOf_Null_False) {
    EXPECT_FALSE(object_is_instance_of(nullptr, &BaseType));
}

// ===== object_as =====

TEST_F(ObjectTest, As_Compatible_ReturnsObject) {
    Object* obj = object_alloc(&DerivedType);
    Object* result = object_as(obj, &BaseType);
    EXPECT_EQ(result, obj);
}

TEST_F(ObjectTest, As_Incompatible_ReturnsNull) {
    Object* obj = object_alloc(&BaseType);
    Object* result = object_as(obj, &DerivedType);
    EXPECT_EQ(result, nullptr);
}

// ===== object_cast =====

TEST_F(ObjectTest, Cast_Compatible_ReturnsObject) {
    Object* obj = object_alloc(&DerivedType);

    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        Object* result = object_cast(obj, &BaseType);
        g_exception_context = ctx.previous;
        EXPECT_EQ(result, obj);
    } else {
        g_exception_context = ctx.previous;
        FAIL() << "Unexpected InvalidCastException";
    }
}

TEST_F(ObjectTest, Cast_Incompatible_Throws) {
    Object* obj = object_alloc(&BaseType);

    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        object_cast(obj, &DerivedType);
        g_exception_context = ctx.previous;
        FAIL() << "Expected InvalidCastException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}

// ===== object_to_string =====

TEST_F(ObjectTest, ToString_ReturnsTypeName) {
    Object* obj = object_alloc(&TestObjType);
    String* str = object_to_string(obj);
    ASSERT_NE(str, nullptr);

    char* utf8 = string_to_utf8(str);
    EXPECT_STREQ(utf8, "Tests.TestObj");
    std::free(utf8);
}

TEST_F(ObjectTest, ToString_Null_ReturnsNullString) {
    String* str = object_to_string(nullptr);
    ASSERT_NE(str, nullptr);

    char* utf8 = string_to_utf8(str);
    EXPECT_STREQ(utf8, "null");
    std::free(utf8);
}

// ===== System_Object__ctor =====

// Defined in runtime.cpp outside cil2cpp namespace
extern void System_Object__ctor(void* obj);

TEST_F(ObjectTest, ObjectCtor_DoesNotCrash) {
    Object* obj = object_alloc(&TestObjType);
    ASSERT_NE(obj, nullptr);
    // Should be a no-op but must not crash
    System_Object__ctor(obj);
    SUCCEED();
}

TEST_F(ObjectTest, ObjectCtor_NullDoesNotCrash) {
    System_Object__ctor(nullptr);
    SUCCEED();
}

// ===== object_equals edge cases =====

TEST_F(ObjectTest, Equals_NullNull_True) {
    EXPECT_TRUE(object_equals(nullptr, nullptr));
}

TEST_F(ObjectTest, Equals_NullAndNonNull_False) {
    Object* obj = object_alloc(&TestObjType);
    EXPECT_FALSE(object_equals(nullptr, obj));
    EXPECT_FALSE(object_equals(obj, nullptr));
}

// ===== object_get_hash_code =====

TEST_F(ObjectTest, GetHashCode_SameObject_SameHash) {
    Object* obj = object_alloc(&TestObjType);
    EXPECT_EQ(object_get_hash_code(obj), object_get_hash_code(obj));
}

TEST_F(ObjectTest, GetHashCode_Null_ReturnsZero) {
    EXPECT_EQ(object_get_hash_code(nullptr), 0);
}

// ===== object_is_instance_of null type =====

TEST_F(ObjectTest, IsInstanceOf_NullType_False) {
    Object* obj = object_alloc(&TestObjType);
    EXPECT_FALSE(object_is_instance_of(obj, nullptr));
}

TEST_F(ObjectTest, IsInstanceOf_BothNull_False) {
    EXPECT_FALSE(object_is_instance_of(nullptr, nullptr));
}

// ===== object_as with null =====

TEST_F(ObjectTest, As_NullObject_ReturnsNull) {
    Object* result = object_as(nullptr, &TestObjType);
    EXPECT_EQ(result, nullptr);
}

// ===== object_cast with null =====

TEST_F(ObjectTest, Cast_NullObject_Throws) {
    ExceptionContext ctx;
    ctx.previous = g_exception_context;
    ctx.current_exception = nullptr;
    ctx.state = 0;
    g_exception_context = &ctx;

    if (setjmp(ctx.jump_buffer) == 0) {
        object_cast(nullptr, &TestObjType);
        g_exception_context = ctx.previous;
        FAIL() << "Expected InvalidCastException";
    } else {
        g_exception_context = ctx.previous;
        ASSERT_NE(ctx.current_exception, nullptr);
        SUCCEED();
    }
}
