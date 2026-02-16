/**
 * CIL2CPP Runtime - Cancellation Token Implementation
 */

#include "cil2cpp/cancellation.h"
#include "cil2cpp/task.h"
#include "cil2cpp/gc.h"
#include "cil2cpp/type_info.h"
#include "cil2cpp/threadpool.h"
#include <atomic>
#include <thread>

namespace cil2cpp {

// Forward declaration
extern TypeInfo CancellationTokenSource_TypeInfo;

CancellationTokenSource* cts_create() {
    auto* cts = static_cast<CancellationTokenSource*>(
        gc::alloc(sizeof(CancellationTokenSource), &CancellationTokenSource_TypeInfo));
    cts->f_state = 0;
    return cts;
}

void cts_cancel(CancellationTokenSource* cts) {
    if (!cts) return;
    // Atomic CAS: only cancel if currently active (state 0 -> 1)
    auto* state = reinterpret_cast<std::atomic<Int32>*>(&cts->f_state);
    Int32 expected = 0;
    state->compare_exchange_strong(expected, 1);
}

// Context struct for delayed cancellation (avoids std::pair template issues)
struct CancelAfterContext {
    CancellationTokenSource* cts;
    Int32 delay_ms;
};

void cts_cancel_after(CancellationTokenSource* cts, Int32 milliseconds) {
    if (!cts || milliseconds < 0) return;
    auto* ctx = new CancelAfterContext{ cts, milliseconds };
    threadpool::queue_work([](void* arg) {
        auto* data = static_cast<CancelAfterContext*>(arg);
        std::this_thread::sleep_for(std::chrono::milliseconds(data->delay_ms));
        cts_cancel(data->cts);
        delete data;
    }, ctx);
}

void ct_throw_if_cancellation_requested(CancellationToken token) {
    if (ct_is_cancellation_requested(token)) {
        throw_operation_canceled();
    }
}

// ===== TaskCompletionSource =====

Task* tcs_create() {
    return task_create_pending();
}

void tcs_set_result(Task* task) {
    task_complete(task);
}

void tcs_set_exception(Task* task, Exception* ex) {
    task_fault(task, ex);
}

void tcs_set_canceled(Task* task) {
    // Fault with OperationCanceledException
    auto* ex = static_cast<Exception*>(
        gc::alloc(sizeof(OperationCanceledException), nullptr));
    ex->__type_info = nullptr;
    ex->__sync_block = 0;
    ex->f_message = string_create_utf8("The operation was canceled.");
    ex->f_innerException = nullptr;
    task_fault(task, ex);
}

Boolean tcs_try_set_result(Task* task) {
    if (!task || task->f_status != 0) return false;
    task_complete(task);
    return true;
}

Boolean tcs_try_set_exception(Task* task, Exception* ex) {
    if (!task || task->f_status != 0) return false;
    task_fault(task, ex);
    return true;
}

Boolean tcs_try_set_canceled(Task* task) {
    if (!task || task->f_status != 0) return false;
    tcs_set_canceled(task);
    return true;
}

// TypeInfo for CancellationTokenSource
TypeInfo CancellationTokenSource_TypeInfo = {
    .name = "CancellationTokenSource",
    .namespace_name = "System.Threading",
    .full_name = "System.Threading.CancellationTokenSource",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(CancellationTokenSource),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

} // namespace cil2cpp
