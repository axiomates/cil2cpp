/**
 * CIL2CPP Runtime - Async Task Types
 * Supports both synchronous and true async execution models.
 */

#pragma once

#include "object.h"
#include "exception.h"

#include <atomic>

namespace cil2cpp {

struct Array;
struct Delegate;

// Forward declare for continuation storage
struct TaskContinuation;

/**
 * Task (reference type, GC-allocated).
 * Non-generic base; generic Task<T> is monomorphized by the compiler.
 *
 * NOTE: Task does NOT inherit from Object to avoid MSVC tail-padding mismatch
 * between the runtime struct (with inheritance padding) and generated flat structs.
 * The __type_info and __sync_block fields are inlined at the same offsets as Object.
 *
 * BCL fields use Object* to match the IL metadata types (BCL uses `object` for many fields).
 */
struct Task {
    TypeInfo* __type_info;              // Object header field 1
    UInt32 __sync_block;                // Object header field 2
    // --- Custom runtime fields (used by task_complete/task_fault/etc.) ---
    Int32 f_status;                     // 0=created, 1=completed, 2=faulted
    Exception* f_exception;
    TaskContinuation* f_continuations;  // Linked list of continuations
    void* f_lock;                       // std::mutex* for thread-safe completion
    // --- BCL fields (accessed by methods compiled from BCL IL) ---
    // Use void* for polymorphic fields: any pointer type converts to void* implicitly.
    // BCL IL uses various concrete types for these fields across different methods.
    Int32 f_m_taskId;                   // Task.m_taskId (int)
    Delegate* f_m_action;               // Task.m_action (Delegate)
    void* f_m_stateObject;             // Task.m_stateObject (object)
    void* f_m_taskScheduler;           // Task.m_taskScheduler (TaskScheduler)
    volatile Int32 f_m_stateFlags;      // Task.m_stateFlags (int, volatile)
    void* f_m_contingentProperties;    // Task.m_contingentProperties (ContingentProperties)
    void* f_m_continuationObject;      // Task.m_continuationObject (object)
};

/**
 * A continuation callback registered on a Task.
 * Stored as a singly-linked list (GC-allocated).
 */
struct TaskContinuation {
    void (*callback)(void*);
    void* state;
    TaskContinuation* next;
};

/**
 * TaskAwaiter (value type, stack-allocated).
 */
struct TaskAwaiter {
    Task* f_task;
};

/**
 * AsyncTaskMethodBuilder (value type, stack-allocated).
 */
struct AsyncTaskMethodBuilder {
    Task* f_task;
};

/** Create a new completed Task (non-generic only). */
Task* task_create_completed();

/** Get or create a singleton completed Task (cached). */
Task* task_get_completed();

/** Create a new pending (incomplete) Task (non-generic only). */
Task* task_create_pending();

/**
 * Initialize an already-allocated Task as pending (status=0, mutex created).
 * Use this for generic Task<T> where the caller allocates with the correct size.
 */
void task_init_pending(Task* t);

/**
 * Initialize an already-allocated Task as completed (status=1, no mutex).
 * Use this for generic Task<T> where the caller allocates with the correct size.
 */
void task_init_completed(Task* t);

/** Check if a Task has completed (status >= 1). Thread-safe (acquire load). */
inline bool task_is_completed(Task* t) {
    if (!t) return false;
    return std::atomic_ref<Int32>(t->f_status).load(std::memory_order_acquire) >= 1;
}

/**
 * Complete a task (set status=1) and run all registered continuations.
 * Thread-safe.
 */
void task_complete(Task* t);

/**
 * Fault a task (set status=2, store exception) and run continuations.
 * Thread-safe.
 */
void task_fault(Task* t, Exception* ex);

/**
 * Register a continuation to run when the task completes.
 * If task is already complete, runs immediately on calling thread.
 * Thread-safe.
 */
void task_add_continuation(Task* t, void (*callback)(void*), void* state);

/**
 * Wait (block) until a task completes.
 * Uses spinning + yield.
 */
void task_wait(Task* t);

// ===== Combinators =====

/** Task that completes when all tasks in the array complete. */
Task* task_when_all(Array* tasks);

/** Task that completes when any task in the array completes. */
Task* task_when_any(Array* tasks);

/** Task that completes after a delay in milliseconds. */
Task* task_delay(Int32 milliseconds);

/** Run a delegate on the thread pool and return a Task. */
Task* task_run(Object* del);

} // namespace cil2cpp

// Mangled-name aliases for generated code
using System_Threading_Tasks_Task = cil2cpp::Task;
using System_Runtime_CompilerServices_TaskAwaiter = cil2cpp::TaskAwaiter;
using System_Runtime_CompilerServices_AsyncTaskMethodBuilder = cil2cpp::AsyncTaskMethodBuilder;

// Phase IV.1: IAsyncStateMachine removed from runtime — now compiled from BCL IL
