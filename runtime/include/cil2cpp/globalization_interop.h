/**
 * CIL2CPP Runtime - Interop.Globalization P/Invoke Implementations
 *
 * Real ICU4C-backed implementations for the low-level Interop.Globalization
 * P/Invoke methods called from compiled BCL IL. These are the same functions
 * that .NET's System.Globalization.Native library provides.
 *
 * Declarations match the P/Invoke signatures in the BCL.
 * Implementations in src/interop/globalization_interop.cpp.
 */

#pragma once

#include "types.h"
#include "string.h"

namespace cil2cpp {

// Forward declare Array for GetCalendars
struct Array;

// ===== Sort Handle Management =====
// Sort handles wrap ICU UCollator* pointers as intptr_t.

/// GetSortHandle — open/cache a UCollator for the given locale.
int32_t interop_globalization_get_sort_handle(String* sortName, intptr_t* sortHandle);

/// CloseSortHandle — release a sort handle (no-op: we cache collators).
void interop_globalization_close_sort_handle(intptr_t sortHandle);

// ===== String Comparison =====

/// CompareString — culture-aware string comparison via ICU collation.
int32_t interop_globalization_compare_string(
    intptr_t sortHandle, char16_t* string1, int32_t string1Length,
    char16_t* string2, int32_t string2Length, int32_t options);

/// IndexOf — culture-aware forward search via ICU string search.
int32_t interop_globalization_index_of(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* pSource, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr);

/// LastIndexOf — culture-aware reverse search via ICU string search.
int32_t interop_globalization_last_index_of(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* pSource, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr);

/// StartsWith — culture-aware prefix match.
int32_t interop_globalization_starts_with(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* source, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr);

/// EndsWith — culture-aware suffix match.
int32_t interop_globalization_ends_with(
    intptr_t sortHandle, char16_t* target, int32_t targetLength,
    char16_t* source, int32_t sourceLength, int32_t options,
    int32_t* matchLengthPtr);

// ===== Locale Data =====

/// GetLocaleName — copy locale name string to output buffer.
int32_t interop_globalization_get_locale_name(
    String* localeName, char16_t* buffer, int32_t bufferLength);

/// GetLocaleInfoString — locale-specific string data (decimal sep, currency, etc.).
int32_t interop_globalization_get_locale_info_string(
    String* localeName, uint32_t type, char16_t* buffer,
    int32_t bufferLength, String* uiCultureName);

/// GetLocaleInfoInt — locale-specific integer data (digit count, format indices, etc.).
int32_t interop_globalization_get_locale_info_int(
    String* localeName, uint32_t type, int32_t* value);

/// GetLocaleInfoGroupingSizes — number grouping sizes (primary + secondary).
int32_t interop_globalization_get_locale_info_grouping_sizes(
    String* localeName, uint32_t type, int32_t* primaryGroupSize,
    int32_t* secondaryGroupSize);

/// GetLocaleTimeFormat — time format pattern string.
int32_t interop_globalization_get_locale_time_format(
    String* localeName, int32_t shortFormat, char16_t* buffer,
    int32_t bufferLength);

/// IsPredefinedLocale — check if locale is known to ICU.
int32_t interop_globalization_is_predefined_locale(String* localeName);

// ===== Calendar Data =====

/// GetCalendars — enumerate available calendars for a locale.
int32_t interop_globalization_get_calendars(
    String* localeName, Array* calendars, int32_t calendarsCapacity);

/// GetCalendarInfo — calendar-specific string data (names, patterns, eras).
int32_t interop_globalization_get_calendar_info(
    String* localeName, int32_t calendarId, int32_t dataType,
    char16_t* buffer, int32_t bufferLength);

/// GetLatestJapaneseEra — returns the number of the latest Japanese era.
int32_t interop_globalization_get_latest_japanese_era();

/// GetJapaneseEraStartDate — returns the start date of a Japanese era.
int32_t interop_globalization_get_japanese_era_start_date(
    int32_t era, int32_t* year, int32_t* month, int32_t* day);

// ===== Timezone =====

/// IanaIdToWindowsId — convert IANA timezone ID to Windows timezone ID.
int32_t interop_globalization_iana_id_to_windows_id(
    String* ianaId, char16_t* buffer, int32_t bufferLength);

// ===== ICU Lifecycle =====
// Variadic templates: BCL has multiple overloads (int vs ReadOnlySpan<char>).
// Both just return 1 since ICU is statically linked.

template<typename... Args>
inline int32_t interop_globalization_load_icu(Args...) { return 1; }

template<typename... Args>
inline int32_t interop_globalization_init_icu_functions(Args...) { return 1; }

} // namespace cil2cpp
