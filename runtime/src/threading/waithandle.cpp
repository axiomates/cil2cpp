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

// ===== TypeInfo definitions =====

TypeInfo System_Threading_WaitHandle_TypeInfo = {
    .name = "WaitHandle",
    .namespace_name = "System.Threading",
    .full_name = "System.Threading.WaitHandle",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedWaitHandle), .element_size = 0,
    .flags = TypeFlags::Abstract,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_Threading_EventWaitHandle_TypeInfo = {
    .name = "EventWaitHandle",
    .namespace_name = "System.Threading",
    .full_name = "System.Threading.EventWaitHandle",
    .base_type = &System_Threading_WaitHandle_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedEventWaitHandle), .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_Threading_ManualResetEvent_TypeInfo = {
    .name = "ManualResetEvent",
    .namespace_name = "System.Threading",
    .full_name = "System.Threading.ManualResetEvent",
    .base_type = &System_Threading_EventWaitHandle_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedEventWaitHandle), .element_size = 0,
    .flags = TypeFlags::Sealed,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_Threading_AutoResetEvent_TypeInfo = {
    .name = "AutoResetEvent",
    .namespace_name = "System.Threading",
    .full_name = "System.Threading.AutoResetEvent",
    .base_type = &System_Threading_EventWaitHandle_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedEventWaitHandle), .element_size = 0,
    .flags = TypeFlags::Sealed,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_Threading_Mutex_TypeInfo = {
    .name = "Mutex",
    .namespace_name = "System.Threading",
    .full_name = "System.Threading.Mutex",
    .base_type = &System_Threading_WaitHandle_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedMutex), .element_size = 0,
    .flags = TypeFlags::Sealed,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_Threading_Semaphore_TypeInfo = {
    .name = "Semaphore",
    .namespace_name = "System.Threading",
    .full_name = "System.Threading.Semaphore",
    .base_type = &System_Threading_WaitHandle_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedSemaphore), .element_size = 0,
    .flags = TypeFlags::Sealed,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

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

// ===== POSIX: pthread mutex + condvar emulation =====

// POSIX event emulation using pthread primitives
struct PosixEvent {
    pthread_mutex_t mutex;
    pthread_cond_t cond;
    bool signaled;
    bool manual_reset;
};

static PosixEvent* posix_event_create(bool initialState, bool manualReset) {
    auto* ev = static_cast<PosixEvent*>(std::malloc(sizeof(PosixEvent)));
    pthread_mutex_init(&ev->mutex, nullptr);
    pthread_cond_init(&ev->cond, nullptr);
    ev->signaled = initialState;
    ev->manual_reset = manualReset;
    return ev;
}

static int posix_event_wait(PosixEvent* ev, int timeout_ms) {
    pthread_mutex_lock(&ev->mutex);
    if (timeout_ms < 0) {
        // Infinite wait
        while (!ev->signaled) {
            pthread_cond_wait(&ev->cond, &ev->mutex);
        }
    } else {
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        ts.tv_sec += timeout_ms / 1000;
        ts.tv_nsec += (timeout_ms % 1000) * 1000000L;
        if (ts.tv_nsec >= 1000000000L) {
            ts.tv_sec++;
            ts.tv_nsec -= 1000000000L;
        }
        while (!ev->signaled) {
            int rc = pthread_cond_timedwait(&ev->cond, &ev->mutex, &ts);
            if (rc == ETIMEDOUT) {
                pthread_mutex_unlock(&ev->mutex);
                return 258; // WAIT_TIMEOUT
            }
        }
    }
    if (!ev->manual_reset) {
        ev->signaled = false; // Auto-reset
    }
    pthread_mutex_unlock(&ev->mutex);
    return 0; // WAIT_OBJECT_0
}

static void posix_event_set(PosixEvent* ev) {
    pthread_mutex_lock(&ev->mutex);
    ev->signaled = true;
    if (ev->manual_reset) {
        pthread_cond_broadcast(&ev->cond);
    } else {
        pthread_cond_signal(&ev->cond);
    }
    pthread_mutex_unlock(&ev->mutex);
}

static void posix_event_reset(PosixEvent* ev) {
    pthread_mutex_lock(&ev->mutex);
    ev->signaled = false;
    pthread_mutex_unlock(&ev->mutex);
}

static void posix_event_destroy(PosixEvent* ev) {
    if (ev) {
        pthread_cond_destroy(&ev->cond);
        pthread_mutex_destroy(&ev->mutex);
        std::free(ev);
    }
}

Int32 WaitHandle_WaitOneCore(intptr_t waitHandle, Int32 millisecondsTimeout) {
    auto* ev = reinterpret_cast<PosixEvent*>(waitHandle);
    return posix_event_wait(ev, millisecondsTimeout);
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
    // TODO: Proper POSIX mutex with ownership tracking
    auto* ev = posix_event_create(!initiallyOwned, false);
    return reinterpret_cast<intptr_t>(ev);
}

void Mutex_ReleaseMutex(intptr_t handle) {
    posix_event_set(reinterpret_cast<PosixEvent*>(handle));
}

intptr_t Semaphore_CreateSemaphoreCoreWin32(Int32 initialCount, Int32 maximumCount) {
    // TODO: Proper POSIX semaphore (sem_init / sem_open)
    (void)maximumCount;
    auto* ev = posix_event_create(initialCount > 0, false);
    return reinterpret_cast<intptr_t>(ev);
}

Int32 Semaphore_ReleaseSemaphore(intptr_t handle, Int32 /*releaseCount*/) {
    // TODO: Proper counting semaphore
    posix_event_set(reinterpret_cast<PosixEvent*>(handle));
    return 0;
}

void WaitHandle_CloseHandle(intptr_t handle) {
    posix_event_destroy(reinterpret_cast<PosixEvent*>(handle));
}

#endif

} // namespace icall
} // namespace cil2cpp
