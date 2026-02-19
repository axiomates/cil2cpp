/**
 * CIL2CPP Runtime Library
 * Main header file - includes all runtime components
 */

#pragma once

#include <atomic>
#include "types.h"
#include "object.h"
#include "string.h"
#include "array.h"
#include "mdarray.h"
#include "stackalloc.h"
#include "gc.h"
#include "exception.h"
#include "type_info.h"
#include "boxing.h"
#include "delegate.h"
#include "icall.h"
#include "checked.h"
#include "task.h"
#include "threadpool.h"
#include "cancellation.h"
#include "async_enumerable.h"
#include "threading.h"
#include "reflection.h"
#include "memberinfo.h"
#include "collections.h"
#include "typed_reference.h"
#include "unicode.h"
#include "globalization.h"
#include "interop.h"
#include "safe_handle.h"
#include "io.h"

// BCL types
#include "bcl/System.Object.h"
#include "bcl/System.String.h"

namespace cil2cpp {

// ===== Unsigned Comparison Helper =====
// Used by cgt.un / clt.un IL opcodes which interpret operands as unsigned.
// E.g., (int)(-55233) compared unsigned becomes (uint32_t)(4294912063).
template<typename T, std::enable_if_t<std::is_integral_v<T> && !std::is_same_v<T, bool>, int> = 0>
inline auto to_unsigned(T v) { return static_cast<std::make_unsigned_t<T>>(v); }

inline auto to_unsigned(bool v) { return static_cast<unsigned>(v); }

template<typename T, std::enable_if_t<std::is_pointer_v<T>, int> = 0>
inline auto to_unsigned(T v) { return reinterpret_cast<uintptr_t>(v); }

template<typename T, std::enable_if_t<std::is_enum_v<T>, int> = 0>
inline auto to_unsigned(T v) { return static_cast<std::make_unsigned_t<std::underlying_type_t<T>>>(v); }

// Float/double: to_unsigned is identity (needed when to_unsigned is used generically)
inline float to_unsigned(float v) { return v; }
inline double to_unsigned(double v) { return v; }

// ===== Unsigned Comparison — ECMA-335 III.1.5 =====
// cgt.un on integers: compare as unsigned.
// cgt.un on floats: returns true if a > b OR either is NaN (unordered).
// Same for clt.un: true if a < b OR unordered.
// IEEE 754: !(a <= b) is true when a > b OR either is NaN.
template<typename T, std::enable_if_t<std::is_floating_point_v<T>, int> = 0>
inline bool unsigned_gt(T a, T b) { return !(a <= b); }

template<typename T, std::enable_if_t<std::is_floating_point_v<T>, int> = 0>
inline bool unsigned_lt(T a, T b) { return !(a >= b); }

template<typename T1, typename T2, std::enable_if_t<!std::is_floating_point_v<T1> && !std::is_floating_point_v<T2>, int> = 0>
inline bool unsigned_gt(T1 a, T2 b) { return to_unsigned(a) > to_unsigned(b); }

template<typename T1, typename T2, std::enable_if_t<!std::is_floating_point_v<T1> && !std::is_floating_point_v<T2>, int> = 0>
inline bool unsigned_lt(T1 a, T2 b) { return to_unsigned(a) < to_unsigned(b); }

// bge.un on floats: true if a >= b OR unordered. !(a < b) handles NaN correctly.
// bge.un on integers: compare as unsigned.
template<typename T, std::enable_if_t<std::is_floating_point_v<T>, int> = 0>
inline bool unsigned_ge(T a, T b) { return !(a < b); }

template<typename T, std::enable_if_t<std::is_floating_point_v<T>, int> = 0>
inline bool unsigned_le(T a, T b) { return !(a > b); }

template<typename T1, typename T2, std::enable_if_t<!std::is_floating_point_v<T1> && !std::is_floating_point_v<T2>, int> = 0>
inline bool unsigned_ge(T1 a, T2 b) { return to_unsigned(a) >= to_unsigned(b); }

template<typename T1, typename T2, std::enable_if_t<!std::is_floating_point_v<T1> && !std::is_floating_point_v<T2>, int> = 0>
inline bool unsigned_le(T1 a, T2 b) { return to_unsigned(a) <= to_unsigned(b); }

// ===== Volatile Read/Write =====
// System.Threading.Volatile.Read/Write — JIT intrinsics for volatile memory access.
// Volatile.Read = acquire: load THEN fence (prevents subsequent ops from reordering before the read).
// Volatile.Write = release: fence THEN store (prevents preceding ops from reordering after the write).
template<typename T>
inline T volatile_read(T* location) {
    T value = *location;
    std::atomic_thread_fence(std::memory_order_acquire);
    return value;
}

template<typename T>
inline void volatile_write(T* location, T value) {
    std::atomic_thread_fence(std::memory_order_release);
    *location = value;
}

/**
 * Initialize the CIL2CPP runtime.
 * Must be called before any other runtime functions.
 */
void runtime_init();

/**
 * Store command-line arguments for Environment.GetCommandLineArgs().
 * Should be called immediately after runtime_init().
 */
void runtime_set_args(int argc, char** argv);

/// Access stored command-line arguments.
int runtime_get_argc();
char** runtime_get_argv();

/**
 * Shutdown the CIL2CPP runtime.
 * Performs final GC and cleanup.
 */
void runtime_shutdown();

} // namespace cil2cpp

// System.Object methods (used by generated code)
void System_Object__ctor(void* obj);
inline void System_Object_Finalize(void*) {} // Finalize is a no-op for System.Object

// Entry point macro for generated code
#define CIL2CPP_MAIN(EntryClass, EntryMethod) \
    int main(int argc, char* argv[]) { \
        cil2cpp::runtime_init(); \
        cil2cpp::runtime_set_args(argc, argv); \
        EntryMethod(); \
        cil2cpp::runtime_shutdown(); \
        return 0; \
    }
