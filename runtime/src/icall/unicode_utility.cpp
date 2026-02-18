/**
 * CIL2CPP Runtime - Unicode Utility ICall Implementations (ICU4C backed)
 *
 * ICU-backed implementations for BCL functions that use
 * System.Runtime.Intrinsics (SSE2/AVX2) which our codegen can't compile.
 *
 * These replace:
 *   - System.Text.Unicode.Utf8Utility.TranscodeToUtf8
 *   - System.Text.Unicode.Utf8Utility.GetPointerToFirstInvalidByte
 */

#include <cil2cpp/types.h>
#include <cil2cpp/unicode.h>

#include <unicode/ustring.h>
#include <unicode/utypes.h>

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
 * TranscodeToUtf8 — Convert UTF-16 to UTF-8 (ICU4C backed).
 *
 * Replacement for System.Text.Unicode.Utf8Utility.TranscodeToUtf8.
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
    UErrorCode err = U_ZERO_ERROR;
    int32_t resultLen = 0;

    u_strToUTF8(
        reinterpret_cast<char*>(pOutputBuffer),
        static_cast<int32_t>(outputBytesRemaining),
        &resultLen,
        reinterpret_cast<const UChar*>(pInputBuffer),
        static_cast<int32_t>(inputLength),
        &err
    );

    if (err == U_BUFFER_OVERFLOW_ERROR) {
        // Partial conversion — figure out how much input was consumed
        // by doing a preflight of just the output we wrote
        *pOutputBufferRemaining = pOutputBuffer + outputBytesRemaining;
        // Estimate input consumed: re-convert with exact output size
        // For simplicity, scan forward to find how many UTF-16 units
        // produced outputBytesRemaining bytes
        int32_t consumed = 0;
        int32_t produced = 0;
        const UChar* src = reinterpret_cast<const UChar*>(pInputBuffer);
        while (consumed < inputLength && produced < outputBytesRemaining) {
            UChar32 cp;
            int32_t prevConsumed = consumed;
            U16_NEXT(src, consumed, inputLength, cp);
            // Calculate UTF-8 bytes for this codepoint
            int32_t cpBytes;
            if (cp < 0x80) cpBytes = 1;
            else if (cp < 0x800) cpBytes = 2;
            else if (cp < 0x10000) cpBytes = 3;
            else cpBytes = 4;
            if (produced + cpBytes > outputBytesRemaining) {
                consumed = prevConsumed;
                break;
            }
            produced += cpBytes;
        }
        *pInputBufferRemaining = pInputBuffer + consumed;
        return DestinationTooSmall;
    }

    if (U_FAILURE(err)) {
        // Find the position of the invalid data
        // Re-scan to find where ICU stopped
        *pInputBufferRemaining = pInputBuffer;
        *pOutputBufferRemaining = pOutputBuffer;
        return InvalidData;
    }

    // Success — all input consumed
    *pInputBufferRemaining = pInputBuffer + inputLength;
    *pOutputBufferRemaining = pOutputBuffer + resultLen;
    return Done;
}

/**
 * GetPointerToFirstInvalidByte — Validate UTF-8 and count UTF-16 units (ICU4C backed).
 *
 * Replacement for System.Text.Unicode.Utf8Utility.GetPointerToFirstInvalidByte.
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
    // Try converting UTF-8 to UTF-16 with ICU to find invalid bytes
    UErrorCode err = U_ZERO_ERROR;
    int32_t utf16Len = 0;

    // Preflight: get UTF-16 length
    u_strFromUTF8(
        nullptr,
        0,
        &utf16Len,
        reinterpret_cast<const char*>(pInputBuffer),
        static_cast<int32_t>(inputLength),
        &err
    );

    if (err == U_BUFFER_OVERFLOW_ERROR || err == U_ZERO_ERROR) {
        // All valid — compute adjustments
        // utf16Adjust = utf16Length - inputLength
        *utf16CodeUnitCountAdjustment = utf16Len - inputLength;

        // Count scalar values (code points, not surrogate halves)
        // Each surrogate pair = 2 UTF-16 units = 1 scalar
        // scalarCount = utf16Len - numSurrogatePairs
        // For valid UTF-8, supplementary chars (4 bytes) produce 2 UTF-16 units (1 pair)
        // scalarAdjust = scalarCount - inputLength
        // We can compute numSurrogatePairs = utf16Len - scalarCount
        // But we need scalarCount. Let's count 4-byte sequences in the input.
        int32_t fourByteCount = 0;
        for (int32_t i = 0; i < inputLength; ) {
            uint8_t b = pInputBuffer[i];
            if (b < 0x80) i += 1;
            else if ((b & 0xE0) == 0xC0) i += 2;
            else if ((b & 0xF0) == 0xE0) i += 3;
            else if ((b & 0xF8) == 0xF0) { i += 4; fourByteCount++; }
            else { i += 1; break; } // shouldn't happen if valid
        }
        int32_t scalarCount = utf16Len - fourByteCount;
        *scalarCountAdjustment = scalarCount - inputLength;

        return pInputBuffer + inputLength;
    }

    // Invalid data — find the exact position by scanning byte-by-byte
    int32_t utf16Adjust = 0;
    int32_t scalarAdjust = 0;
    uint8_t* p = pInputBuffer;
    uint8_t* pEnd = pInputBuffer + inputLength;

    while (p < pEnd) {
        uint8_t b = *p;

        if (b < 0x80) {
            p++;
        } else if ((b & 0xE0) == 0xC0) {
            if (p + 2 > pEnd) break;
            if ((p[1] & 0xC0) != 0x80) break;
            if (b < 0xC2) break; // overlong
            utf16Adjust -= 1;
            scalarAdjust -= 1;
            p += 2;
        } else if ((b & 0xF0) == 0xE0) {
            if (p + 3 > pEnd) break;
            if ((p[1] & 0xC0) != 0x80 || (p[2] & 0xC0) != 0x80) break;
            uint32_t cp = ((uint32_t)(b & 0x0F) << 12) |
                          ((uint32_t)(p[1] & 0x3F) << 6) |
                          (uint32_t)(p[2] & 0x3F);
            if (cp < 0x0800 || (cp >= 0xD800 && cp <= 0xDFFF)) break;
            utf16Adjust -= 2;
            scalarAdjust -= 2;
            p += 3;
        } else if ((b & 0xF8) == 0xF0) {
            if (p + 4 > pEnd) break;
            if ((p[1] & 0xC0) != 0x80 || (p[2] & 0xC0) != 0x80 || (p[3] & 0xC0) != 0x80) break;
            uint32_t cp = ((uint32_t)(b & 0x07) << 18) |
                          ((uint32_t)(p[1] & 0x3F) << 12) |
                          ((uint32_t)(p[2] & 0x3F) << 6) |
                          (uint32_t)(p[3] & 0x3F);
            if (cp < 0x10000 || cp > 0x10FFFF) break;
            utf16Adjust -= 2;
            scalarAdjust -= 3;
            p += 4;
        } else {
            break;
        }
    }

    *utf16CodeUnitCountAdjustment = utf16Adjust;
    *scalarCountAdjustment = scalarAdjust;
    return p;
}

} // namespace cil2cpp
