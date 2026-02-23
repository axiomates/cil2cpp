/**
 * CIL2CPP Runtime - GCHandle Implementation
 *
 * Handle table backed by a vector + free list.
 * - Normal/Pinned: strong reference via GC_MALLOC_UNCOLLECTABLE slot.
 * - Weak/WeakTrackResurrection: uses BoehmGC's GC_register_disappearing_link
 *   so the handle is automatically cleared when the object is collected.
 *   BoehmGC treats both Weak and WeakTrackResurrection the same (no distinction
 *   for resurrection tracking in conservative GC).
 */

#include <cil2cpp/gchandle.h>
#include <gc.h>

#include <mutex>
#include <vector>

namespace cil2cpp {

namespace {

struct HandleEntry {
    void* object = nullptr;
    GCHandleType type = GCHandleType::Normal;
    bool allocated = false;
};

std::mutex g_handle_mutex;
std::vector<HandleEntry> g_handle_table;
std::vector<int32_t> g_free_list;

// Handle encoding: index + 1 (0 is reserved for "no handle")
intptr_t encode_handle(int32_t index) {
    return static_cast<intptr_t>(index + 1);
}

int32_t decode_handle(intptr_t handle) {
    return static_cast<int32_t>(handle) - 1;
}

bool is_weak_type(GCHandleType type) {
    return type == GCHandleType::Weak || type == GCHandleType::WeakTrackResurrection;
}

} // anonymous namespace

void gchandle_init() {
    std::lock_guard lock(g_handle_mutex);
    g_handle_table.clear();
    g_free_list.clear();
    // Pre-allocate some slots
    g_handle_table.reserve(64);
}

intptr_t gchandle_alloc(void* obj, GCHandleType type) {
    std::lock_guard lock(g_handle_mutex);

    int32_t index;
    if (!g_free_list.empty()) {
        index = g_free_list.back();
        g_free_list.pop_back();
    } else {
        index = static_cast<int32_t>(g_handle_table.size());
        g_handle_table.push_back({});
    }

    g_handle_table[index] = { obj, type, true };

    // For weak handles, register a disappearing link with BoehmGC.
    // When the object is collected, BoehmGC will set the link to NULL.
    if (is_weak_type(type) && obj != nullptr) {
        GC_register_disappearing_link(&g_handle_table[index].object);
    }

    return encode_handle(index);
}

void gchandle_free(intptr_t handle) {
    std::lock_guard lock(g_handle_mutex);

    auto index = decode_handle(handle);
    if (index < 0 || index >= static_cast<int32_t>(g_handle_table.size()))
        return;

    auto& entry = g_handle_table[index];
    if (entry.allocated) {
        // Unregister disappearing link for weak handles
        if (is_weak_type(entry.type) && entry.object != nullptr) {
            GC_unregister_disappearing_link(&entry.object);
        }
        entry.allocated = false;
        entry.object = nullptr;
        g_free_list.push_back(index);
    }
}

void* gchandle_get(intptr_t handle) {
    std::lock_guard lock(g_handle_mutex);

    auto index = decode_handle(handle);
    if (index < 0 || index >= static_cast<int32_t>(g_handle_table.size()))
        return nullptr;

    auto& entry = g_handle_table[index];
    return entry.allocated ? entry.object : nullptr;
}

void gchandle_set(intptr_t handle, void* obj) {
    std::lock_guard lock(g_handle_mutex);

    auto index = decode_handle(handle);
    if (index < 0 || index >= static_cast<int32_t>(g_handle_table.size()))
        return;

    auto& entry = g_handle_table[index];
    if (entry.allocated) {
        // Update disappearing link for weak handles
        if (is_weak_type(entry.type)) {
            if (entry.object != nullptr) {
                GC_unregister_disappearing_link(&entry.object);
            }
            entry.object = obj;
            if (obj != nullptr) {
                GC_register_disappearing_link(&entry.object);
            }
        } else {
            entry.object = obj;
        }
    }
}

bool gchandle_is_allocated(intptr_t handle) {
    std::lock_guard lock(g_handle_mutex);

    auto index = decode_handle(handle);
    if (index < 0 || index >= static_cast<int32_t>(g_handle_table.size()))
        return false;

    return g_handle_table[index].allocated;
}

namespace icall {

intptr_t GCHandle_InternalAlloc(void* obj, Int32 type) {
    return gchandle_alloc(obj, static_cast<GCHandleType>(type));
}

void GCHandle_InternalFree(intptr_t handle) {
    gchandle_free(handle);
}

void GCHandle_InternalSet(intptr_t handle, void* obj) {
    gchandle_set(handle, obj);
}

void* GCHandle_InternalGet(intptr_t handle) {
    return gchandle_get(handle);
}

} // namespace icall
} // namespace cil2cpp
