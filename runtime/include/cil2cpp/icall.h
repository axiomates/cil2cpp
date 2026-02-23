/**
 * CIL2CPP Runtime - Internal Call Declarations
 *
 * C++ implementations for [InternalCall] methods in the .NET BCL.
 * These are called by generated code when the compiler encounters
 * [MethodImpl(MethodImplOptions.InternalCall)] methods.
 */

#pragma once

#include "types.h"
#include "object.h"
#include "string.h"

namespace cil2cpp {
namespace icall {

// System.Environment
String* Environment_get_NewLine();
Int32 Environment_get_TickCount();
Int64 Environment_get_TickCount64();
Int32 Environment_get_ProcessorCount();
Int32 Environment_get_CurrentManagedThreadId();
void Environment_Exit(Int32 exitCode);
Object* Environment_GetCommandLineArgs();
String* Environment_GetEnvironmentVariable(String* variable);

// System.Buffer
void Buffer_Memmove(void* dest, void* src, UInt64 len);
void Buffer_ZeroMemory(void* b, UInt64 byteLength);
void Buffer_BulkMoveWithWriteBarrier(void* dest, void* src, UInt64 len);
void Buffer_BlockCopy(Object* src, Int32 srcOffset, Object* dst, Int32 dstOffset, Int32 count);

// System.Runtime.InteropServices.Marshal
intptr_t Marshal_AllocHGlobal(intptr_t cb);
void Marshal_FreeHGlobal(intptr_t hglobal);
intptr_t Marshal_AllocCoTaskMem(Int32 cb);
void Marshal_FreeCoTaskMem(intptr_t ptr);

// System.Type
Object* Type_GetTypeFromHandle(void* handle);

// System.Threading.Monitor
void Monitor_Enter(Object* obj);
void Monitor_Enter2(Object* obj, bool* lockTaken);
void Monitor_Exit(Object* obj);
void Monitor_ReliableEnter(Object* obj, bool* lockTaken);
bool Monitor_Wait(Object* obj, Int32 timeout_ms);
inline bool Monitor_Wait(Object* obj) { return Monitor_Wait(obj, -1); }
void Monitor_Pulse(Object* obj);
void Monitor_PulseAll(Object* obj);

// System.Threading.Interlocked
Int32 Interlocked_Increment_i32(Int32* location);
Int32 Interlocked_Decrement_i32(Int32* location);
Int32 Interlocked_Exchange_i32(Int32* location, Int32 value);
Int32 Interlocked_CompareExchange_i32(Int32* location, Int32 value, Int32 comparand);
Int32 Interlocked_Add_i32(Int32* location, Int32 value);
Int64 Interlocked_Add_i64(Int64* location, Int64 value);
Int64 Interlocked_Increment_i64(Int64* location);
Int64 Interlocked_Decrement_i64(Int64* location);
Int64 Interlocked_Exchange_i64(Int64* location, Int64 value);
Int64 Interlocked_CompareExchange_i64(Int64* location, Int64 value, Int64 comparand);
void* Interlocked_Exchange_obj(void* location, void* value);
void* Interlocked_CompareExchange_obj(void* location, void* value, void* comparand);
void Interlocked_MemoryBarrier();

// System.Threading.Thread
void Thread_Sleep(Int32 milliseconds);
void Thread_SpinWait(Int32 iterations);
Boolean Thread_Yield();
Int32 Thread_get_OptimalMaxSpinWaitsPerSpinIteration();
void* Thread_get_CurrentThread();
UInt64 Thread_GetCurrentOSThreadId();

// System.Runtime.CompilerServices.RuntimeHelpers
void RuntimeHelpers_InitializeArray(Object* array, void* fieldHandle);
Boolean RuntimeHelpers_TryEnsureSufficientExecutionStack();
void RuntimeHelpers_EnsureSufficientExecutionStack();
void* RuntimeHelpers_GetObjectMethodTablePointer(Object* obj);

// System.Runtime.InteropServices.GCHandle
intptr_t GCHandle_InternalAlloc(void* obj, Int32 type);
void GCHandle_InternalFree(intptr_t handle);
void GCHandle_InternalSet(intptr_t handle, void* obj);
void* GCHandle_InternalGet(intptr_t handle);

// System.Enum
Object* Enum_InternalBoxEnum(void* enumType, Int64 value);
Int32 Enum_InternalGetCorElementType(void* enumType);

// System.Delegate (internal)
Object* Delegate_InternalAlloc(void* type);

// System.Math (double)
double Math_Abs_double(double value);
float Math_Abs_float(float value);
int32_t Math_Abs_int(int32_t value);
int64_t Math_Abs_long(int64_t value);
double Math_Sqrt(double value);
double Math_Sin(double value);
double Math_Cos(double value);
double Math_Tan(double value);
double Math_Asin(double value);
double Math_Acos(double value);
double Math_Atan(double value);
double Math_Atan2(double y, double x);
double Math_Pow(double x, double y);
double Math_Exp(double value);
double Math_Log(double value);
double Math_Log10(double value);
double Math_Log2(double value);
double Math_Floor(double value);
double Math_Ceiling(double value);
double Math_Round(double value);
double Math_Truncate(double value);
double Math_Max_double(double a, double b);
double Math_Min_double(double a, double b);
int32_t Math_Max_int(int32_t a, int32_t b);
int32_t Math_Min_int(int32_t a, int32_t b);
double Math_Cbrt(double value);
double Math_IEEERemainder(double x, double y);
double Math_FusedMultiplyAdd(double x, double y, double z);

// System.MathF (float)
float MathF_Sqrt(float value);
float MathF_Sin(float value);
float MathF_Cos(float value);
float MathF_Tan(float value);
float MathF_Asin(float value);
float MathF_Acos(float value);
float MathF_Atan(float value);
float MathF_Atan2(float y, float x);
float MathF_Pow(float x, float y);
float MathF_Exp(float value);
float MathF_Log(float value);
float MathF_Log10(float value);
float MathF_Log2(float value);
float MathF_Floor(float value);
float MathF_Ceiling(float value);
float MathF_Round(float value);
float MathF_Truncate(float value);
float MathF_Max(float a, float b);
float MathF_Min(float a, float b);
float MathF_Cbrt(float value);
float MathF_FusedMultiplyAdd(float x, float y, float z);

// System.ThrowHelper — BCL exception throw helpers.
// These take enum parameters (ExceptionArgument, ExceptionResource) that we ignore,
// since our runtime throws generic exception instances.
[[noreturn]] void ThrowHelper_ThrowArgumentOutOfRangeException(Int32 argument);
[[noreturn]] void ThrowHelper_ThrowArgumentOutOfRangeException2(Int32 argument, Int32 resource);
[[noreturn]] void ThrowHelper_ThrowArgumentNullException(Int32 argument);
[[noreturn]] void ThrowHelper_ThrowArgumentNullException2(cil2cpp::String* paramName);
[[noreturn]] void ThrowHelper_ThrowArgumentException(Int32 resource);
[[noreturn]] void ThrowHelper_ThrowArgumentException2(Int32 resource, Int32 argument);
[[noreturn]] void ThrowHelper_ThrowInvalidOperationException(Int32 resource);
[[noreturn]] void ThrowHelper_ThrowInvalidOperationException0();
[[noreturn]] void ThrowHelper_ThrowNotSupportedException(Int32 resource);
[[noreturn]] void ThrowHelper_ThrowNotSupportedException0();
[[noreturn]] void ThrowHelper_ThrowFormatInvalidString(Int32 offset, Int32 reason);
[[noreturn]] void ThrowHelper_ThrowUnexpectedStateForKnownCallback(Int32 state);
// Overload for calls where the enum parameter resolves to Object* in generated code
[[noreturn]] inline void ThrowHelper_ThrowUnexpectedStateForKnownCallback(cil2cpp::Object*) {
    ThrowHelper_ThrowUnexpectedStateForKnownCallback(0);
}

// System.ThrowHelper non-throwing helpers (return exception objects for GetXxx pattern)
cil2cpp::Object* ThrowHelper_GetArgumentException(Int32 resource);
cil2cpp::Object* ThrowHelper_GetArgumentException2(Int32 resource, Int32 argument);
cil2cpp::Object* ThrowHelper_GetArgumentOutOfRangeException(Int32 argument);
cil2cpp::Object* ThrowHelper_GetInvalidOperationException(Int32 resource);
cil2cpp::String* ThrowHelper_GetResourceString(Int32 resource);
cil2cpp::String* ThrowHelper_GetArgumentName(Int32 argument);

} // namespace icall

// System.Text.Unicode.Utf8Utility — scalar replacements for SIMD-based BCL methods
Int32 utf8_utility_transcode_to_utf8(
    Char* pInputBuffer, Int32 inputLength,
    uint8_t* pOutputBuffer, Int32 outputBytesRemaining,
    Char** pInputBufferRemaining, uint8_t** pOutputBufferRemaining);

uint8_t* utf8_utility_get_pointer_to_first_invalid_byte(
    uint8_t* pInputBuffer, Int32 inputLength,
    Int32* utf16CodeUnitCountAdjustment, Int32* scalarCountAdjustment);

} // namespace cil2cpp
