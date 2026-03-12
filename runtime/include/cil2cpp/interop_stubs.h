/**
 * CIL2CPP Runtime - Platform Interop Declarations
 *
 * Declarations and inline implementations for BCL P/Invoke wrapper methods
 * called from compiled BCL IL.
 *
 * - Globalization: real ICU4C calls (globalization_interop.h / globalization_interop.cpp)
 * - Win32 Registry: returns nullptr (key-not-found) — BCL uses fallback defaults
 * - BCrypt/NtDll/User32: real Windows API calls (interop_platform.cpp)
 * - Ucrtbase: forwards to C stdlib malloc/free
 */

#pragma once

#include "types.h"
#include "string.h"
#include "globalization_interop.h"
#include <cstdlib>
#include <cstdio>
#include <cstring>

namespace cil2cpp {

// ===== Generic return-constant helpers =====
// Used by ICallRegistry for methods that correctly return a fixed value
// (e.g., Reflection.Emit no-ops in AOT, runtime type queries).
template<typename... Args>
inline int32_t icall_return_zero(Args...) { return 0; }

template<typename... Args>
inline int32_t icall_return_one(Args...) { return 1; }

// ===== Internal.Win32.RegistryKey =====
// Registry access is used by BCL for timezone, locale, and system settings.
// Returning nullptr is architecturally correct (not a stub): equivalent to
// RegOpenKeyExW returning ERROR_FILE_NOT_FOUND. The BCL handles this gracefully
// by falling back to defaults. AOT binaries use ICU for locale/timezone data
// instead of the Windows registry.
template<typename... Args>
inline Object* win32_registry_stub(Args...) { return nullptr; }

// ===== Interop.NtDll =====
// Real implementations in interop_platform.cpp (uses Windows API on Windows, stubs elsewhere).
int32_t interop_ntdll_rtl_get_version(void* versionInfo);
int32_t interop_ntdll_query_system_info(uint32_t infoClass, void* buffer,
                                         uint32_t bufferSize, uint32_t* returnLength);

// ===== Interop.User32 =====
// Real implementation in interop_platform.cpp (calls LoadStringW on Windows).
int32_t interop_user32_load_string(intptr_t hInstance, uint32_t uID,
                                    char16_t* lpBuffer, int32_t cchBufferMax);

// ===== Interop.BCrypt =====
// Real implementation in interop_platform.cpp (calls BCryptGenRandom on Windows,
// /dev/urandom on Linux).
int32_t interop_bcrypt_gen_random(intptr_t hAlgorithm, uint8_t* pbBuffer,
                                   int32_t cbBuffer, int32_t dwFlags);

// ===== Interop.Ucrtbase =====
// Forward to C stdlib malloc/free/calloc/realloc
inline void* interop_ucrtbase_malloc(uintptr_t size) { return std::malloc(static_cast<size_t>(size)); }
inline void interop_ucrtbase_free(void* ptr) { std::free(ptr); }

// ===== System.Array.InternalCreate =====
// Returns nullptr — BCL handles gracefully.
template<typename... Args>
inline void* array_internal_create(Args...) { return nullptr; }

// ===== System.Delegate.BindToMethodInfo =====
// Returns false (no runtime method binding in AOT).
template<typename... Args>
inline bool delegate_bind_to_method_info(Args...) { return false; }

// ===== System.Diagnostics =====
template<typename... Args>
inline void* stackframehelper_get_method_base(Args...) { return nullptr; }
template<typename... Args>
inline int32_t stackframe_get_il_offset(Args...) { return 0; }

} // namespace cil2cpp
