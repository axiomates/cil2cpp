/**
 * CIL2CPP Runtime - GCHandle Implementation
 *
 * Provides pinned/normal/weak object handles that prevent GC collection.
 * With BoehmGC (conservative scanning), handles are simulated using a
 * simple handle table since BoehmGC scans all roots automatically.
 */

#pragma once

#include "types.h"

namespace cil2cpp {

/// GCHandleType enum matching System.Runtime.InteropServices.GCHandleType
enum class GCHandleType : Int32 {
    Weak = 0,
    WeakTrackResurrection = 1,
    Normal = 2,
    Pinned = 3
};

/// Initialize the GCHandle subsystem
void gchandle_init();

/// Allocate a GC handle for the given object
/// Returns an opaque handle (intptr_t)
intptr_t gchandle_alloc(void* obj, GCHandleType type);

/// Free a previously allocated GC handle
void gchandle_free(intptr_t handle);

/// Get the object referenced by a handle
void* gchandle_get(intptr_t handle);

/// Set the object referenced by a handle
void gchandle_set(intptr_t handle, void* obj);

/// Check if a handle is allocated
bool gchandle_is_allocated(intptr_t handle);

namespace icall {

/// System.Runtime.InteropServices.GCHandle.InternalAlloc(object, GCHandleType)
intptr_t GCHandle_InternalAlloc(void* obj, Int32 type);

/// System.Runtime.InteropServices.GCHandle.InternalFree(IntPtr)
void GCHandle_InternalFree(intptr_t handle);

/// System.Runtime.InteropServices.GCHandle.InternalSet(IntPtr, object)
void GCHandle_InternalSet(intptr_t handle, void* obj);

/// System.Runtime.InteropServices.GCHandle.InternalGet(IntPtr)
void* GCHandle_InternalGet(intptr_t handle);

} // namespace icall
} // namespace cil2cpp
