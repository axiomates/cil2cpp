/**
 * CIL2CPP Runtime - SafeHandle Support
 *
 * ECMA-335 System.Runtime.InteropServices.SafeHandle
 * Provides safe OS handle wrapping with reference counting.
 */

#pragma once

#include "types.h"
#include "object.h"
#include "type_info.h"
#include <atomic>
#include <cstring>

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
/// Atomically increments the reference count. Sets success=false if handle is already closed.
inline void SafeHandle_DangerousAddRef(void* self, bool* success) {
    auto* sh = static_cast<SafeHandleLayout*>(self);
    constexpr int32_t refCountOne = static_cast<int32_t>(SafeHandleState::RefCountOne);
    constexpr int32_t closedBit = static_cast<int32_t>(SafeHandleState::Closed);

    auto* atomic_state = reinterpret_cast<std::atomic<int32_t>*>(&sh->f_state);
    int32_t old_state = atomic_state->load(std::memory_order_relaxed);
    for (;;) {
        if (old_state & closedBit) {
            // Handle already closed — cannot add ref
            if (success) *success = false;
            return;
        }
        int32_t new_state = old_state + refCountOne;
        if (atomic_state->compare_exchange_weak(old_state, new_state,
                std::memory_order_acq_rel, std::memory_order_relaxed)) {
            if (success) *success = true;
            return;
        }
        // old_state updated by CAS failure — retry
    }
}

/// SafeHandle.DangerousRelease()
/// Atomically decrements the reference count. Calls ReleaseHandle() when count reaches zero.
inline void SafeHandle_DangerousRelease(void* self) {
    auto* sh = static_cast<SafeHandleLayout*>(self);
    constexpr int32_t refCountOne = static_cast<int32_t>(SafeHandleState::RefCountOne);
    constexpr int32_t closedBit = static_cast<int32_t>(SafeHandleState::Closed);

    auto* atomic_state = reinterpret_cast<std::atomic<int32_t>*>(&sh->f_state);
    int32_t old_state = atomic_state->load(std::memory_order_relaxed);
    for (;;) {
        int32_t new_state = old_state - refCountOne;
        // Check if ref count is reaching zero (bits 2+ all zero after decrement)
        bool releasing = (new_state >> 2) == 0 && !(old_state & closedBit);
        if (releasing) {
            new_state |= closedBit;
        }
        if (atomic_state->compare_exchange_weak(old_state, new_state,
                std::memory_order_acq_rel, std::memory_order_relaxed)) {
            if (releasing) {
                // Call ReleaseHandle() via vtable
                auto* ti = sh->__type_info;
                if (ti && ti->vtable && sh->f_handle != 0 && sh->f_handle != -1) {
                    int32_t releaseSlot = -1;
                    for (UInt32 i = 0; i < ti->method_count; i++) {
                        if (ti->methods[i].name &&
                            std::strcmp(ti->methods[i].name, "ReleaseHandle") == 0 &&
                            ti->methods[i].vtable_slot >= 0) {
                            releaseSlot = ti->methods[i].vtable_slot;
                            break;
                        }
                    }
                    if (releaseSlot >= 0 &&
                        static_cast<UInt32>(releaseSlot) < ti->vtable->method_count &&
                        ti->vtable->methods[releaseSlot] != nullptr) {
                        auto releaseHandle = reinterpret_cast<bool(*)(void*)>(
                            ti->vtable->methods[releaseSlot]);
                        releaseHandle(self);
                    }
                }
            }
            return;
        }
        // old_state updated by CAS failure — retry
    }
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
/// Calls ReleaseHandle() via vtable lookup to close the OS handle, then marks as disposed.
inline void SafeHandle_Dispose(void* self, bool disposing) {
    auto* sh = static_cast<SafeHandleLayout*>(self);
    // Skip if already closed
    if (sh->f_state & static_cast<int32_t>(SafeHandleState::Closed))
        return;

    // Call ReleaseHandle() via vtable if the handle is valid.
    // SafeFileHandle now has an ICall .ctor that sets f_ownsHandle=true via
    // SafeHandle__ctor. For other SafeHandle-derived types whose .ctor is still
    // stubbed, f_ownsHandle may be false (zero-init). Skip the check for safety.
    if (sh->f_handle != 0 && sh->f_handle != -1) {
        auto* ti = sh->__type_info;
        if (ti && ti->vtable) {
            // Look up ReleaseHandle vtable slot by name from MethodInfo metadata
            int32_t releaseSlot = -1;
            for (UInt32 i = 0; i < ti->method_count; i++) {
                if (ti->methods[i].name &&
                    std::strcmp(ti->methods[i].name, "ReleaseHandle") == 0 &&
                    ti->methods[i].vtable_slot >= 0) {
                    releaseSlot = ti->methods[i].vtable_slot;
                    break;
                }
            }
            if (releaseSlot >= 0 &&
                static_cast<UInt32>(releaseSlot) < ti->vtable->method_count &&
                ti->vtable->methods[releaseSlot] != nullptr) {
                auto releaseHandle = reinterpret_cast<bool(*)(void*)>(
                    ti->vtable->methods[releaseSlot]);
                releaseHandle(self);
            }
        }
    }

    sh->f_state |= static_cast<int32_t>(SafeHandleState::Disposed) |
                    static_cast<int32_t>(SafeHandleState::Closed);
}

/// SafeFileHandle default .ctor — initializes _fileType = -1, base fields via SafeHandle__ctor.
/// The BCL field initializer `private volatile int _fileType = -1` is compiled into .ctor IL,
/// but the ctor body is stubbed in AOT (generic Activator.CreateInstance<T> prevents discovery).
/// Layout must match the generated SafeFileHandle struct (fields in Cecil metadata order).
inline void SafeFileHandle__ctor(void* self) {
    // Initialize base SafeHandle fields: handle=0, ownsHandle=true
    SafeHandle__ctor(self, 0, true);
    // Set _fileType = -1 (field initializer that triggers lazy GetFileType() call)
    // Uses SafeHandleLayout size to offset past base fields to SafeFileHandle-specific fields.
    // SafeFileHandle layout after SafeHandle fields:
    //   String* f_path;
    //   int64_t f_length;
    //   bool f_lengthCanBeCached;
    //   int32_t f_fileOptions;  (enum)
    //   int32_t f_fileType;     ← this field
    struct SafeFileHandleLayout {
        TypeInfo* __type_info;
        UInt32 __sync_block;
        intptr_t f_handle;
        int32_t f_state;
        bool f_ownsHandle;
        bool f_fullyInitialized;
        void* f_path;
        int64_t f_length;
        bool f_lengthCanBeCached;
        int32_t f_fileOptions;
        int32_t f_fileType;
    };
    auto* sfh = static_cast<SafeFileHandleLayout*>(self);
    sfh->f_fileType = -1;
}

} // namespace icall
} // namespace cil2cpp
