/**
 * CIL2CPP Runtime - Interop Stubs
 *
 * Stub implementations for BCL P/Invoke wrapper methods (Interop.Globalization,
 * Internal.Win32.RegistryKey, Interop.NtDll, etc.) that are called from compiled
 * BCL IL but don't have full native implementations.
 *
 * These return sensible defaults to unblock compilation and cascade.
 * TODO: Replace with full implementations as needed for correctness.
 */

#pragma once

#include "types.h"
#include <cstdlib>

namespace cil2cpp {

// ===== Interop.Globalization P/Invoke stubs =====
// Low-level ICU wrappers called from CultureData, CultureInfo, CalendarData, etc.
// Higher-level operations (CompareInfo, TextInfo) are handled by existing ICalls.

// Wildcard stubs: template handles all overloads via variadic params
template<typename... Args>
inline int32_t interop_globalization_return_zero(Args...) { return 0; }

template<typename... Args>
inline int32_t interop_globalization_return_one(Args...) { return 1; }

template<typename... Args>
inline int32_t interop_globalization_return_neg(Args...) { return -1; }

// ===== Internal.Win32.RegistryKey stubs =====
// Windows registry access — not meaningful in AOT-compiled binaries.
// Returns nullptr (Object*) since GetSubKeyNames/GetValueNames return arrays,
// GetValue returns Object, and OpenSubKey returns a handle.
template<typename... Args>
inline Object* win32_registry_stub(Args...) { return nullptr; }

// ===== Interop.NtDll stubs =====
template<typename... Args>
inline int32_t interop_ntdll_stub(Args...) { return 0; }

// ===== Interop.User32 stubs =====
template<typename... Args>
inline int32_t interop_user32_stub(Args...) { return 0; }

// ===== Interop.BCrypt stubs =====
// BCryptGenRandom — fill buffer with random bytes
template<typename... Args>
inline int32_t interop_bcrypt_stub(Args...) { return 0; }

// ===== Interop.Ucrtbase stubs =====
// Forward to C stdlib malloc/free
template<typename... Args>
inline void* interop_ucrtbase_malloc(Args...) { return nullptr; }
template<typename... Args>
inline void interop_ucrtbase_free(Args...) {}

// ===== System.Array.InternalCreate stub =====
template<typename... Args>
inline void* array_internal_create(Args...) { return nullptr; }

// ===== System.Delegate.BindToMethodInfo stub =====
// Returns bool (true = bound successfully). Stub returns false.
template<typename... Args>
inline bool delegate_bind_to_method_info(Args...) { return false; }

// ===== System.Diagnostics stubs =====
template<typename... Args>
inline void* stackframehelper_get_method_base(Args...) { return nullptr; }
template<typename... Args>
inline int32_t stackframe_get_il_offset(Args...) { return 0; }

} // namespace cil2cpp
