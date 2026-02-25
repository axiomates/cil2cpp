/**
 * CIL2CPP Runtime Tests - Async (ThreadPool, Task combinators)
 */

#include <gtest/gtest.h>
#include <cil2cpp/task.h>
#include <cil2cpp/threadpool.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/array.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/delegate.h>

#include <atomic>
#include <chrono>
#include <thread>

using namespace cil2cpp;

// GC must be initialized for allocation
class AsyncTestEnvironment : public ::testing::Environment {
public:
    void SetUp() override {
        gc::init();
        threadpool::init(4);
    }
    void TearDown() override {
        threadpool::shutdown();
    }
};
static auto* const g_async_env =
    ::testing::AddGlobalTestEnvironment(new AsyncTestEnvironment);

// ===== ThreadPool Tests =====

TEST(ThreadPoolTest, IsInitialized) {
    EXPECT_TRUE(threadpool::is_initialized());
}

TEST(ThreadPoolTest, QueueWork_Executes) {
    std::atomic<int> result{0};
    auto callback = [](void* state) {
        auto* r = static_cast<std::atomic<int>*>(state);
        r->store(42);
    };
    threadpool::queue_work(callback, &result);
    // Wait for execution
    for (int i = 0; i < 1000 && result.load() == 0; i++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_EQ(result.load(), 42);
}

TEST(ThreadPoolTest, QueueWork_MultipleConcurrent) {
    std::atomic<int> counter{0};
    constexpr int num_items = 100;

    auto callback = [](void* state) {
        auto* c = static_cast<std::atomic<int>*>(state);
        c->fetch_add(1);
    };
    for (int i = 0; i < num_items; i++) {
        threadpool::queue_work(callback, &counter);
    }
    // Wait for all to complete
    for (int i = 0; i < 5000 && counter.load() < num_items; i++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_EQ(counter.load(), num_items);
}

// ===== Task Creation Tests =====

TEST(TaskTest, CreateCompleted_IsComplete) {
    auto* t = task_create_completed();
    ASSERT_NE(t, nullptr);
    EXPECT_TRUE(task_is_completed(t));
    EXPECT_EQ(t->f_status, 1);
}

TEST(TaskTest, GetCompleted_ReturnsSameInstance) {
    auto* t1 = task_get_completed();
    auto* t2 = task_get_completed();
    EXPECT_EQ(t1, t2);
    EXPECT_TRUE(task_is_completed(t1));
}

TEST(TaskTest, CreatePending_NotComplete) {
    auto* t = task_create_pending();
    ASSERT_NE(t, nullptr);
    EXPECT_FALSE(task_is_completed(t));
    EXPECT_EQ(t->f_status, 0);
}

TEST(TaskTest, Complete_SetsStatus) {
    auto* t = task_create_pending();
    task_complete(t);
    EXPECT_TRUE(task_is_completed(t));
    EXPECT_EQ(t->f_status, 1);
}

TEST(TaskTest, Fault_SetsStatusAndException) {
    auto* t = task_create_pending();
    auto* ex = static_cast<Exception*>(gc::alloc(sizeof(Exception), nullptr));
    task_fault(t, ex);
    EXPECT_TRUE(task_is_completed(t));
    EXPECT_EQ(t->f_status, 2);
    EXPECT_EQ(t->f_exception, ex);
}

// ===== Continuation Tests =====

TEST(TaskTest, Continuation_RunsOnComplete) {
    auto* t = task_create_pending();
    std::atomic<int> result{0};

    task_add_continuation(t, [](void* state) {
        auto* r = static_cast<std::atomic<int>*>(state);
        r->store(99);
    }, &result);

    EXPECT_EQ(result.load(), 0); // Not yet triggered
    task_complete(t);
    // Continuation runs on the completing thread synchronously
    // or on the calling thread if already complete
    for (int i = 0; i < 1000 && result.load() == 0; i++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_EQ(result.load(), 99);
}

TEST(TaskTest, Continuation_RunsImmediatelyIfAlreadyComplete) {
    auto* t = task_create_completed();
    std::atomic<int> result{0};

    task_add_continuation(t, [](void* state) {
        auto* r = static_cast<std::atomic<int>*>(state);
        r->store(77);
    }, &result);

    EXPECT_EQ(result.load(), 77); // Should have run immediately
}

TEST(TaskTest, MultipleContinuations) {
    auto* t = task_create_pending();
    std::atomic<int> counter{0};

    for (int i = 0; i < 5; i++) {
        task_add_continuation(t, [](void* state) {
            auto* c = static_cast<std::atomic<int>*>(state);
            c->fetch_add(1);
        }, &counter);
    }

    task_complete(t);
    for (int i = 0; i < 1000 && counter.load() < 5; i++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_EQ(counter.load(), 5);
}

// ===== Wait Tests =====

TEST(TaskTest, Wait_CompletedTask_ReturnsImmediately) {
    auto* t = task_create_completed();
    task_wait(t);
    // No hang = success
}

TEST(TaskTest, Wait_BlocksUntilComplete) {
    auto* t = task_create_pending();

    // Complete from another thread
    std::thread([t]() {
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
        task_complete(t);
    }).detach();

    task_wait(t);
    EXPECT_TRUE(task_is_completed(t));
}

// ===== Combinator Tests =====

static TypeInfo TaskArrayTypeInfo = {
    .name = "Task[]",
    .namespace_name = "",
    .full_name = "Task[]",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Array),
    .element_size = sizeof(void*),
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .finalizer = nullptr,
};

TEST(TaskTest, WhenAll_AllComplete_Completes) {
    auto* t1 = task_create_completed();
    auto* t2 = task_create_completed();
    auto* t3 = task_create_completed();

    auto* tasks = array_create(&TaskArrayTypeInfo, 3);
    auto** data = static_cast<Task**>(array_data(tasks));
    data[0] = t1;
    data[1] = t2;
    data[2] = t3;

    auto* result = task_when_all(tasks);
    ASSERT_NE(result, nullptr);
    task_wait(result);
    EXPECT_TRUE(task_is_completed(result));
}

TEST(TaskTest, WhenAll_PendingTasks_CompletesWhenAllDone) {
    auto* t1 = task_create_pending();
    auto* t2 = task_create_pending();

    auto* tasks = array_create(&TaskArrayTypeInfo, 2);
    auto** data = static_cast<Task**>(array_data(tasks));
    data[0] = t1;
    data[1] = t2;

    auto* result = task_when_all(tasks);
    EXPECT_FALSE(task_is_completed(result));

    task_complete(t1);
    // Should still be incomplete — t2 is pending
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
    EXPECT_FALSE(task_is_completed(result));

    task_complete(t2);
    // Now should be complete
    for (int i = 0; i < 1000 && !task_is_completed(result); i++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_TRUE(task_is_completed(result));
}

TEST(TaskTest, WhenAll_EmptyArray_CompletesImmediately) {
    auto* tasks = array_create(&TaskArrayTypeInfo, 0);
    auto* result = task_when_all(tasks);
    EXPECT_TRUE(task_is_completed(result));
}

TEST(TaskTest, WhenAny_FirstComplete_Completes) {
    auto* t1 = task_create_pending();
    auto* t2 = task_create_pending();

    auto* tasks = array_create(&TaskArrayTypeInfo, 2);
    auto** data = static_cast<Task**>(array_data(tasks));
    data[0] = t1;
    data[1] = t2;

    auto* result = task_when_any(tasks);
    EXPECT_FALSE(task_is_completed(result));

    task_complete(t1);
    for (int i = 0; i < 1000 && !task_is_completed(result); i++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_TRUE(task_is_completed(result));
    // t2 still pending is fine
}

TEST(TaskTest, WhenAny_AllComplete_StillCompletes) {
    auto* t1 = task_create_completed();
    auto* t2 = task_create_completed();

    auto* tasks = array_create(&TaskArrayTypeInfo, 2);
    auto** data = static_cast<Task**>(array_data(tasks));
    data[0] = t1;
    data[1] = t2;

    auto* result = task_when_any(tasks);
    EXPECT_TRUE(task_is_completed(result));
}

// ===== Delay Tests =====

TEST(TaskTest, Delay_Zero_CompletesImmediately) {
    auto* t = task_delay(0);
    EXPECT_TRUE(task_is_completed(t));
}

TEST(TaskTest, Delay_Negative_CompletesImmediately) {
    auto* t = task_delay(-1);
    EXPECT_TRUE(task_is_completed(t));
}

TEST(TaskTest, Delay_Positive_CompletesAfterDelay) {
    auto start = std::chrono::steady_clock::now();
    auto* t = task_delay(100); // 100ms
    EXPECT_FALSE(task_is_completed(t));

    task_wait(t);
    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - start).count();

    EXPECT_TRUE(task_is_completed(t));
    EXPECT_GE(elapsed, 80); // Allow 20ms tolerance
}

// ===== Task.Run Tests =====

static std::atomic<int> g_run_result{0};

static void run_test_static_fn() {
    g_run_result.store(123);
}

static TypeInfo RunDelegateTypeInfo = {
    .name = "Action",
    .namespace_name = "System",
    .full_name = "System.Action",
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
    .finalizer = nullptr,
};

TEST(TaskTest, Run_ExecutesDelegateOnPool) {
    g_run_result.store(0);

    auto* del = delegate_create(&RunDelegateTypeInfo, nullptr,
        reinterpret_cast<void*>(&run_test_static_fn));
    ASSERT_NE(del, nullptr);

    auto* t = task_run(static_cast<Object*>(del));
    ASSERT_NE(t, nullptr);

    task_wait(t);
    EXPECT_TRUE(task_is_completed(t));
    EXPECT_EQ(g_run_result.load(), 123);
}

// ===== Singleton Thread-Safety Tests =====

TEST(TaskTest, GetCompleted_ThreadSafe_SameInstance) {
    constexpr int num_threads = 8;
    std::vector<Task*> results(num_threads, nullptr);
    std::vector<std::thread> threads;

    for (int i = 0; i < num_threads; i++) {
        threads.emplace_back([&results, i]() {
            gc::register_thread();
            results[i] = task_get_completed();
            gc::unregister_thread();
        });
    }
    for (auto& th : threads) th.join();

    // All threads must get the exact same pointer
    for (int i = 1; i < num_threads; i++) {
        EXPECT_EQ(results[0], results[i]);
    }
    EXPECT_TRUE(task_is_completed(results[0]));
}

// ===== Thread-Safety Tests =====

TEST(TaskTest, ConcurrentContinuations_ThreadSafe) {
    auto* t = task_create_pending();
    std::atomic<int> counter{0};
    constexpr int num_threads = 8;
    constexpr int conts_per_thread = 50;

    // Register continuations from multiple threads simultaneously
    std::vector<std::thread> threads;
    for (int i = 0; i < num_threads; i++) {
        threads.emplace_back([t, &counter]() {
            gc::register_thread();
            for (int j = 0; j < conts_per_thread; j++) {
                task_add_continuation(t, [](void* state) {
                    auto* c = static_cast<std::atomic<int>*>(state);
                    c->fetch_add(1);
                }, &counter);
            }
            gc::unregister_thread();
        });
    }
    for (auto& th : threads) th.join();

    // Now complete — all continuations should fire
    task_complete(t);
    for (int i = 0; i < 5000 && counter.load() < num_threads * conts_per_thread; i++) {
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    EXPECT_EQ(counter.load(), num_threads * conts_per_thread);
}
