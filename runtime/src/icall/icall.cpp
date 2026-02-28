/**
 * CIL2CPP Runtime - Internal Call Implementations
 *
 * C++ implementations for BCL [InternalCall] methods.
 */

#include <cil2cpp/cil2cpp.h>
#include <cil2cpp/icall.h>
#include <cil2cpp/string.h>
#include <cil2cpp/array.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/threading.h>
#include <cil2cpp/reflection.h>

#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <string>
#include <thread>

#if defined(_MSC_VER)
#include <intrin.h>
#endif

#ifdef CIL2CPP_WINDOWS
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#elif defined(__linux__)
#include <sys/syscall.h>
#include <unistd.h>
#endif

namespace cil2cpp {
namespace icall {

// ===== System.Environment =====

String* Environment_get_NewLine() {
#ifdef CIL2CPP_WINDOWS
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

void Environment_Exit(Int32 exitCode) {
    runtime_shutdown();
    std::exit(exitCode);
}

Object* Environment_GetCommandLineArgs() {
    int argc = runtime_get_argc();
    char** argv = runtime_get_argv();

    // Create a String[] array
    static TypeInfo stringArrayType = {
        .name = "String[]",
        .namespace_name = "System",
        .full_name = "System.String[]",
        .base_type = nullptr,
        .interfaces = nullptr,
        .interface_count = 0,
        .instance_size = sizeof(void*),
        .element_size = sizeof(void*),
        .flags = TypeFlags::None,
    };

    auto* arr = array_create(&stringArrayType, argc);
    auto** data = reinterpret_cast<String**>(array_data(arr));
    for (int i = 0; i < argc; i++) {
        data[i] = argv ? string_create_utf8(argv[i]) : string_create_utf8("");
    }
    return reinterpret_cast<Object*>(arr);
}

String* Environment_GetEnvironmentVariable(String* variable) {
    if (!variable) return nullptr;
    auto* name = string_to_utf8(variable);
    if (!name) return nullptr;
#ifdef _MSC_VER
    char* value = nullptr;
    size_t len = 0;
    _dupenv_s(&value, &len, name);
    std::free(name);
    if (!value) return nullptr;
    auto* result = string_create_utf8(value);
    std::free(value);
    return result;
#else
    const char* value = std::getenv(name);
    std::free(name);
    if (!value) return nullptr;
    return string_create_utf8(value);
#endif
}

// ===== System.Buffer =====

void Buffer_Memmove(void* dest, void* src, UInt64 len) {
    if (dest && src && len > 0) {
        std::memmove(dest, src, static_cast<size_t>(len));
    }
}

void Buffer_ZeroMemory(void* b, UInt64 byteLength) {
    if (b && byteLength > 0) {
        std::memset(b, 0, static_cast<size_t>(byteLength));
    }
}

void Buffer_BulkMoveWithWriteBarrier(void* dest, void* src, UInt64 len) {
    // BoehmGC is conservative — no write barrier needed, same as memmove
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

// ===== System.Runtime.InteropServices.Marshal =====

intptr_t Marshal_AllocHGlobal(intptr_t cb) {
    return reinterpret_cast<intptr_t>(std::malloc(static_cast<size_t>(cb)));
}

void Marshal_FreeHGlobal(intptr_t hglobal) {
    std::free(reinterpret_cast<void*>(hglobal));
}

intptr_t Marshal_AllocCoTaskMem(Int32 cb) {
    return reinterpret_cast<intptr_t>(std::malloc(static_cast<size_t>(cb)));
}

void Marshal_FreeCoTaskMem(intptr_t ptr) {
    std::free(reinterpret_cast<void*>(ptr));
}

Object* Marshal_StringToCoTaskMemUni(Object* str) {
    // Allocate CoTaskMem and copy UTF-16 string content (static method — no __this)
    if (!str) return reinterpret_cast<Object*>(static_cast<intptr_t>(0));
    auto* s = reinterpret_cast<String*>(str);
    auto len = string_length(s);
    auto bytes = static_cast<size_t>(len + 1) * sizeof(char16_t);
    auto* mem = std::malloc(bytes);
    if (!mem) return reinterpret_cast<Object*>(static_cast<intptr_t>(0));
    std::memcpy(mem, string_get_raw_data(s), static_cast<size_t>(len) * sizeof(char16_t));
    reinterpret_cast<char16_t*>(mem)[len] = u'\0';
    return reinterpret_cast<Object*>(reinterpret_cast<intptr_t>(mem));
}

// ===== System.HashCode / System.Marvin (RNG seed) =====

uint64_t HashCode_GenerateGlobalSeed() {
    uint64_t seed = 0;
#ifdef _WIN32
    // BCrypt.GenRandom — minimal approach with system time + address entropy
    auto tp = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    seed = static_cast<uint64_t>(tp) ^ reinterpret_cast<uintptr_t>(&seed);
#else
    auto tp = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    seed = static_cast<uint64_t>(tp) ^ reinterpret_cast<uintptr_t>(&seed);
#endif
    return seed;
}

uint64_t Marvin_GenerateSeed() {
    return HashCode_GenerateGlobalSeed();
}

// ===== System.Runtime.InteropServices.NativeLibrary =====

intptr_t NativeLibrary_GetSymbol(intptr_t handle, Object* name) {
#ifdef _WIN32
    if (!name) return 0;
    auto* s = reinterpret_cast<String*>(name);
    // Convert managed string to narrow string for GetProcAddress
    auto len = string_length(s);
    auto* chars = string_get_raw_data(s);
    std::string narrow;
    narrow.reserve(static_cast<size_t>(len));
    for (int32_t i = 0; i < len; i++)
        narrow.push_back(static_cast<char>(reinterpret_cast<const char16_t*>(chars)[i]));
    auto* result = GetProcAddress(reinterpret_cast<HMODULE>(handle), narrow.c_str());
    return reinterpret_cast<intptr_t>(result);
#else
    // TODO: dlsym on Linux
    (void)handle; (void)name;
    return 0;
#endif
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
uint8_t Interlocked_Exchange_u8(uint8_t* location, uint8_t value) { return interlocked::exchange_u8(location, value); }
uint8_t Interlocked_CompareExchange_u8(uint8_t* location, uint8_t value, uint8_t comparand) { return interlocked::compare_exchange_u8(location, value, comparand); }
uint16_t Interlocked_Exchange_u16(uint16_t* location, uint16_t value) { return interlocked::exchange_u16(location, value); }
uint16_t Interlocked_CompareExchange_u16(uint16_t* location, uint16_t value, uint16_t comparand) { return interlocked::compare_exchange_u16(location, value, comparand); }

void* Interlocked_Exchange_obj(void* location, void* value) {
    return interlocked::exchange_obj(static_cast<Object**>(location), static_cast<Object*>(value));
}
void* Interlocked_CompareExchange_obj(void* location, void* value, void* comparand) {
    return interlocked::compare_exchange_obj(static_cast<Object**>(location), static_cast<Object*>(value), static_cast<Object*>(comparand));
}

void Interlocked_MemoryBarrier() {
    std::atomic_thread_fence(std::memory_order_seq_cst);
}
void Interlocked_ReadMemoryBarrier() {
    interlocked::read_memory_barrier();
}

// ===== System.Threading.Thread =====

void Thread_Sleep(Int32 milliseconds) {
    thread::sleep(milliseconds);
}

void Thread_SpinWait(Int32 iterations) {
    for (Int32 i = 0; i < iterations; ++i) {
#if defined(_MSC_VER)
        _mm_pause();
#elif defined(__x86_64__) || defined(__i386__)
        __builtin_ia32_pause();
#else
        // ARM: yield hint
        std::this_thread::yield();
#endif
    }
}

Boolean Thread_Yield() {
    std::this_thread::yield();
    return 1; // Always succeeds
}

Int32 Thread_get_OptimalMaxSpinWaitsPerSpinIteration() {
    return 70; // Same default as .NET runtime
}

void* Thread_get_CurrentThread() {
    auto* t = thread_get_current();
    if (!t) {
        // Lazy-create main thread object on first access
        t = static_cast<ManagedThread*>(gc::alloc(sizeof(ManagedThread), nullptr));
        t->managed_id = 1;  // Main thread is always ID 1
        t->state = 1;       // Running
        thread_set_current(t);
    }
    return reinterpret_cast<Object*>(t);
}

UInt64 Thread_GetCurrentOSThreadId() {
#ifdef CIL2CPP_WINDOWS
    return static_cast<UInt64>(::GetCurrentThreadId());
#elif defined(__linux__)
    return static_cast<UInt64>(::syscall(SYS_gettid));
#else
    return 0;
#endif
}

void Thread_Initialize(void* __this) {
    // HACK: No-op — CIL2CPP manages threads through its own runtime.
    // TODO: implement proper thread initialization when Thread becomes IL-compiled
    (void)__this;
}

void* Thread_GetCurrentThreadNative() {
    // Delegate to the existing Thread_get_CurrentThread implementation
    return Thread_get_CurrentThread();
}

Boolean Thread_IsBackgroundNative(void* __this) {
    (void)__this;
    return 1; // HACK: all non-main threads are background by default in CIL2CPP
}

void Thread_SetBackgroundNative(void* __this, Boolean isBackground) {
    (void)__this;
    (void)isBackground;
    // HACK: no-op — CIL2CPP thread pool manages thread lifecycle
}

Int32 Thread_GetPriorityNative(void* __this) {
    (void)__this;
    return 2; // Normal priority (ThreadPriority.Normal = 2)
}

void Thread_SetPriorityNative(void* __this, Int32 priority) {
    (void)__this;
    (void)priority;
    // HACK: no-op — thread priority not implemented
}

Int32 Thread_get_ManagedThreadId(void* __this) {
    (void)__this;
    // HACK: return OS thread ID as managed thread ID for now
    // TODO: implement proper managed thread ID tracking
    return static_cast<Int32>(Thread_GetCurrentOSThreadId() & 0x7FFFFFFF);
}

void Thread_InternalFinalize(void* __this) {
    (void)__this;
    // No-op — CIL2CPP doesn't need thread finalization
}

// ===== System.Runtime.CompilerServices.RuntimeHelpers =====

Boolean RuntimeHelpers_TryEnsureSufficientExecutionStack() {
    return 1; // Always succeeds in AOT — we don't have stack probing
}

void RuntimeHelpers_EnsureSufficientExecutionStack() {
    // No-op in AOT
}

void* RuntimeHelpers_GetObjectMethodTablePointer(Object* obj) {
    if (!obj) return nullptr;
    return const_cast<TypeInfo*>(obj->__type_info);
}

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
    // Maps element_size to CorElementType: 1→I1, 2→I2, 4→I4, 8→I8.
    auto* typeInfo = reinterpret_cast<TypeInfo*>(enumType);
    if (typeInfo) {
        switch (typeInfo->element_size) {
            case 1: return 0x04; // ELEMENT_TYPE_I1
            case 2: return 0x06; // ELEMENT_TYPE_I2
            case 4: return 0x08; // ELEMENT_TYPE_I4
            case 8: return 0x0A; // ELEMENT_TYPE_I8
        }
    }
    return 0x08; // fallback: ELEMENT_TYPE_I4
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

// ===== System.Threading.Interlocked (additional) =====

Int32 Interlocked_ExchangeAdd_i32(Int32* location, Int32 value) {
    // ExchangeAdd returns the OLD value (unlike Add which returns new)
    auto* atomic = reinterpret_cast<std::atomic<Int32>*>(location);
    return atomic->fetch_add(value, std::memory_order_seq_cst);
}

Int64 Interlocked_ExchangeAdd_i64(Int64* location, Int64 value) {
    auto* atomic = reinterpret_cast<std::atomic<Int64>*>(location);
    return atomic->fetch_add(value, std::memory_order_seq_cst);
}

// ===== System.Threading.ThreadPool (CIL2CPP thread pool — mostly no-ops) =====
// CIL2CPP uses its own fixed-size thread pool (cil2cpp::threadpool).
// BCL ThreadPool API calls are redirected here as no-ops or simple stubs.

Int32 ThreadPool_GetNextConfigUInt32Value(Int32 /*configVariableIndex*/,
    uint32_t* configValue, bool* isBoolean, char16_t** /*appContextConfigName*/) {
    // No runtime config variables — return 0 (not found)
    if (configValue) *configValue = 0;
    if (isBoolean) *isBoolean = false;
    return 0; // 0 = not found
}

Object* ThreadPool_GetOrCreateThreadLocalCompletionCountObject() {
    // CIL2CPP thread pool doesn't track per-thread completion counts.
    // Return nullptr — callers check for null before use.
    return nullptr;
}

bool ThreadPool_NotifyWorkItemComplete(Object* /*threadLocalCompletionCountObject*/,
    Int32 /*currentTimeMs*/) {
    // No-op: CIL2CPP thread pool manages its own worker lifecycle
    return false; // false = don't request more workers
}

void ThreadPool_NotifyWorkItemProgress() {
    // No-op: CIL2CPP thread pool doesn't track progress metrics
}

void ThreadPool_ReportThreadStatus(bool /*isWorking*/) {
    // No-op: CIL2CPP thread pool manages its own worker states
}

void ThreadPool_RequestWorkerThread() {
    // No-op: CIL2CPP thread pool has a fixed worker count
}

bool ThreadPoolWorkQueue_Dispatch() {
    // No-op: CIL2CPP thread pool handles its own dispatch loop
    return false; // false = no more work items
}

void ThreadPoolWorkQueue_Enqueue(void* /*__this*/, Object* callback, bool /*forceGlobal*/) {
    // TODO: delegate to cil2cpp::threadpool::queue_work if callback is IThreadPoolWorkItem
    (void)callback;
}

// ===== System.Type (reflection introspection) =====

static Type* get_type_from_this(void* __this) {
    return reinterpret_cast<Type*>(__this);
}

Object* Type_GetEnumUnderlyingType(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return nullptr;
    // HACK: return the type_info itself as the "underlying type" object.
    // Most enums are int32-based. We can't reference generated TypeInfos from the runtime.
    // TODO: store actual underlying type in TypeInfo for enum types.
    return reinterpret_cast<Object*>(type_get_type_object(t->type_info));
}

Boolean Type_get_IsPublic(void* __this) {
    // HACK: simplified — return true for all types.
    // TODO: add visibility flags to TypeInfo.
    (void)__this;
    return true;
}

Boolean Type_get_IsAbstract(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::Abstract;
}

Boolean Type_get_IsValueType(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::ValueType;
}

Boolean Type_get_IsNestedPublic(void* __this) {
    (void)__this;
    return false; // HACK: simplified
}

Boolean Type_IsArrayImpl(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::Array;
}

Boolean Type_IsEnumDefined(void* __this, void* /*value*/) {
    (void)__this;
    return false; // HACK: simplified — would need enum value table
}

Boolean Type_IsEquivalentTo(void* __this, void* other) {
    return __this == other;
}

Int32 Type_GetTypeCodeImpl(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return 0; // TypeCode.Empty
    // HACK: return Object (1) for all types
    // TODO: map TypeInfo to proper TypeCode values
    return 1;
}

Int32 Type_get_GenericParameterAttributes(void* __this) {
    (void)__this;
    return 0; // GenericParameterAttributes.None
}

// ===== System.RuntimeTypeHandle (additional) =====

Object* RuntimeTypeHandle_GetElementType(void* /*handle*/) {
    return nullptr; // HACK: would need element type info for arrays/pointers
}

Boolean RuntimeTypeHandle_IsEquivalentTo(void* /*handle1*/) {
    return false; // HACK: simplified
}

void* RuntimeTypeHandle_GetAssembly(void* /*handle*/) {
    return nullptr; // HACK: no assembly object support yet
}

Boolean RuntimeTypeHandle_IsByRefLike(void* /*handle*/) {
    return false; // HACK: no ref structs in AOT HelloWorld
}

Int32 RuntimeTypeHandle_GetToken(void* /*handle*/) {
    return 0; // HACK: no metadata tokens
}

Boolean RuntimeTypeHandle_IsInstanceOfType(void* /*handle*/, void* /*obj*/) {
    return false; // HACK: simplified
}

void* RuntimeTypeHandle_GetDeclaringMethod(void* /*handle*/) {
    return nullptr; // HACK: no generic parameter method info
}

// ===== System.RuntimeType =====

void* RuntimeType_CreateEnum(void* /*__this*/, Int64 /*value*/) {
    return nullptr; // HACK: would need to box the value as enum
}

// ===== System.Reflection =====

Boolean MethodBase_get_IsVirtual(void* __this) {
    // Read flags from ManagedMethodInfo if available
    (void)__this;
    return false; // HACK: simplified
}

Int32 RuntimeMethodInfo_get_BindingFlags(void* __this) {
    (void)__this;
    return 0x14; // BindingFlags.Public | BindingFlags.Instance (simplified)
}

void* RuntimeMethodInfo_GetGenericArgumentsInternal(void* __this) {
    (void)__this;
    return nullptr; // HACK: return null (no generic args)
}

void* RuntimeMethodInfo_GetDeclaringTypeInternal(void* __this) {
    (void)__this;
    return nullptr; // HACK: simplified
}

Int32 RuntimeConstructorInfo_get_BindingFlags(void* __this) {
    (void)__this;
    return 0x14; // BindingFlags.Public | BindingFlags.Instance
}

Int32 RuntimeFieldInfo_get_BindingFlags(void* __this) {
    (void)__this;
    return 0x14; // BindingFlags.Public | BindingFlags.Instance
}

// ===== System.Delegate =====

void* Delegate_get_Method(void* /*__this*/) {
    return nullptr; // HACK: would need MethodInfo for delegate target
}

// ===== System.Runtime.InteropServices.GCHandle =====

intptr_t GCHandle_InternalCompareExchange(intptr_t handle, cil2cpp::Object* value, cil2cpp::Object* comparand) {
    // HACK: GCHandle is an opaque integer handle. In a real implementation this would
    // do atomic CAS on the handle table. For now, just return the handle unchanged.
    // TODO: implement proper GCHandle table with atomic compare-exchange
    (void)value;
    (void)comparand;
    return handle;
}

// ===== System.Diagnostics =====

void* StackFrameHelper_GetMethodBase(void* /*__this*/, Int32 /*index*/) {
    return nullptr; // HACK: no method base for stack frames yet
}

void* StackFrame_GetMethod(void* /*__this*/) {
    return nullptr; // HACK: no method info for stack frames yet
}

} // namespace icall
} // namespace cil2cpp
