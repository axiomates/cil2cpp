/**
 * CIL2CPP Runtime - GCHandle Implementation
 *
 * Handle table backed by a vector + free list.
 * - Normal/Pinned: strong reference (kept in GC-visible table).
 * - Weak/WeakTrackResurrection: stored as raw pointers. Currently
 *   weak handles do NOT auto-clear when the object is collected.
 *   This is a known limitation — BoehmGC's GC_register_disappearing_link
 *   deadlocks when other threads are blocked in native calls (e.g. accept()).
 *   Proper fix: GC-safe transitions before blocking native calls.
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

// Handle encoding: (index + 1) << 1 — always even.
// .NET's managed GCHandle uses the low bit as a "pinned" flag
// (OR 1 for pinned, AND ~1 in GetHandleValue). Our internal handles
// must be even so the low bit is available for that flag.
intptr_t encode_handle(int32_t index) {
    return static_cast<intptr_t>((index + 1) << 1);
}

int32_t decode_handle(intptr_t handle) {
    return static_cast<int32_t>(handle >> 1) - 1;
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

    // NOTE: We intentionally do NOT call GC_register_disappearing_link here.
    // That function acquires BoehmGC's internal allocation lock, which can
    // deadlock if another GC-registered thread is blocked in a native call
    // (e.g. accept(), recv()). BoehmGC tries to stop-the-world to synchronize,
    // but can't properly handle threads suspended in kernel calls.
    //
    // This means weak handles won't auto-clear on collection. Since BoehmGC
    // is conservative GC (may not collect objects with ambiguous roots anyway),
    // this is acceptable. The proper fix is GC-safe transitions before blocking
    // native calls (future work).

    return encode_handle(index);
}

void gchandle_free(intptr_t handle) {
    std::lock_guard lock(g_handle_mutex);

    auto index = decode_handle(handle);
    if (index < 0 || index >= static_cast<int32_t>(g_handle_table.size()))
        return;

    auto& entry = g_handle_table[index];
    if (entry.allocated) {
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
        entry.object = obj;
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
