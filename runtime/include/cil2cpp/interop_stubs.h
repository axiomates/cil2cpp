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
#include "string.h"
#include <cstdlib>
#include <cstring>

namespace cil2cpp {

// ===== Interop.Globalization P/Invoke stubs =====
// Low-level ICU wrappers called from CultureData, CultureInfo, CalendarData, etc.
// Higher-level operations (CompareInfo, TextInfo) are handled by existing ICalls.

// Wildcard stubs: template handles all overloads via variadic params
template<typename... Args>
inline int32_t interop_globalization_return_zero(Args...) { return 0; }

// Interop.Globalization.GetLocaleName — copy locale name to output buffer.
// This enables CultureData initialization for the user's default culture,
// which is required for correct NumberFormatInfo (infinity/NaN symbols, etc.).
inline int32_t interop_globalization_get_locale_name(
    String* localeName, char16_t* buffer, int32_t bufferLength) {
    if (!localeName || bufferLength <= 0) return 0;
    auto len = string_length(localeName);
    if (len <= 0 || len >= bufferLength) return 0;
    std::memcpy(buffer, &localeName->f__firstChar, len * sizeof(char16_t));
    buffer[len] = 0;
    return 1;
}

// Interop.Globalization.GetLocaleInfoString — provide locale string data.
// Returns culture-specific values for key locale properties.
// Uses en-US/invariant defaults for all standard LocaleStringData values.
inline int32_t interop_globalization_get_locale_info_string(
    String* /*localeName*/, uint32_t type, char16_t* buffer,
    int32_t /*bufferLength*/, String* /*uiCultureName*/) {
    // Helper: write a null-terminated char16_t string from ASCII
    auto write = [&](const char* s) {
        int i = 0;
        while (s[i]) { buffer[i] = static_cast<char16_t>(s[i]); i++; }
        buffer[i] = 0;
        return 1;
    };
    switch (type) {
    // Number formatting
    case 14:  return write(".");   // NumberDecimalSeparator
    case 15:  return write(",");   // NumberGroupSeparator
    case 80:  return write("+");   // PositiveSign
    case 81:  return write("-");   // NegativeSign
    case 105: return write("NaN"); // NaNSymbol
    case 106: buffer[0] = 0x221E; buffer[1] = 0; return 1;   // PositiveInfinitySymbol (∞)
    case 107: buffer[0] = u'-'; buffer[1] = 0x221E; buffer[2] = 0; return 2; // NegativeInfinitySymbol (-∞)
    // Currency
    case 20:  return write("$");   // CurrencySymbol
    case 22:  return write(".");   // CurrencyDecimalSeparator
    case 23:  return write(",");   // CurrencyGroupSeparator
    // Percent
    case 19:  return write("%");   // PercentSymbol
    case 89:  return write("+");   // PercentPositivePattern (string rep)
    case 90:  return write("-");   // PercentNegativePattern (string rep)
    case 118: return write("%");   // PerMilleSymbol
    case 119: return write("+");   // (additional locale data)
    // List/misc
    case 40:  return write("AM");  // AMDesignator
    case 41:  return write("PM");  // PMDesignator
    default:
        return 0;
    }
}

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

// ===== Interop.Ucrtbase =====
// Forward to C stdlib malloc/free/calloc/realloc
inline void* interop_ucrtbase_malloc(uintptr_t size) { return std::malloc(static_cast<size_t>(size)); }
inline void interop_ucrtbase_free(void* ptr) { std::free(ptr); }

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
