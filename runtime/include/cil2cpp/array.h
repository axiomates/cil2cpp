/**
 * CIL2CPP Runtime - Array Type
 * Corresponds to System.Array in .NET.
 */

#pragma once

#include "object.h"
#include <type_traits>

namespace cil2cpp {

/**
 * Base array type.
 * All arrays derive from this.
 */
struct Array : Object {
    // Element type information
    TypeInfo* element_type;

    // Number of elements
    Int32 length;

    // Array data follows (flexible array member)
    // The actual element data starts at offset sizeof(Array)
};

/**
 * Create a new array.
 * @param element_type Type of elements
 * @param length Number of elements
 */
Array* array_create(TypeInfo* element_type, Int32 length);

/**
 * Get array length.
 */
inline Int32 array_length(Array* arr) {
    return arr ? arr->length : 0;
}

/**
 * Get pointer to array element data.
 * Element data is stored immediately after the Array header (trailing data pattern).
 */
inline void* array_data(Array* arr) {
    return reinterpret_cast<char*>(arr) + sizeof(Array);
}

/**
 * Get element at index (with bounds check).
 */
void* array_get_element_ptr(Array* arr, Int32 index);

/**
 * Bounds check - throws IndexOutOfRangeException if invalid.
 */
void array_bounds_check(Array* arr, Int32 index);

/**
 * Create a subarray (slice) from source array.
 * Copies elements from [start, start+length) into a new array.
 */
Array* array_get_subarray(Array* source, Int32 start, Int32 length);

// Typed array access templates
template<typename T>
inline T& array_get(Array* arr, Int32 index) {
    array_bounds_check(arr, index);
    T* data = static_cast<T*>(array_data(arr));
    return data[index];
}

// array_set: accepts a value of type V which may differ from T for reference types.
// Three cases:
//   1. T=SomeType (non-ptr), V=SomeType* (ptr): reference type array where element type
//      omits '*'. Data is actually V* (pointer array). Store V directly.
//   2. T=SomeType*, V=OtherType*: different pointer types (flat structs, no inheritance).
//      Use reinterpret_cast<T>.
//   3. T==V: normal case, direct assignment via static_cast.
template<typename T, typename V>
inline void array_set(Array* arr, Int32 index, V value) {
    array_bounds_check(arr, index);
    if constexpr (!std::is_pointer_v<T> && std::is_pointer_v<V>) {
        // Reference type array: T is base type, V is T* — data is pointer array
        V* data = static_cast<V*>(array_data(arr));
        data[index] = value;
    } else if constexpr (std::is_pointer_v<T> && std::is_pointer_v<V> && !std::is_same_v<T, V>) {
        // Different pointer types — use reinterpret_cast
        T* data = static_cast<T*>(array_data(arr));
        data[index] = reinterpret_cast<T>(value);
    } else {
        // Same types or primitive types
        T* data = static_cast<T*>(array_data(arr));
        data[index] = static_cast<T>(value);
    }
}

// ===== ICall functions for System.Array (work with both 1D and multi-dim arrays) =====

/// System.Array::get_Length — total element count.
Int32 array_get_length(Object* arr);

/// System.Array::get_Rank — 1 for 1D arrays, rank for multi-dim.
Int32 array_get_rank(Object* arr);

/// System.Array::GetLength(int dimension) — length of a specific dimension.
Int32 array_get_length_dim(Object* arr, Int32 dimension);

/// System.Array::Clear(Array, int, int) — zero out a range of elements.
void array_clear(Array* arr, Int32 index, Int32 length);

/// System.Array::Clear(Array) — zero out all elements (1-param overload, .NET 6+).
void array_clear_all(void* arr);

/// System.Array::Copy(Array, int, Array, int, int) — copy elements between arrays.
void array_copy(Array* src, Int32 srcIndex, Array* dst, Int32 dstIndex, Int32 length);

/// System.Array::Copy(Array, Array, int) — copy from start of both arrays.
void array_copy_simple(void* src, void* dst, Int32 length);

/// System.Array::Clone() — shallow-copy the array.
void* array_clone(void* arr);

/// System.Array::Reverse(Array, int, int) — reverse elements in a range.
void array_reverse(void* arr, Int32 index, Int32 length);

/// System.Array::get_NativeLength — returns length as nuint (used by some BCL code).
uintptr_t array_get_native_length(void* arr);

/// System.Array::GetValue(int) — get element boxed as Object*.
void* array_get_value(void* arr, Int32 index);

/// System.Array::CopyImpl — internal copy used by BCL.
void array_copy_impl(void* src, Int32 srcIndex, void* dst, Int32 dstIndex, Int32 length);

/// System.Array::GetCorElementTypeOfElementType — returns CorElementType enum.
Int32 array_get_cor_element_type(void* arr);

} // namespace cil2cpp
