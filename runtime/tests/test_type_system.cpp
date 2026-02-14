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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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

// ===== Interface VTable Dispatch =====

static int32_t test_vtable_method(void* self) {
    return 42;
}

static int32_t test_iface_method(void* self) {
    return 99;
}

TEST_F(TypeSystemTest, GetInterfaceVTable_Found_ReturnsNonNull) {
    // Set up Dog with an interface vtable for IRunnable
    static void* irunnable_methods[] = { (void*)test_iface_method };
    static InterfaceVTable dog_iface_vtables[] = {
        { &IRunnableType, irunnable_methods, 1 }
    };
    DogType.interface_vtables = dog_iface_vtables;
    DogType.interface_vtable_count = 1;

    auto* result = type_get_interface_vtable(&DogType, &IRunnableType);
    EXPECT_NE(result, nullptr);
    EXPECT_EQ(result->interface_type, &IRunnableType);
    EXPECT_EQ(result->method_count, 1u);

    DogType.interface_vtables = nullptr;
    DogType.interface_vtable_count = 0;
}

TEST_F(TypeSystemTest, GetInterfaceVTable_NotFound_ReturnsNull) {
    auto* result = type_get_interface_vtable(&CatType, &IRunnableType);
    EXPECT_EQ(result, nullptr);
}

TEST_F(TypeSystemTest, GetInterfaceVTable_InheritedFromBase) {
    // Set up Animal with an interface vtable, Dog should inherit it
    static TypeInfo IWalkableType = {
        .name = "IWalkable",
        .namespace_name = "Tests",
        .full_name = "Tests.IWalkable",
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
        .interface_vtables = nullptr,
        .interface_vtable_count = 0,
    };
    static void* walkable_methods[] = { (void*)test_iface_method };
    static InterfaceVTable animal_iface_vtables[] = {
        { &IWalkableType, walkable_methods, 1 }
    };
    AnimalType.interface_vtables = animal_iface_vtables;
    AnimalType.interface_vtable_count = 1;

    // Dog inherits from Animal, should find Animal's interface vtable
    auto* result = type_get_interface_vtable(&DogType, &IWalkableType);
    EXPECT_NE(result, nullptr);
    EXPECT_EQ(result->interface_type, &IWalkableType);

    AnimalType.interface_vtables = nullptr;
    AnimalType.interface_vtable_count = 0;
}

TEST_F(TypeSystemTest, VTable_FunctionPointer_Dispatches) {
    static void* vtable_methods[] = { (void*)test_vtable_method };
    static VTable dog_vtable = { &DogType, vtable_methods, 1 };
    DogType.vtable = &dog_vtable;

    // Simulate virtual dispatch through vtable
    auto fn = (int32_t(*)(void*))DogType.vtable->methods[0];
    EXPECT_EQ(fn(nullptr), 42);

    DogType.vtable = nullptr;
}

TEST_F(TypeSystemTest, InterfaceVTable_FunctionPointer_Dispatches) {
    static void* iface_methods[] = { (void*)test_iface_method };
    static InterfaceVTable dog_iface_vtables[] = {
        { &IRunnableType, iface_methods, 1 }
    };
    DogType.interface_vtables = dog_iface_vtables;
    DogType.interface_vtable_count = 1;

    auto* ivtable = type_get_interface_vtable(&DogType, &IRunnableType);
    ASSERT_NE(ivtable, nullptr);
    auto fn = (int32_t(*)(void*))ivtable->methods[0];
    EXPECT_EQ(fn(nullptr), 99);

    DogType.interface_vtables = nullptr;
    DogType.interface_vtable_count = 0;
}

// ===== type_get_interface_vtable_checked =====

