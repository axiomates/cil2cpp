/**
 * CIL2CPP Runtime - Platform Interop Implementations
 *
 * Real Windows API implementations for BCryptGenRandom, NtDll, and User32
 * P/Invoke methods. These are called from compiled BCL IL via the ICall registry.
 *
 * Declarations are in include/cil2cpp/interop_stubs.h (no windows.h needed there).
 * Implementations here use the real Windows APIs on Windows, with POSIX fallbacks.
 */

#include "cil2cpp/types.h"
#include <cstdio>

#ifdef CIL2CPP_WINDOWS
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "user32.lib")
#endif

namespace cil2cpp {

// ===== Interop.BCrypt =====

int32_t interop_bcrypt_gen_random(intptr_t hAlgorithm, uint8_t* pbBuffer, int32_t cbBuffer, int32_t dwFlags) {
#ifdef CIL2CPP_WINDOWS
    return static_cast<int32_t>(BCryptGenRandom(
        reinterpret_cast<BCRYPT_ALG_HANDLE>(hAlgorithm),
        pbBuffer, static_cast<ULONG>(cbBuffer), static_cast<ULONG>(dwFlags)));
#else
    (void)hAlgorithm; (void)dwFlags;
    FILE* f = fopen("/dev/urandom", "rb");
    if (f) { fread(pbBuffer, 1, static_cast<size_t>(cbBuffer), f); fclose(f); return 0; }
    return -1;
#endif
}

// ===== Interop.NtDll =====

int32_t interop_ntdll_rtl_get_version(void* versionInfo) {
#ifdef CIL2CPP_WINDOWS
    using RtlGetVersionFn = LONG(WINAPI*)(PRTL_OSVERSIONINFOW);
    static auto fn = reinterpret_cast<RtlGetVersionFn>(
        GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"));
    if (fn) return fn(static_cast<PRTL_OSVERSIONINFOW>(versionInfo));
#else
    (void)versionInfo;
#endif
    return -1; // STATUS_UNSUCCESSFUL
}

int32_t interop_ntdll_query_system_info(uint32_t infoClass, void* buffer,
                                         uint32_t bufferSize, uint32_t* returnLength) {
#ifdef CIL2CPP_WINDOWS
    using NtQueryFn = LONG(WINAPI*)(ULONG, PVOID, ULONG, PULONG);
    static auto fn = reinterpret_cast<NtQueryFn>(
        GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQuerySystemInformation"));
    if (fn) return fn(infoClass, buffer, bufferSize, reinterpret_cast<PULONG>(returnLength));
#else
    (void)infoClass; (void)buffer; (void)bufferSize; (void)returnLength;
#endif
    return -1;
}

// ===== Interop.User32 =====

int32_t interop_user32_load_string(intptr_t hInstance, uint32_t uID,
                                    char16_t* lpBuffer, int32_t cchBufferMax) {
#ifdef CIL2CPP_WINDOWS
    return LoadStringW(reinterpret_cast<HINSTANCE>(hInstance), uID,
                       reinterpret_cast<LPWSTR>(lpBuffer), cchBufferMax);
#else
    (void)hInstance; (void)uID; (void)lpBuffer; (void)cchBufferMax;
    return 0;
#endif
}

} // namespace cil2cpp
