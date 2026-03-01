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
Object* Marshal_StringToCoTaskMemUni(Object* str);

// System.HashCode / System.Marvin (RNG seed)
uint64_t HashCode_GenerateGlobalSeed();
uint64_t Marvin_GenerateSeed();

// System.Diagnostics.Tracing.ActivityTracker
inline void* ActivityTracker_get_Instance() { return nullptr; }

// System.Type (reflection introspection)
Object* Type_GetEnumUnderlyingType(void* __this);
Boolean Type_get_IsPublic(void* __this);
Boolean Type_get_IsAbstract(void* __this);
Boolean Type_get_IsValueType(void* __this);
Boolean Type_get_IsNestedPublic(void* __this);
Boolean Type_IsArrayImpl(void* __this);
Boolean Type_IsEnumDefined(void* __this, void* value);
Boolean Type_IsEquivalentTo(void* __this, void* other);
Int32 Type_GetTypeCodeImpl(void* __this);
Int32 Type_get_GenericParameterAttributes(void* __this);

// System.RuntimeTypeHandle
Object* RuntimeTypeHandle_GetElementType(void* handle);
Boolean RuntimeTypeHandle_IsEquivalentTo(void* handle1);
void* RuntimeTypeHandle_GetAssembly(void* handle);
Boolean RuntimeTypeHandle_IsByRefLike(void* handle);
Int32 RuntimeTypeHandle_GetToken(void* handle);
Boolean RuntimeTypeHandle_IsInstanceOfType(void* handle, void* obj);
void* RuntimeTypeHandle_GetDeclaringMethod(void* handle);

// System.RuntimeMethodHandle
inline Boolean RuntimeMethodHandle_IsDynamicMethod(void*) { return false; }
inline void* RuntimeMethodHandle_ReboxToNullable(void* src, void*) { return src; }

// System.RuntimeType (internal helpers)
inline Boolean RuntimeType_CanValueSpecialCast(void*) { return false; }
void* RuntimeType_CreateEnum(void* __this, Int64 value);

// System.Reflection
inline void* TypeInfo_AsType(void* __this) { return __this; }
Boolean MethodBase_get_IsVirtual(void* __this);
Int32 RuntimeMethodInfo_get_BindingFlags(void* __this);
void* RuntimeMethodInfo_GetGenericArgumentsInternal(void* __this);
void* RuntimeMethodInfo_GetDeclaringTypeInternal(void* __this);
Int32 RuntimeConstructorInfo_get_BindingFlags(void* __this);
Int32 RuntimeFieldInfo_get_BindingFlags(void* __this);

// System.Delegate
void* Delegate_get_Method(void* __this);

// System.Runtime.InteropServices.GCHandle
intptr_t GCHandle_InternalCompareExchange(intptr_t handle, cil2cpp::Object* value, cil2cpp::Object* comparand);

// System.Diagnostics (stack traces)
void* StackFrameHelper_GetMethodBase(void* __this, Int32 index);
void* StackFrame_GetMethod(void* __this);

// System.Runtime.Loader
inline void* AssemblyLoadContext_OnTypeResolve(void*) { return nullptr; }

// System.Runtime.InteropServices.NativeLibrary
intptr_t NativeLibrary_GetSymbol(intptr_t handle, Object* name);

// System.IntPtr / System.UIntPtr
// IntPtr/UIntPtr are aliased to intptr_t/uintptr_t (scalars).
// IL methods access f_value field which doesn't exist on a scalar.
inline void IntPtr_ctor_i32(intptr_t* self, Int32 value) { *self = static_cast<intptr_t>(value); }
inline void IntPtr_ctor_i64(intptr_t* self, Int64 value) { *self = static_cast<intptr_t>(value); }
inline void* IntPtr_ToPointer(intptr_t* self) { return reinterpret_cast<void*>(*self); }
inline void UIntPtr_ctor_u32(uintptr_t* self, UInt32 value) { *self = static_cast<uintptr_t>(value); }
inline void UIntPtr_ctor_u64(uintptr_t* self, UInt64 value) { *self = static_cast<uintptr_t>(value); }
inline void* UIntPtr_ToPointer(uintptr_t* self) { return reinterpret_cast<void*>(*self); }

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
uint8_t Interlocked_Exchange_u8(uint8_t* location, uint8_t value);
uint8_t Interlocked_CompareExchange_u8(uint8_t* location, uint8_t value, uint8_t comparand);
uint16_t Interlocked_Exchange_u16(uint16_t* location, uint16_t value);
uint16_t Interlocked_CompareExchange_u16(uint16_t* location, uint16_t value, uint16_t comparand);
void* Interlocked_Exchange_obj(void* location, void* value);
void* Interlocked_CompareExchange_obj(void* location, void* value, void* comparand);
void Interlocked_MemoryBarrier();
void Interlocked_ReadMemoryBarrier();
Int32 Interlocked_ExchangeAdd_i32(Int32* location, Int32 value);
Int64 Interlocked_ExchangeAdd_i64(Int64* location, Int64 value);

