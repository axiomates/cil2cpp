/**
 * CIL2CPP Runtime — Compression Interop (zlib)
 *
 * Implements the CompressionNative_* entry points that .NET BCL's
 * System.IO.Compression calls via P/Invoke. The managed side uses a
 * PAL_ZStream wrapper struct (6 fields); the actual zlib z_stream is
 * allocated in native code and stored in internalState.
 *
 * Follows .NET runtime's pal_zlib.c architecture:
 * https://github.com/dotnet/runtime/blob/main/src/native/libs/System.IO.Compression.Native/pal_zlib.c
 */

#include <zlib.h>
#include <cstdlib>
#include <cstdint>
#include <cstring>

// PAL_ZStream: managed-side wrapper passed to CompressionNative_* functions.
// Maps to System.IO.Compression.ZLibNative.ZStream in .NET BCL IL.
// Layout must match the generated C++ struct:
//   intptr_t f_nextIn, f_nextOut, f_msg, f_internalState;
//   uint32_t f_availIn, f_availOut;
struct PAL_ZStream {
    uint8_t*  nextIn;
    uint8_t*  nextOut;
    char*     msg;
    void*     internalState;   // opaque — points to our allocated z_stream
    uint32_t  availIn;
    uint32_t  availOut;
};

// Allocate and zero-initialize the internal z_stream.
static int32_t Init(PAL_ZStream* stream) {
    z_stream* zs = static_cast<z_stream*>(std::calloc(1, sizeof(z_stream)));
    if (!zs) return Z_MEM_ERROR;
    stream->internalState = zs;
    return Z_OK;
}

// Get the internal z_stream, syncing input from PAL_ZStream.
static z_stream* GetCurrentZStream(PAL_ZStream* stream) {
    z_stream* zs = static_cast<z_stream*>(stream->internalState);
    if (!zs) return nullptr;
    zs->next_in   = stream->nextIn;
    zs->avail_in  = stream->availIn;
    zs->next_out  = stream->nextOut;
    zs->avail_out = stream->availOut;
    return zs;
}

// Sync output from z_stream back to PAL_ZStream.
static void TransferState(PAL_ZStream* stream, z_stream* zs) {
    stream->nextIn   = zs->next_in;
    stream->availIn  = zs->avail_in;
    stream->nextOut  = zs->next_out;
    stream->availOut = zs->avail_out;
    stream->msg      = zs->msg;
}

extern "C" {

int32_t CompressionNative_DeflateInit2_(
    PAL_ZStream* stream,
    int32_t level,
    int32_t method,
    int32_t windowBits,
    int32_t memLevel,
    int32_t strategy)
{
    int32_t ret = Init(stream);
    if (ret != Z_OK) return ret;

    z_stream* zs = GetCurrentZStream(stream);
    ret = deflateInit2(zs, level, method, windowBits, memLevel, strategy);
    TransferState(stream, zs);
    return ret;
}

int32_t CompressionNative_Deflate(PAL_ZStream* stream, int32_t flush) {
    z_stream* zs = GetCurrentZStream(stream);
    if (!zs) return Z_STREAM_ERROR;

    int32_t ret = deflate(zs, flush);
    TransferState(stream, zs);
    return ret;
}

int32_t CompressionNative_DeflateEnd(PAL_ZStream* stream) {
    z_stream* zs = GetCurrentZStream(stream);
    if (!zs) return Z_STREAM_ERROR;

    int32_t ret = deflateEnd(zs);
    std::free(zs);
    stream->internalState = nullptr;
    return ret;
}

int32_t CompressionNative_InflateInit2_(PAL_ZStream* stream, int32_t windowBits) {
    int32_t ret = Init(stream);
    if (ret != Z_OK) return ret;

    z_stream* zs = GetCurrentZStream(stream);
    ret = inflateInit2(zs, windowBits);
    TransferState(stream, zs);
    return ret;
}

int32_t CompressionNative_Inflate(PAL_ZStream* stream, int32_t flush) {
    z_stream* zs = GetCurrentZStream(stream);
    if (!zs) return Z_STREAM_ERROR;

    int32_t ret = inflate(zs, flush);
    TransferState(stream, zs);
    return ret;
}

int32_t CompressionNative_InflateEnd(PAL_ZStream* stream) {
    z_stream* zs = GetCurrentZStream(stream);
    if (!zs) return Z_STREAM_ERROR;

    int32_t ret = inflateEnd(zs);
    std::free(zs);
    stream->internalState = nullptr;
    return ret;
}

uint32_t CompressionNative_Crc32(uint32_t crc, uint8_t* buf, int32_t len) {
    if (!buf || len <= 0) return static_cast<uint32_t>(crc32(crc, Z_NULL, 0));
    return static_cast<uint32_t>(crc32(crc, buf, static_cast<uInt>(len)));
}

} // extern "C"
