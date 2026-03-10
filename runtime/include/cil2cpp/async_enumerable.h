/**
 * CIL2CPP Runtime - Async Enumerable Support
 * ValueTask, ValueTask<T>, AsyncIteratorMethodBuilder types.
 *
 * IAsyncEnumerable<T> / IAsyncEnumerator<T> state machines compile from IL.
 * This header provides the infrastructure types they reference.
 */

#pragma once

#include "object.h"
#include "task.h"

namespace cil2cpp {

// ── Thread-local for async iterator promise -> ValueTask bridge ──
// ManualResetValueTaskSourceCore.Reset() stores the pending Task here.
// ValueTask<bool>.ctor(IValueTaskSource, short) picks it up.
// This is safe because the sequence Reset -> MoveNext -> ctor is synchronous
// within a single MoveNextAsync() call on one thread.
extern thread_local Task* g_async_iter_current_task;

// ── Non-generic ValueTask (for DisposeAsync) ──
// Field names match BCL IL: System.Threading.Tasks.ValueTask._obj, _token, _continueOnCapturedContext
struct ValueTaskVoid {
    Object* f__obj;                     // IValueTaskSource or Task (null = completed)
    int16_t f__token;
    bool f__continueOnCapturedContext;
};

// ── Non-generic ValueTaskAwaiter ──
// Field name matches BCL IL: System.Runtime.CompilerServices.ValueTaskAwaiter._value
struct ValueTaskAwaiterVoid {
    ValueTaskVoid f__value;
};

// ── AsyncIteratorMethodBuilder ──
// Field name matches BCL IL: System.Runtime.CompilerServices.AsyncIteratorMethodBuilder._task
// BCL IL field type is Task<VoidTaskResult>, but generated code uses the concrete generic struct
// (System_Threading_Tasks_Task_1_VoidTaskResult) which is unrelated to cil2cpp::Task in C++.
// Use void* to accept any task pointer type without cast errors.
struct AsyncIteratorMethodBuilder {
    void* f_m_task;  // BCL IL: Task<VoidTaskResult> _task → mangled as f_m_task
};

} // namespace cil2cpp

// Mangled-name aliases for generated code (non-generic types)
using System_Threading_Tasks_ValueTask = cil2cpp::ValueTaskVoid;
using System_Runtime_CompilerServices_ValueTaskAwaiter = cil2cpp::ValueTaskAwaiterVoid;
using System_Runtime_CompilerServices_AsyncIteratorMethodBuilder = cil2cpp::AsyncIteratorMethodBuilder;
