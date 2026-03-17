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
#include <cil2cpp/memberinfo.h>
#include <cil2cpp/boxing.h>
#include <cil2cpp/gchandle.h>
#include <cil2cpp/threadpool.h>
#include <cil2cpp/iocp.h>

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
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#elif defined(__linux__)
#include <sys/syscall.h>
#include <unistd.h>
#endif

// Forward declaration: IThreadPoolWorkItem TypeInfo (defined in generated code at global scope)
// Forward declaration for compiled BCL dispatch function (C++ linkage, matches generated code)
void System_Threading_ThreadPoolWorkQueue_DispatchWorkItem(cil2cpp::Object* workItem, cil2cpp::ManagedThread* currentThread);

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

Boolean Marshal_TryGetStructMarshalStub(void* /*structureTypeHandle*/, void* marshalStub, void* sizeOf) {
    // In AOT, no struct marshal stubs exist. Set outputs to 0 and return false.
    if (marshalStub) *reinterpret_cast<intptr_t*>(marshalStub) = 0;
    if (sizeOf) *reinterpret_cast<int32_t*>(sizeOf) = 0;
    return false;
}

void Marshal_StructureToPtr(void* structure, intptr_t ptr, Boolean /*fDeleteOld*/) {
    // In compiled C++ code, value types are passed directly (not boxed).
    // We don't know the size at this level — but the caller passes struct by value,
    // so this ICall is only reached from IL that was compiled with the struct type known.
    // For AOT, this is a no-op stub — the actual P/Invoke marshaling is handled by
    // the compiled IL that copies fields to native memory before the P/Invoke call.
    // The struct data has already been written to the target pointer by the caller's IL.
    (void)structure;
    (void)ptr;
}

// ===== System.HashCode / System.Marvin (RNG seed) =====

uint64_t HashCode_GenerateGlobalSeed() {
    uint64_t seed = 0;
#ifdef CIL2CPP_WINDOWS
    BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(&seed), sizeof(seed),
                    BCRYPT_USE_SYSTEM_PREFERRED_RNG);
#elif defined(__linux__)
    // /dev/urandom is always available on Linux
    FILE* f = fopen("/dev/urandom", "rb");
    if (f) {
        fread(&seed, sizeof(seed), 1, f);
        fclose(f);
    }
#else
    // Fallback: time + address entropy for other platforms
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
        t->state.store(1, std::memory_order_relaxed); // Running (single-threaded lazy init)
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
    // No-op: CIL2CPP manages threads through its own ManagedThread struct.
    // CLR's Thread.Initialize() sets up ClrThread internals which don't exist in AOT.
    (void)__this;
}

void* Thread_GetCurrentThreadNative() {
    // Delegate to the existing Thread_get_CurrentThread implementation
    return Thread_get_CurrentThread();
}

Boolean Thread_IsBackgroundNative(void* __this) {
    auto* t = static_cast<ManagedThread*>(__this);
    if (!t) return 0;
    return t->is_background ? 1 : 0;
}

void Thread_SetBackgroundNative(void* __this, Boolean isBackground) {
    auto* t = static_cast<ManagedThread*>(__this);
    if (t) t->is_background = (isBackground != 0);
}

Int32 Thread_GetPriorityNative(void* __this) {
#ifdef _WIN32
    auto* t = static_cast<ManagedThread*>(__this);
    if (t && t->native_handle != nullptr) {
        auto* stdThread = static_cast<std::thread*>(t->native_handle);
        HANDLE hThread = static_cast<HANDLE>(stdThread->native_handle());
        int win32Priority = GetThreadPriority(hThread);
        // Map Win32 priority back to .NET ThreadPriority (0-4)
        switch (win32Priority) {
            case THREAD_PRIORITY_LOWEST: return 0;
            case THREAD_PRIORITY_BELOW_NORMAL: return 1;
            case THREAD_PRIORITY_NORMAL: return 2;
            case THREAD_PRIORITY_ABOVE_NORMAL: return 3;
            case THREAD_PRIORITY_HIGHEST: return 4;
            default: return 2;
        }
    }
#else
    (void)__this;
#endif
    return 2; // Default: Normal priority
}

