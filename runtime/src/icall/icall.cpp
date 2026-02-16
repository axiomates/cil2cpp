/**
 * CIL2CPP Runtime - Internal Call Implementations
 *
 * C++ implementations for BCL [InternalCall] methods.
 */

#include <cil2cpp/icall.h>
#include <cil2cpp/string.h>
#include <cil2cpp/array.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/threading.h>
#include <cil2cpp/reflection.h>

#include <chrono>
#include <cstring>
#include <thread>

namespace cil2cpp {
namespace icall {

// ===== System.Environment =====

String* Environment_get_NewLine() {
#ifdef _WIN32
    return string_literal("\r\n");
#else
    return string_literal("\n");
#endif
}

Int32 Environment_get_TickCount() {
    auto now = std::chrono::steady_clock::now();
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch());
    return static_cast<Int32>(ms.count());
}

Int64 Environment_get_TickCount64() {
    auto now = std::chrono::steady_clock::now();
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch());
    return static_cast<Int64>(ms.count());
}

Int32 Environment_get_ProcessorCount() {
    auto count = std::thread::hardware_concurrency();
    return count > 0 ? static_cast<Int32>(count) : 1;
}

Int32 Environment_get_CurrentManagedThreadId() {
    // Return a hash of the native thread ID as a managed thread ID
    return static_cast<Int32>(std::hash<std::thread::id>{}(std::this_thread::get_id()) & 0x7FFFFFFF);
}

// ===== System.Buffer =====

void Buffer_Memmove(void* dest, void* src, UInt64 len) {
    if (dest && src && len > 0) {
        std::memmove(dest, src, static_cast<size_t>(len));
    }
}

void Buffer_BlockCopy(Object* src, Int32 srcOffset, Object* dst, Int32 dstOffset, Int32 count) {
    if (!src || !dst || count <= 0) return;
    auto srcBytes = reinterpret_cast<uint8_t*>(array_data(reinterpret_cast<Array*>(src)));
    auto dstBytes = reinterpret_cast<uint8_t*>(array_data(reinterpret_cast<Array*>(dst)));
    std::memmove(dstBytes + dstOffset, srcBytes + srcOffset, static_cast<size_t>(count));
}

// ===== System.Type =====

Object* Type_GetTypeFromHandle(void* handle) {
    return reinterpret_cast<Object*>(type_get_type_from_handle(handle));
}

// ===== System.Threading.Monitor =====

void Monitor_Enter(Object* obj) {
    monitor::enter(obj);
}

void Monitor_Enter2(Object* obj, bool* lockTaken) {
    monitor::reliable_enter(obj, lockTaken);
}

void Monitor_Exit(Object* obj) {
    monitor::exit(obj);
}

void Monitor_ReliableEnter(Object* obj, bool* lockTaken) {
    monitor::reliable_enter(obj, lockTaken);
}

bool Monitor_Wait(Object* obj, Int32 timeout_ms) {
    return monitor::wait(obj, timeout_ms);
}

void Monitor_Pulse(Object* obj) {
    monitor::pulse(obj);
}

void Monitor_PulseAll(Object* obj) {
    monitor::pulse_all(obj);
}

// ===== System.Threading.Interlocked =====

Int32 Interlocked_Increment_i32(Int32* location) { return interlocked::increment_i32(location); }
Int32 Interlocked_Decrement_i32(Int32* location) { return interlocked::decrement_i32(location); }
Int32 Interlocked_Exchange_i32(Int32* location, Int32 value) { return interlocked::exchange_i32(location, value); }
Int32 Interlocked_CompareExchange_i32(Int32* location, Int32 value, Int32 comparand) { return interlocked::compare_exchange_i32(location, value, comparand); }
Int32 Interlocked_Add_i32(Int32* location, Int32 value) { return interlocked::add_i32(location, value); }
Int64 Interlocked_Add_i64(Int64* location, Int64 value) { return interlocked::add_i64(location, value); }
Int64 Interlocked_Increment_i64(Int64* location) { return interlocked::increment_i64(location); }
Int64 Interlocked_Decrement_i64(Int64* location) { return interlocked::decrement_i64(location); }
Int64 Interlocked_Exchange_i64(Int64* location, Int64 value) { return interlocked::exchange_i64(location, value); }
Int64 Interlocked_CompareExchange_i64(Int64* location, Int64 value, Int64 comparand) { return interlocked::compare_exchange_i64(location, value, comparand); }
void* Interlocked_Exchange_obj(void* location, void* value) {
    return interlocked::exchange_obj(static_cast<Object**>(location), static_cast<Object*>(value));
}
void* Interlocked_CompareExchange_obj(void* location, void* value, void* comparand) {
    return interlocked::compare_exchange_obj(static_cast<Object**>(location), static_cast<Object*>(value), static_cast<Object*>(comparand));
}

// ===== System.Threading.Thread =====

void Thread_Sleep(Int32 milliseconds) {
    thread::sleep(milliseconds);
}

