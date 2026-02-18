/**
 * CIL2CPP Runtime - Unicode Support (ICU4C backed)
 *
 * Provides character classification, case conversion, and UTF encoding
 * via ICU4C. Replaces hand-rolled UTF-8/UTF-16 conversion code.
 */

#pragma once

#include "types.h"

namespace cil2cpp {
namespace unicode {

/// Initialize ICU subsystem. Called from runtime_init().
void init();

// ===== Character Classification (System.Char static methods) =====

bool is_whitespace(Char c);
bool is_digit(Char c);
bool is_letter(Char c);
bool is_letter_or_digit(Char c);
bool is_upper(Char c);
bool is_lower(Char c);
bool is_punctuation(Char c);
bool is_separator(Char c);
bool is_control(Char c);
bool is_surrogate(Char c);
bool is_high_surrogate(Char c);
bool is_low_surrogate(Char c);

// ===== Case Conversion =====

Char to_upper(Char c);
Char to_lower(Char c);

// ===== UTF-8 ↔ UTF-16 Conversion =====

/// Convert UTF-8 string to UTF-16. Returns number of UTF-16 code units written.
Int32 utf8_to_utf16(const char* utf8, Char* out, Int32 outCapacity);

/// Convert UTF-16 string to UTF-8. Returns number of UTF-8 bytes written.
Int32 utf16_to_utf8(const Char* utf16, Int32 utf16Len, char* out, Int32 outCapacity);

/// Calculate UTF-16 length of a UTF-8 string (without converting).
Int32 utf8_to_utf16_length(const char* utf8);

/// Calculate UTF-8 length of a UTF-16 string (without converting).
Int32 utf16_to_utf8_length(const Char* utf16, Int32 utf16Len);

// ===== Char Classification ICalls (called from generated code) =====
// Wrappers with icall calling convention (Char passed as value)

Boolean char_is_whitespace(Char c);
Boolean char_is_digit(Char c);
Boolean char_is_letter(Char c);
Boolean char_is_letter_or_digit(Char c);
Boolean char_is_upper(Char c);
Boolean char_is_lower(Char c);
Boolean char_is_punctuation(Char c);
Boolean char_is_separator(Char c);
Boolean char_is_control(Char c);
Boolean char_is_surrogate(Char c);
Boolean char_is_high_surrogate(Char c);
Boolean char_is_low_surrogate(Char c);
Char char_to_upper(Char c);
Char char_to_lower(Char c);

} // namespace unicode
} // namespace cil2cpp
