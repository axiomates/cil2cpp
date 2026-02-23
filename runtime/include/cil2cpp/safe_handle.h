/**
 * CIL2CPP Runtime - SafeHandle Support
 *
 * ECMA-335 System.Runtime.InteropServices.SafeHandle
 * Provides safe OS handle wrapping with reference counting.
 */

#pragma once

#include "types.h"
#include "object.h"

namespace cil2cpp {

/// SafeHandle state bits (matches .NET internal StateBits)
enum class SafeHandleState : int32_t {
    Closed      = 1 << 0,  // Handle has been closed
    Disposed    = 1 << 1,  // Dispose() has been called
    RefCountOne = 1 << 2,  // Initial reference count (bit 2 = first ref)
};

/// Common layout for SafeHandle-derived types.
/// Generated code produces matching flat structs, so we access fields by offset.
struct SafeHandleLayout {
    TypeInfo* __type_info;
    UInt32 __sync_block;
    intptr_t f_handle;
    int32_t f_state;
    bool f_ownsHandle;
    bool f_fullyInitialized;
};

/// SafeHandle internal call implementations
namespace icall {

/// SafeHandle..ctor(IntPtr invalidHandleValue, bool ownsHandle)
inline void SafeHandle__ctor(void* self, intptr_t invalidHandleValue, bool ownsHandle) {
    auto* sh = static_cast<SafeHandleLayout*>(self);
    sh->f_handle = invalidHandleValue;
    sh->f_state = static_cast<int32_t>(SafeHandleState::RefCountOne);
    sh->f_ownsHandle = ownsHandle;
    sh->f_fullyInitialized = true;
}

/// SafeHandle.DangerousGetHandle() → IntPtr
inline intptr_t SafeHandle_DangerousGetHandle(void* self) {
    return static_cast<SafeHandleLayout*>(self)->f_handle;
}

/// SafeHandle.SetHandle(IntPtr handle)
inline void SafeHandle_SetHandle(void* self, intptr_t handle) {
    static_cast<SafeHandleLayout*>(self)->f_handle = handle;
}

/// SafeHandle.DangerousAddRef(ref bool success)
/// Simplified: just set success = true (no ref counting in AOT)
inline void SafeHandle_DangerousAddRef(void* /*self*/, bool* success) {
    // TODO: Implement proper reference counting when needed
    if (success) *success = true;
}

/// SafeHandle.DangerousRelease()
/// Simplified: no-op (no ref counting in AOT)
inline void SafeHandle_DangerousRelease(void* /*self*/) {
    // TODO: Implement proper reference counting and ReleaseHandle() callback
}

/// SafeHandle.get_IsClosed() → bool
inline bool SafeHandle_get_IsClosed(void* self) {
    auto state = static_cast<SafeHandleLayout*>(self)->f_state;
    return (state & static_cast<int32_t>(SafeHandleState::Closed)) != 0;
}

/// SafeHandle.SetHandleAsInvalid()
inline void SafeHandle_SetHandleAsInvalid(void* self) {
    auto* sh = static_cast<SafeHandleLayout*>(self);
    sh->f_state |= static_cast<int32_t>(SafeHandleState::Closed);
}

/// SafeHandle.Dispose(bool disposing)
/// Simplified: just mark as disposed + closed
inline void SafeHandle_Dispose(void* self, bool /*disposing*/) {
    auto* sh = static_cast<SafeHandleLayout*>(self);
    sh->f_state |= static_cast<int32_t>(SafeHandleState::Disposed) |
                    static_cast<int32_t>(SafeHandleState::Closed);
    // TODO: Call ReleaseHandle() virtual when proper ref counting is implemented
}

} // namespace icall
} // namespace cil2cpp
