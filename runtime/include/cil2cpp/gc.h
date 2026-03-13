/**
 * CIL2CPP Runtime - Garbage Collector (BoehmGC wrapper)
 */

#pragma once

#include "types.h"

namespace cil2cpp {
namespace gc {

/**
 * GC configuration options.
 * BoehmGC manages heap sizing automatically; this struct is kept
 * for API compatibility.
 */
struct GCConfig {};

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
 * Trigger a full garbage collection cycle.
 */
void collect();

/**
 * Enable or disable incremental garbage collection.
 * Incremental mode spreads collection work across multiple small steps,
 * reducing pause times. Enabled by default at init.
 * Note: BoehmGC does not support disabling incremental mode once enabled.
 */
void set_incremental(bool enabled);

/**
 * Perform a small amount of incremental collection work.
 * @return true if more work remains, false if collection is complete.
 */
bool collect_a_little();

/**
 * Register the current thread with the GC.
 * Must be called at the start of every managed thread (except the main thread).
 * Required for BoehmGC to scan the thread's stack for roots.
 */
void register_thread();

/**
 * Unregister the current thread from the GC.
 * Must be called before a managed thread exits.
 */
void unregister_thread();

/**
 * Add a root reference (no-op -BoehmGC scans roots automatically).
 */
void add_root(void** root);

/**
 * Remove a root reference (no-op -BoehmGC scans roots automatically).
 */
void remove_root(void** root);

/**
 * Write barrier (no-op -BoehmGC does not require write barriers).
 */
inline void write_barrier(Object* obj, Object* value) {
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

/**
 * GC.SuppressFinalize — tells the GC not to run the finalizer for obj.
 * With BoehmGC, we unregister the finalizer callback via GC_register_finalizer.
 */
void gc_suppress_finalize(void* obj);

/**
 * GC.KeepAlive — prevents the object from being collected before this point.
 * With BoehmGC (conservative scanning), this is effectively a no-op since
 * BoehmGC scans the entire stack for roots. The volatile write ensures
 * the compiler doesn't optimize away the variable.
 */
inline void gc_keep_alive(void* obj) {
    static volatile void* _gc_keep_alive_sink;
    _gc_keep_alive_sink = obj;
}

/// No-op GC operation (used for finalizer-related stubs with BoehmGC)
inline void gc_noop() {}
inline void gc_noop(void*) {}

/// GC.GetTotalMemory — returns approximate heap size
inline Int64 gc_get_total_memory(bool forceFullCollection) {
    if (forceFullCollection) gc::collect();
    return static_cast<Int64>(gc::get_stats().current_heap_size);
}

/// GC.GetTotalMemory (no param version)
inline Int64 gc_get_total_memory_simple() {
    return static_cast<Int64>(gc::get_stats().current_heap_size);
}

/// GC.GetMemoryInfo — fills GCMemoryInfoData with BoehmGC-available stats.
/// BoehmGC doesn't track most .NET GC metrics, so most fields stay zero.
/// Fields after Object header: 9×int64, 2×int32, 1×uint8.
inline void gc_get_memory_info(void* data, Int32 /*kind*/) {
    if (!data) return;
    auto stats = gc::get_stats();
    // GCMemoryInfoData fields start after the managed object header
    // (TypeInfo* + UInt32 sync_block + padding to align int64_t).
    constexpr size_t header_size = (sizeof(TypeInfo*) + sizeof(UInt32) + 7) & ~size_t(7);
    auto* fields = reinterpret_cast<int64_t*>(
        static_cast<char*>(data) + header_size);
    // fields[0] = highMemoryLoadThresholdBytes  (leave 0)
    // fields[1] = totalAvailableMemoryBytes     (leave 0)
    // fields[2] = memoryLoadBytes               (leave 0)
    // fields[3] = heapSizeBytes
    fields[3] = static_cast<int64_t>(stats.current_heap_size);
    // fields[4] = fragmentedBytes               (leave 0)
    // fields[5] = totalCommittedBytes
    fields[5] = static_cast<int64_t>(stats.current_heap_size);
}

// GC.AllocateUninitializedArray<T>() is handled as a compiler intrinsic —
// the IR builder replaces it with array_create(&ElementType_TypeInfo, length).
// No runtime ICall needed.

} // namespace cil2cpp
