/**
 * CIL2CPP Runtime - Array Type
 * Corresponds to System.Array in .NET.
 */

#pragma once

#include "object.h"

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

template<typename T>
inline void array_set(Array* arr, Int32 index, T value) {
    array_bounds_check(arr, index);
    T* data = static_cast<T*>(array_data(arr));
    data[index] = value;
}

} // namespace cil2cpp
