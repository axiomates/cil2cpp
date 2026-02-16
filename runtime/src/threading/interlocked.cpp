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