TEST_F(TypeSystemTest, GetInterfaceVTableChecked_Found_ReturnsVTable) {
    static void* irunnable_methods[] = { (void*)test_iface_method };
    static InterfaceVTable dog_iface_vtables[] = {
        { &IRunnableType, irunnable_methods, 1 }
    };
    DogType.interface_vtables = dog_iface_vtables;
    DogType.interface_vtable_count = 1;

    CIL2CPP_TRY
        auto* result = type_get_interface_vtable_checked(&DogType, &IRunnableType);
        ASSERT_NE(result, nullptr);
        EXPECT_EQ(result->interface_type, &IRunnableType);
    CIL2CPP_CATCH_ALL
        FAIL() << "Unexpected InvalidCastException";
    CIL2CPP_END_TRY

    DogType.interface_vtables = nullptr;
    DogType.interface_vtable_count = 0;
}

TEST_F(TypeSystemTest, GetInterfaceVTableChecked_NotFound_Throws) {
    bool caught = false;

    CIL2CPP_TRY
        type_get_interface_vtable_checked(&CatType, &IRunnableType);
        FAIL() << "Expected InvalidCastException";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(caught);
}

// ===== Deep inheritance chain (4 levels) =====

static TypeInfo GrandParentType = {
    .name = "GrandParent",
    .namespace_name = "Tests",
    .full_name = "Tests.GrandParent",
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

static TypeInfo ParentType = {
    .name = "Parent",
    .namespace_name = "Tests",
    .full_name = "Tests.Parent",
    .base_type = &GrandParentType,
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

static TypeInfo ChildType = {
    .name = "Child",
    .namespace_name = "Tests",
    .full_name = "Tests.Child",
    .base_type = &ParentType,
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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static TypeInfo GrandChildType = {
    .name = "GrandChild",
    .namespace_name = "Tests",
    .full_name = "Tests.GrandChild",
    .base_type = &ChildType,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + 24,
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

TEST_F(TypeSystemTest, DeepHierarchy_IsSubclassOf_AllLevels) {
    EXPECT_TRUE(type_is_subclass_of(&GrandChildType, &ChildType));
    EXPECT_TRUE(type_is_subclass_of(&GrandChildType, &ParentType));
    EXPECT_TRUE(type_is_subclass_of(&GrandChildType, &GrandParentType));
    EXPECT_TRUE(type_is_subclass_of(&ChildType, &ParentType));
    EXPECT_TRUE(type_is_subclass_of(&ChildType, &GrandParentType));
}

TEST_F(TypeSystemTest, DeepHierarchy_IsAssignableFrom) {
    EXPECT_TRUE(type_is_assignable_from(&GrandParentType, &GrandChildType));
    EXPECT_FALSE(type_is_assignable_from(&GrandChildType, &GrandParentType));
}

// ===== Multiple interfaces on a single type =====

static TypeInfo ISwimmableType = {
    .name = "ISwimmable",
    .namespace_name = "Tests",
    .full_name = "Tests.ISwimmable",
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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static TypeInfo IFlyableType = {
    .name = "IFlyable",
    .namespace_name = "Tests",
    .full_name = "Tests.IFlyable",
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
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TEST_F(TypeSystemTest, MultipleInterfaces_AllImplemented) {
    static TypeInfo* duck_ifaces[] = { &ISwimmableType, &IFlyableType, &IRunnableType };
    static TypeInfo DuckType = {
        .name = "Duck",
        .namespace_name = "Tests",
        .full_name = "Tests.Duck",
        .base_type = &AnimalType,
        .interfaces = duck_ifaces,
        .interface_count = 3,
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
        .interface_vtables = nullptr,
        .interface_vtable_count = 0,
    };

    EXPECT_TRUE(type_implements_interface(&DuckType, &ISwimmableType));
    EXPECT_TRUE(type_implements_interface(&DuckType, &IFlyableType));
    EXPECT_TRUE(type_implements_interface(&DuckType, &IRunnableType));
}

TEST_F(TypeSystemTest, MultipleInterfaces_IsAssignableFrom) {
    static TypeInfo* duck_ifaces[] = { &ISwimmableType, &IFlyableType };
    static TypeInfo DuckType2 = {
        .name = "Duck2",
        .namespace_name = "Tests",
        .full_name = "Tests.Duck2",
        .base_type = &AnimalType,
        .interfaces = duck_ifaces,
        .interface_count = 2,
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
        .interface_vtables = nullptr,
        .interface_vtable_count = 0,
    };

    EXPECT_TRUE(type_is_assignable_from(&ISwimmableType, &DuckType2));
    EXPECT_TRUE(type_is_assignable_from(&IFlyableType, &DuckType2));
    EXPECT_FALSE(type_is_assignable_from(&IRunnableType, &DuckType2));
}

// ===== type_get_by_name edge cases =====

TEST_F(TypeSystemTest, GetByName_EmptyString_ReturnsNull) {
    TypeInfo* found = type_get_by_name("");
    EXPECT_EQ(found, nullptr);
}

TEST_F(TypeSystemTest, Register_MultipleTypes_LookupEach) {
    type_register(&AnimalType);
    type_register(&DogType);
    type_register(&CatType);

    EXPECT_EQ(type_get_by_name("Tests.Animal"), &AnimalType);
    EXPECT_EQ(type_get_by_name("Tests.Dog"), &DogType);
    EXPECT_EQ(type_get_by_name("Tests.Cat"), &CatType);
}

// ===== TypeFlags combinations =====

TEST_F(TypeSystemTest, TypeFlags_MultipleCombinations) {
    auto flags = TypeFlags::ValueType | TypeFlags::Sealed | TypeFlags::Primitive;
    EXPECT_TRUE(flags & TypeFlags::ValueType);
    EXPECT_TRUE(flags & TypeFlags::Sealed);
    EXPECT_TRUE(flags & TypeFlags::Primitive);
    EXPECT_FALSE(flags & TypeFlags::Interface);
    EXPECT_FALSE(flags & TypeFlags::Abstract);
    EXPECT_FALSE(flags & TypeFlags::Enum);
}

// ===== type_is_assignable_from with both null =====

TEST_F(TypeSystemTest, IsAssignableFrom_BothNull_False) {
    EXPECT_FALSE(type_is_assignable_from(nullptr, nullptr));
}

// ===== object_alloc + object_is_instance_of round-trip =====

TEST_F(TypeSystemTest, AllocAndInstanceOf_RoundTrip) {
    Object* dog = object_alloc(&DogType);
    ASSERT_NE(dog, nullptr);

    EXPECT_TRUE(object_is_instance_of(dog, &DogType));
    EXPECT_TRUE(object_is_instance_of(dog, &AnimalType));
    EXPECT_TRUE(object_is_instance_of(dog, &ObjectType));
    EXPECT_FALSE(object_is_instance_of(dog, &CatType));
}

// ===== object_as and object_cast with type hierarchy =====

TEST_F(TypeSystemTest, ObjectAs_CompatibleType_ReturnsObject) {
    Object* dog = object_alloc(&DogType);
    EXPECT_EQ(object_as(dog, &AnimalType), dog);
}

TEST_F(TypeSystemTest, ObjectAs_IncompatibleType_ReturnsNull) {
    Object* cat = object_alloc(&CatType);
    EXPECT_EQ(object_as(cat, &DogType), nullptr);
}

TEST_F(TypeSystemTest, ObjectAs_Null_ReturnsNull) {
    EXPECT_EQ(object_as(nullptr, &DogType), nullptr);
}

TEST_F(TypeSystemTest, ObjectCast_Compatible_Succeeds) {
    Object* dog = object_alloc(&DogType);

    CIL2CPP_TRY
        Object* result = object_cast(dog, &AnimalType);
        EXPECT_EQ(result, dog);
    CIL2CPP_CATCH_ALL
        FAIL() << "Unexpected InvalidCastException";
    CIL2CPP_END_TRY
}

TEST_F(TypeSystemTest, ObjectCast_Incompatible_Throws) {
    Object* cat = object_alloc(&CatType);
    bool caught = false;

    CIL2CPP_TRY
        object_cast(cat, &DogType);
        FAIL() << "Expected InvalidCastException";
    CIL2CPP_CATCH_ALL
        caught = true;
    CIL2CPP_END_TRY

    EXPECT_TRUE(caught);
}
