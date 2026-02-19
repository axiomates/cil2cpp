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

// FIXME: Missing SafeHandle ICalls (only .ctor is implemented):
//   - DangerousGetHandle()
//   - SetHandle(IntPtr)
//   - DangerousAddRef(ref bool)
//   - DangerousRelease()
//   - Dispose()
//   - get_IsInvalid()
//   - get_IsClosed()
//   - SetHandleAsInvalid()

/// SafeHandle internal call implementations
namespace icall {

/// SafeHandle..ctor(IntPtr invalidHandleValue, bool ownsHandle)
/// Sets the initial handle value, state, and ownership flag.
/// Layout must match generated struct:
///   TypeInfo* __type_info; UInt32 __sync_block;
///   intptr_t f_handle; int32_t f_state; bool f_ownsHandle; bool f_fullyInitialized;
inline void SafeHandle__ctor(void* self, intptr_t invalidHandleValue, bool ownsHandle) {
    // FIXME: Layout assumes MSVC x64 padding (Object header = 12 bytes, padded to 16
    // before intptr_t). May need adjustment for other compilers/platforms where
    // alignment or padding rules differ.
    struct SafeHandleLayout {
        TypeInfo* __type_info;
        UInt32 __sync_block;
        intptr_t f_handle;
        int32_t f_state;
        bool f_ownsHandle;
        bool f_fullyInitialized;
    };
    auto* sh = static_cast<SafeHandleLayout*>(self);
    sh->f_handle = invalidHandleValue;
    sh->f_state = static_cast<int32_t>(SafeHandleState::RefCountOne);
    sh->f_ownsHandle = ownsHandle;
    sh->f_fullyInitialized = true;
}

} // namespace icall
} // namespace cil2cpp
