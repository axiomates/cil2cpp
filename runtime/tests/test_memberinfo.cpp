/**
 * CIL2CPP Runtime Tests - MemberInfo Reflection
 *
 * Tests ManagedMethodInfo, ManagedFieldInfo, and Type.GetMethods/GetFields API.
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

// ===== Test fixtures =====

// A simple C++ function to act as a method pointer
static void DummyMethod(Object*) {}
static String* DummyStringMethod(Object*) { return nullptr; }

// Method and field metadata for test types
static TypeInfo MemberInfoTest_Int32Type = {
    .name = "Int32", .namespace_name = "System", .full_name = "System.Int32",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Int32), .element_size = 0,
    .flags = TypeFlags::ValueType | TypeFlags::Primitive, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
.properties = nullptr, .property_count = 0,
};

static TypeInfo* MemberInfoTest_Ctor_ParamTypes[] = { &System_String_TypeInfo };

static MethodInfo MemberInfoTest_Methods[] = {
    {
        .name = ".ctor",
        .declaring_type = nullptr, // set below
        .return_type = nullptr,
        .parameter_types = MemberInfoTest_Ctor_ParamTypes,
        .parameter_count = 1,
        .method_pointer = reinterpret_cast<void*>(&DummyMethod),
        .flags = 0x1886, // Public | SpecialName | RTSpecialName
        .vtable_slot = -1,
    },
    {
        .name = "Speak",
        .declaring_type = nullptr,
        .return_type = &System_String_TypeInfo,
        .parameter_types = nullptr,
        .parameter_count = 0,
        .method_pointer = reinterpret_cast<void*>(&DummyStringMethod),
        .flags = 0x01C6, // Public | Virtual | HideBySig
        .vtable_slot = 3,
    },
    {
        .name = "GetCount",
        .declaring_type = nullptr,
        .return_type = &MemberInfoTest_Int32Type,
        .parameter_types = nullptr,
        .parameter_count = 0,
        .method_pointer = nullptr,
        .flags = 0x0016, // Public | Static
        .vtable_slot = -1,
    },
};

static FieldInfo MemberInfoTest_Fields[] = {
    {
        .name = "_name",
        .declaring_type = nullptr,
        .field_type = &System_String_TypeInfo,
        .offset = sizeof(Object), // first field after Object header
        .flags = 0x0004, // Family (protected)
    },
    {
        .name = "_count",
        .declaring_type = nullptr,
        .field_type = &MemberInfoTest_Int32Type,
        .offset = 0,
        .flags = 0x0011, // Private | Static
    },
};

static TypeInfo MemberInfoTest_Animal = {
    .name = "Animal", .namespace_name = "Test", .full_name = "Test.Animal",
    .base_type = &System_Object_TypeInfo, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object) + sizeof(String*), .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = MemberInfoTest_Fields, .field_count = 2,
    .methods = MemberInfoTest_Methods, .method_count = 3,
.properties = nullptr, .property_count = 0,
};

class MemberInfoTestFixture : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
        // Wire up declaring_type references
        for (auto& m : MemberInfoTest_Methods)
            m.declaring_type = &MemberInfoTest_Animal;
        for (auto& f : MemberInfoTest_Fields)
            f.declaring_type = &MemberInfoTest_Animal;
    }
    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== Type.GetMethods tests =====

TEST_F(MemberInfoTestFixture, TypeGetMethods_ReturnsAllMethods) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    auto* arr = type_get_methods(t);
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(array_length(arr), 3);
}

TEST_F(MemberInfoTestFixture, TypeGetMethods_ElementsNotNull) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    auto* arr = type_get_methods(t);
    auto** data = static_cast<ManagedMethodInfo**>(array_data(arr));
    for (int i = 0; i < 3; i++) {
        ASSERT_NE(data[i], nullptr);
        EXPECT_NE(data[i]->native_info, nullptr);
    }
}

TEST_F(MemberInfoTestFixture, TypeGetMethod_ByName_Found) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    auto* name = string_literal("Speak");
    auto* mi = type_get_method(t, name);
    ASSERT_NE(mi, nullptr);
    EXPECT_STREQ(mi->native_info->name, "Speak");
}

TEST_F(MemberInfoTestFixture, TypeGetMethod_ByName_NotFound) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    auto* name = string_literal("NonExistent");
    auto* mi = type_get_method(t, name);
    EXPECT_EQ(mi, nullptr);
}

// ===== Type.GetFields tests =====

TEST_F(MemberInfoTestFixture, TypeGetFields_ReturnsAllFields) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    auto* arr = type_get_fields(t);
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(array_length(arr), 2);
}

TEST_F(MemberInfoTestFixture, TypeGetField_ByName_Found) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    auto* name = string_literal("_name");
    auto* fi = type_get_field(t, name);
    ASSERT_NE(fi, nullptr);
    EXPECT_STREQ(fi->native_info->name, "_name");
}

TEST_F(MemberInfoTestFixture, TypeGetField_ByName_NotFound) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    auto* name = string_literal("xyz");
    auto* fi = type_get_field(t, name);
    EXPECT_EQ(fi, nullptr);
}

// ===== MethodInfo property tests =====

TEST_F(MemberInfoTestFixture, MethodInfoGetName) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    auto* name = methodinfo_get_name(mi);
    auto* utf8 = string_to_utf8(name);
    EXPECT_STREQ(utf8, "Speak");
}

TEST_F(MemberInfoTestFixture, MethodInfoGetDeclaringType) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    auto* dt = methodinfo_get_declaring_type(mi);
    ASSERT_NE(dt, nullptr);
    EXPECT_EQ(dt->type_info, &MemberInfoTest_Animal);
}

TEST_F(MemberInfoTestFixture, MethodInfoGetReturnType) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    auto* rt = methodinfo_get_return_type(mi);
    ASSERT_NE(rt, nullptr);
    EXPECT_EQ(rt->type_info, &System_String_TypeInfo);
}

TEST_F(MemberInfoTestFixture, MethodInfoIsPublic) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    EXPECT_TRUE(methodinfo_get_is_public(mi));
}

TEST_F(MemberInfoTestFixture, MethodInfoIsVirtual) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    EXPECT_TRUE(methodinfo_get_is_virtual(mi));
}

TEST_F(MemberInfoTestFixture, MethodInfoIsStatic) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("GetCount"));
    ASSERT_NE(mi, nullptr);
    EXPECT_TRUE(methodinfo_get_is_static(mi));
    EXPECT_FALSE(methodinfo_get_is_virtual(mi));
}

TEST_F(MemberInfoTestFixture, MethodInfoGetParameters) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal(".ctor"));
    ASSERT_NE(mi, nullptr);
    auto* params = methodinfo_get_parameters(mi);
    ASSERT_NE(params, nullptr);
    EXPECT_EQ(array_length(params), 1);
    auto** data = static_cast<ManagedParameterInfo**>(array_data(params));
    ASSERT_NE(data[0], nullptr);
    EXPECT_EQ(data[0]->param_type, &System_String_TypeInfo);
    EXPECT_EQ(data[0]->position, 0);
}

TEST_F(MemberInfoTestFixture, MethodInfoToString) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    auto* str = methodinfo_to_string(mi);
    auto* utf8 = string_to_utf8(str);
    // Should be "String Speak()"
    EXPECT_NE(std::string(utf8).find("Speak"), std::string::npos);
}

// ===== FieldInfo property tests =====

TEST_F(MemberInfoTestFixture, FieldInfoGetName) {
    auto* fi = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                              string_literal("_name"));
    ASSERT_NE(fi, nullptr);
    auto* name = fieldinfo_get_name(fi);
    auto* utf8 = string_to_utf8(name);
    EXPECT_STREQ(utf8, "_name");
}

TEST_F(MemberInfoTestFixture, FieldInfoGetFieldType) {
    auto* fi = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                              string_literal("_name"));
    ASSERT_NE(fi, nullptr);
    auto* ft = fieldinfo_get_field_type(fi);
    ASSERT_NE(ft, nullptr);
    EXPECT_EQ(ft->type_info, &System_String_TypeInfo);
}

TEST_F(MemberInfoTestFixture, FieldInfoIsStatic) {
    auto* fiInst = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                                  string_literal("_name"));
    auto* fiStatic = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                                    string_literal("_count"));
    ASSERT_NE(fiInst, nullptr);
    ASSERT_NE(fiStatic, nullptr);
    EXPECT_FALSE(fieldinfo_get_is_static(fiInst));
    EXPECT_TRUE(fieldinfo_get_is_static(fiStatic));
}

TEST_F(MemberInfoTestFixture, FieldInfoIsPublic) {
    auto* fi = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                              string_literal("_name"));
    ASSERT_NE(fi, nullptr);
    // Family (protected) is not Public
    EXPECT_FALSE(fieldinfo_get_is_public(fi));
}

TEST_F(MemberInfoTestFixture, FieldInfoToString) {
    auto* fi = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                              string_literal("_name"));
    ASSERT_NE(fi, nullptr);
    auto* str = fieldinfo_to_string(fi);
    auto* utf8 = string_to_utf8(str);
    // Should be "String _name"
    EXPECT_NE(std::string(utf8).find("_name"), std::string::npos);
}

// ===== MemberInfo TypeInfo tests =====

TEST_F(MemberInfoTestFixture, MethodInfoTypeInfo_Exists) {
    EXPECT_STREQ(System_Reflection_MethodInfo_TypeInfo.full_name,
                 "System.Reflection.MethodInfo");
}

TEST_F(MemberInfoTestFixture, FieldInfoTypeInfo_Exists) {
    EXPECT_STREQ(System_Reflection_FieldInfo_TypeInfo.full_name,
                 "System.Reflection.FieldInfo");
}

TEST_F(MemberInfoTestFixture, ManagedMethodInfoHasCorrectTypeInfo) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    EXPECT_STREQ(mi->__type_info->full_name, "System.Reflection.MethodInfo");
}

TEST_F(MemberInfoTestFixture, ManagedFieldInfoHasCorrectTypeInfo) {
    auto* fi = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                              string_literal("_name"));
    ASSERT_NE(fi, nullptr);
    EXPECT_STREQ(fi->__type_info->full_name, "System.Reflection.FieldInfo");
}

// ===== Universal MemberInfo dispatchers =====

TEST_F(MemberInfoTestFixture, MemberInfoGetName_OnMethodInfo) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    auto* name = memberinfo_get_name(reinterpret_cast<Object*>(mi));
    auto* utf8 = string_to_utf8(name);
    EXPECT_STREQ(utf8, "Speak");
}

TEST_F(MemberInfoTestFixture, MemberInfoGetName_OnFieldInfo) {
    auto* fi = type_get_field(type_get_type_object(&MemberInfoTest_Animal),
                              string_literal("_name"));
    ASSERT_NE(fi, nullptr);
    auto* name = memberinfo_get_name(reinterpret_cast<Object*>(fi));
    auto* utf8 = string_to_utf8(name);
    EXPECT_STREQ(utf8, "_name");
}

TEST_F(MemberInfoTestFixture, MemberInfoGetName_OnType) {
    auto* t = type_get_type_object(&MemberInfoTest_Animal);
    ASSERT_NE(t, nullptr);
    auto* name = memberinfo_get_name(reinterpret_cast<Object*>(t));
    auto* utf8 = string_to_utf8(name);
    EXPECT_STREQ(utf8, "Animal");
}

TEST_F(MemberInfoTestFixture, MemberInfoGetDeclaringType_OnMethodInfo) {
    auto* mi = type_get_method(type_get_type_object(&MemberInfoTest_Animal),
                               string_literal("Speak"));
    ASSERT_NE(mi, nullptr);
    auto* dt = memberinfo_get_declaring_type(reinterpret_cast<Object*>(mi));
    ASSERT_NE(dt, nullptr);
    EXPECT_EQ(dt->type_info, &MemberInfoTest_Animal);
}