// System.Threading.Thread
void Thread_Sleep(Int32 milliseconds);
void Thread_SpinWait(Int32 iterations);
Boolean Thread_Yield();
Int32 Thread_get_OptimalMaxSpinWaitsPerSpinIteration();
void* Thread_get_CurrentThread();
UInt64 Thread_GetCurrentOSThreadId();
void Thread_Initialize(void* __this);
void* Thread_GetCurrentThreadNative();
Boolean Thread_IsBackgroundNative(void* __this);
void Thread_SetBackgroundNative(void* __this, Boolean isBackground);
Int32 Thread_GetPriorityNative(void* __this);
void Thread_SetPriorityNative(void* __this, Int32 priority);
Int32 Thread_get_ManagedThreadId(void* __this);
void Thread_InternalFinalize(void* __this);

// System.Runtime.CompilerServices.RuntimeHelpers
void RuntimeHelpers_InitializeArray(Object* array, void* fieldHandle);
Boolean RuntimeHelpers_TryEnsureSufficientExecutionStack();
void RuntimeHelpers_EnsureSufficientExecutionStack();
void* RuntimeHelpers_GetObjectMethodTablePointer(Object* obj);
Boolean RuntimeHelpers_ObjectHasComponentSize(Object* obj);

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

// System.Threading.ThreadPool (CIL2CPP has its own thread pool — mostly no-ops)
Int32 ThreadPool_GetNextConfigUInt32Value(Int32 configVariableIndex,
    uint32_t* configValue, bool* isBoolean, char16_t** appContextConfigName);
Object* ThreadPool_GetOrCreateThreadLocalCompletionCountObject();
bool ThreadPool_NotifyWorkItemComplete(Object* threadLocalCompletionCountObject, Int32 currentTimeMs);
void ThreadPool_NotifyWorkItemProgress();
void ThreadPool_ReportThreadStatus(bool isWorking);
void ThreadPool_RequestWorkerThread();
bool ThreadPoolWorkQueue_Dispatch();
void ThreadPoolWorkQueue_Enqueue(void* __this, Object* callback, bool forceGlobal);

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
double Math_CopySign(double x, double y);
double Math_BitDecrement(double value);
double Math_BitIncrement(double value);

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
cil2cpp::String* SR_GetResourceString(cil2cpp::String* key);
cil2cpp::String* ThrowHelper_GetResourceString(Int32 resource);
cil2cpp::String* ThrowHelper_GetArgumentName(Int32 argument);

// System.Text.Ascii — scalar replacements for SIMD-dependent BCL methods
bool Ascii_AllBytesInUInt32AreAscii(uint32_t value);
bool Ascii_AllBytesInUInt64AreAscii(uint64_t value);
bool Ascii_AllCharsInUInt32AreAscii(uint32_t value);
bool Ascii_AllCharsInUInt64AreAscii(uint64_t value);
bool Ascii_FirstCharInUInt32IsAscii(uint32_t value);
bool Ascii_IsValid_byte(uint8_t value);
bool Ascii_IsValid_char(char16_t value);
uintptr_t Ascii_WidenAsciiToUtf16(uint8_t* pAsciiBuffer, char16_t* pUtf16Buffer, uintptr_t elementCount);
void Ascii_WidenFourAsciiBytesToUtf16AndWriteToBuffer(char16_t* outputBuffer, uint32_t value);
uintptr_t Ascii_NarrowUtf16ToAscii(char16_t* pUtf16Buffer, uint8_t* pAsciiBuffer, uintptr_t elementCount);
void Ascii_NarrowFourUtf16CharsToAsciiAndWriteToBuffer(uint8_t* outputBuffer, uint64_t value);
void Ascii_NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(uint8_t* outputBuffer, uint32_t value);
uint32_t Ascii_CountNumberOfLeadingAsciiBytesFromUInt32WithSomeNonAsciiData(uint32_t value);
uintptr_t Ascii_GetIndexOfFirstNonAsciiByte(uint8_t* pBuffer, uintptr_t bufferLength);
uintptr_t Ascii_GetIndexOfFirstNonAsciiChar(char16_t* pBuffer, uintptr_t bufferLength);
bool Ascii_ContainsNonAsciiByte_Sse2(uint32_t sseMask);

// SpanHelpers.DontNegate<T> / Negate<T> — generic helper structs for IndexOfAny
// DontNegate: identity function, Negate: logical negation
// The bool overload is used by scalar loop; Vector overloads unused (SIMD disabled).
bool SpanHelpers_DontNegate_NegateIfNeeded(bool equals);
bool SpanHelpers_Negate_NegateIfNeeded(bool equals);

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
