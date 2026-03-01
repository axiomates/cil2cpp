/**
 * CIL2CPP Runtime Tests - Reflection (System.Type)
 *
 * Tests the Type object cache, property accessors, and type query methods.
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

// Forward declare icall for TypeCode test (avoid pulling full icall.h dependencies)
namespace cil2cpp { namespace icall {
    Int32 Type_GetTypeCodeImpl(void* __this);
}}

using namespace cil2cpp;

// ===== Test TypeInfo setup =====

static TypeInfo ReflObject = {
    .name = "Object", .namespace_name = "System", .full_name = "System.Object",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object), .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflAnimal = {
    .name = "Animal", .namespace_name = "Test", .full_name = "Test.Animal",
    .base_type = &ReflObject, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object) + 8, .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflDog = {
    .name = "Dog", .namespace_name = "Test", .full_name = "Test.Dog",
    .base_type = &ReflAnimal, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object) + 8, .element_size = 0,
    .flags = TypeFlags::Sealed, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflValueType = {
    .name = "Int32", .namespace_name = "System", .full_name = "System.Int32",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Int32), .element_size = 0,
    .flags = TypeFlags::ValueType | TypeFlags::Primitive, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflInterface = {
    .name = "IRunnable", .namespace_name = "Test", .full_name = "Test.IRunnable",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = 0, .element_size = 0,
    .flags = TypeFlags::Interface | TypeFlags::Abstract, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflEnum = {
    .name = "Color", .namespace_name = "Test", .full_name = "Test.Color",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Int32), .element_size = 0,
    .flags = TypeFlags::ValueType | TypeFlags::Enum, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflGeneric = {
    .name = "List`1", .namespace_name = "Test", .full_name = "Test.List`1",
    .base_type = &ReflObject, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object) + 8, .element_size = 0,
    .flags = TypeFlags::Generic, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflArray = {
    .name = "Int32[]", .namespace_name = "System", .full_name = "System.Int32[]",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object) + 8, .element_size = sizeof(Int32),
    .flags = TypeFlags::Array, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// TypeInfos for TypeCode tests (H.1)
static TypeInfo ReflString = {
    .name = "String", .namespace_name = "System", .full_name = "System.String",
    .base_type = &ReflObject, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object) + 8, .element_size = 0,
    .flags = TypeFlags::Sealed, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflBoolean = {
    .name = "Boolean", .namespace_name = "System", .full_name = "System.Boolean",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Boolean), .element_size = 0,
    .flags = TypeFlags::ValueType | TypeFlags::Primitive, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ReflDouble = {
    .name = "Double", .namespace_name = "System", .full_name = "System.Double",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Double), .element_size = 0,
    .flags = TypeFlags::ValueType | TypeFlags::Primitive, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// ===== Type Object Cache =====

TEST(ReflectionTest, GetTypeObject_NotNull) {
    auto* t = type_get_type_object(&ReflAnimal);
    ASSERT_NE(t, nullptr);
}

TEST(ReflectionTest, GetTypeObject_Cached) {
    auto* t1 = type_get_type_object(&ReflAnimal);
    auto* t2 = type_get_type_object(&ReflAnimal);
    EXPECT_EQ(t1, t2) << "Same TypeInfo must return same Type* (reference equality)";
}

TEST(ReflectionTest, GetTypeObject_DifferentTypesAreDifferent) {
    auto* t1 = type_get_type_object(&ReflAnimal);
    auto* t2 = type_get_type_object(&ReflDog);
    EXPECT_NE(t1, t2);
}

TEST(ReflectionTest, GetTypeObject_Null) {
    EXPECT_EQ(type_get_type_object(nullptr), nullptr);
}

TEST(ReflectionTest, GetTypeFromHandle_ReturnsType) {
    auto* t = type_get_type_from_handle(&ReflDog);
    ASSERT_NE(t, nullptr);
    EXPECT_EQ(t->type_info, &ReflDog);
}

TEST(ReflectionTest, GetTypeFromHandle_Null) {
    EXPECT_EQ(type_get_type_from_handle(nullptr), nullptr);
}

// ===== Object.GetType (managed) =====

TEST(ReflectionTest, ObjectGetTypeManaged_ReturnsType) {
    auto* obj = static_cast<Object*>(gc::alloc(sizeof(Object), &ReflAnimal));
    auto* t = object_get_type_managed(obj);
    ASSERT_NE(t, nullptr);
    EXPECT_EQ(t->type_info, &ReflAnimal);
}

TEST(ReflectionTest, ObjectGetTypeManaged_NullThrows) {
    EXPECT_DEATH(object_get_type_managed(nullptr), "");
}

// ===== Property Accessors =====

TEST(ReflectionTest, GetName) {
    auto* t = type_get_type_object(&ReflDog);
    auto* name = type_get_name(t);
    ASSERT_NE(name, nullptr);
    EXPECT_STREQ(string_to_utf8(name), "Dog");
}

TEST(ReflectionTest, GetFullName) {
    auto* t = type_get_type_object(&ReflDog);
    auto* fn = type_get_full_name(t);
    ASSERT_NE(fn, nullptr);
    EXPECT_STREQ(string_to_utf8(fn), "Test.Dog");
}

TEST(ReflectionTest, GetNamespace) {
    auto* t = type_get_type_object(&ReflDog);
    auto* ns = type_get_namespace(t);
    ASSERT_NE(ns, nullptr);
    EXPECT_STREQ(string_to_utf8(ns), "Test");
}

TEST(ReflectionTest, GetBaseType_HasBase) {
    auto* t = type_get_type_object(&ReflDog);
    auto* base_t = type_get_base_type(t);
    ASSERT_NE(base_t, nullptr);
    EXPECT_EQ(base_t->type_info, &ReflAnimal);
}

TEST(ReflectionTest, GetBaseType_NoBase) {
    auto* t = type_get_type_object(&ReflObject);
    auto* base_t = type_get_base_type(t);
    EXPECT_EQ(base_t, nullptr);
}

// ===== Boolean Type Flags =====

TEST(ReflectionTest, IsValueType_True) {
    auto* t = type_get_type_object(&ReflValueType);
    EXPECT_TRUE(type_get_is_value_type(t));
}

TEST(ReflectionTest, IsValueType_False) {
    auto* t = type_get_type_object(&ReflAnimal);
    EXPECT_FALSE(type_get_is_value_type(t));
}

TEST(ReflectionTest, IsInterface_True) {
    auto* t = type_get_type_object(&ReflInterface);
    EXPECT_TRUE(type_get_is_interface(t));
}

TEST(ReflectionTest, IsInterface_False) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_get_is_interface(t));
}

TEST(ReflectionTest, IsAbstract_True) {
    auto* t = type_get_type_object(&ReflInterface);
    EXPECT_TRUE(type_get_is_abstract(t));
}

TEST(ReflectionTest, IsAbstract_False) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_get_is_abstract(t));
}

TEST(ReflectionTest, IsSealed_True) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_TRUE(type_get_is_sealed(t));
}

TEST(ReflectionTest, IsSealed_False) {
    auto* t = type_get_type_object(&ReflAnimal);
    EXPECT_FALSE(type_get_is_sealed(t));
}

TEST(ReflectionTest, IsEnum_True) {
    auto* t = type_get_type_object(&ReflEnum);
    EXPECT_TRUE(type_get_is_enum(t));
}

TEST(ReflectionTest, IsEnum_False) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_get_is_enum(t));
}

TEST(ReflectionTest, IsArray_True) {
    auto* t = type_get_type_object(&ReflArray);
    EXPECT_TRUE(type_get_is_array(t));
}

TEST(ReflectionTest, IsArray_False) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_get_is_array(t));
}

TEST(ReflectionTest, IsPrimitive_True) {
    auto* t = type_get_type_object(&ReflValueType);
    EXPECT_TRUE(type_get_is_primitive(t));
}

TEST(ReflectionTest, IsPrimitive_False) {
    auto* t = type_get_type_object(&ReflEnum);
    EXPECT_FALSE(type_get_is_primitive(t));
}

TEST(ReflectionTest, IsClass_ForReferenceType) {
    auto* t = type_get_type_object(&ReflAnimal);
    EXPECT_TRUE(type_get_is_class(t));
}

TEST(ReflectionTest, IsClass_ForValueType) {
    auto* t = type_get_type_object(&ReflValueType);
    EXPECT_FALSE(type_get_is_class(t));
}

TEST(ReflectionTest, IsClass_ForInterface) {
    auto* t = type_get_type_object(&ReflInterface);
    EXPECT_FALSE(type_get_is_class(t));
}

TEST(ReflectionTest, IsGenericType_True) {
    auto* t = type_get_type_object(&ReflGeneric);
    EXPECT_TRUE(type_get_is_generic_type(t));
}

TEST(ReflectionTest, IsGenericType_False) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_get_is_generic_type(t));
}

// ===== Type Methods =====

TEST(ReflectionTest, IsAssignableFrom_SameType) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_TRUE(type_is_assignable_from_managed(t, t));
}

TEST(ReflectionTest, IsAssignableFrom_BaseType) {
    auto* animal = type_get_type_object(&ReflAnimal);
    auto* dog = type_get_type_object(&ReflDog);
    EXPECT_TRUE(type_is_assignable_from_managed(animal, dog));
    EXPECT_FALSE(type_is_assignable_from_managed(dog, animal));
}

TEST(ReflectionTest, IsSubclassOf_True) {
    auto* animal = type_get_type_object(&ReflAnimal);
    auto* dog = type_get_type_object(&ReflDog);
    EXPECT_TRUE(type_is_subclass_of_managed(dog, animal));
}

TEST(ReflectionTest, IsSubclassOf_False) {
    auto* animal = type_get_type_object(&ReflAnimal);
    auto* dog = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_is_subclass_of_managed(animal, dog));
}

TEST(ReflectionTest, IsSubclassOf_SameType) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_is_subclass_of_managed(t, t));
}

TEST(ReflectionTest, Equals_SameType) {
    auto* t1 = type_get_type_object(&ReflDog);
    auto* t2 = type_get_type_object(&ReflDog);
    EXPECT_TRUE(type_equals(t1, reinterpret_cast<Object*>(t2)));
}

TEST(ReflectionTest, Equals_DifferentType) {
    auto* t1 = type_get_type_object(&ReflDog);
    auto* t2 = type_get_type_object(&ReflAnimal);
    EXPECT_FALSE(type_equals(t1, reinterpret_cast<Object*>(t2)));
}

TEST(ReflectionTest, Equals_NonTypeObject) {
    auto* t = type_get_type_object(&ReflDog);
    auto* obj = static_cast<Object*>(gc::alloc(sizeof(Object), &ReflAnimal));
    EXPECT_FALSE(type_equals(t, obj));
}

TEST(ReflectionTest, Equals_Null) {
    auto* t = type_get_type_object(&ReflDog);
    EXPECT_FALSE(type_equals(t, nullptr));
}

TEST(ReflectionTest, ToString_ReturnsFullName) {
    auto* t = type_get_type_object(&ReflDog);
    auto* str = type_to_string(t);
    ASSERT_NE(str, nullptr);
    EXPECT_STREQ(string_to_utf8(str), "Test.Dog");
}

// ===== Runtime-Provided TypeInfo =====

TEST(ReflectionTest, SystemObjectTypeInfo_Exists) {
    EXPECT_STREQ(System_Object_TypeInfo.full_name, "System.Object");
}

TEST(ReflectionTest, SystemStringTypeInfo_Exists) {
    EXPECT_STREQ(System_String_TypeInfo.full_name, "System.String");
}

TEST(ReflectionTest, SystemTypeTypeInfo_Exists) {
    EXPECT_STREQ(System_Type_TypeInfo.full_name, "System.Type");
    EXPECT_TRUE(System_Type_TypeInfo.flags & TypeFlags::Sealed);
}

TEST(ReflectionTest, TypeObjectHasCorrectTypeInfo) {
    auto* t = type_get_type_object(&ReflDog);
    // type_get_type_object allocates as RuntimeType (BCL expects Type.op_Equality to work)
    extern TypeInfo System_RuntimeType_TypeInfo;
    EXPECT_EQ(t->__type_info, &System_RuntimeType_TypeInfo);
}

// ===== TypeCode ICall (H.1) =====

TEST(ReflectionTest, TypeCode_Int32) {
    auto* t = type_get_type_object(&ReflValueType); // System.Int32
    EXPECT_EQ(icall::Type_GetTypeCodeImpl(t), 9);   // TypeCode.Int32
}

TEST(ReflectionTest, TypeCode_String) {
    auto* t = type_get_type_object(&ReflString);
    EXPECT_EQ(icall::Type_GetTypeCodeImpl(t), 18);   // TypeCode.String
}

TEST(ReflectionTest, TypeCode_Boolean) {
    auto* t = type_get_type_object(&ReflBoolean);
    EXPECT_EQ(icall::Type_GetTypeCodeImpl(t), 3);    // TypeCode.Boolean
}

TEST(ReflectionTest, TypeCode_Double) {
    auto* t = type_get_type_object(&ReflDouble);
    EXPECT_EQ(icall::Type_GetTypeCodeImpl(t), 14);   // TypeCode.Double
}

TEST(ReflectionTest, TypeCode_Object) {
    auto* t = type_get_type_object(&ReflObject);
    EXPECT_EQ(icall::Type_GetTypeCodeImpl(t), 1);    // TypeCode.Object
}

TEST(ReflectionTest, TypeCode_CustomType_ReturnsObject) {
    auto* t = type_get_type_object(&ReflDog);         // Test.Dog
    EXPECT_EQ(icall::Type_GetTypeCodeImpl(t), 1);    // TypeCode.Object for non-primitive types
}

TEST(ReflectionTest, TypeCode_Null_ReturnsEmpty) {
    EXPECT_EQ(icall::Type_GetTypeCodeImpl(nullptr), 0); // TypeCode.Empty
}
