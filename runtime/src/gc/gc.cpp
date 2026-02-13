/**
 * CIL2CPP Runtime - Garbage Collector Implementation
 *
 * MVP: Simple mark-sweep collector
 * TODO: Upgrade to generational GC or integrate Boehm GC
 */

#include <cil2cpp/gc.h>
#include <cil2cpp/object.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/array.h>

#include <vector>
#include <mutex>
#include <cstdlib>
#include <cstring>

namespace cil2cpp {
namespace gc {

// GC state
static std::vector<Object*> g_all_objects;
static std::vector<void**> g_roots;
static std::mutex g_gc_mutex;
static size_t g_allocated_size = 0;
static size_t g_freed_size = 0;
static size_t g_collection_count = 0;
static GCConfig g_config;
static bool g_initialized = false;

void init(const GCConfig& config) {
    std::lock_guard<std::mutex> lock(g_gc_mutex);
    g_config = config;
    g_all_objects.reserve(1024);
    g_roots.reserve(64);
    g_initialized = true;
}

void shutdown() {
    std::lock_guard<std::mutex> lock(g_gc_mutex);

    // Free all remaining objects
    for (Object* obj : g_all_objects) {
        // Call finalizer if present
        if (obj->__type_info && obj->__type_info->finalizer) {
            obj->__type_info->finalizer(obj);
        }
        std::free(obj);
    }

    g_all_objects.clear();
    g_roots.clear();
    g_initialized = false;
}

void* alloc(size_t size, TypeInfo* type) {
    std::lock_guard<std::mutex> lock(g_gc_mutex);

    // Check if we need to collect
    if (g_allocated_size > g_config.initial_heap_size * g_config.gc_threshold) {
        // Unlock for collection (TODO: proper synchronization)
    }

    // Allocate memory
    void* memory = std::malloc(size);
    if (!memory) {
        // Try GC and retry
        collect();
        memory = std::malloc(size);
        if (!memory) {
            return nullptr;  // Out of memory
        }
    }

    // Zero-initialize
    std::memset(memory, 0, size);

    // Initialize object header
    Object* obj = static_cast<Object*>(memory);
    obj->__type_info = type;
    obj->__gc_mark = 0;
    obj->__sync_block = 0;

    // Track allocation
    g_all_objects.push_back(obj);
    g_allocated_size += size;

    return memory;
}

void* alloc_array(TypeInfo* element_type, size_t length) {
    // Calculate array size: header + elements
    size_t element_size = element_type->element_size;
    if (element_size == 0) {
        element_size = sizeof(void*);  // Reference type
    }
    size_t total_size = sizeof(Array) + (element_size * length);

    Array* arr = static_cast<Array*>(alloc(total_size, nullptr));  // TODO: Array type info
    if (arr) {
        arr->element_type = element_type;
        arr->length = static_cast<Int32>(length);
    }

    return arr;
}

static void mark(Object* obj) {
    if (!obj || obj->__gc_mark == 1) {
        return;
    }

    obj->__gc_mark = 1;

    // Traverse reference fields
    TypeInfo* type = obj->__type_info;
    if (!type || !type->fields) {
        return;
    }

    char* obj_ptr = reinterpret_cast<char*>(obj);
    for (UInt32 i = 0; i < type->field_count; i++) {
        FieldInfo* field = &type->fields[i];

        // Check if field is a reference type
        if (field->field_type && !(field->field_type->flags & TypeFlags::ValueType)) {
            Object** field_ptr = reinterpret_cast<Object**>(obj_ptr + field->offset);
            mark(*field_ptr);
        }
    }
}

void collect() {
    std::lock_guard<std::mutex> lock(g_gc_mutex);

    g_collection_count++;

    // Mark phase: mark all reachable objects from roots
    for (void** root : g_roots) {
        if (*root) {
            mark(static_cast<Object*>(*root));
        }
    }

    // Sweep phase: free unmarked objects
    auto it = g_all_objects.begin();
    while (it != g_all_objects.end()) {
        Object* obj = *it;

        if (obj->__gc_mark == 0) {
            // Object is not reachable, free it

            // Call finalizer if present
            if (obj->__type_info && obj->__type_info->finalizer) {
                obj->__type_info->finalizer(obj);
            }

            // Track freed memory
            if (obj->__type_info) {
                g_freed_size += obj->__type_info->instance_size;
            }

            std::free(obj);
            it = g_all_objects.erase(it);
        } else {
            // Reset mark for next collection
            obj->__gc_mark = 0;
            ++it;
        }
    }
}

void add_root(void** root) {
    std::lock_guard<std::mutex> lock(g_gc_mutex);
    g_roots.push_back(root);
}

void remove_root(void** root) {
    std::lock_guard<std::mutex> lock(g_gc_mutex);
    auto it = std::find(g_roots.begin(), g_roots.end(), root);
    if (it != g_roots.end()) {
        g_roots.erase(it);
    }
}

GCStats get_stats() {
    std::lock_guard<std::mutex> lock(g_gc_mutex);
    return GCStats{
        .total_allocated = g_allocated_size,
        .total_freed = g_freed_size,
        .current_heap_size = g_allocated_size - g_freed_size,
        .collection_count = g_collection_count,
        .total_pause_time_ms = 0.0  // TODO: measure pause time
    };
}

} // namespace gc
} // namespace cil2cpp
