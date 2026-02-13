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

#include <gc.h>

namespace cil2cpp {
namespace gc {

void init(const GCConfig&) {
    GC_INIT();
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
    if (type && type->finalizer) {
        GC_register_finalizer_no_order(
            memory,
            [](void* p, void*) {
                Object* o = static_cast<Object*>(p);
                if (o->__type_info && o->__type_info->finalizer) {
                    o->__type_info->finalizer(o);
                }
            },
            nullptr, nullptr, nullptr
        );
    }

    return memory;
}

void* alloc_array(TypeInfo* element_type, size_t length) {
    size_t element_size = element_type->element_size;
    if (element_size == 0) {
        element_size = sizeof(void*);  // Reference type
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
