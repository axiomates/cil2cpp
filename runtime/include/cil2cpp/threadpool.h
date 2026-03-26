/**
 * CIL2CPP Runtime - Thread Pool
 * Dynamic worker pool with hill-climbing thread count adjustment.
 */

#pragma once

#include "types.h"

#include <cstdint>

namespace cil2cpp {

struct Object;
struct Delegate;
struct Task;

namespace threadpool {

/**
 * Initialize the thread pool.
 * @param num_threads Initial (and minimum) worker count. 0 = hardware_concurrency.
 */
void init(int num_threads = 0);

/** Shutdown the thread pool (waits for pending work to complete). */
void shutdown();

/** Check if thread pool is initialized. */
bool is_initialized();

/**
 * Queue a work item (C function pointer + state).
 * The function will be called on a worker thread.
 */
void queue_work(void (*func)(void*), void* state);

/**
 * Queue a delegate invocation on the thread pool.
 * For Task.Run(() => ...) support.
 */
void queue_delegate(Delegate* del);

// --- Dynamic thread management ---

/** Set minimum thread count (floor for hill-climbing). */
void set_min_threads(int count);

/** Set maximum thread count (ceiling for hill-climbing). */
void set_max_threads(int count);

/** Get minimum thread count. */
int get_min_threads();

/** Get maximum thread count. */
int get_max_threads();

/** Get current live worker count. */
int get_thread_count();

/** Get number of workers currently executing work items. */
int get_active_count();

/** Get monotonically increasing total completed work items. */
int64_t get_completions();

/** Request a new worker thread injection (called by ICall). */
void request_worker();

/** Check if there are pending work items in the queue. */
bool has_pending_work();

/** Notify that work item progress was made (prevents idle shrinking). */
void notify_progress();

} // namespace threadpool
} // namespace cil2cpp
