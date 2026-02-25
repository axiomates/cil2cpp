/**
 * CIL2CPP Runtime - WaitHandle OS Primitives
 *
 * Phase II.5: Platform-specific implementations for wait handle operations.
 * Windows: Win32 CreateEvent/WaitForSingleObject/SetEvent/ResetEvent/CreateMutex/CreateSemaphore
 * POSIX: pthread mutex + condvar emulation
 */

#include <cil2cpp/waithandle.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/reflection.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <pthread.h>
#include <cstdlib>
#include <ctime>
#include <cerrno>
#endif

namespace cil2cpp {

// Phase IV.3-IV.7: TypeInfo definitions removed — now generated from BCL IL

// ===== OS Primitive Implementations =====

namespace icall {

#ifdef _WIN32

// ===== Windows: use native Win32 wait primitives =====

Int32 WaitHandle_WaitOneCore(intptr_t waitHandle, Int32 millisecondsTimeout) {
    DWORD timeout = (millisecondsTimeout < 0) ? INFINITE : static_cast<DWORD>(millisecondsTimeout);
    DWORD result = WaitForSingleObject(reinterpret_cast<HANDLE>(waitHandle), timeout);
    switch (result) {
        case WAIT_OBJECT_0:    return 0;   // Success
        case WAIT_TIMEOUT:     return 258; // WaitTimeout (WAIT_TIMEOUT = 258)
        case WAIT_ABANDONED:   return 128; // Abandoned (WAIT_ABANDONED_0 = 0x80)
        default:
            throw_invalid_operation();
    }
}

intptr_t EventWaitHandle_CreateEventCoreWin32(bool initialState, Int32 eventResetMode) {
    BOOL manualReset = (eventResetMode == 1) ? TRUE : FALSE; // 0=AutoReset, 1=ManualReset
    HANDLE h = CreateEventW(nullptr, manualReset, initialState ? TRUE : FALSE, nullptr);
    if (!h) throw_invalid_operation();
    return reinterpret_cast<intptr_t>(h);
}

bool EventWaitHandle_Set(intptr_t handle) {
    return SetEvent(reinterpret_cast<HANDLE>(handle)) != 0;
}

bool EventWaitHandle_Reset(intptr_t handle) {
    return ResetEvent(reinterpret_cast<HANDLE>(handle)) != 0;
}

intptr_t Mutex_CreateMutexCoreWin32(bool initiallyOwned) {
    HANDLE h = CreateMutexW(nullptr, initiallyOwned ? TRUE : FALSE, nullptr);
    if (!h) throw_invalid_operation();
    return reinterpret_cast<intptr_t>(h);
}

void Mutex_ReleaseMutex(intptr_t handle) {
    if (!ReleaseMutex(reinterpret_cast<HANDLE>(handle))) {
        throw_invalid_operation();
    }
}

intptr_t Semaphore_CreateSemaphoreCoreWin32(Int32 initialCount, Int32 maximumCount) {
    HANDLE h = CreateSemaphoreW(nullptr, initialCount, maximumCount, nullptr);
    if (!h) throw_invalid_operation();
    return reinterpret_cast<intptr_t>(h);
}

Int32 Semaphore_ReleaseSemaphore(intptr_t handle, Int32 releaseCount) {
    LONG previousCount = 0;
    if (!::ReleaseSemaphore(reinterpret_cast<HANDLE>(handle), releaseCount, &previousCount)) {
        throw_invalid_operation();
    }
    return static_cast<Int32>(previousCount);
}

void WaitHandle_CloseHandle(intptr_t handle) {
    if (handle && handle != static_cast<intptr_t>(-1)) {
        CloseHandle(reinterpret_cast<HANDLE>(handle));
    }
}

#else

// ===== POSIX: pthread-based wait handle emulation =====

// Type tag to distinguish event/mutex/semaphore at WaitOneCore dispatch time
enum class PosixHandleType : int { Event = 0, Mutex = 1, Semaphore = 2 };

// Common header for all POSIX handle types
struct PosixHandleBase {
    PosixHandleType type;
};

// --- Event (AutoReset / ManualReset) ---
struct PosixEvent : PosixHandleBase {
    pthread_mutex_t mutex;
    pthread_cond_t cond;
    bool signaled;
    bool manual_reset;
};

static PosixEvent* posix_event_create(bool initialState, bool manualReset) {
    auto* ev = static_cast<PosixEvent*>(std::malloc(sizeof(PosixEvent)));
    ev->type = PosixHandleType::Event;
    pthread_mutex_init(&ev->mutex, nullptr);
    pthread_cond_init(&ev->cond, nullptr);
    ev->signaled = initialState;
    ev->manual_reset = manualReset;
    return ev;
}

// Helper: compute absolute deadline from relative timeout_ms
static struct timespec posix_deadline(int timeout_ms) {
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    ts.tv_sec += timeout_ms / 1000;
    ts.tv_nsec += (timeout_ms % 1000) * 1000000L;
    if (ts.tv_nsec >= 1000000000L) {
        ts.tv_sec++;
        ts.tv_nsec -= 1000000000L;
    }
    return ts;
}

static int posix_event_wait(PosixEvent* ev, int timeout_ms) {
    pthread_mutex_lock(&ev->mutex);
    if (timeout_ms < 0) {
        while (!ev->signaled)
            pthread_cond_wait(&ev->cond, &ev->mutex);
    } else {
        auto ts = posix_deadline(timeout_ms);
        while (!ev->signaled) {
            if (pthread_cond_timedwait(&ev->cond, &ev->mutex, &ts) == ETIMEDOUT) {
                pthread_mutex_unlock(&ev->mutex);
                return 258; // WAIT_TIMEOUT
            }
        }
    }
    if (!ev->manual_reset)
        ev->signaled = false;
    pthread_mutex_unlock(&ev->mutex);
    return 0;
}

static void posix_event_set(PosixEvent* ev) {
    pthread_mutex_lock(&ev->mutex);
    ev->signaled = true;
    if (ev->manual_reset)
        pthread_cond_broadcast(&ev->cond);
    else
        pthread_cond_signal(&ev->cond);
    pthread_mutex_unlock(&ev->mutex);
}

static void posix_event_reset(PosixEvent* ev) {
    pthread_mutex_lock(&ev->mutex);
    ev->signaled = false;
    pthread_mutex_unlock(&ev->mutex);
}

static void posix_event_destroy(PosixEvent* ev) {
    if (!ev) return;
    pthread_cond_destroy(&ev->cond);
    pthread_mutex_destroy(&ev->mutex);
    std::free(ev);
}

// --- Mutex (recursive, owned by a single thread) ---
struct PosixMutex : PosixHandleBase {
    pthread_mutex_t mutex;  // PTHREAD_MUTEX_RECURSIVE
};

static PosixMutex* posix_mutex_create(bool initiallyOwned) {
    auto* m = static_cast<PosixMutex*>(std::malloc(sizeof(PosixMutex)));
    m->type = PosixHandleType::Mutex;

    pthread_mutexattr_t attr;
    pthread_mutexattr_init(&attr);
    pthread_mutexattr_settype(&attr, PTHREAD_MUTEX_RECURSIVE);
    pthread_mutex_init(&m->mutex, &attr);
    pthread_mutexattr_destroy(&attr);

    if (initiallyOwned)
        pthread_mutex_lock(&m->mutex);
    return m;
}

static int posix_mutex_wait(PosixMutex* m, int timeout_ms) {
    if (timeout_ms < 0) {
        pthread_mutex_lock(&m->mutex);
        return 0;
    }
    // Timed lock via polling (pthread_mutex_timedlock not portable to all platforms)
    auto ts = posix_deadline(timeout_ms);
    while (true) {
        if (pthread_mutex_trylock(&m->mutex) == 0)
            return 0;
        struct timespec now;
        clock_gettime(CLOCK_REALTIME, &now);
        if (now.tv_sec > ts.tv_sec || (now.tv_sec == ts.tv_sec && now.tv_nsec >= ts.tv_nsec))
            return 258; // WAIT_TIMEOUT
        // Brief yield before retrying
        struct timespec sleep_ts = {0, 1000000}; // 1ms
        nanosleep(&sleep_ts, nullptr);
    }
}

static void posix_mutex_release(PosixMutex* m) {
    pthread_mutex_unlock(&m->mutex);
}

static void posix_mutex_destroy(PosixMutex* m) {
    if (!m) return;
    pthread_mutex_destroy(&m->mutex);
    std::free(m);
}

// --- Semaphore (counting, with maximum) ---
struct PosixSemaphore : PosixHandleBase {
    pthread_mutex_t mutex;
    pthread_cond_t cond;
    int count;
    int max_count;
};

static PosixSemaphore* posix_semaphore_create(int initialCount, int maximumCount) {
    auto* s = static_cast<PosixSemaphore*>(std::malloc(sizeof(PosixSemaphore)));
    s->type = PosixHandleType::Semaphore;
    pthread_mutex_init(&s->mutex, nullptr);
    pthread_cond_init(&s->cond, nullptr);
    s->count = initialCount;
    s->max_count = maximumCount;
    return s;
}

static int posix_semaphore_wait(PosixSemaphore* s, int timeout_ms) {
    pthread_mutex_lock(&s->mutex);
    if (timeout_ms < 0) {
        while (s->count <= 0)
            pthread_cond_wait(&s->cond, &s->mutex);
    } else {
        auto ts = posix_deadline(timeout_ms);
        while (s->count <= 0) {
            if (pthread_cond_timedwait(&s->cond, &s->mutex, &ts) == ETIMEDOUT) {
                pthread_mutex_unlock(&s->mutex);
                return 258; // WAIT_TIMEOUT
            }
        }
    }
    s->count--;
    pthread_mutex_unlock(&s->mutex);
    return 0;
}

static int posix_semaphore_release(PosixSemaphore* s, int releaseCount) {
    pthread_mutex_lock(&s->mutex);
    int prev = s->count;
    s->count += releaseCount;
    if (s->count > s->max_count) {
        s->count = prev; // rollback
        pthread_mutex_unlock(&s->mutex);
        throw_invalid_operation(); // SemaphoreFullException equivalent
    }
    pthread_cond_broadcast(&s->cond);
    pthread_mutex_unlock(&s->mutex);
    return prev;
}

static void posix_semaphore_destroy(PosixSemaphore* s) {
    if (!s) return;
    pthread_cond_destroy(&s->cond);
    pthread_mutex_destroy(&s->mutex);
    std::free(s);
}

// --- WaitHandle dispatch (routes to event/mutex/semaphore by type tag) ---

Int32 WaitHandle_WaitOneCore(intptr_t waitHandle, Int32 millisecondsTimeout) {
    auto* base = reinterpret_cast<PosixHandleBase*>(waitHandle);
    switch (base->type) {
        case PosixHandleType::Event:
            return posix_event_wait(static_cast<PosixEvent*>(base), millisecondsTimeout);
        case PosixHandleType::Mutex:
            return posix_mutex_wait(static_cast<PosixMutex*>(base), millisecondsTimeout);
        case PosixHandleType::Semaphore:
            return posix_semaphore_wait(static_cast<PosixSemaphore*>(base), millisecondsTimeout);
    }
    throw_invalid_operation();
}

intptr_t EventWaitHandle_CreateEventCoreWin32(bool initialState, Int32 eventResetMode) {
    bool manualReset = (eventResetMode == 1);
    return reinterpret_cast<intptr_t>(posix_event_create(initialState, manualReset));
}

bool EventWaitHandle_Set(intptr_t handle) {
    posix_event_set(reinterpret_cast<PosixEvent*>(handle));
    return true;
}

bool EventWaitHandle_Reset(intptr_t handle) {
    posix_event_reset(reinterpret_cast<PosixEvent*>(handle));
    return true;
}

intptr_t Mutex_CreateMutexCoreWin32(bool initiallyOwned) {
    return reinterpret_cast<intptr_t>(posix_mutex_create(initiallyOwned));
}

void Mutex_ReleaseMutex(intptr_t handle) {
    posix_mutex_release(reinterpret_cast<PosixMutex*>(handle));
}

intptr_t Semaphore_CreateSemaphoreCoreWin32(Int32 initialCount, Int32 maximumCount) {
    return reinterpret_cast<intptr_t>(posix_semaphore_create(initialCount, maximumCount));
}

Int32 Semaphore_ReleaseSemaphore(intptr_t handle, Int32 releaseCount) {
    return posix_semaphore_release(reinterpret_cast<PosixSemaphore*>(handle), releaseCount);
}

void WaitHandle_CloseHandle(intptr_t handle) {
    auto* base = reinterpret_cast<PosixHandleBase*>(handle);
    if (!base) return;
    switch (base->type) {
        case PosixHandleType::Event:
            posix_event_destroy(static_cast<PosixEvent*>(base)); break;
        case PosixHandleType::Mutex:
            posix_mutex_destroy(static_cast<PosixMutex*>(base)); break;
        case PosixHandleType::Semaphore:
            posix_semaphore_destroy(static_cast<PosixSemaphore*>(base)); break;
    }
}

#endif

} // namespace icall
} // namespace cil2cpp
