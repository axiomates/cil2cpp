/**
 * CIL2CPP Runtime Tests - Collections (List, Dictionary)
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

// === TypeInfo definitions for tests ===

static TypeInfo ListIntTypeInfo = {
    .name = "List_Int32",
    .namespace_name = "System.Collections.Generic",
    .full_name = "System.Collections.Generic.List`1<System.Int32>",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ListBase),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr, .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo Int32ElemTypeInfo = {
    .name = "Int32", .namespace_name = "System", .full_name = "System.Int32",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Int32), .element_size = sizeof(Int32),
    .flags = TypeFlags::ValueType | TypeFlags::Primitive,
    .vtable = nullptr, .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ListStringTypeInfo = {
    .name = "List_String",
    .namespace_name = "System.Collections.Generic",
    .full_name = "System.Collections.Generic.List`1<System.String>",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ListBase),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr, .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo DictStringIntTypeInfo = {
    .name = "Dictionary_String_Int32",
    .namespace_name = "System.Collections.Generic",
    .full_name = "System.Collections.Generic.Dictionary`2<System.String,System.Int32>",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(DictBase),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr, .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

class CollectionTest : public ::testing::Test {
protected:
    void SetUp() override { runtime_init(); }
    void TearDown() override { runtime_shutdown(); }
};

// ======================================================================
// List<int> tests
// ======================================================================

TEST_F(CollectionTest, ListInt_Create) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    ASSERT_NE(list, nullptr);
    EXPECT_EQ(list_get_count(list), 0);
    EXPECT_EQ(list_get_capacity(list), 0);
}

TEST_F(CollectionTest, ListInt_CreateWithCapacity) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 10);
    ASSERT_NE(list, nullptr);
    EXPECT_EQ(list_get_count(list), 0);
    EXPECT_GE(list_get_capacity(list), 10);
}

TEST_F(CollectionTest, ListInt_Add) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    Int32 val = 42;
    list_add(list, &val);
    EXPECT_EQ(list_get_count(list), 1);

    auto result = *static_cast<Int32*>(list_get_ref(list, 0));
    EXPECT_EQ(result, 42);
}

TEST_F(CollectionTest, ListInt_AddMultiple) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    for (Int32 i = 0; i < 10; i++) {
        list_add(list, &i);
    }
    EXPECT_EQ(list_get_count(list), 10);

    for (Int32 i = 0; i < 10; i++) {
        auto val = *static_cast<Int32*>(list_get_ref(list, i));
        EXPECT_EQ(val, i);
    }
}

TEST_F(CollectionTest, ListInt_Set) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    Int32 val = 10;
    list_add(list, &val);

    Int32 newVal = 99;
    list_set(list, 0, &newVal);

    auto result = *static_cast<Int32*>(list_get_ref(list, 0));
    EXPECT_EQ(result, 99);
}

TEST_F(CollectionTest, ListInt_RemoveAt) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    for (Int32 i = 10; i <= 30; i += 10) {
        list_add(list, &i);
    }
    EXPECT_EQ(list_get_count(list), 3);

    list_remove_at(list, 1); // Remove 20
    EXPECT_EQ(list_get_count(list), 2);
    EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, 0)), 10);
    EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, 1)), 30);
}

TEST_F(CollectionTest, ListInt_Contains) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    Int32 a = 10, b = 20, c = 30;
    list_add(list, &a);
    list_add(list, &b);

    EXPECT_TRUE(list_contains(list, &a));
    EXPECT_TRUE(list_contains(list, &b));
    EXPECT_FALSE(list_contains(list, &c));
}

TEST_F(CollectionTest, ListInt_IndexOf) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    Int32 vals[] = {10, 20, 30};
    for (auto& v : vals) list_add(list, &v);

    EXPECT_EQ(list_index_of(list, &vals[0]), 0);
    EXPECT_EQ(list_index_of(list, &vals[1]), 1);
    EXPECT_EQ(list_index_of(list, &vals[2]), 2);

    Int32 missing = 99;
    EXPECT_EQ(list_index_of(list, &missing), -1);
}

TEST_F(CollectionTest, ListInt_Insert) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    Int32 a = 10, b = 30, c = 20;
    list_add(list, &a);
    list_add(list, &b);

    list_insert(list, 1, &c); // Insert 20 between 10 and 30
    EXPECT_EQ(list_get_count(list), 3);
    EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, 0)), 10);
    EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, 1)), 20);
    EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, 2)), 30);
}

TEST_F(CollectionTest, ListInt_Remove) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    Int32 vals[] = {10, 20, 30};
    for (auto& v : vals) list_add(list, &v);

    Int32 target = 20;
    EXPECT_TRUE(list_remove(list, &target));
    EXPECT_EQ(list_get_count(list), 2);
    EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, 0)), 10);
    EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, 1)), 30);

    Int32 missing = 99;
    EXPECT_FALSE(list_remove(list, &missing));
}

TEST_F(CollectionTest, ListInt_Clear) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    Int32 vals[] = {10, 20, 30};
    for (auto& v : vals) list_add(list, &v);

    list_clear(list);
    EXPECT_EQ(list_get_count(list), 0);
}

TEST_F(CollectionTest, ListInt_Growth) {
    auto* list = list_create(&ListIntTypeInfo, &Int32ElemTypeInfo, 0);
    // Add many elements to trigger growth
    for (Int32 i = 0; i < 100; i++) {
        list_add(list, &i);
    }
    EXPECT_EQ(list_get_count(list), 100);

    // Verify all values preserved after growth
    for (Int32 i = 0; i < 100; i++) {
        EXPECT_EQ(*static_cast<Int32*>(list_get_ref(list, i)), i);
    }
}

// ======================================================================
// List<String*> tests (reference type)
// ======================================================================

TEST_F(CollectionTest, ListString_Create) {
    auto* list = list_create(&ListStringTypeInfo, &System_String_TypeInfo, 0);
    ASSERT_NE(list, nullptr);
    EXPECT_EQ(list_get_count(list), 0);
}

TEST_F(CollectionTest, ListString_AddAndGet) {
    auto* list = list_create(&ListStringTypeInfo, &System_String_TypeInfo, 0);

    String* s1 = string_literal("hello");
    String* s2 = string_literal("world");
    list_add(list, &s1);
    list_add(list, &s2);

    EXPECT_EQ(list_get_count(list), 2);

    auto* got1 = *static_cast<String**>(list_get_ref(list, 0));
    auto* got2 = *static_cast<String**>(list_get_ref(list, 1));
    EXPECT_EQ(got1, s1);
    EXPECT_EQ(got2, s2);
}

TEST_F(CollectionTest, ListString_GrowthPreservesPointers) {
    auto* list = list_create(&ListStringTypeInfo, &System_String_TypeInfo, 0);
    String* strings[20];
    for (int i = 0; i < 20; i++) {
        strings[i] = string_literal("test");
        list_add(list, &strings[i]);
    }
    EXPECT_EQ(list_get_count(list), 20);

    for (int i = 0; i < 20; i++) {
        auto* got = *static_cast<String**>(list_get_ref(list, i));
        EXPECT_EQ(got, strings[i]);
    }
}

// ======================================================================
// Dictionary<String, Int32> tests
// ======================================================================

TEST_F(CollectionTest, DictStringInt_Create) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);
    ASSERT_NE(dict, nullptr);
    EXPECT_EQ(dict_get_count(dict), 0);
}

TEST_F(CollectionTest, DictStringInt_SetAndGet) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    String* key = string_literal("answer");
    Int32 val = 42;
    dict_set(dict, &key, &val);

    EXPECT_EQ(dict_get_count(dict), 1);

    auto result = *static_cast<Int32*>(dict_get_ref(dict, &key));
    EXPECT_EQ(result, 42);
}

TEST_F(CollectionTest, DictStringInt_UpdateExisting) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    String* key = string_literal("key");
    Int32 val1 = 10, val2 = 20;
    dict_set(dict, &key, &val1);
    dict_set(dict, &key, &val2); // Update

    EXPECT_EQ(dict_get_count(dict), 1);
    EXPECT_EQ(*static_cast<Int32*>(dict_get_ref(dict, &key)), 20);
}

TEST_F(CollectionTest, DictStringInt_MultipleEntries) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    String* k1 = string_literal("alpha");
    String* k2 = string_literal("beta");
    String* k3 = string_literal("gamma");
    Int32 v1 = 1, v2 = 2, v3 = 3;

    dict_set(dict, &k1, &v1);
    dict_set(dict, &k2, &v2);
    dict_set(dict, &k3, &v3);

    EXPECT_EQ(dict_get_count(dict), 3);
    EXPECT_EQ(*static_cast<Int32*>(dict_get_ref(dict, &k1)), 1);
    EXPECT_EQ(*static_cast<Int32*>(dict_get_ref(dict, &k2)), 2);
    EXPECT_EQ(*static_cast<Int32*>(dict_get_ref(dict, &k3)), 3);
}

TEST_F(CollectionTest, DictStringInt_ContainsKey) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    String* k1 = string_literal("exists");
    String* k2 = string_literal("missing");
    Int32 val = 10;
    dict_set(dict, &k1, &val);

    EXPECT_TRUE(dict_contains_key(dict, &k1));
    EXPECT_FALSE(dict_contains_key(dict, &k2));
}

TEST_F(CollectionTest, DictStringInt_TryGetValue) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    String* key = string_literal("found");
    String* missing = string_literal("nope");
    Int32 val = 77;
    dict_set(dict, &key, &val);

    Int32 result = 0;
    EXPECT_TRUE(dict_try_get_value(dict, &key, &result));
    EXPECT_EQ(result, 77);

    Int32 result2 = -1;
    EXPECT_FALSE(dict_try_get_value(dict, &missing, &result2));
    EXPECT_EQ(result2, 0); // zeroed on miss
}

TEST_F(CollectionTest, DictStringInt_Remove) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    String* k1 = string_literal("a");
    String* k2 = string_literal("b");
    Int32 v1 = 1, v2 = 2;
    dict_set(dict, &k1, &v1);
    dict_set(dict, &k2, &v2);

    EXPECT_TRUE(dict_remove(dict, &k1));
    EXPECT_EQ(dict_get_count(dict), 1);
    EXPECT_FALSE(dict_contains_key(dict, &k1));
    EXPECT_TRUE(dict_contains_key(dict, &k2));

    EXPECT_FALSE(dict_remove(dict, &k1)); // Already removed
}

TEST_F(CollectionTest, DictStringInt_Clear) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    String* k1 = string_literal("x");
    String* k2 = string_literal("y");
    Int32 v1 = 1, v2 = 2;
    dict_set(dict, &k1, &v1);
    dict_set(dict, &k2, &v2);

    dict_clear(dict);
    EXPECT_EQ(dict_get_count(dict), 0);
    EXPECT_FALSE(dict_contains_key(dict, &k1));
    EXPECT_FALSE(dict_contains_key(dict, &k2));
}

TEST_F(CollectionTest, DictStringInt_Growth) {
    auto* dict = dict_create(&DictStringIntTypeInfo, &System_String_TypeInfo, &Int32ElemTypeInfo);

    // Add many entries to trigger resize
    char buf[32];
    for (Int32 i = 0; i < 50; i++) {
        std::snprintf(buf, sizeof(buf), "key_%d", i);
        String* key = string_literal(buf);
        dict_set(dict, &key, &i);
    }
    EXPECT_EQ(dict_get_count(dict), 50);

    // Verify all values accessible after resize
    for (Int32 i = 0; i < 50; i++) {
        std::snprintf(buf, sizeof(buf), "key_%d", i);
        String* key = string_literal(buf);
        EXPECT_TRUE(dict_contains_key(dict, &key));
        EXPECT_EQ(*static_cast<Int32*>(dict_get_ref(dict, &key)), i);
    }
}

// ======================================================================
// Dictionary<Int32, Int32> tests (value type keys)
// ======================================================================

static TypeInfo DictIntIntTypeInfo = {
    .name = "Dictionary_Int32_Int32",
    .namespace_name = "System.Collections.Generic",
    .full_name = "System.Collections.Generic.Dictionary`2<System.Int32,System.Int32>",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(DictBase),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr, .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TEST_F(CollectionTest, DictIntInt_SetAndGet) {
    auto* dict = dict_create(&DictIntIntTypeInfo, &Int32ElemTypeInfo, &Int32ElemTypeInfo);

    Int32 key = 10, val = 100;
    dict_set(dict, &key, &val);

    EXPECT_EQ(*static_cast<Int32*>(dict_get_ref(dict, &key)), 100);
}

TEST_F(CollectionTest, DictIntInt_ManyEntries) {
    auto* dict = dict_create(&DictIntIntTypeInfo, &Int32ElemTypeInfo, &Int32ElemTypeInfo);

    for (Int32 i = 0; i < 100; i++) {
        Int32 val = i * 10;
        dict_set(dict, &i, &val);
    }
    EXPECT_EQ(dict_get_count(dict), 100);

    for (Int32 i = 0; i < 100; i++) {
        EXPECT_EQ(*static_cast<Int32*>(dict_get_ref(dict, &i)), i * 10);
    }
}

// ======================================================================
// element_equals / element_hash tests
// ======================================================================

TEST_F(CollectionTest, ElementEquals_ValueType) {
    Int32 a = 42, b = 42, c = 99;
    EXPECT_TRUE(element_equals(&a, &b, &Int32ElemTypeInfo));
    EXPECT_FALSE(element_equals(&a, &c, &Int32ElemTypeInfo));
}

TEST_F(CollectionTest, ElementEquals_ReferenceType_SamePointer) {
    String* s = string_literal("test");
    EXPECT_TRUE(element_equals(&s, &s, &System_String_TypeInfo));
}

TEST_F(CollectionTest, ElementEquals_ReferenceType_NullHandling) {
    String* s = string_literal("test");
    String* n = nullptr;
    EXPECT_FALSE(element_equals(&s, &n, &System_String_TypeInfo));
    EXPECT_FALSE(element_equals(&n, &s, &System_String_TypeInfo));
    EXPECT_TRUE(element_equals(&n, &n, &System_String_TypeInfo));
}

TEST_F(CollectionTest, ElementHash_ValueType_Deterministic) {
    Int32 a = 42;
    Int32 h1 = element_hash(&a, &Int32ElemTypeInfo);
    Int32 h2 = element_hash(&a, &Int32ElemTypeInfo);
    EXPECT_EQ(h1, h2);
}

TEST_F(CollectionTest, ElementHash_ReferenceType_Null) {
    String* n = nullptr;
    EXPECT_EQ(element_hash(&n, &System_String_TypeInfo), 0);
}
