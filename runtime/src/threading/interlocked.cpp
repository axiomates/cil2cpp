/**
 * CIL2CPP Runtime - Interlocked Operations
 *
 * Atomic operations corresponding to System.Threading.Interlocked.
 * Uses compiler intrinsics for guaranteed atomicity on non-atomic variables.
 */

#include <cil2cpp/threading.h>

#ifdef _MSC_VER
#include <intrin.h>
#endif

namespace cil2cpp {
namespace interlocked {

// ===== Int32 operations =====

Int32 increment_i32(Int32* location) {
#ifdef _MSC_VER
    return _InterlockedIncrement(reinterpret_cast<long*>(location));
#else
    return __sync_add_and_fetch(location, 1);
#endif
}

Int32 decrement_i32(Int32* location) {
#ifdef _MSC_VER
    return _InterlockedDecrement(reinterpret_cast<long*>(location));
#else
    return __sync_sub_and_fetch(location, 1);
#endif
}

Int32 exchange_i32(Int32* location, Int32 value) {
#ifdef _MSC_VER
    return _InterlockedExchange(reinterpret_cast<long*>(location), value);
#else
    return __sync_lock_test_and_set(location, value);
#endif
}

Int32 compare_exchange_i32(Int32* location, Int32 value, Int32 comparand) {
#ifdef _MSC_VER
    return _InterlockedCompareExchange(reinterpret_cast<long*>(location), value, comparand);
#else
    return __sync_val_compare_and_swap(location, comparand, value);
#endif
}

Int32 add_i32(Int32* location, Int32 value) {
#ifdef _MSC_VER
    return _InterlockedExchangeAdd(reinterpret_cast<long*>(location), value) + value;
#else
    return __sync_add_and_fetch(location, value);
#endif
}

Int64 add_i64(Int64* location, Int64 value) {
#ifdef _MSC_VER
    return _InterlockedExchangeAdd64(location, value) + value;
#else
    return __sync_add_and_fetch(location, value);
#endif
}

// ===== Int64 operations =====

Int64 increment_i64(Int64* location) {
#ifdef _MSC_VER
    return _InterlockedIncrement64(location);
#else
    return __sync_add_and_fetch(location, static_cast<Int64>(1));
#endif
}

Int64 decrement_i64(Int64* location) {
#ifdef _MSC_VER
    return _InterlockedDecrement64(location);
#else
    return __sync_sub_and_fetch(location, static_cast<Int64>(1));
#endif
}

Int64 exchange_i64(Int64* location, Int64 value) {
#ifdef _MSC_VER
    return _InterlockedExchange64(location, value);
#else
    return __sync_lock_test_and_set(location, value);
#endif
}

Int64 compare_exchange_i64(Int64* location, Int64 value, Int64 comparand) {
#ifdef _MSC_VER
    return _InterlockedCompareExchange64(location, value, comparand);
#else
    return __sync_val_compare_and_swap(location, comparand, value);
#endif
}

// ===== Byte (uint8) operations =====
// These are JIT intrinsics in CoreCLR — the BCL IL calls itself expecting the JIT to replace.

uint8_t exchange_u8(uint8_t* location, uint8_t value) {
#ifdef _MSC_VER
    return _InterlockedExchange8(reinterpret_cast<char*>(location), static_cast<char>(value));
#else
    return __atomic_exchange_n(location, value, __ATOMIC_SEQ_CST);
#endif
}

uint8_t compare_exchange_u8(uint8_t* location, uint8_t value, uint8_t comparand) {
#ifdef _MSC_VER
    return static_cast<uint8_t>(
        _InterlockedCompareExchange8(reinterpret_cast<char*>(location),
                                     static_cast<char>(value),
                                     static_cast<char>(comparand)));
#else
    __atomic_compare_exchange_n(location, &comparand, value, false,
                                __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);
    return comparand;
#endif
}

// ===== UInt16 operations =====

uint16_t exchange_u16(uint16_t* location, uint16_t value) {
#ifdef _MSC_VER
    return _InterlockedExchange16(reinterpret_cast<short*>(location), static_cast<short>(value));
#else
    return __atomic_exchange_n(location, value, __ATOMIC_SEQ_CST);
#endif
}

uint16_t compare_exchange_u16(uint16_t* location, uint16_t value, uint16_t comparand) {
#ifdef _MSC_VER
    return static_cast<uint16_t>(
        _InterlockedCompareExchange16(reinterpret_cast<short*>(location),
                                      static_cast<short>(value),
                                      static_cast<short>(comparand)));
#else
    __atomic_compare_exchange_n(location, &comparand, value, false,
                                __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);
    return comparand;
#endif
}

// ===== Memory barriers =====

void read_memory_barrier() {
#ifdef _MSC_VER
    _ReadBarrier();
    _mm_lfence();
#else
    __atomic_thread_fence(__ATOMIC_ACQUIRE);
#endif
}

// ===== Object reference operations =====

Object* exchange_obj(Object** location, Object* value) {
#ifdef _MSC_VER
    return reinterpret_cast<Object*>(
        _InterlockedExchangePointer(reinterpret_cast<void* volatile*>(location), value));
#else
    return __sync_lock_test_and_set(location, value);
#endif
}

Object* compare_exchange_obj(Object** location, Object* value, Object* comparand) {
#ifdef _MSC_VER
    return reinterpret_cast<Object*>(
        _InterlockedCompareExchangePointer(
            reinterpret_cast<void* volatile*>(location), value, comparand));
#else
    return __sync_val_compare_and_swap(location, comparand, value);
#endif
}

} // namespace interlocked
} // namespace cil2cpp
