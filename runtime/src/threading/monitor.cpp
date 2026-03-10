/**
 * CIL2CPP Runtime - Monitor Implementation
 *
 * ECMA-335 compliant sync block table approach:
 * - Each Object has __sync_block (uint32_t), initially 0
 * - Global table maps sync block indices → CRITICAL_SECTION + CONDITION_VARIABLE
 * - Reentrant locks (CRITICAL_SECTION is inherently reentrant on Windows)
 * - Thread-safe slot allocation via atomic CAS
 *
 * Uses Windows native synchronization for reliability with setjmp/longjmp
 * exception handling used by CIL2CPP_TRY/CIL2CPP_CATCH macros.
 */

#include <cil2cpp/threading.h>
#include <cil2cpp/exception.h>

#include <atomic>
#include <mutex>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#else
#include <condition_variable>
#endif

namespace cil2cpp {
namespace monitor {

struct SyncBlock {
#ifdef _WIN32
    CRITICAL_SECTION cs;
    CONDITION_VARIABLE condvar;
    SyncBlock() {
        InitializeCriticalSection(&cs);
        InitializeConditionVariable(&condvar);
    }
    ~SyncBlock() {
        DeleteCriticalSection(&cs);
    }
#else
    std::recursive_mutex mutex;
    std::condition_variable_any condvar;
#endif
};

// Global sync block table — slot 0 is unused (0 means "no sync block")
static std::vector<SyncBlock*> g_sync_table;
static std::mutex g_table_lock;
static std::atomic<uint32_t> g_next_index{1};

/**
 * Get or allocate a sync block for an object.
 * Uses atomic CAS on __sync_block for thread-safe allocation.
 */
static SyncBlock* get_sync_block(Object* obj) {
    // Fast path: sync block already assigned
    auto* slot = reinterpret_cast<std::atomic<uint32_t>*>(&obj->__sync_block);
    uint32_t index = slot->load(std::memory_order_acquire);
    if (index != 0) {
        std::lock_guard<std::mutex> guard(g_table_lock);
        return g_sync_table[index];
    }

    // Slow path: allocate a new sync block
    uint32_t new_index = g_next_index.fetch_add(1, std::memory_order_relaxed);
    auto* block = new SyncBlock();

    {
        std::lock_guard<std::mutex> guard(g_table_lock);
        if (g_sync_table.size() <= new_index) {
            g_sync_table.resize(new_index + 1, nullptr);
        }
        g_sync_table[new_index] = block;
    }

    // Try to assign our new index; another thread may have beaten us
    uint32_t expected = 0;
    if (slot->compare_exchange_strong(expected, new_index, std::memory_order_acq_rel)) {
        return block;
    }

    // Another thread assigned first — use theirs, discard ours
    {
        std::lock_guard<std::mutex> guard(g_table_lock);
        g_sync_table[new_index] = nullptr;
    }
    delete block;

    // Use the winner's sync block
    std::lock_guard<std::mutex> guard(g_table_lock);
    return g_sync_table[expected];
}

void enter(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
#ifdef _WIN32
    EnterCriticalSection(&block->cs);
#else
    block->mutex.lock();
#endif
}

void exit(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
#ifdef _WIN32
    LeaveCriticalSection(&block->cs);
#else
    block->mutex.unlock();
#endif
}

void reliable_enter(Object* obj, bool* lockTaken) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
#ifdef _WIN32
    EnterCriticalSection(&block->cs);
#else
    block->mutex.lock();
#endif
    if (lockTaken) *lockTaken = true;
}

bool wait(Object* obj, Int32 timeout_ms) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);

#ifdef _WIN32
    DWORD dwTimeout = (timeout_ms < 0) ? INFINITE : static_cast<DWORD>(timeout_ms);
    // Use polling with short timeouts to avoid GC interaction with blocked threads
    if (dwTimeout == INFINITE) {
        while (true) {
            BOOL r = SleepConditionVariableCS(&block->condvar, &block->cs, 50);
            if (r) return true;
            if (GetLastError() != ERROR_TIMEOUT) return false;
        }
    } else {
        DWORD start = GetTickCount();
        DWORD remaining = dwTimeout;
        while (remaining > 0) {
            DWORD chunk = (remaining < 50) ? remaining : 50;
            BOOL r = SleepConditionVariableCS(&block->condvar, &block->cs, chunk);
            if (r) return true;
            if (GetLastError() != ERROR_TIMEOUT) return false;
            DWORD elapsed = GetTickCount() - start;
            remaining = (elapsed >= dwTimeout) ? 0 : (dwTimeout - elapsed);
        }
        return false;
    }
#else
    std::unique_lock<std::recursive_mutex> lock(block->mutex, std::adopt_lock);
    bool result;
    if (timeout_ms < 0) {
        block->condvar.wait(lock);
        result = true;
    } else {
        auto status = block->condvar.wait_for(
            lock,
            std::chrono::milliseconds(timeout_ms)
        );
        result = status == std::cv_status::no_timeout;
    }
    lock.release();
    return result;
#endif
}

void pulse(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
#ifdef _WIN32
    WakeConditionVariable(&block->condvar);
#else
    block->condvar.notify_one();
#endif
}

void pulse_all(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
#ifdef _WIN32
    WakeAllConditionVariable(&block->condvar);
#else
    block->condvar.notify_all();
#endif
}

} // namespace monitor
} // namespace cil2cpp