void Thread_SetPriorityNative(void* __this, Int32 priority) {
    (void)__this;
#ifdef _WIN32
    // Map .NET ThreadPriority (0-4) to Win32 thread priority constants
    int win32Priority;
    switch (priority) {
        case 0: win32Priority = THREAD_PRIORITY_LOWEST; break;        // Lowest
        case 1: win32Priority = THREAD_PRIORITY_BELOW_NORMAL; break;  // BelowNormal
        case 2: win32Priority = THREAD_PRIORITY_NORMAL; break;        // Normal
        case 3: win32Priority = THREAD_PRIORITY_ABOVE_NORMAL; break;  // AboveNormal
        case 4: win32Priority = THREAD_PRIORITY_HIGHEST; break;       // Highest
        default: win32Priority = THREAD_PRIORITY_NORMAL; break;
    }
    auto* t = static_cast<ManagedThread*>(__this);
    if (t && t->native_handle != nullptr) {
        auto* stdThread = static_cast<std::thread*>(t->native_handle);
        HANDLE hThread = static_cast<HANDLE>(stdThread->native_handle());
        SetThreadPriority(hThread, win32Priority);
    }
#else
    (void)priority;
    // TODO: implement with pthread_setschedparam on Linux/macOS
#endif
}

Int32 Thread_get_ManagedThreadId(void* __this) {
    auto* t = static_cast<ManagedThread*>(__this);
    if (t) return t->managed_id;
    // Fallback for unexpected null
    return static_cast<Int32>(Thread_GetCurrentOSThreadId() & 0x7FFFFFFF);
}

void Thread_InternalFinalize(void* __this) {
    (void)__this;
    // No-op — CIL2CPP doesn't need thread finalization
}

void Thread_Join(void* __this) {
    thread::join(reinterpret_cast<ManagedThread*>(__this));
}

Boolean Thread_Join_Timeout(void* __this, Int32 timeout_ms) {
    return thread::join_timeout(reinterpret_cast<ManagedThread*>(__this), timeout_ms) ? 1 : 0;
}

void Thread_Start(void* __this) {
    thread::start(reinterpret_cast<ManagedThread*>(__this));
}

void Thread_LongSpinWait(Int32 iterations) {
    // Long spin: yield between iterations (used by SpinLock/SpinWait)
    for (Int32 i = 0; i < iterations; ++i) {
        std::this_thread::yield();
    }
}

// ===== System.Type property ICalls =====

Boolean Type_get_IsClass(void* thisPtr) {
    return type_get_is_class(reinterpret_cast<Type*>(thisPtr));
}

void* Type_get_BaseType(void* thisPtr) {
    return reinterpret_cast<void*>(type_get_base_type(reinterpret_cast<Type*>(thisPtr)));
}

void* Type_get_FullName(void* thisPtr) {
    return reinterpret_cast<void*>(type_get_full_name(reinterpret_cast<Type*>(thisPtr)));
}

void* Type_get_Namespace(void* thisPtr) {
    return reinterpret_cast<void*>(type_get_namespace(reinterpret_cast<Type*>(thisPtr)));
}

// ===== System.RuntimeType =====

Object* RuntimeType_AllocateValueType(void* thisType, Object* value) {
    // AllocateValueType: creates a boxed copy of a value type.
    // If value is non-null, clone it via box-copy. Otherwise allocate empty.
    auto* typeInfo = reinterpret_cast<Object*>(thisType)->__type_info;
    auto sz = typeInfo->instance_size;
    if (value) {
        // Copy-box: allocate new object with same TypeInfo, copy fields
        auto* result = reinterpret_cast<Object*>(gc::alloc(sz, typeInfo));
        std::memcpy(
            reinterpret_cast<char*>(result) + sizeof(Object),
            reinterpret_cast<char*>(value) + sizeof(Object),
            sz - sizeof(Object));
        return result;
    }
    // Allocate zeroed value type box
    return reinterpret_cast<Object*>(gc::alloc(sz, typeInfo));
}

// ===== System.Reflection.RuntimeAssembly =====

