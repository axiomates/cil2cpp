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

// System.Buffer
void Buffer_Memmove(void* dest, void* src, UInt64 len);
void Buffer_BlockCopy(Object* src, Int32 srcOffset, Object* dst, Int32 dstOffset, Int32 count);

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

// System.ArgumentNullException
void ArgumentNullException_ThrowIfNull(Object* arg, String* paramName);

// System.ThrowHelper (BCL internal)
void ThrowHelper_ThrowArgumentException(Int32 resource);

// System.Threading.Thread
void Thread_Sleep(Int32 milliseconds);

// System.Runtime.CompilerServices.RuntimeHelpers
void RuntimeHelpers_InitializeArray(Object* array, void* fieldHandle);
bool RuntimeHelpers_IsReferenceOrContainsReferences();

// System.Enum
Object* Enum_InternalBoxEnum(void* enumType, Int64 value);
Int32 Enum_InternalGetCorElementType(void* enumType);

// System.Delegate (internal)
Object* Delegate_InternalAlloc(void* type);
Object* Delegate_BindToMethodInfo(Object* target, void* method, void* methodType, Int32 flags);

// System.Char (primitive classification)
bool Char_IsWhiteSpace(char16_t c);
bool Char_IsAsciiDigit(char16_t c);
bool Char_IsAscii(char16_t c);
bool Char_IsLetter(char16_t c);
bool Char_IsDigit(char16_t c);
bool Char_IsUpper(char16_t c);
bool Char_IsLower(char16_t c);

// System.Int32
String* Int32_ToString(Int32* value);

} // namespace icall
} // namespace cil2cpp
