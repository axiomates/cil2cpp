/**
 * CIL2CPP Runtime - Garbage Collector
 */

#pragma once

#include "types.h"

namespace cil2cpp {
namespace gc {

/**
 * GC configuration options.
 */
struct GCConfig {
    size_t initial_heap_size = 16 * 1024 * 1024;  // 16 MB
    size_t max_heap_size = 512 * 1024 * 1024;     // 512 MB
    float gc_threshold = 0.75f;                    // Trigger GC at 75% usage
};

/**
 * Initialize the garbage collector.
 */
void init(const GCConfig& config = GCConfig{});

/**
 * Shutdown the garbage collector.
 */
void shutdown();

/**
 * Allocate memory for a managed object.
 * @param size Size in bytes to allocate
 * @param type Type information for the object
 * @return Pointer to allocated memory, or nullptr on failure
 */
void* alloc(size_t size, TypeInfo* type);

/**
 * Allocate memory for an array.
 * @param element_type Type of array elements
 * @param length Number of elements
 * @return Pointer to allocated array
 */
void* alloc_array(TypeInfo* element_type, size_t length);

/**
 * Trigger a garbage collection cycle.
 */
void collect();

/**
 * Add a root reference (global/static variables).
 */
void add_root(void** root);

/**
 * Remove a root reference.
 */
void remove_root(void** root);

/**
 * Write barrier - called when writing a reference field.
 * Required for generational GC.
 */
inline void write_barrier(Object* obj, Object* value) {
    // TODO: Implement for generational GC
    (void)obj;
    (void)value;
}

/**
 * GC statistics.
 */
struct GCStats {
    size_t total_allocated;
    size_t total_freed;
    size_t current_heap_size;
    size_t collection_count;
    double total_pause_time_ms;
};

/**
 * Get current GC statistics.
 */
GCStats get_stats();

} // namespace gc
} // namespace cil2cpp
