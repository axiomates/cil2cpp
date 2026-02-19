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
    Closed   = 1,
    Disposed = 2,
    RefCountOne = 4,  // initial ref count
};

/// SafeHandle internal call implementations
namespace icall {

/// SafeHandle..ctor(IntPtr invalidHandleValue, bool ownsHandle)
/// Sets the initial handle value, state, and ownership flag.
/// Layout must match generated struct:
///   TypeInfo* __type_info; UInt32 __sync_block;
///   intptr_t f_handle; int32_t f_state; bool f_ownsHandle; bool f_fullyInitialized;
inline void SafeHandle__ctor(void* self, intptr_t invalidHandleValue, bool ownsHandle) {
    // Field offsets: after object header (TypeInfo* + UInt32 __sync_block + padding)
    // Object header: 8 (TypeInfo*) + 4 (sync_block) = 12 bytes, aligned to 16
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
