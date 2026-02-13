/**
 * CIL2CPP Runtime Tests - Type System
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

// Create a type hierarchy for testing:
// Object -> Animal -> Dog
//                  -> Cat
// IRunnable (interface)

static TypeInfo ObjectType = {
    .name = "Object",
    .namespace_name = "System",
    .full_name = "System.Object",
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
};

static TypeInfo IRunnableType = {
    .name = "IRunnable",
    .namespace_name = "Tests",
    .full_name = "Tests.IRunnable",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = 0,
    .element_size = 0,
    .flags = TypeFlags::Interface,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
};

static TypeInfo* DogInterfaces[] = { &IRunnableType };

static TypeInfo AnimalType = {
    .name = "Animal",
    .namespace_name = "Tests",
    .full_name = "Tests.Animal",
    .base_type = &ObjectType,
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
};

static TypeInfo DogType = {
    .name = "Dog",
    .namespace_name = "Tests",
    .full_name = "Tests.Dog",
    .base_type = &AnimalType,
    .interfaces = DogInterfaces,
    .interface_count = 1,
    .instance_size = sizeof(Object) + 16,
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
};

static TypeInfo CatType = {
    .name = "Cat",
    .namespace_name = "Tests",
    .full_name = "Tests.Cat",
    .base_type = &AnimalType,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + 16,
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
};

class TypeSystemTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== type_is_subclass_of =====

TEST_F(TypeSystemTest, IsSubclassOf_Direct) {
    EXPECT_TRUE(type_is_subclass_of(&DogType, &AnimalType));
}

TEST_F(TypeSystemTest, IsSubclassOf_Transitive) {
    EXPECT_TRUE(type_is_subclass_of(&DogType, &ObjectType));
}

TEST_F(TypeSystemTest, IsSubclassOf_NotSubclass) {
    EXPECT_FALSE(type_is_subclass_of(&CatType, &DogType));
}

TEST_F(TypeSystemTest, IsSubclassOf_SameType_False) {
    EXPECT_FALSE(type_is_subclass_of(&DogType, &DogType));
}

TEST_F(TypeSystemTest, IsSubclassOf_Null) {
    EXPECT_FALSE(type_is_subclass_of(nullptr, &ObjectType));
    EXPECT_FALSE(type_is_subclass_of(&DogType, nullptr));
}

// ===== type_is_assignable_from =====

TEST_F(TypeSystemTest, IsAssignableFrom_SameType) {
    EXPECT_TRUE(type_is_assignable_from(&DogType, &DogType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_BaseFromDerived) {
    EXPECT_TRUE(type_is_assignable_from(&AnimalType, &DogType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_DerivedFromBase_False) {
    EXPECT_FALSE(type_is_assignable_from(&DogType, &AnimalType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_InterfaceFromImplementor) {
    EXPECT_TRUE(type_is_assignable_from(&IRunnableType, &DogType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_InterfaceFromNonImplementor) {
    EXPECT_FALSE(type_is_assignable_from(&IRunnableType, &CatType));
}

// ===== type_implements_interface =====

TEST_F(TypeSystemTest, ImplementsInterface_Direct) {
    EXPECT_TRUE(type_implements_interface(&DogType, &IRunnableType));
}

TEST_F(TypeSystemTest, ImplementsInterface_NotImplemented) {
    EXPECT_FALSE(type_implements_interface(&CatType, &IRunnableType));
}

TEST_F(TypeSystemTest, ImplementsInterface_Null) {
    EXPECT_FALSE(type_implements_interface(nullptr, &IRunnableType));
    EXPECT_FALSE(type_implements_interface(&DogType, nullptr));
}

// ===== Type registry =====

TEST_F(TypeSystemTest, Register_ThenGetByName) {
    type_register(&DogType);
    TypeInfo* found = type_get_by_name("Tests.Dog");
    EXPECT_EQ(found, &DogType);
}

TEST_F(TypeSystemTest, GetByName_NotRegistered_ReturnsNull) {
    TypeInfo* found = type_get_by_name("NonExistent.Type");
    EXPECT_EQ(found, nullptr);
}

TEST_F(TypeSystemTest, Register_NullType_NoOp) {
    type_register(nullptr);  // Should not crash
    SUCCEED();
}

// ===== TypeFlags =====

TEST_F(TypeSystemTest, TypeFlags_BitwiseOr) {
    auto flags = TypeFlags::ValueType | TypeFlags::Sealed;
    EXPECT_TRUE(flags & TypeFlags::ValueType);
    EXPECT_TRUE(flags & TypeFlags::Sealed);
    EXPECT_FALSE(flags & TypeFlags::Interface);
}

TEST_F(TypeSystemTest, TypeFlags_None) {
    EXPECT_FALSE(TypeFlags::None & TypeFlags::ValueType);
}
