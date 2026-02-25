/**
 * CIL2CPP Runtime - Thread Implementation
 *
 * Wraps std::thread for managed thread support.
 * Each thread registers with BoehmGC for safe allocations.
 */

#include <cil2cpp/threading.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/type_info.h>

#include <atomic>
#include <chrono>
#include <thread>

namespace cil2cpp {

// TLS: current managed thread for each OS thread
static thread_local ManagedThread* g_current_thread = nullptr;

ManagedThread* thread_get_current() {
    return g_current_thread;
}

void thread_set_current(ManagedThread* t) {
    g_current_thread = t;
}

namespace thread {

static std::atomic<Int32> g_next_managed_id{1};

// Thread entry point — runs on the new thread
static void thread_entry(ManagedThread* t) {
    gc::register_thread();
    thread_set_current(t);

    t->state = 1; // running

#ifdef _MSC_VER
#pragma warning(push)
// C4611: setjmp + C++ destructors interaction. Safe here because the TRY/CATCH
// scope contains only raw pointers (no RAII objects), so longjmp won't skip any
// destructors. setjmp/longjmp is our exception mechanism (like Unity IL2CPP).
#pragma warning(disable: 4611)
#endif
    CIL2CPP_TRY
        // Invoke the ThreadStart delegate: void()
        if (t->start_delegate && t->start_delegate->method_ptr) {
            using ThreadStartFn = void(*)(Object*);
            auto fn = reinterpret_cast<ThreadStartFn>(t->start_delegate->method_ptr);
            fn(t->start_delegate->target);
        }
    CIL2CPP_CATCH_ALL
        // ECMA-335: unhandled exceptions in threads terminate the thread
    CIL2CPP_END_TRY
#ifdef _MSC_VER
#pragma warning(pop)
#endif

    t->state = 2; // stopped
    thread_set_current(nullptr);

    gc::unregister_thread();
}

ManagedThread* create(Delegate* start) {
    if (!start) throw_null_reference();

    // Allocate ManagedThread as a GC object
    auto* t = static_cast<ManagedThread*>(
        gc::alloc(sizeof(ManagedThread), nullptr));
    t->native_handle = nullptr;
    t->start_delegate = start;
    t->managed_id = g_next_managed_id.fetch_add(1, std::memory_order_relaxed);
    t->state = 0; // unstarted
    return t;
}

void start(ManagedThread* t) {
    if (!t) throw_null_reference();
    if (t->state != 0) throw_invalid_operation();

    // Create the native thread
    auto* native = new std::thread(thread_entry, t);
    t->native_handle = native;
}

void join(ManagedThread* t) {
    if (!t) throw_null_reference();
    auto* native = static_cast<std::thread*>(t->native_handle);
    if (native && native->joinable()) {
        native->join();
        delete native;
        t->native_handle = nullptr;
    }
}

bool join_timeout(ManagedThread* t, Int32 timeout_ms) {
    if (!t) throw_null_reference();

    // std::thread doesn't support timed join directly.
    // For now, poll the state flag.
    if (t->state == 2) return true;

    auto deadline = std::chrono::steady_clock::now() +
        std::chrono::milliseconds(timeout_ms);

    // Exponential backoff: 1ms → 2ms → 4ms → ... → cap 50ms
    Int32 sleep_ms = 1;
    while (std::chrono::steady_clock::now() < deadline) {
        if (t->state == 2) {
            // Thread finished — join to clean up
            auto* native = static_cast<std::thread*>(t->native_handle);
            if (native && native->joinable()) {
                native->join();
                delete native;
                t->native_handle = nullptr;
            }
            return true;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(sleep_ms));
        if (sleep_ms < 50) sleep_ms *= 2;
    }
    return false;
}

void sleep(Int32 milliseconds) {
    if (milliseconds > 0) {
        std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds));
    } else if (milliseconds == 0) {
        std::this_thread::yield();
    } else {
        // .NET: Sleep(-1) = Timeout.Infinite = wait forever.
        // Other negative values are ArgumentOutOfRange in full .NET,
        // but we treat all negatives as infinite for simplicity.
        while (true) {
            std::this_thread::sleep_for(std::chrono::hours(24));
        }
    }
}

bool is_alive(ManagedThread* t) {
    if (!t) throw_null_reference();
    return t->state == 1;
}

Int32 get_managed_id(ManagedThread* t) {
    if (!t) throw_null_reference();
    return t->managed_id;
}

} // namespace thread
} // namespace cil2cpp