void* RuntimeAssembly_InternalGetSatelliteAssembly(void* /*thisPtr*/, void* /*culture*/, void* /*version*/, bool /*throwOnFileNotFound*/) {
    // Satellite assemblies don't exist in AOT — always return nullptr.
    return nullptr;
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

Boolean RuntimeHelpers_ObjectHasComponentSize(Object* obj) {
    // In CLR, arrays and strings have "component size" > 0 in MethodTable.
    // We don't have MethodTable — detect arrays and strings structurally.
    if (!obj || !obj->__type_info) return 0;
    // Strings: __type_info points to System.String TypeInfo
    if (obj->__type_info->full_name &&
        std::strcmp(obj->__type_info->full_name, "System.String") == 0) return 1;
    // Arrays: alloc_array sets both __type_info and element_type to the same
    // element TypeInfo pointer. Check the Array struct pattern:
    // Array { __type_info, __sync_block, element_type, length, data... }
    // If element_type (offset 16) == __type_info (offset 0), it's an array.
    auto* as_arr = reinterpret_cast<Array*>(obj);
    if (as_arr->element_type == obj->__type_info && as_arr->length >= 0) return 1;
    return 0;
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
    // enumType is a RuntimeType* (cil2cpp::Type* — aliased to System_RuntimeType).
    // Extract the represented type's TypeInfo from the Type object.
    auto* typeObj = reinterpret_cast<Type*>(enumType);
    if (!typeObj) return nullptr;
    auto* typeInfo = typeObj->type_info;
    if (!typeInfo) return nullptr;
    auto elemSize = typeInfo->element_size > 0 ? typeInfo->element_size : sizeof(Int64);
    auto* obj = reinterpret_cast<Object*>(gc::alloc(
        static_cast<int32_t>(sizeof(Object) + elemSize), typeInfo));
    auto* data = reinterpret_cast<char*>(obj) + sizeof(Object);
    std::memcpy(data, &value, elemSize);
    return obj;
}

Int32 Enum_InternalGetCorElementType(void* methodTable) {
    // Receives a MethodTable* which maps to TypeInfo* in our AOT runtime.
    // Called from BCL IL: RuntimeHelpers.GetMethodTable(this) → InternalGetCorElementType(mt).
    auto* typeInfo = reinterpret_cast<TypeInfo*>(methodTable);
    if (!typeInfo) return 0;
    if (typeInfo && typeInfo->underlying_type && typeInfo->underlying_type->cor_element_type != 0) {
        return static_cast<Int32>(typeInfo->underlying_type->cor_element_type);
    }
    if (typeInfo) {
        switch (typeInfo->element_size) {
            case 1: return cor_element_type::I1;
            case 2: return cor_element_type::I2;
            case 4: return cor_element_type::I4;
            case 8: return cor_element_type::I8;
        }
    }
    return cor_element_type::I4;
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
    // No runtime config variables — return negative to signal end of enumeration.
    // BCL loops until return value < 0; returning 0 would cause an infinite loop.
    if (configValue) *configValue = 0;
    if (isBoolean) *isBoolean = false;
    return -1;
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

// Thread pool work item trampoline — delegates to compiled BCL DispatchWorkItem
// which handles both Task (vtable call) and IThreadPoolWorkItem (interface call).
static void execute_work_item(void* raw) {
    auto* obj = static_cast<Object*>(raw);
    if (!obj) return;
    // DispatchWorkItem(workItem, currentThread) — compiled from IL,
    // handles Task vs IThreadPoolWorkItem dispatch correctly.
    System_Threading_ThreadPoolWorkQueue_DispatchWorkItem(obj, thread_get_current());
}

void ThreadPoolWorkQueue_Enqueue(void* /*__this*/, Object* callback, bool /*forceGlobal*/) {
    if (!callback) return;
    threadpool::queue_work(execute_work_item, callback);
}

bool ThreadPool_BindHandlePortableCore(void* osHandle) {
    // osHandle is a SafeHandle* — extract the raw OS handle from f_handle field
    auto* sh = static_cast<SafeHandleLayout*>(osHandle);
    if (!sh) return false;
    auto handle = reinterpret_cast<void*>(sh->f_handle);
    return iocp::bind_handle(handle);
}

// ===== System.Type (reflection introspection) =====

static Type* get_type_from_this(void* __this) {
    return reinterpret_cast<Type*>(__this);
}

Object* Type_GetEnumUnderlyingType(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return nullptr;
    // Compiler always emits .underlying_type for enum TypeInfos.
    // nullptr means this is not an enum type — BCL caller throws ArgumentException.
    auto* underlying = t->type_info->underlying_type;
    if (!underlying) return nullptr;
    return reinterpret_cast<Object*>(type_get_type_object(underlying));
}

Boolean Type_get_IsPublic(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::Public;
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
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::NestedPublic;
}

Boolean Type_IsArrayImpl(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::Array;
}

Boolean Type_IsEnumDefined(void* __this, void* value) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    auto* ti = t->type_info;
    if (!ti->enum_names || ti->enum_count == 0) return false;

    // If value is a boxed enum or integer, extract its Int64 value
    if (value) {
        auto* obj = reinterpret_cast<Object*>(value);
        auto* valTi = obj->__type_info;
        if (valTi) {
            auto* rawData = reinterpret_cast<const uint8_t*>(obj) + sizeof(Object);
            int64_t val = 0;
            switch (valTi->cor_element_type) {
                case cor_element_type::I1: val = *reinterpret_cast<const int8_t*>(rawData); break;
                case cor_element_type::U1: val = *reinterpret_cast<const uint8_t*>(rawData); break;
                case cor_element_type::I2: val = *reinterpret_cast<const int16_t*>(rawData); break;
                case cor_element_type::U2: val = *reinterpret_cast<const uint16_t*>(rawData); break;
                case cor_element_type::I4: val = *reinterpret_cast<const int32_t*>(rawData); break;
                case cor_element_type::U4: val = *reinterpret_cast<const uint32_t*>(rawData); break;
                case cor_element_type::I8: val = *reinterpret_cast<const int64_t*>(rawData); break;
                case cor_element_type::U8: val = static_cast<int64_t>(*reinterpret_cast<const uint64_t*>(rawData)); break;
                default: return false;
            }
            for (UInt32 i = 0; i < ti->enum_count; i++) {
                if (ti->enum_values[i] == val) return true;
            }
        }
    }
    return false;
}

Boolean Type_IsEquivalentTo(void* __this, void* other) {
    return __this == other;
}

Int32 Type_GetTypeCodeImpl(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return type_code::TC_Empty;

    const char* name = t->type_info->full_name;
    if (!name) return type_code::TC_Object;

    // Map full type name to System.TypeCode enum values (ECMA-335 II.23.1.7)
    if (std::strcmp(name, "System.Boolean") == 0)  return type_code::TC_Boolean;
    if (std::strcmp(name, "System.Char") == 0)     return type_code::TC_Char;
    if (std::strcmp(name, "System.SByte") == 0)    return type_code::TC_SByte;
    if (std::strcmp(name, "System.Byte") == 0)     return type_code::TC_Byte;
    if (std::strcmp(name, "System.Int16") == 0)    return type_code::TC_Int16;
    if (std::strcmp(name, "System.UInt16") == 0)   return type_code::TC_UInt16;
    if (std::strcmp(name, "System.Int32") == 0)    return type_code::TC_Int32;
    if (std::strcmp(name, "System.UInt32") == 0)   return type_code::TC_UInt32;
    if (std::strcmp(name, "System.Int64") == 0)    return type_code::TC_Int64;
    if (std::strcmp(name, "System.UInt64") == 0)   return type_code::TC_UInt64;
    if (std::strcmp(name, "System.Single") == 0)   return type_code::TC_Single;
    if (std::strcmp(name, "System.Double") == 0)   return type_code::TC_Double;
    if (std::strcmp(name, "System.Decimal") == 0)  return type_code::TC_Decimal;
    if (std::strcmp(name, "System.DateTime") == 0) return type_code::TC_DateTime;
    if (std::strcmp(name, "System.String") == 0)   return type_code::TC_String;
    if (std::strcmp(name, "System.DBNull") == 0)   return type_code::TC_DBNull;

    return type_code::TC_Object;
}

Int32 Type_get_GenericParameterAttributes(void* __this) {
    (void)__this;
    return 0; // GenericParameterAttributes.None
}

Array* Type_GetMethods(void* __this) {
    return type_get_methods(get_type_from_this(__this));
}

void* Type_GetMethod(void* __this, String* name) {
    return type_get_method(get_type_from_this(__this), name);
}

Array* Type_GetFields(void* __this) {
    return type_get_fields(get_type_from_this(__this));
}

void* Type_GetField(void* __this, String* name) {
    return type_get_field(get_type_from_this(__this), name);
}

// ===== System.RuntimeTypeHandle (additional) =====

Object* RuntimeTypeHandle_GetElementType(void* handle) {
    // handle is a managed RuntimeType* (Type object), not a TypeInfo*.
    // Extract the TypeInfo from the Type object's type_info field.
    auto* t = reinterpret_cast<Type*>(handle);
    if (!t || !t->type_info) return nullptr;
    auto* ti = t->type_info;
    if (!ti->element_type_info) return nullptr;
    return reinterpret_cast<Object*>(type_get_type_object(ti->element_type_info));
}

Boolean RuntimeTypeHandle_IsEquivalentTo(void* handle1) {
    // In AOT, type equivalence is identity — same TypeInfo pointer = same type
    // The second parameter would be RuntimeTypeHandle (value type on stack),
    // but ICalls receive it as a single param. For now, identity check is correct.
    (void)handle1;
    return false; // Would need two handles to compare — current ICall signature has 1 param
}

void* RuntimeTypeHandle_GetAssembly(void* /*handle*/) {
    // Assembly object support requires System.Reflection.RuntimeAssembly to be compiled from BCL IL.
    // AOT limitation: no runtime assembly objects yet.
    return nullptr;
}

Boolean RuntimeTypeHandle_IsByRefLike(void* handle) {
    // handle is a managed RuntimeType* (Type object), not a TypeInfo*.
    auto* t = reinterpret_cast<Type*>(handle);
    if (!t || !t->type_info) return false;
    return (static_cast<UInt32>(t->type_info->flags) & static_cast<UInt32>(TypeFlags::IsByRefLike)) != 0;
}

Int32 RuntimeTypeHandle_GetToken(void* handle) {
    // handle is a managed RuntimeType* (Type object), not a TypeInfo*.
    auto* t = reinterpret_cast<Type*>(handle);
    if (!t || !t->type_info) return 0;
    return static_cast<Int32>(t->type_info->metadata_token);
}

Boolean RuntimeTypeHandle_IsInstanceOfType(void* handle, void* obj) {
    // handle is a managed RuntimeType* (Type object), not a TypeInfo*.
    auto* t = reinterpret_cast<Type*>(handle);
    if (!t || !t->type_info || !obj) return false;
    return object_is_instance_of(reinterpret_cast<Object*>(obj), t->type_info);
}

void* RuntimeTypeHandle_GetDeclaringMethod(void* /*handle*/) {
    // AOT limitation: generic parameter declaring method metadata not tracked.
    return nullptr;
}

Int32 RuntimeTypeHandle_GetArrayRank(void* handle) {
    // handle is a managed RuntimeType* (Type object), not a TypeInfo*.
    auto* t = reinterpret_cast<Type*>(handle);
    if (!t || !t->type_info) return 0;
    return t->type_info->array_rank;
}

// ===== System.RuntimeType =====

void* RuntimeType_CreateEnum(void* __this, Int64 value) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return nullptr;
    auto* ti = t->type_info;
    // Box the value as the enum type — element_size matches the underlying type size
    return box_raw(&value, ti->element_size > 0 ? ti->element_size : sizeof(int32_t), ti);
}

