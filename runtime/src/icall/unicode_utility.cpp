/**
 * CIL2CPP Runtime - Unicode Utility ICall Implementations
 *
 * Scalar (non-SIMD) implementations for BCL functions that use
 * System.Runtime.Intrinsics (SSE2/AVX2) which our codegen can't compile.
 *
 * These replace:
 *   - System.Text.Unicode.Utf8Utility.TranscodeToUtf8
 *   - System.Text.Unicode.Utf8Utility.GetPointerToFirstInvalidByte
 */

#include <cil2cpp/types.h>
#include <cstdint>

namespace cil2cpp {

// OperationStatus enum values (System.Buffers.OperationStatus)
enum OperationStatus : int32_t {
    Done = 0,
    DestinationTooSmall = 1,
    NeedMoreData = 2,
    InvalidData = 3,
};

/**
 * TranscodeToUtf8 — Convert UTF-16 to UTF-8.
 *
 * Scalar implementation of System.Text.Unicode.Utf8Utility.TranscodeToUtf8.
 * The BCL version uses SSE2/AVX2 intrinsics for fast ASCII paths.
 */
Int32 utf8_utility_transcode_to_utf8(
    Char* pInputBuffer,
    Int32 inputLength,
    uint8_t* pOutputBuffer,
    Int32 outputBytesRemaining,
    Char** pInputBufferRemaining,
    uint8_t** pOutputBufferRemaining)
{
    Char* pInput = pInputBuffer;
    Char* pInputEnd = pInputBuffer + inputLength;
    uint8_t* pOutput = pOutputBuffer;
    uint8_t* pOutputEnd = pOutputBuffer + outputBytesRemaining;

    while (pInput < pInputEnd) {
        uint32_t codePoint = static_cast<uint16_t>(*pInput);

        // Check for surrogate pair
        if (codePoint >= 0xD800 && codePoint <= 0xDBFF) {
            // High surrogate — need low surrogate
            if (pInput + 1 >= pInputEnd) {
                // Incomplete surrogate pair at end of input
                break; // NeedMoreData handled by caller
            }
            uint32_t low = static_cast<uint16_t>(pInput[1]);
            if (low < 0xDC00 || low > 0xDFFF) {
                // Invalid low surrogate
                *pInputBufferRemaining = pInput;
                *pOutputBufferRemaining = pOutput;
                return InvalidData;
            }
            codePoint = 0x10000 + ((codePoint - 0xD800) << 10) + (low - 0xDC00);
        } else if (codePoint >= 0xDC00 && codePoint <= 0xDFFF) {
            // Lone low surrogate
            *pInputBufferRemaining = pInput;
            *pOutputBufferRemaining = pOutput;
            return InvalidData;
        }

        // Encode code point as UTF-8
        if (codePoint < 0x80) {
            if (pOutput >= pOutputEnd) {
                *pInputBufferRemaining = pInput;
                *pOutputBufferRemaining = pOutput;
                return DestinationTooSmall;
            }
            *pOutput++ = static_cast<uint8_t>(codePoint);
            pInput++;
        } else if (codePoint < 0x800) {
            if (pOutput + 2 > pOutputEnd) {
                *pInputBufferRemaining = pInput;
                *pOutputBufferRemaining = pOutput;
                return DestinationTooSmall;
            }
            *pOutput++ = static_cast<uint8_t>(0xC0 | (codePoint >> 6));
            *pOutput++ = static_cast<uint8_t>(0x80 | (codePoint & 0x3F));
            pInput++;
        } else if (codePoint < 0x10000) {
            if (pOutput + 3 > pOutputEnd) {
                *pInputBufferRemaining = pInput;
                *pOutputBufferRemaining = pOutput;
                return DestinationTooSmall;
            }
            *pOutput++ = static_cast<uint8_t>(0xE0 | (codePoint >> 12));
            *pOutput++ = static_cast<uint8_t>(0x80 | ((codePoint >> 6) & 0x3F));
            *pOutput++ = static_cast<uint8_t>(0x80 | (codePoint & 0x3F));
            pInput++;
        } else {
            // Supplementary character (surrogate pair consumed 2 UTF-16 code units)
            if (pOutput + 4 > pOutputEnd) {
                *pInputBufferRemaining = pInput;
                *pOutputBufferRemaining = pOutput;
                return DestinationTooSmall;
            }
            *pOutput++ = static_cast<uint8_t>(0xF0 | (codePoint >> 18));
            *pOutput++ = static_cast<uint8_t>(0x80 | ((codePoint >> 12) & 0x3F));
            *pOutput++ = static_cast<uint8_t>(0x80 | ((codePoint >> 6) & 0x3F));
            *pOutput++ = static_cast<uint8_t>(0x80 | (codePoint & 0x3F));
            pInput += 2; // consumed both high and low surrogate
        }
    }

    *pInputBufferRemaining = pInput;
    *pOutputBufferRemaining = pOutput;
    return Done;
}

/**
 * GetPointerToFirstInvalidByte — Validate UTF-8 and count UTF-16 units.
 *
 * Scalar implementation of System.Text.Unicode.Utf8Utility.GetPointerToFirstInvalidByte.
 * Returns pointer to first invalid byte, or pInputBuffer + inputLength if all valid.
 * Also computes:
 *   utf16CodeUnitCountAdjustment: (utf16Length - inputLength)
 *   scalarCountAdjustment: (scalarCount - inputLength)
 */
uint8_t* utf8_utility_get_pointer_to_first_invalid_byte(
    uint8_t* pInputBuffer,
    Int32 inputLength,
    Int32* utf16CodeUnitCountAdjustment,
    Int32* scalarCountAdjustment)
{
    uint8_t* p = pInputBuffer;
    uint8_t* pEnd = pInputBuffer + inputLength;
    int32_t utf16Adjust = 0;   // difference from inputLength
    int32_t scalarAdjust = 0;  // difference from inputLength

    while (p < pEnd) {
        uint8_t b = *p;

        if (b < 0x80) {
            // ASCII — 1 byte → 1 UTF-16 code unit, 1 scalar
            p++;
        } else if ((b & 0xE0) == 0xC0) {
            // 2-byte sequence → 1 UTF-16 code unit, 1 scalar
            if (p + 2 > pEnd) break; // incomplete
            if ((p[1] & 0xC0) != 0x80) break; // invalid continuation
            // Reject overlong (< U+0080 encoded as 2 bytes)
            if (b < 0xC2) break;
            utf16Adjust -= 1;  // 2 bytes → 1 code unit (saves 1)
            scalarAdjust -= 1;
            p += 2;
        } else if ((b & 0xF0) == 0xE0) {
            // 3-byte sequence → 1 UTF-16 code unit, 1 scalar
            if (p + 3 > pEnd) break; // incomplete
            if ((p[1] & 0xC0) != 0x80 || (p[2] & 0xC0) != 0x80) break;
            uint32_t cp = ((uint32_t)(b & 0x0F) << 12) |
                          ((uint32_t)(p[1] & 0x3F) << 6) |
                          (uint32_t)(p[2] & 0x3F);
            // Reject overlong or surrogate range
            if (cp < 0x0800 || (cp >= 0xD800 && cp <= 0xDFFF)) break;
            utf16Adjust -= 2;  // 3 bytes → 1 code unit (saves 2)
            scalarAdjust -= 2;
            p += 3;
        } else if ((b & 0xF8) == 0xF0) {
            // 4-byte sequence → 2 UTF-16 code units (surrogate pair), 1 scalar
            if (p + 4 > pEnd) break; // incomplete
            if ((p[1] & 0xC0) != 0x80 || (p[2] & 0xC0) != 0x80 || (p[3] & 0xC0) != 0x80) break;
            uint32_t cp = ((uint32_t)(b & 0x07) << 18) |
                          ((uint32_t)(p[1] & 0x3F) << 12) |
                          ((uint32_t)(p[2] & 0x3F) << 6) |
                          (uint32_t)(p[3] & 0x3F);
            // Valid range: U+10000 to U+10FFFF
            if (cp < 0x10000 || cp > 0x10FFFF) break;
            utf16Adjust -= 2;  // 4 bytes → 2 code units (saves 2)
            scalarAdjust -= 3; // 4 bytes → 1 scalar (saves 3)
            p += 4;
        } else {
            // Invalid leading byte
            break;
        }
    }

    *utf16CodeUnitCountAdjustment = utf16Adjust;
    *scalarCountAdjustment = scalarAdjust;
    return p;
}

} // namespace cil2cpp
