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
/// Simplified: just set success = true (no ref counting in AOT)
inline void SafeHandle_DangerousAddRef(void* self, bool* success) {
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
