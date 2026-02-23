/**
 * CIL2CPP Runtime - WaitHandle Support
 *
 * Phase II.5: Runtime-provided types for System.Threading.WaitHandle hierarchy.
 * Architecture: BCL IL compiles for upper-layer methods, OS primitives via ICalls.
 *
 * Hierarchy:
 *   WaitHandle (abstract base, holds SafeWaitHandle)
 *     ├─ EventWaitHandle (Create/Set/Reset OS event)
 *     │   ├─ ManualResetEvent
 *     │   └─ AutoResetEvent
 *     ├─ Mutex
 *     └─ Semaphore
 */

#pragma once

#include "object.h"
#include "type_info.h"

namespace cil2cpp {

// Forward declarations
struct String;

// TypeInfo for wait handle types
extern TypeInfo System_Threading_WaitHandle_TypeInfo;
extern TypeInfo System_Threading_EventWaitHandle_TypeInfo;
extern TypeInfo System_Threading_ManualResetEvent_TypeInfo;
extern TypeInfo System_Threading_AutoResetEvent_TypeInfo;
extern TypeInfo System_Threading_Mutex_TypeInfo;
extern TypeInfo System_Threading_Semaphore_TypeInfo;

/**
 * Managed System.Threading.WaitHandle — abstract base class.
 * BCL IL accesses f_waitHandle (SafeWaitHandle) which holds the OS handle.
 */
struct ManagedWaitHandle : Object {
    void* f_waitHandle;     // SafeWaitHandle (generated flat struct, not our type)
};

/**
 * EventWaitHandle — wraps OS event (manual-reset or auto-reset).
 */
struct ManagedEventWaitHandle : ManagedWaitHandle {
    // No additional fields — the OS handle is in SafeWaitHandle
};

/**
 * Mutex — wraps OS mutex.
 */
struct ManagedMutex : ManagedWaitHandle {
    // No additional fields
};

/**
 * Semaphore — wraps OS semaphore.
 */
struct ManagedSemaphore : ManagedWaitHandle {
    // No additional fields
};

// ===== OS Primitive ICalls =====

namespace icall {

/**
 * WaitHandle.WaitOneCore(IntPtr waitHandle, int millisecondsTimeout) → int
 * Waits on a single OS handle. Returns 0 (WAIT_OBJECT_0) on success.
 */
Int32 WaitHandle_WaitOneCore(intptr_t waitHandle, Int32 millisecondsTimeout);

/**
 * EventWaitHandle.CreateEventCoreWin32(bool initialState, int eventResetMode) → IntPtr
 * Creates an OS event. eventResetMode: 0=AutoReset, 1=ManualReset.
 */
intptr_t EventWaitHandle_CreateEventCoreWin32(bool initialState, Int32 eventResetMode);

/**
 * EventWaitHandle.Set() → bool
 * Signals the event.
 */
bool EventWaitHandle_Set(intptr_t handle);

/**
 * EventWaitHandle.Reset() → bool
 * Un-signals the event.
 */
bool EventWaitHandle_Reset(intptr_t handle);

/**
 * Mutex.CreateMutexCoreWin32(bool initiallyOwned) → IntPtr
 * Creates an OS mutex.
 */
intptr_t Mutex_CreateMutexCoreWin32(bool initiallyOwned);

/**
 * Mutex.ReleaseMutex() → void
 * Releases ownership of the mutex.
 */
void Mutex_ReleaseMutex(intptr_t handle);

/**
 * Semaphore.CreateSemaphoreCoreWin32(int initialCount, int maximumCount) → IntPtr
 * Creates an OS semaphore.
 */
intptr_t Semaphore_CreateSemaphoreCoreWin32(Int32 initialCount, Int32 maximumCount);

/**
 * Semaphore.ReleaseSemaphore(int releaseCount) → int (previous count)
 */
Int32 Semaphore_ReleaseSemaphore(intptr_t handle, Int32 releaseCount);

/**
 * WaitHandle close/dispose: CloseHandle on the OS handle.
 */
void WaitHandle_CloseHandle(intptr_t handle);

} // namespace icall
} // namespace cil2cpp

// Type aliases used by generated code
using System_Threading_WaitHandle = cil2cpp::ManagedWaitHandle;
using System_Threading_EventWaitHandle = cil2cpp::ManagedEventWaitHandle;
using System_Threading_ManualResetEvent = cil2cpp::ManagedEventWaitHandle;
using System_Threading_AutoResetEvent = cil2cpp::ManagedEventWaitHandle;
using System_Threading_Mutex = cil2cpp::ManagedMutex;
using System_Threading_Semaphore = cil2cpp::ManagedSemaphore;
