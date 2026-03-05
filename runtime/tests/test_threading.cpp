/**
 * CIL2CPP Runtime Tests - Threading (Monitor, Interlocked, Thread)
 */

#include <gtest/gtest.h>
#include <cil2cpp/threading.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/object.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/delegate.h>

#include <atomic>
#include <thread>

using namespace cil2cpp;

// Ensure GC is initialized before threading tests
class ThreadingTestEnvironment : public ::testing::Environment {
public:
    void SetUp() override { gc::init(); }
};
static auto* const g_threading_env =
    ::testing::AddGlobalTestEnvironment(new ThreadingTestEnvironment);

// Shared TypeInfo for test objects
static TypeInfo MonitorTestType = {
    .name = "MonitorTestObj",
    .namespace_name = "Tests",
    .full_name = "Tests.MonitorTestObj",
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
    .finalizer = nullptr,
};

// ===== Monitor Tests =====

TEST(MonitorTest, EnterExit_BasicLock) {
    auto* obj = object_alloc(&MonitorTestType);
    ASSERT_NE(obj, nullptr);

    monitor::enter(obj);
    monitor::exit(obj);
    // No deadlock = success
}

TEST(MonitorTest, ReentrantLock) {
    auto* obj = object_alloc(&MonitorTestType);
    ASSERT_NE(obj, nullptr);

    // Recursive lock: same thread locks twice
    monitor::enter(obj);
    monitor::enter(obj);
    monitor::exit(obj);
    monitor::exit(obj);
    // No deadlock = success
}

TEST(MonitorTest, ReliableEnter_SetsLockTaken) {
    auto* obj = object_alloc(&MonitorTestType);
    ASSERT_NE(obj, nullptr);

    bool lockTaken = false;
    monitor::reliable_enter(obj, &lockTaken);
    EXPECT_TRUE(lockTaken);
    monitor::exit(obj);
}

TEST(MonitorTest, MultiThread_NoRace) {
    auto* obj = object_alloc(&MonitorTestType);
    ASSERT_NE(obj, nullptr);

    std::atomic<int> counter{0};
    constexpr int iterations = 1000;

    auto worker = [&]() {
        gc::register_thread();
        for (int i = 0; i < iterations; i++) {
            monitor::enter(obj);
            counter.fetch_add(1, std::memory_order_relaxed);
            monitor::exit(obj);
        }
        gc::unregister_thread();
    };

    std::thread t1(worker);
    std::thread t2(worker);
    t1.join();
    t2.join();

    EXPECT_EQ(counter.load(), iterations * 2);
}

TEST(MonitorTest, WaitPulse_BasicSignal) {
    auto* obj = object_alloc(&MonitorTestType);
    ASSERT_NE(obj, nullptr);

    std::atomic<bool> signaled{false};

    std::thread t([&]() {
        gc::register_thread();
        monitor::enter(obj);
        signaled.store(true);
        monitor::pulse(obj);
        monitor::exit(obj);
        gc::unregister_thread();
    });

    monitor::enter(obj);
    while (!signaled.load()) {
        monitor::wait(obj, -1);
    }
    monitor::exit(obj);

    t.join();
    EXPECT_TRUE(signaled.load());
}

// ===== Interlocked Tests =====

TEST(InterlockedTest, Increment_ReturnsNewValue) {
    Int32 val = 0;
    Int32 result = interlocked::increment_i32(&val);
    EXPECT_EQ(result, 1);
    EXPECT_EQ(val, 1);
}

TEST(InterlockedTest, Decrement_ReturnsNewValue) {
    Int32 val = 5;
    Int32 result = interlocked::decrement_i32(&val);
    EXPECT_EQ(result, 4);
    EXPECT_EQ(val, 4);
}

TEST(InterlockedTest, Exchange_ReturnsOldValue) {
    Int32 val = 10;
    Int32 old = interlocked::exchange_i32(&val, 42);
    EXPECT_EQ(old, 10);
    EXPECT_EQ(val, 42);
}

TEST(InterlockedTest, CompareExchange_Success) {
    Int32 val = 1;
    Int32 old = interlocked::compare_exchange_i32(&val, 100, 1);
    EXPECT_EQ(old, 1);     // returned original value
    EXPECT_EQ(val, 100);   // swapped
}

TEST(InterlockedTest, CompareExchange_Failure) {
    Int32 val = 1;
    Int32 old = interlocked::compare_exchange_i32(&val, 100, 999);
    EXPECT_EQ(old, 1);     // returned original value
    EXPECT_EQ(val, 1);     // not swapped (comparand didn't match)
}

TEST(InterlockedTest, Add_ReturnsNewValue) {
    Int32 val = 10;
    Int32 result = interlocked::add_i32(&val, 5);
    EXPECT_EQ(result, 15);
    EXPECT_EQ(val, 15);
}

TEST(InterlockedTest, Increment64_ReturnsNewValue) {
    Int64 val = 0;
    Int64 result = interlocked::increment_i64(&val);
    EXPECT_EQ(result, 1);
    EXPECT_EQ(val, 1);
}

TEST(InterlockedTest, CompareExchange64_Success) {
    Int64 val = 42;
    Int64 old = interlocked::compare_exchange_i64(&val, 100, 42);
    EXPECT_EQ(old, 42);
    EXPECT_EQ(val, 100);
}

// ===== Thread Tests =====

// Simple thread-start function for testing
static std::atomic<int> g_thread_result{0};

static void test_thread_fn(Object* /*target*/) {
    g_thread_result.store(42);
}

static TypeInfo DelegateTestType = {
    .name = "ThreadStartDelegate",
    .namespace_name = "Tests",
    .full_name = "Tests.ThreadStartDelegate",
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
    .properties = nullptr, .property_count = 0,
    .finalizer = nullptr,
};

TEST(ThreadTest, CreateStartJoin_Completes) {
    g_thread_result.store(0);

    // Create a delegate manually
    auto* del = delegate_create(&DelegateTestType, nullptr,
        reinterpret_cast<void*>(&test_thread_fn));
    ASSERT_NE(del, nullptr);

    auto* t = thread::create(del);
    ASSERT_NE(t, nullptr);
    EXPECT_EQ(t->state, 0); // unstarted

    thread::start(t);
    thread::join(t);

    EXPECT_EQ(t->state, 2); // stopped
    EXPECT_EQ(g_thread_result.load(), 42);
}

TEST(ThreadTest, Sleep_DoesNotCrash) {
    thread::sleep(1);
    thread::sleep(0);
    // No crash = success
}

TEST(ThreadTest, IsAlive_ReflectsState) {
    auto* del = delegate_create(&DelegateTestType, nullptr,
        reinterpret_cast<void*>(&test_thread_fn));
    auto* t = thread::create(del);
    EXPECT_FALSE(thread::is_alive(t)); // unstarted

    thread::start(t);
    thread::join(t);
    EXPECT_FALSE(thread::is_alive(t)); // stopped
}

TEST(ThreadTest, GetManagedId_UniquePerThread) {
    auto* del1 = delegate_create(&DelegateTestType, nullptr,
        reinterpret_cast<void*>(&test_thread_fn));
    auto* del2 = delegate_create(&DelegateTestType, nullptr,
        reinterpret_cast<void*>(&test_thread_fn));

    auto* t1 = thread::create(del1);
    auto* t2 = thread::create(del2);

    EXPECT_NE(thread::get_managed_id(t1), thread::get_managed_id(t2));

    // Clean up
    thread::start(t1);
    thread::start(t2);
    thread::join(t1);
    thread::join(t2);
}
