/**
 * CIL2CPP Runtime - Task Implementation
 * Thread-safe task completion with continuation support.
 */

#include <cil2cpp/task.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/array.h>
#include <cil2cpp/threadpool.h>
#include <cil2cpp/delegate.h>

#include <mutex>
#include <thread>
#include <atomic>
#include <chrono>
#include <new>

namespace cil2cpp {

// Releases the CRT-heap mutex when Task is GC-collected.
static void task_finalizer(Object* raw) {
    auto* t = reinterpret_cast<Task*>(raw);
    if (t->f_lock) {
        delete static_cast<std::mutex*>(t->f_lock);
        t->f_lock = nullptr;
    }
}

static TypeInfo Task_TypeInfo_Internal = {
    .name = "Task",
    .namespace_name = "System.Threading.Tasks",
    .full_name = "System.Threading.Tasks.Task",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Task),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = task_finalizer,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static std::once_flag s_completed_task_once;
static Task* s_completed_task = nullptr;

// Atomic helpers: avoid data races between mutex-protected writes and non-mutex reads
static inline Int32 task_load_status(Task* t) {
    return std::atomic_ref<Int32>(t->f_status).load(std::memory_order_acquire);
}
static inline void task_store_status(Task* t, Int32 status) {
    std::atomic_ref<Int32>(t->f_status).store(status, std::memory_order_release);
}

// Allocate a new Task with a fresh mutex
// Note: Task doesn't inherit from Object (to avoid MSVC tail-padding mismatch),
// so we use reinterpret_cast instead of static_cast.
static Task* task_alloc() {
    auto* t = reinterpret_cast<Task*>(gc::alloc(sizeof(Task), &Task_TypeInfo_Internal));
    t->f_status = 0;
    t->f_exception = nullptr;
    t->f_continuations = nullptr;
    t->f_lock = new std::mutex();
    return t;
}

// Run all continuations and clear the list
static void run_continuations(TaskContinuation* head) {
    while (head) {
        head->callback(head->state);
        head = head->next;
    }
}

Task* task_create_completed() {
    auto* t = task_alloc();
    t->f_status = 1;
    return t;
}

Task* task_get_completed() {
    std::call_once(s_completed_task_once, []() {
        s_completed_task = task_create_completed();
    });
    return s_completed_task;
}

Task* task_create_pending() {
    return task_alloc();
}

void task_init_pending(Task* t) {
    if (!t) return;
    t->f_status = 0;
    t->f_exception = nullptr;
    t->f_continuations = nullptr;
    t->f_lock = new std::mutex();
}

void task_init_completed(Task* t) {
    if (!t) return;
    t->f_status = 1;
    t->f_exception = nullptr;
    t->f_continuations = nullptr;
    t->f_lock = nullptr;
}

void task_complete(Task* t) {
    if (!t) return;
    TaskContinuation* conts = nullptr;
    {
        auto* mtx = static_cast<std::mutex*>(t->f_lock);
        std::lock_guard<std::mutex> lock(*mtx);
        if (task_load_status(t) >= 1) return;
        task_store_status(t, 1);
        conts = t->f_continuations;
        t->f_continuations = nullptr;
    }
    run_continuations(conts);
}

void task_fault(Task* t, Exception* ex) {
    if (!t) return;
    TaskContinuation* conts = nullptr;
    {
        auto* mtx = static_cast<std::mutex*>(t->f_lock);
        std::lock_guard<std::mutex> lock(*mtx);
        if (task_load_status(t) >= 1) return;
        task_store_status(t, 2);
        t->f_exception = ex;
        conts = t->f_continuations;
        t->f_continuations = nullptr;
    }
    run_continuations(conts);
}

void task_add_continuation(Task* t, void (*callback)(void*), void* state) {
    if (!t) return;
    {
        auto* mtx = static_cast<std::mutex*>(t->f_lock);
        std::lock_guard<std::mutex> lock(*mtx);
        if (task_load_status(t) < 1) {
            // Task not yet complete — queue the continuation
            auto* cont = static_cast<TaskContinuation*>(
                gc::alloc(sizeof(TaskContinuation), nullptr));
            cont->callback = callback;
            cont->state = state;
            cont->next = t->f_continuations;
            t->f_continuations = cont;
            return;
        }
    }
    // Task already complete — run immediately
    callback(state);
}

void task_wait(Task* t) {
    if (!t || task_load_status(t) >= 1) return;
    while (task_load_status(t) < 1) {
        std::this_thread::yield();
    }
}

// ===== Combinators =====

struct WhenAllState {
    Task* result_task;
    std::atomic<Int32> remaining;
};

static void when_all_callback(void* raw) {
    auto* state = static_cast<WhenAllState*>(raw);
    if (state->remaining.fetch_sub(1) == 1) {
        task_complete(state->result_task);
    }
}

Task* task_when_all(Array* tasks) {
    if (!tasks || tasks->length == 0) {
        return task_create_completed();
    }

    auto* result = task_create_pending();
    // FIXME: WhenAllState is CRT-heap allocated (std::atomic not safe in GC memory).
    // Leaked after all tasks complete. Acceptable for short-lived combinators.
    auto* state = new WhenAllState();
    state->result_task = result;
    state->remaining.store(tasks->length);

    auto** task_ptrs = static_cast<Task**>(array_data(tasks));
    for (Int32 i = 0; i < tasks->length; i++) {
        Task* t = task_ptrs[i];
        if (t) {
            task_add_continuation(t, when_all_callback, state);
        } else {
            when_all_callback(state);
        }
    }

    return result;
}

struct WhenAnyState {
    Task* result_task;
    std::atomic<bool> completed;
};

static void when_any_callback(void* raw) {
    auto* state = static_cast<WhenAnyState*>(raw);
    bool expected = false;
    if (state->completed.compare_exchange_strong(expected, true)) {
        task_complete(state->result_task);
    }
}

Task* task_when_any(Array* tasks) {
    if (!tasks || tasks->length == 0) {
        return task_create_completed();
    }

    auto* result = task_create_pending();
    // FIXME: WhenAnyState is CRT-heap allocated (std::atomic not safe in GC memory).
    // Leaked after any task completes. Acceptable for short-lived combinators.
    auto* state = new WhenAnyState();
    state->result_task = result;
    state->completed.store(false);

    auto** task_ptrs = static_cast<Task**>(array_data(tasks));
    for (Int32 i = 0; i < tasks->length; i++) {
        Task* t = task_ptrs[i];
        if (t) {
            task_add_continuation(t, when_any_callback, state);
        } else {
            when_any_callback(state);
        }
    }

    return result;
}

struct DelayState {
    Task* task;
    Int32 milliseconds;
};

static void delay_thread_func(void* raw) {
    auto* state = static_cast<DelayState*>(raw);
    std::this_thread::sleep_for(std::chrono::milliseconds(state->milliseconds));
    task_complete(state->task);
    delete state;
}

Task* task_delay(Int32 milliseconds) {
    if (milliseconds <= 0) {
        return task_create_completed();
    }

    auto* result = task_create_pending();
    auto* state = new DelayState{result, milliseconds};

    if (threadpool::is_initialized()) {
        threadpool::queue_work(delay_thread_func, state);
    } else {
        std::thread(delay_thread_func, state).detach();
    }

    return result;
}

struct RunState {
    Task* task;
    Delegate* del;
};

static void run_delegate_func(void* raw) {
    auto* state = static_cast<RunState*>(raw);
    auto* del = state->del;

    if (del && del->method_ptr) {
        if (del->target) {
            using InstanceFn = void(*)(Object*);
            reinterpret_cast<InstanceFn>(del->method_ptr)(del->target);
        } else {
            using StaticFn = void(*)();
            reinterpret_cast<StaticFn>(del->method_ptr)();
        }
    }

    task_complete(state->task);
    delete state;
}

Task* task_run(Object* del) {
    auto* result = task_create_pending();
    auto* state = new RunState{result, static_cast<Delegate*>(del)};

    if (threadpool::is_initialized()) {
        threadpool::queue_work(run_delegate_func, state);
    } else {
        run_delegate_func(state);
    }

    return result;
}

} // namespace cil2cpp
