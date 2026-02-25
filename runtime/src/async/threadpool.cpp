/**
 * CIL2CPP Runtime - Thread Pool Implementation
 * Fixed-size pool with work queue protected by mutex + condition variable.
 */

#include <cil2cpp/threadpool.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/delegate.h>

#include <thread>
#include <mutex>
#include <condition_variable>
#include <queue>
#include <vector>
#include <functional>

namespace cil2cpp::threadpool {

struct WorkItem {
    void (*func)(void*);
    void* state;
};

static std::vector<std::thread> s_workers;
static std::queue<WorkItem> s_queue;
static std::mutex s_mutex;
static std::condition_variable s_cv;
static bool s_shutdown = false;
static bool s_initialized = false;

static void worker_loop() {
    gc::register_thread();

    while (true) {
        WorkItem item;
        {
            std::unique_lock<std::mutex> lock(s_mutex);
            s_cv.wait(lock, [] { return s_shutdown || !s_queue.empty(); });
            if (s_shutdown && s_queue.empty()) break;
            item = s_queue.front();
            s_queue.pop();
        }
        // Execute the work item
        item.func(item.state);
    }

    gc::unregister_thread();
}

void init(int num_threads) {
    if (s_initialized) return;

    if (num_threads <= 0) {
        num_threads = static_cast<int>(std::thread::hardware_concurrency());
        if (num_threads <= 0) num_threads = 4; // fallback
    }

    s_shutdown = false;
    s_workers.reserve(num_threads);
    for (int i = 0; i < num_threads; i++) {
        s_workers.emplace_back(worker_loop);
    }
    s_initialized = true;
}

void shutdown() {
    if (!s_initialized) return;

    {
        std::lock_guard<std::mutex> lock(s_mutex);
        s_shutdown = true;
    }
    s_cv.notify_all();

    for (auto& w : s_workers) {
        if (w.joinable()) w.join();
    }
    s_workers.clear();
    s_initialized = false;
}

bool is_initialized() {
    return s_initialized;
}

void queue_work(void (*func)(void*), void* state) {
    {
        std::lock_guard<std::mutex> lock(s_mutex);
        if (s_shutdown) {
            // Pool shut down — run synchronously to avoid silent data loss.
            // Unlock before calling func to avoid deadlock if func re-enters.
        } else {
            s_queue.push({func, state});
            s_cv.notify_one();
            return;
        }
    }
    // Fallback: synchronous execution after shutdown
    func(state);
}

// Delegate invocation trampoline
static void delegate_trampoline(void* raw) {
    auto* del = static_cast<Delegate*>(raw);
    if (!del || !del->method_ptr) return;

    // Invoke: void delegate with no args
    if (del->target) {
        // Instance delegate: fn(target)
        using InstanceFn = void(*)(Object*);
        auto fn = reinterpret_cast<InstanceFn>(del->method_ptr);
        fn(del->target);
    } else {
        // Static delegate: fn()
        using StaticFn = void(*)();
        auto fn = reinterpret_cast<StaticFn>(del->method_ptr);
        fn();
    }
}

void queue_delegate(Delegate* del) {
    queue_work(delegate_trampoline, del);
}

} // namespace cil2cpp::threadpool
