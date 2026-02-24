/**
 * CIL2CPP Runtime - Cancellation Token Support
 * Implements CancellationTokenSource (reference type) and CancellationToken (value type).
 *
 * NOTE: CancellationTokenSource does NOT inherit from Object to avoid MSVC
 * tail-padding mismatch (same pattern as Task).
 */

#pragma once

#include "object.h"
#include "exception.h"

namespace cil2cpp {

struct Task;

/**
 * CancellationTokenSource (reference type, GC-allocated).
 * Inlines Object header fields to avoid MSVC tail-padding issues.
 */
struct CancellationTokenSource {
    TypeInfo* __type_info;
    UInt32 __sync_block;
    Int32 f_state;            // _state: 0 = active, 1 = canceled, 2 = disposed
    Boolean f_disposed;       // _disposed
    void* f_timer;            // _timer (ITimer)
    void* f_kernelEvent;      // _kernelEvent (ManualResetEvent)
    void* f_registrations;    // _registrations (Registrations)
};

/**
 * CancellationToken (value type, stack-allocated).
 * Wraps a nullable pointer to a CancellationTokenSource.
 * Default token (no source) is never canceled.
 */
struct CancellationToken {
    CancellationTokenSource* f_source;
};

// ===== CancellationTokenSource API =====

/** Create a new CancellationTokenSource (state=active). */
CancellationTokenSource* cts_create();

/** Cancel the token source (sets state=1, thread-safe). */
void cts_cancel(CancellationTokenSource* cts);

/** Cancel after a delay in milliseconds (thread pool). */
void cts_cancel_after(CancellationTokenSource* cts, Int32 milliseconds);

/** Check if cancellation has been requested. */
inline Boolean cts_is_cancellation_requested(CancellationTokenSource* cts) {
    return cts != nullptr && cts->f_state == 1;
}

/** Dispose the token source (prevents further cancellation). */
inline void cts_dispose(CancellationTokenSource* cts) {
    if (cts) cts->f_state = 2;
}

/** Get the CancellationToken for this source. */
inline CancellationToken cts_get_token(CancellationTokenSource* cts) {
    return CancellationToken{ cts };
}

// ===== CancellationToken API =====

/** Check if cancellation has been requested. */
inline Boolean ct_is_cancellation_requested(CancellationToken token) {
    return token.f_source != nullptr && token.f_source->f_state == 1;
}

/** Check if this token can be canceled (has a non-null source). */
inline Boolean ct_can_be_canceled(CancellationToken token) {
    return token.f_source != nullptr;
}

/** Throw OperationCanceledException if cancellation requested. */
void ct_throw_if_cancellation_requested(CancellationToken token);

/** Get the default (non-cancelable) token. */
inline CancellationToken ct_get_none() {
    return CancellationToken{ nullptr };
}

// ===== TaskCompletionSource API =====
// TaskCompletionSource<T> is essentially a wrapper around Task<T>.
// The compiler generates monomorphized types; these functions operate on Task*.

/** Create a TaskCompletionSource (returns a new pending Task). */
Task* tcs_create();

/** Set the result on a TCS (completes the underlying Task). */
void tcs_set_result(Task* task);

/** Set an exception on a TCS (faults the underlying Task). */
void tcs_set_exception(Task* task, Exception* ex);

/** Set canceled state on a TCS. */
void tcs_set_canceled(Task* task);

/** Try to set result (returns false if already completed). */
Boolean tcs_try_set_result(Task* task);

/** Try to set exception (returns false if already completed). */
Boolean tcs_try_set_exception(Task* task, Exception* ex);

/** Try to set canceled (returns false if already completed). */
Boolean tcs_try_set_canceled(Task* task);

} // namespace cil2cpp

// Mangled-name aliases for generated code
using System_Threading_CancellationTokenSource = cil2cpp::CancellationTokenSource;
using System_Threading_CancellationToken = cil2cpp::CancellationToken;
