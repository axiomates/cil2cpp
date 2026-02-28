/**
 * CIL2CPP Runtime - Threading Support
 *
 * Monitor (lock), Interlocked atomics, Thread management.
 * ECMA-335 compliant: Monitor is reentrant, uses sync block table.
 */

#pragma once

#include "object.h"
#include "delegate.h"

namespace cil2cpp {

// ===== Monitor (ECMA-335 II.15.4.4) =====

namespace monitor {

/**
 * Acquire the monitor lock for an object.
 * Reentrant: the same thread can lock the same object multiple times.
 */
void enter(Object* obj);

/**
 * Release the monitor lock for an object.
 */
void exit(Object* obj);

/**
 * Reliable enter: atomically acquires lock and sets lockTaken.
 * Used by C# lock statement (Monitor.ReliableEnter).
 */
void reliable_enter(Object* obj, bool* lockTaken);

/**
 * Release the lock and wait for a pulse.
 * @param obj The object whose monitor to wait on
 * @param timeout_ms Timeout in milliseconds (-1 = infinite)
 * @return true if pulsed, false if timed out
 */
bool wait(Object* obj, Int32 timeout_ms);

/**
 * Wake one thread waiting on the object's monitor.
 */
void pulse(Object* obj);

/**
 * Wake all threads waiting on the object's monitor.
 */
void pulse_all(Object* obj);

} // namespace monitor

// ===== Interlocked =====

namespace interlocked {

Int32 increment_i32(Int32* location);
Int32 decrement_i32(Int32* location);
Int32 exchange_i32(Int32* location, Int32 value);
Int32 compare_exchange_i32(Int32* location, Int32 value, Int32 comparand);
Int32 add_i32(Int32* location, Int32 value);
Int64 add_i64(Int64* location, Int64 value);

Int64 increment_i64(Int64* location);
Int64 decrement_i64(Int64* location);
Int64 exchange_i64(Int64* location, Int64 value);
Int64 compare_exchange_i64(Int64* location, Int64 value, Int64 comparand);

uint8_t exchange_u8(uint8_t* location, uint8_t value);
uint8_t compare_exchange_u8(uint8_t* location, uint8_t value, uint8_t comparand);

uint16_t exchange_u16(uint16_t* location, uint16_t value);
uint16_t compare_exchange_u16(uint16_t* location, uint16_t value, uint16_t comparand);

Object* exchange_obj(Object** location, Object* value);
Object* compare_exchange_obj(Object** location, Object* value, Object* comparand);

void read_memory_barrier();

} // namespace interlocked

// ===== Thread =====

/**
 * Managed thread object layout.
 * Corresponds to System.Threading.Thread.
 */
struct ManagedThread : Object {
    void* native_handle;        // std::thread* (heap-allocated)
    Delegate* start_delegate;   // ThreadStart delegate
    Int32 managed_id;           // Managed thread ID
    Int32 state;                // 0=unstarted, 1=running, 2=stopped
    void* f_executionContext;        // BCL: Thread._executionContext (ExecutionContext)
    void* f_synchronizationContext;  // BCL: Thread._synchronizationContext (SynchronizationContext)
    String* f_name;                  // BCL: Thread._name
    void* f_startHelper;             // BCL: Thread._startHelper (StartHelper)
    bool f_mayNeedResetForThreadPool; // BCL: Thread._mayNeedResetForThreadPool
};

/// Get the current managed thread for this OS thread (TLS-backed).
/// Returns nullptr if no managed thread has been set (e.g., native-only thread).
ManagedThread* thread_get_current();

/// Set the current managed thread for this OS thread.
void thread_set_current(ManagedThread* t);

namespace thread {

/**
 * Create a new managed thread with a ThreadStart delegate.
 */
ManagedThread* create(Delegate* start);

/**
 * Start the thread.
 */
void start(ManagedThread* t);

/**
 * Wait for the thread to complete.
 */
void join(ManagedThread* t);

/**
 * Wait for the thread to complete with a timeout.
 * @return true if thread completed, false if timed out
 */
bool join_timeout(ManagedThread* t, Int32 timeout_ms);

/**
 * Suspend the current thread for the specified duration.
 */
void sleep(Int32 milliseconds);

/**
 * Check if the thread is still running.
 */
bool is_alive(ManagedThread* t);

/**
 * Get the managed thread ID.
 */
Int32 get_managed_id(ManagedThread* t);

} // namespace thread

} // namespace cil2cpp

// Type alias for generated code
using System_Threading_Thread = cil2cpp::ManagedThread;
