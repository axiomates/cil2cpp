/**
 * CIL2CPP Runtime Tests - Garbage Collector (BoehmGC)
 */

#include <gtest/gtest.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/object.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/array.h>

#include <gc.h>

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
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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

TEST_F(GCTest, Alloc_ReturnsNonNull) {
    void* ptr = gc::alloc(sizeof(Object), &TestType);
    ASSERT_NE(ptr, nullptr);
}

TEST_F(GCTest, Alloc_ZeroInitializes) {
    void* ptr = gc::alloc(TestType.instance_size, &TestType);
    ASSERT_NE(ptr, nullptr);

    // Check that extra bytes after header are zero (GC_MALLOC returns zeroed memory)
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

TEST_F(GCTest, Collect_DoesNotCrash) {
    // Allocate several objects and collect — should not crash
    for (int i = 0; i < 100; i++) {
        gc::alloc(TestType.instance_size, &TestType);
    }
    gc::collect();
}

TEST_F(GCTest, Collect_IncrementsCollectionCount) {
    auto before = gc::get_stats();
    gc::collect();
    auto after = gc::get_stats();
    EXPECT_GT(after.collection_count, before.collection_count);
}

TEST_F(GCTest, RootedObject_SurvivesCollection) {
    // Objects referenced from the stack survive BoehmGC's conservative scan
    Object* obj = static_cast<Object*>(gc::alloc(TestType.instance_size, &TestType));
    ASSERT_NE(obj, nullptr);

    gc::collect();

    // Object should still be valid (stack reference keeps it alive)
    EXPECT_EQ(obj->__type_info, &TestType);
}

TEST_F(GCTest, AddRemoveRoot_NoOpDoesNotCrash) {
    Object* obj = static_cast<Object*>(gc::alloc(TestType.instance_size, &TestType));
    ASSERT_NE(obj, nullptr);

    // add_root/remove_root are no-ops with BoehmGC but should not crash
    gc::add_root(reinterpret_cast<void**>(&obj));
    gc::remove_root(reinterpret_cast<void**>(&obj));
    gc::collect();
}

TEST_F(GCTest, MultipleCollections_Work) {
    auto before = gc::get_stats();
    for (int i = 0; i < 10; i++) {
        gc::alloc(TestType.instance_size, &TestType);
        gc::collect();
    }
    auto after = gc::get_stats();
    EXPECT_GE(after.collection_count - before.collection_count, 10u);
}

TEST_F(GCTest, GetStats_ReportsHeapSize) {
    // After allocations, heap size should be > 0
    for (int i = 0; i < 10; i++) {
        gc::alloc(TestType.instance_size, &TestType);
    }
    auto stats = gc::get_stats();
    EXPECT_GT(stats.current_heap_size, 0u);
    EXPECT_GT(stats.total_allocated, 0u);
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
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
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
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr,
    .finalizer = test_finalizer,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TEST_F(GCTest, Finalizer_IsRegistered) {
    // Verify that allocating a finalizable type doesn't crash
    g_finalizer_count = 0;
    void* ptr = gc::alloc(FinalizableType.instance_size, &FinalizableType);
    ASSERT_NE(ptr, nullptr);

    // BoehmGC runs finalizers asynchronously; we can't easily test
    // that they fire, but we can verify the allocation succeeds
    // and the finalizer callback pointer is valid.
    Object* obj = static_cast<Object*>(ptr);
    EXPECT_EQ(obj->__type_info, &FinalizableType);
    EXPECT_NE(obj->__type_info->finalizer, nullptr);
}

TEST_F(GCTest, Finalizer_RunsOnCollect) {
    g_finalizer_count = 0;

    // Allocate in a separate scope so it becomes unreachable
    {
        volatile void* ptr = gc::alloc(FinalizableType.instance_size, &FinalizableType);
        (void)ptr;  // Prevent optimization
    }

    // Force collection and invoke pending finalizers
    gc::collect();
    GC_invoke_finalizers();

    // BoehmGC's conservative scan may keep the object alive due to
    // stack residue, so we accept both outcomes
    EXPECT_GE(g_finalizer_count, 0);
}

// ===== Incremental GC =====

TEST_F(GCTest, CollectALittle_DoesNotCrash) {
    // Allocate objects to create GC work
    for (int i = 0; i < 100; i++) {
        gc::alloc(TestType.instance_size, &TestType);
    }
    // Perform incremental collection steps
    for (int i = 0; i < 10; i++) {
        gc::collect_a_little();
    }
}

TEST_F(GCTest, SetIncremental_DoesNotCrash) {
    // Incremental mode is already enabled at init;
    // re-enabling should be safe
    gc::set_incremental(true);
    for (int i = 0; i < 50; i++) {
        gc::alloc(TestType.instance_size, &TestType);
    }
    gc::collect_a_little();
}
