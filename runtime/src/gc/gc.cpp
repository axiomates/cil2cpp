/**
 * CIL2CPP Runtime - Garbage Collector Implementation (BoehmGC)
 *
 * Thin wrapper around the Boehm-Demers-Weiser conservative GC.
 * BoehmGC automatically scans the stack, global variables, and
 * heap-allocated memory for pointers -no manual root registration
 * or mark bitmaps needed.
 */

#include <cil2cpp/gc.h>
#include <cil2cpp/object.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/array.h>
#include <cil2cpp/exception.h>

#include <gc.h>

namespace cil2cpp {
namespace gc {

void init(const GCConfig&) {
    GC_INIT();
    GC_enable_incremental();
    GC_allow_register_threads();
}

void shutdown() {
    // BoehmGC cleans up at process exit; nothing to do here.
}

void* alloc(size_t size, TypeInfo* type) {
    // GC_MALLOC returns zeroed, GC-tracked memory
    void* memory = GC_MALLOC(size);
    if (!memory) {
        return nullptr;
    }

    // Initialize object header
    Object* obj = static_cast<Object*>(memory);
    obj->__type_info = type;
    obj->__sync_block = 0;

    // Register finalizer if the type has one
#ifdef _MSC_VER
#pragma warning(push)
// C4611: setjmp + C++ destructors interaction. Safe here because the TRY/CATCH
// scope contains only raw pointers (no RAII objects), so longjmp won't skip any
// destructors. setjmp/longjmp is our exception mechanism (like Unity IL2CPP).
#pragma warning(disable: 4611)
#endif
    if (type && type->finalizer) {
        GC_register_finalizer_no_order(
            memory,
            [](void* p, void*) {
                Object* o = static_cast<Object*>(p);
                if (o->__type_info && o->__type_info->finalizer) {
                    // Guard against exceptions escaping the finalizer.
                    // CIL2CPP uses setjmp/longjmp; an unguarded throw would
                    // longjmp through the BoehmGC callback stack (UB).
                    CIL2CPP_TRY
                        o->__type_info->finalizer(o);
                    CIL2CPP_CATCH_ALL
                        // Swallow: ECMA-335 - exceptions in finalizers are ignored
                    CIL2CPP_END_TRY
                }
            },
            nullptr, nullptr, nullptr
        );
    }
#ifdef _MSC_VER
#pragma warning(pop)
#endif

    return memory;
}

void* alloc_array(TypeInfo* element_type, size_t length) {
    size_t element_size = element_type->element_size;
    if (element_size == 0) {
        // Reference types have element_size 0 in TypeInfo; arrays of references
        // store pointer-sized entries (each element is an Object*).
        element_size = sizeof(void*);
    }
    size_t total_size = sizeof(Array) + (element_size * length);

    Array* arr = static_cast<Array*>(alloc(total_size, element_type));
    if (arr) {
        arr->element_type = element_type;
        arr->length = static_cast<Int32>(length);
    }

    return arr;
}

void collect() {
    GC_gcollect();
}

void set_incremental(bool enabled) {
    if (enabled) {
        GC_enable_incremental();
    } else {
        // BoehmGC does not provide a disable_incremental API;
        // once enabled, incremental mode stays active.
    }
}

bool collect_a_little() {
    return GC_collect_a_little() != 0;
}

void register_thread() {
    struct GC_stack_base sb;
    GC_get_stack_base(&sb);
    GC_register_my_thread(&sb);
}

void unregister_thread() {
    GC_unregister_my_thread();
}

void add_root(void**) {
    // No-op: BoehmGC conservatively scans globals and stack
}

void remove_root(void**) {
    // No-op
}

GCStats get_stats() {
    return GCStats{
        .total_allocated = GC_get_total_bytes(),
        .total_freed = 0,  // BoehmGC doesn't track freed bytes directly
        .current_heap_size = GC_get_heap_size(),
        .collection_count = static_cast<size_t>(GC_get_gc_no()),
        .total_pause_time_ms = 0.0
    };
}

} // namespace gc
} // namespace cil2cpp
