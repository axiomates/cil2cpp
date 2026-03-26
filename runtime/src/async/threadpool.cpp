/**
 * CIL2CPP Runtime - Thread Pool Implementation
 * Dynamic worker pool with hill-climbing thread count adjustment.
 *
 * Architecture:
 *   - WorkerState per thread: tracks active/should_exit flags
 *   - Metrics: atomic counters for completions, active workers, queue depth
 *   - Gate thread: runs hill-climbing algorithm every 500ms, injects/retires workers
 *   - Global FIFO queue protected by mutex + condition_variable
 */

#include <cil2cpp/threadpool.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/delegate.h>

#include "hill_climbing.h"

#include <thread>
#include <mutex>
#include <condition_variable>
#include <queue>
#include <vector>
#include <atomic>
#include <chrono>
#include <algorithm>

namespace cil2cpp::threadpool {

// ===== Internal types =====

struct WorkItem {
    void (*func)(void*);
    void* state;
};

struct WorkerState {
    std::thread thread;
    std::atomic<bool> active{false};       // Currently executing a work item
    std::atomic<bool> should_exit{false};   // Marked for retirement by gate thread
    std::atomic<bool> exited{false};        // Thread has finished running
};

struct Metrics {
    std::atomic<int32_t> total_workers{0};
    std::atomic<int32_t> active_workers{0};
    std::atomic<int32_t> queued_items{0};
    std::atomic<int64_t> total_completions{0};
    std::atomic<int64_t> last_progress_time{0}; // Milliseconds since init
    int32_t min_threads = 0;
    int32_t max_threads = 1023;
};

// ===== Global state =====

static std::vector<WorkerState*> s_workers;
static std::mutex s_workers_mutex;          // Protects s_workers vector
static std::queue<WorkItem> s_queue;
static std::mutex s_mutex;                  // Protects s_queue + s_shutdown
static std::condition_variable s_cv;
static bool s_shutdown = false;
static bool s_initialized = false;
static std::once_flag s_init_flag;
static Metrics s_metrics;

// Gate thread
static std::thread s_gate_thread;
static std::atomic<bool> s_gate_running{false};

// Time reference for progress tracking
static std::chrono::steady_clock::time_point s_init_time;

static constexpr int kDefaultThreadCount = 4;
static constexpr int kDefaultMaxThreads = 1023;

// ===== Internal helpers =====

static int64_t elapsed_ms() {
    auto now = std::chrono::steady_clock::now();
    return std::chrono::duration_cast<std::chrono::milliseconds>(now - s_init_time).count();
}

// Forward declaration
static void worker_loop(WorkerState* self);

static WorkerState* create_worker() {
    auto* state = new WorkerState();
    state->thread = std::thread(worker_loop, state);
    return state;
}

static void inject_worker() {
    std::lock_guard<std::mutex> lock(s_workers_mutex);
    if (s_metrics.total_workers.load(std::memory_order_relaxed) >= s_metrics.max_threads) return;
    auto* state = create_worker();
    s_workers.push_back(state);
    s_metrics.total_workers.fetch_add(1, std::memory_order_relaxed);
}

static void retire_one_idle_worker() {
    std::lock_guard<std::mutex> lock(s_workers_mutex);
    if (s_metrics.total_workers.load(std::memory_order_relaxed) <= s_metrics.min_threads) return;
    for (auto* w : s_workers) {
        if (!w->active.load(std::memory_order_relaxed) &&
            !w->should_exit.load(std::memory_order_relaxed)) {
            w->should_exit.store(true, std::memory_order_relaxed);
            s_cv.notify_all(); // Wake idle workers to check should_exit
            return;
        }
    }
}

static void cleanup_retired_workers() {
    std::lock_guard<std::mutex> lock(s_workers_mutex);
    auto it = s_workers.begin();
    while (it != s_workers.end()) {
        auto* w = *it;
        if (w->exited.load(std::memory_order_relaxed)) {
            if (w->thread.joinable()) w->thread.join();
            delete w;
            it = s_workers.erase(it);
        } else {
            ++it;
        }
    }
}

// ===== Worker loop =====

static void worker_loop(WorkerState* self) {
    gc::register_thread();

    while (true) {
        WorkItem item;
        {
            std::unique_lock<std::mutex> lock(s_mutex);
            s_cv.wait(lock, [self] {
                return s_shutdown || !s_queue.empty() ||
                       self->should_exit.load(std::memory_order_relaxed);
            });

            // Exit conditions: shutdown with empty queue, or marked for retirement with empty queue
            if (s_queue.empty() &&
                (s_shutdown || self->should_exit.load(std::memory_order_relaxed))) {
                break;
            }

            item = s_queue.front();
            s_queue.pop();
            s_metrics.queued_items.fetch_sub(1, std::memory_order_relaxed);
        }

        // Track active state
        self->active.store(true, std::memory_order_relaxed);
        s_metrics.active_workers.fetch_add(1, std::memory_order_relaxed);

        // Execute work item
        item.func(item.state);

        // Update metrics
        self->active.store(false, std::memory_order_relaxed);
        s_metrics.active_workers.fetch_sub(1, std::memory_order_relaxed);
        s_metrics.total_completions.fetch_add(1, std::memory_order_relaxed);
    }

    s_metrics.total_workers.fetch_sub(1, std::memory_order_relaxed);
    self->exited.store(true, std::memory_order_relaxed);
    gc::unregister_thread();
}

// ===== Gate thread =====

static void gate_thread_func() {
    hill_climbing::State hc;
    hill_climbing::init(hc,
        s_metrics.total_workers.load(std::memory_order_relaxed),
        s_metrics.min_threads,
        s_metrics.max_threads);

    while (s_gate_running.load(std::memory_order_relaxed)) {
        std::this_thread::sleep_for(
            std::chrono::milliseconds(hill_climbing::State::kSampleIntervalMs));

        if (s_shutdown) break;

        int adj = hill_climbing::update(hc,
            s_metrics.total_completions.load(std::memory_order_relaxed),
            s_metrics.active_workers.load(std::memory_order_relaxed),
            s_metrics.queued_items.load(std::memory_order_relaxed));

        if (adj > 0) {
            inject_worker();
        } else if (adj < 0) {
            retire_one_idle_worker();
        }

        // Periodically clean up retired workers
        cleanup_retired_workers();
    }
}

// ===== Public API =====

void init(int num_threads) {
    std::call_once(s_init_flag, [num_threads]() mutable {
        s_init_time = std::chrono::steady_clock::now();

        if (num_threads <= 0) {
            num_threads = static_cast<int>(std::thread::hardware_concurrency());
            if (num_threads <= 0) num_threads = kDefaultThreadCount;
        }

        s_metrics.min_threads = num_threads;
        s_metrics.max_threads = kDefaultMaxThreads;
        s_shutdown = false;

        // Create initial workers
        {
            std::lock_guard<std::mutex> lock(s_workers_mutex);
            s_workers.reserve(num_threads * 2); // Room for growth
            for (int i = 0; i < num_threads; i++) {
                auto* state = create_worker();
                s_workers.push_back(state);
            }
            s_metrics.total_workers.store(num_threads, std::memory_order_relaxed);
        }

        // Start gate thread for hill-climbing
        s_gate_running.store(true, std::memory_order_relaxed);
        s_gate_thread = std::thread(gate_thread_func);

        s_initialized = true;
    });
}

void shutdown() {
    if (!s_initialized) return;

    // Stop gate thread first
    s_gate_running.store(false, std::memory_order_relaxed);
    if (s_gate_thread.joinable()) s_gate_thread.join();

    // Signal all workers to stop
    {
        std::lock_guard<std::mutex> lock(s_mutex);
        s_shutdown = true;
    }
    s_cv.notify_all();

    // Join and clean up all workers
    {
        std::lock_guard<std::mutex> lock(s_workers_mutex);
        for (auto* w : s_workers) {
            if (w->thread.joinable()) w->thread.join();
            delete w;
        }
        s_workers.clear();
    }

    // Reset metrics
    s_metrics.total_workers.store(0, std::memory_order_relaxed);
    s_metrics.active_workers.store(0, std::memory_order_relaxed);
    s_metrics.queued_items.store(0, std::memory_order_relaxed);
    s_metrics.total_completions.store(0, std::memory_order_relaxed);
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
        } else {
            s_queue.push({func, state});
            s_metrics.queued_items.fetch_add(1, std::memory_order_relaxed);
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

// ===== Dynamic thread management =====

void set_min_threads(int count) {
    if (count < 1) count = 1;
    s_metrics.min_threads = count;
}

void set_max_threads(int count) {
    if (count < s_metrics.min_threads) count = s_metrics.min_threads;
    s_metrics.max_threads = count;
}

int get_min_threads() {
    return s_metrics.min_threads;
}

int get_max_threads() {
    return s_metrics.max_threads;
}

int get_thread_count() {
    return s_metrics.total_workers.load(std::memory_order_relaxed);
}

int get_active_count() {
    return s_metrics.active_workers.load(std::memory_order_relaxed);
}

int64_t get_completions() {
    return s_metrics.total_completions.load(std::memory_order_relaxed);
}

void request_worker() {
    if (!s_initialized || s_shutdown) return;
    // Inject immediately if below minimum — don't wait for gate thread
    if (s_metrics.total_workers.load(std::memory_order_relaxed) < s_metrics.min_threads) {
        inject_worker();
    }
    // Otherwise gate thread will detect the need via throughput/starvation metrics
}

bool has_pending_work() {
    return s_metrics.queued_items.load(std::memory_order_relaxed) > 0;
}

void notify_progress() {
    s_metrics.last_progress_time.store(elapsed_ms(), std::memory_order_relaxed);
}

} // namespace cil2cpp::threadpool
