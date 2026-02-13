/**
 * CIL2CPP Runtime Tests - Garbage Collector
 */

#include <gtest/gtest.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/object.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/array.h>

using namespace cil2cpp;

// Test type info
static TypeInfo TestType = {
    .name = "TestClass",
    .namespace_name = "Tests",
    .full_name = "Tests.TestClass",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + sizeof(int32_t),  // Object header + one int field
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

class GCTest : public ::testing::Test {
protected:
    void SetUp() override {
        gc::init();
    }

    void TearDown() override {
        gc::shutdown();
    }
};

TEST_F(GCTest, Init_SetsInitialStats) {
    auto stats = gc::get_stats();
    EXPECT_EQ(stats.collection_count, 0u);
}

TEST_F(GCTest, Alloc_ReturnsNonNull) {
    void* ptr = gc::alloc(sizeof(Object), &TestType);
    ASSERT_NE(ptr, nullptr);
}

TEST_F(GCTest, Alloc_ZeroInitializes) {
    // Allocate larger block to check zeroing
    void* ptr = gc::alloc(TestType.instance_size, &TestType);
    ASSERT_NE(ptr, nullptr);

    // Check that extra bytes after header are zero
    char* data = reinterpret_cast<char*>(ptr) + sizeof(Object);
    for (size_t i = 0; i < sizeof(int32_t); i++) {
        EXPECT_EQ(data[i], 0) << "Byte at offset " << (sizeof(Object) + i) << " not zero";
    }
}

TEST_F(GCTest, Alloc_SetsTypeInfo) {
    Object* obj = static_cast<Object*>(gc::alloc(TestType.instance_size, &TestType));
    ASSERT_NE(obj, nullptr);
    EXPECT_EQ(obj->__type_info, &TestType);
}

TEST_F(GCTest, Alloc_ClearsGCMark) {
    Object* obj = static_cast<Object*>(gc::alloc(TestType.instance_size, &TestType));
    ASSERT_NE(obj, nullptr);
    EXPECT_EQ(obj->__gc_mark, 0u);
}

TEST_F(GCTest, Alloc_IncreasesAllocatedSize) {
    auto before = gc::get_stats();
    gc::alloc(TestType.instance_size, &TestType);
    auto after = gc::get_stats();
    EXPECT_GT(after.total_allocated, before.total_allocated);
}

TEST_F(GCTest, Collect_IncrementsCollectionCount) {
    gc::collect();
    auto stats = gc::get_stats();
    EXPECT_EQ(stats.collection_count, 1u);
}

TEST_F(GCTest, Collect_FreesUnreachableObjects) {
    // Allocate an object but don't root it
    gc::alloc(TestType.instance_size, &TestType);

    auto before = gc::get_stats();
    gc::collect();
    auto after = gc::get_stats();

    // Object should have been freed
    EXPECT_GT(after.total_freed, before.total_freed);
}

TEST_F(GCTest, AddRoot_PreservesObjectDuringCollect) {
    Object* obj = static_cast<Object*>(gc::alloc(TestType.instance_size, &TestType));
    ASSERT_NE(obj, nullptr);

    // Root the object
    gc::add_root(reinterpret_cast<void**>(&obj));
    gc::collect();

    // Object should still be valid (not freed)
    EXPECT_EQ(obj->__type_info, &TestType);

    gc::remove_root(reinterpret_cast<void**>(&obj));
}

TEST_F(GCTest, RemoveRoot_AllowsCollection) {
    Object* obj = static_cast<Object*>(gc::alloc(TestType.instance_size, &TestType));
    ASSERT_NE(obj, nullptr);

    gc::add_root(reinterpret_cast<void**>(&obj));
    gc::remove_root(reinterpret_cast<void**>(&obj));

    // After removing root and nulling, object should be collectible
    obj = nullptr;
    auto before = gc::get_stats();
    gc::collect();
    auto after = gc::get_stats();
    EXPECT_GT(after.total_freed, before.total_freed);
}

TEST_F(GCTest, MultipleCollections_Work) {
    auto before = gc::get_stats();
    for (int i = 0; i < 10; i++) {
        gc::alloc(TestType.instance_size, &TestType);
        gc::collect();
    }
    auto after = gc::get_stats();
    // Note: gc::init() doesn't reset counters, so use delta
    EXPECT_EQ(after.collection_count - before.collection_count, 10u);
}

TEST_F(GCTest, GetStats_TracksAllocations) {
    auto before = gc::get_stats();
    size_t total = 0;
    for (int i = 0; i < 5; i++) {
        gc::alloc(TestType.instance_size, &TestType);
        total += TestType.instance_size;
    }
    auto after = gc::get_stats();
    EXPECT_EQ(after.total_allocated - before.total_allocated, total);
}

// Array allocation tests
static TypeInfo IntElementType = {
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

TEST_F(GCTest, AllocArray_ReturnsNonNull) {
    void* arr = gc::alloc_array(&IntElementType, 10);
    ASSERT_NE(arr, nullptr);
}

TEST_F(GCTest, AllocArray_SetsLength) {
    Array* arr = static_cast<Array*>(gc::alloc_array(&IntElementType, 10));
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(arr->length, 10);
}

TEST_F(GCTest, AllocArray_SetsElementType) {
    Array* arr = static_cast<Array*>(gc::alloc_array(&IntElementType, 10));
    ASSERT_NE(arr, nullptr);
    EXPECT_EQ(arr->element_type, &IntElementType);
}

// Finalizer test
static int g_finalizer_count = 0;
static void test_finalizer(Object*) {
    g_finalizer_count++;
}

static TypeInfo FinalizableType = {
    .name = "Finalizable",
    .namespace_name = "Tests",
    .full_name = "Tests.Finalizable",
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
    .finalizer = test_finalizer,
};

TEST_F(GCTest, Collect_CallsFinalizer) {
    g_finalizer_count = 0;
    gc::alloc(FinalizableType.instance_size, &FinalizableType);

    gc::collect();
    EXPECT_EQ(g_finalizer_count, 1);
}

TEST_F(GCTest, Shutdown_CallsFinalizer) {
    gc::shutdown();  // shutdown the one from SetUp

    gc::init();
    g_finalizer_count = 0;
    gc::alloc(FinalizableType.instance_size, &FinalizableType);

    gc::shutdown();
    EXPECT_EQ(g_finalizer_count, 1);

    // Re-init for TearDown
    gc::init();
}