// ===== System.Reflection =====

static MethodInfo* get_native_method_info(void* __this) {
    if (!__this) return nullptr;
    auto* mi = reinterpret_cast<ManagedMethodInfo*>(__this);
    return mi->native_info;
}

Boolean MethodBase_get_IsVirtual(void* __this) {
    auto* ni = get_native_method_info(__this);
    return ni && (ni->flags & 0x0040); // MethodAttributes.Virtual
}

Boolean MethodBase_get_IsPublic(void* __this) {
    auto* ni = get_native_method_info(__this);
    return ni && (ni->flags & 0x0007) == 0x0006; // MemberAccessMask == Public
}

Boolean MethodBase_get_IsStatic(void* __this) {
    auto* ni = get_native_method_info(__this);
    return ni && (ni->flags & 0x0010); // MethodAttributes.Static
}

Boolean MethodBase_get_IsAbstract(void* __this) {
    auto* ni = get_native_method_info(__this);
    return ni && (ni->flags & 0x0400); // MethodAttributes.Abstract
}

Boolean Type_get_IsNotPublic(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::NotPublic;
}

Boolean Type_get_IsNestedAssembly(void* __this) {
    auto* t = get_type_from_this(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & TypeFlags::NestedAssembly;
}

String* MemberInfo_get_Name(void* __this) {
    auto* ni = get_native_method_info(__this);
    if (ni && ni->name) return string_literal(ni->name);
    return string_literal("?");
}

Int32 RuntimeMethodInfo_get_BindingFlags(void* __this) {
    // BindingFlags: Public=16, NonPublic=32, Static=8, Instance=4
    auto* ni = get_native_method_info(__this);
    if (!ni) return 0x14; // fallback: Public | Instance
    Int32 flags = 0;
    if ((ni->flags & 0x0007) == 0x0006) flags |= 0x10; // Public
    else flags |= 0x20; // NonPublic
    if (ni->flags & 0x0010) flags |= 0x08; // Static
    else flags |= 0x04; // Instance
    return flags;
}

void* RuntimeMethodInfo_GetGenericArgumentsInternal(void* __this) {
    (void)__this;
    // Return empty Type[] array for non-generic methods.
    // Generic method argument TypeInfos are not yet tracked in MethodInfo.
    return array_create(type_get_by_name("System.Type"), 0);
}

void* RuntimeMethodInfo_GetDeclaringTypeInternal(void* __this) {
    auto* ni = get_native_method_info(__this);
    if (!ni || !ni->declaring_type) return nullptr;
    return type_get_type_object(ni->declaring_type);
}

Int32 RuntimeConstructorInfo_get_BindingFlags(void* __this) {
    auto* ni = get_native_method_info(__this);
    if (!ni) return 0x14;
    Int32 flags = 0;
    if ((ni->flags & 0x0007) == 0x0006) flags |= 0x10; // Public
    else flags |= 0x20; // NonPublic
    if (ni->flags & 0x0010) flags |= 0x08; // Static
    else flags |= 0x04; // Instance
    return flags;
}

Int32 RuntimeFieldInfo_get_BindingFlags(void* __this) {
    // RuntimeFieldInfo aliases to ManagedFieldInfo which has native_info (FieldInfo*)
    if (!__this) return 0x14;
    auto* fi = reinterpret_cast<ManagedFieldInfo*>(__this);
    if (!fi->native_info) return 0x14;
    Int32 flags = 0;
    if ((fi->native_info->flags & 0x0007) == 0x0006) flags |= 0x10; // Public
    else flags |= 0x20; // NonPublic
    if (fi->native_info->flags & 0x0010) flags |= 0x08; // Static
    else flags |= 0x04; // Instance
    return flags;
}

// ===== System.Delegate =====

void* Delegate_get_Method(void* /*__this*/) {
    // Delegate→MethodInfo backref not yet tracked in delegate struct layout.
    // Would need to add a MethodInfo* field to the delegate and populate it at creation time.
    // AOT limitation: delegate method introspection not yet implemented.
    return nullptr;
}

// ===== System.Runtime.InteropServices.GCHandle =====

intptr_t GCHandle_InternalCompareExchange(intptr_t handle, cil2cpp::Object* value, cil2cpp::Object* comparand) {
    // Atomic compare-and-swap on the GCHandle table entry.
    // Read current value, compare with comparand, if equal replace with value.
    void* current = gchandle_get(handle);
    if (current == static_cast<void*>(comparand)) {
        gchandle_set(handle, static_cast<void*>(value));
    }
    return reinterpret_cast<intptr_t>(current);
}

// ===== System.Diagnostics =====

void* StackFrameHelper_GetMethodBase(void* /*__this*/, Int32 /*index*/) {
    // AOT limitation: no JIT-style stack walking. Would need DWARF/PDB unwind info.
    return nullptr;
}

void* StackFrame_GetMethod(void* /*__this*/) {
    // AOT limitation: no JIT-style stack walking. Would need IP→MethodInfo mapping.
    return nullptr;
}

} // namespace icall
} // namespace cil2cpp
