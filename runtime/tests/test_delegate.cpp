/**
 * CIL2CPP Runtime Tests - Delegate
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

static TypeInfo DelegateTypeInfo = {
    .name = "TestDelegate",
    .namespace_name = "Tests",
    .full_name = "Tests.TestDelegate",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Delegate),
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

// Test target functions
static int32_t test_static_add(int32_t a, int32_t b) {
    return a + b;
}

static int32_t test_static_mul(int32_t a, int32_t b) {
    return a * b;
}

class DelegateTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== delegate_create =====

TEST_F(DelegateTest, Create_ReturnsNonNull) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    ASSERT_NE(del, nullptr);
}

TEST_F(DelegateTest, Create_SetsTypeInfo) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    EXPECT_EQ(del->__type_info, &DelegateTypeInfo);
}

TEST_F(DelegateTest, Create_StaticDelegate_NullTarget) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    EXPECT_EQ(del->target, nullptr);
    EXPECT_EQ(del->method_ptr, (void*)test_static_add);
}

TEST_F(DelegateTest, Create_InstanceDelegate_HasTarget) {
    auto* obj = object_alloc(&DelegateTypeInfo);
    auto* del = delegate_create(&DelegateTypeInfo, obj, (void*)test_static_add);
    EXPECT_EQ(del->target, obj);
    EXPECT_EQ(del->method_ptr, (void*)test_static_add);
}

TEST_F(DelegateTest, Invoke_StaticDelegate) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto fn = (int32_t(*)(int32_t, int32_t))del->method_ptr;
    EXPECT_EQ(fn(3, 4), 7);
}

// ===== delegate_combine =====

TEST_F(DelegateTest, Combine_NullFirst_ReturnsSecond) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* result = delegate_combine(nullptr, (Object*)del);
    EXPECT_EQ(result, (Object*)del);
}

TEST_F(DelegateTest, Combine_NullSecond_ReturnsFirst) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* result = delegate_combine((Object*)del, nullptr);
    EXPECT_EQ(result, (Object*)del);
}

TEST_F(DelegateTest, Combine_BothValid_ReturnsSecond) {
    auto* del1 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* del2 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_mul);
    auto* result = delegate_combine((Object*)del1, (Object*)del2);
    EXPECT_EQ(result, (Object*)del2);
}

// ===== delegate_remove =====

TEST_F(DelegateTest, Remove_Matching_ReturnsNull) {
    auto* del1 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* del2 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* result = delegate_remove((Object*)del1, (Object*)del2);
    EXPECT_EQ(result, nullptr);
}

TEST_F(DelegateTest, Remove_NotMatching_ReturnsSource) {
    auto* del1 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* del2 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_mul);
    auto* result = delegate_remove((Object*)del1, (Object*)del2);
    EXPECT_EQ(result, (Object*)del1);
}

TEST_F(DelegateTest, Remove_NullSource_ReturnsNull) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* result = delegate_remove(nullptr, (Object*)del);
    EXPECT_EQ(result, nullptr);
}

TEST_F(DelegateTest, Remove_NullValue_ReturnsSource) {
    auto* del = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* result = delegate_remove((Object*)del, nullptr);
    EXPECT_EQ(result, (Object*)del);
}

// ===== Invoke through function pointer =====

static int32_t test_static_sub(int32_t a, int32_t b) {
    return a - b;
}

TEST_F(DelegateTest, Invoke_DifferentFunctions) {
    auto* addDel = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* mulDel = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_mul);
    auto* subDel = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_sub);

    auto addFn = (int32_t(*)(int32_t, int32_t))addDel->method_ptr;
    auto mulFn = (int32_t(*)(int32_t, int32_t))mulDel->method_ptr;
    auto subFn = (int32_t(*)(int32_t, int32_t))subDel->method_ptr;

    EXPECT_EQ(addFn(10, 3), 13);
    EXPECT_EQ(mulFn(10, 3), 30);
    EXPECT_EQ(subFn(10, 3), 7);
}

// ===== Multiple sequential creates =====

TEST_F(DelegateTest, Create_Multiple_AllDistinct) {
    auto* d1 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* d2 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);
    auto* d3 = delegate_create(&DelegateTypeInfo, nullptr, (void*)test_static_add);

    EXPECT_NE(d1, d2);
    EXPECT_NE(d2, d3);
    EXPECT_NE(d1, d3);
}

// ===== Combine null+null =====

TEST_F(DelegateTest, Combine_BothNull_ReturnsNull) {
    auto* result = delegate_combine(nullptr, nullptr);
    EXPECT_EQ(result, nullptr);
}

// ===== Remove both null =====

TEST_F(DelegateTest, Remove_BothNull_ReturnsNull) {
    auto* result = delegate_remove(nullptr, nullptr);
    EXPECT_EQ(result, nullptr);
}

// ===== Instance delegate with target =====

static TypeInfo TargetTypeInfo = {
    .name = "Target",
    .namespace_name = "Tests",
    .full_name = "Tests.Target",
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

TEST_F(DelegateTest, InstanceDelegate_TargetPreserved) {
    auto* target = object_alloc(&TargetTypeInfo);
    auto* del = delegate_create(&DelegateTypeInfo, target, (void*)test_static_add);

    EXPECT_EQ(del->target, target);
    EXPECT_NE(del->target, nullptr);
}

TEST_F(DelegateTest, Remove_SameMethodPtr_Matches) {
    auto* target = object_alloc(&TargetTypeInfo);
    auto* del1 = delegate_create(&DelegateTypeInfo, target, (void*)test_static_add);
    auto* del2 = delegate_create(&DelegateTypeInfo, target, (void*)test_static_add);

    // Same method_ptr → match → remove returns null
    auto* result = delegate_remove((Object*)del1, (Object*)del2);
    EXPECT_EQ(result, nullptr);
}
