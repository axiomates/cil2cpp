/**
 * CIL2CPP Runtime - System.Text.Ascii ICall implementations
 *
 * Scalar C++ replacements for BCL System.Text.Ascii methods that depend on
 * SIMD intrinsics (Vector128/SSE2/AVX2). The BCL implementations use
 * hardware-accelerated paths which our codegen cannot compile from IL.
 *
 * These provide correct behavior for all ASCII text processing used by
 * StreamReader/StreamWriter, Encoding.UTF8, and related BCL chains.
 */

#include <cil2cpp/icall.h>
#include <cstdint>
#include <cstring>

namespace cil2cpp {
namespace icall {

// ===== Byte/Char classification helpers =====

bool Ascii_AllBytesInUInt32AreAscii(uint32_t value) {
    return (value & 0x80808080u) == 0;
}

bool Ascii_AllBytesInUInt64AreAscii(uint64_t value) {
    return (value & 0x8080808080808080ULL) == 0;
}

bool Ascii_AllCharsInUInt32AreAscii(uint32_t value) {
    return (value & 0xFF80FF80u) == 0;
}

bool Ascii_AllCharsInUInt64AreAscii(uint64_t value) {
    return (value & 0xFF80FF80FF80FF80ULL) == 0;
}

bool Ascii_FirstCharInUInt32IsAscii(uint32_t value) {
    return (value & 0xFF80u) == 0;
}

bool Ascii_IsValid_byte(uint8_t value) {
    return value <= 0x7F;
}

bool Ascii_IsValid_char(char16_t value) {
    return value <= 0x7F;
}

// ===== Widening (byte -> char16_t) =====

uintptr_t Ascii_WidenAsciiToUtf16(uint8_t* pAsciiBuffer, char16_t* pUtf16Buffer, uintptr_t elementCount) {
    uintptr_t i = 0;
    for (; i < elementCount; i++) {
        if (pAsciiBuffer[i] > 0x7F) break;
        pUtf16Buffer[i] = static_cast<char16_t>(pAsciiBuffer[i]);
    }
    return i;
}

void Ascii_WidenFourAsciiBytesToUtf16AndWriteToBuffer(char16_t* outputBuffer, uint32_t value) {
    outputBuffer[0] = static_cast<char16_t>(value & 0xFF);
    outputBuffer[1] = static_cast<char16_t>((value >> 8) & 0xFF);
    outputBuffer[2] = static_cast<char16_t>((value >> 16) & 0xFF);
    outputBuffer[3] = static_cast<char16_t>((value >> 24) & 0xFF);
}

// ===== Narrowing (char16_t -> byte) =====

uintptr_t Ascii_NarrowUtf16ToAscii(char16_t* pUtf16Buffer, uint8_t* pAsciiBuffer, uintptr_t elementCount) {
    uintptr_t i = 0;
    for (; i < elementCount; i++) {
        if (pUtf16Buffer[i] > 0x7F) break;
        pAsciiBuffer[i] = static_cast<uint8_t>(pUtf16Buffer[i]);
    }
    return i;
}

void Ascii_NarrowFourUtf16CharsToAsciiAndWriteToBuffer(uint8_t* outputBuffer, uint64_t value) {
    outputBuffer[0] = static_cast<uint8_t>(value & 0xFFFF);
    outputBuffer[1] = static_cast<uint8_t>((value >> 16) & 0xFFFF);
    outputBuffer[2] = static_cast<uint8_t>((value >> 32) & 0xFFFF);
    outputBuffer[3] = static_cast<uint8_t>((value >> 48) & 0xFFFF);
}

void Ascii_NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(uint8_t* outputBuffer, uint32_t value) {
    outputBuffer[0] = static_cast<uint8_t>(value & 0xFFFF);
    outputBuffer[1] = static_cast<uint8_t>((value >> 16) & 0xFFFF);
}

// ===== Scanning =====

uint32_t Ascii_CountNumberOfLeadingAsciiBytesFromUInt32WithSomeNonAsciiData(uint32_t value) {
    // Little-endian: check bytes from low to high
    if (value & 0x80) return 0;
    if (value & 0x8000) return 1;
    if (value & 0x800000) return 2;
    return 3;
}

uintptr_t Ascii_GetIndexOfFirstNonAsciiByte(uint8_t* pBuffer, uintptr_t bufferLength) {
    for (uintptr_t i = 0; i < bufferLength; i++) {
        if (pBuffer[i] > 0x7F) return i;
    }
    return bufferLength;
}

uintptr_t Ascii_GetIndexOfFirstNonAsciiChar(char16_t* pBuffer, uintptr_t bufferLength) {
    for (uintptr_t i = 0; i < bufferLength; i++) {
        if (pBuffer[i] > 0x7F) return i;
    }
    return bufferLength;
}

// ===== SSE-specific stub (scalar fallback) =====

bool Ascii_ContainsNonAsciiByte_Sse2(uint32_t sseMask) {
    return sseMask != 0;
}

// ===== SpanHelpers.DontNegate/Negate =====

bool SpanHelpers_DontNegate_NegateIfNeeded(bool equals) {
    return equals;  // identity — DontNegate returns value unchanged
}

bool SpanHelpers_Negate_NegateIfNeeded(bool equals) {
    return !equals;  // negate — Negate returns logical negation
}

} // namespace icall
} // namespace cil2cpp