// ===== System.ArgumentNullException =====

void ArgumentNullException_ThrowIfNull(Object* arg, String* paramName) {
    if (arg == nullptr) {
        throw_argument_null();
    }
}

// ===== System.ThrowHelper =====

void ThrowHelper_ThrowArgumentException(Int32 resource) {
    throw_argument();
}

// ===== System.Runtime.CompilerServices.RuntimeHelpers =====

void RuntimeHelpers_InitializeArray(Object* array, void* fieldHandle) {
    // This is typically handled by the compiler via InitializeArray intrinsic,
    // but when called as an icall, we copy the data directly.
    if (!array || !fieldHandle) return;
    auto arr = reinterpret_cast<Array*>(array);
    auto dataPtr = array_data(arr);
    auto length = array_length(arr);
    if (length > 0 && dataPtr && arr->element_type) {
        auto elemSize = arr->element_type->element_size;
        std::memcpy(dataPtr, fieldHandle, static_cast<size_t>(length) * elemSize);
    }
}

bool RuntimeHelpers_IsReferenceOrContainsReferences() {
    // Conservative: assume it may contain references
    return true;
}

// ===== System.Enum =====

Object* Enum_InternalBoxEnum(void* enumType, Int64 value) {
    // Box an enum value given its RuntimeType handle and int64 value.
    // enumType is a RuntimeType* (treated as TypeInfo* for type metadata).
    auto* typeInfo = reinterpret_cast<TypeInfo*>(enumType);
    if (!typeInfo) return nullptr;
    auto elemSize = typeInfo->element_size > 0 ? typeInfo->element_size : sizeof(Int64);
    auto* obj = reinterpret_cast<Object*>(gc::alloc(
        static_cast<int32_t>(sizeof(Object) + elemSize), typeInfo));
    auto* data = reinterpret_cast<char*>(obj) + sizeof(Object);
    std::memcpy(data, &value, elemSize);
    return obj;
}

Int32 Enum_InternalGetCorElementType(void* enumType) {
    // Returns the CorElementType for the underlying type of an enum.
    // 0x08 = I4 (int), which is the most common enum underlying type.
    (void)enumType;
    return 0x08; // ELEMENT_TYPE_I4
}

// ===== System.Delegate (internal) =====

Object* Delegate_InternalAlloc(void* type) {
    // Allocate a delegate instance of the given RuntimeType.
    auto* typeInfo = reinterpret_cast<TypeInfo*>(type);
    if (!typeInfo) return nullptr;
    auto size = typeInfo->instance_size > 0
        ? typeInfo->instance_size
        : static_cast<int32_t>(sizeof(Object) + 3 * sizeof(void*));
    return reinterpret_cast<Object*>(gc::alloc(size, typeInfo));
}

Object* Delegate_BindToMethodInfo(Object* target, void* method, void* methodType, Int32 flags) {
    // Bind a delegate to a method. In our flat model, this is a stub.
    // The generated code handles delegate binding directly via constructors.
    (void)target; (void)method; (void)methodType; (void)flags;
    return nullptr;
}

// ===== System.Char (primitive classification) =====

bool Char_IsWhiteSpace(char16_t c) {
    return c == u' ' || c == u'\t' || c == u'\n' || c == u'\r' || c == u'\f' || c == u'\v'
        || c == 0x00A0 || c == 0x0085 || c == 0x2000 || c == 0x2001 || c == 0x2002
        || c == 0x2003 || c == 0x2004 || c == 0x2005 || c == 0x2006 || c == 0x2007
        || c == 0x2008 || c == 0x2009 || c == 0x200A || c == 0x2028 || c == 0x2029
        || c == 0x202F || c == 0x205F || c == 0x3000 || c == 0xFEFF;
}

bool Char_IsAsciiDigit(char16_t c) {
    return c >= u'0' && c <= u'9';
}

bool Char_IsAscii(char16_t c) {
    return c <= 0x007F;
}

bool Char_IsLetter(char16_t c) {
    return (c >= u'A' && c <= u'Z') || (c >= u'a' && c <= u'z')
        || (c >= 0x00C0 && c <= 0x024F && c != 0x00D7 && c != 0x00F7);
}

bool Char_IsDigit(char16_t c) {
    return c >= u'0' && c <= u'9';
}

bool Char_IsUpper(char16_t c) {
    return (c >= u'A' && c <= u'Z') || (c >= 0x00C0 && c <= 0x00DE && c != 0x00D7);
}

bool Char_IsLower(char16_t c) {
    return (c >= u'a' && c <= u'z') || (c >= 0x00DF && c <= 0x00FF && c != 0x00F7);
}

// ===== System.Int32 =====

String* Int32_ToString(Int32* value) {
    if (!value) return string_literal("0");
    char buf[16];
    int len = snprintf(buf, sizeof(buf), "%d", *value);
    buf[len] = '\0';
    return string_create_utf8(buf);
}

} // namespace icall
} // namespace cil2cpp
