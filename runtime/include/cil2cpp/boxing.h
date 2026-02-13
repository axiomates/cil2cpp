/**
 * CIL2CPP Runtime - Boxing/Unboxing
 * Converts between value types and heap-allocated objects.
 */

#pragma once

#include "object.h"
#include "gc.h"
#include "exception.h"

namespace cil2cpp {

/**
 * Box a value type. Allocates on GC heap.
 * Layout: [Object header] [value data]
 */
template<typename T>
inline Object* box(T value, TypeInfo* type) {
    Object* obj = static_cast<Object*>(gc::alloc(sizeof(Object) + sizeof(T), type));
    *reinterpret_cast<T*>(reinterpret_cast<char*>(obj) + sizeof(Object)) = value;
    return obj;
}

/**
 * Unbox: extract value from boxed object (unbox.any).
 * Returns a copy of the value.
 */
template<typename T>
inline T unbox(Object* obj) {
    if (!obj) throw_null_reference();
    return *reinterpret_cast<T*>(reinterpret_cast<char*>(obj) + sizeof(Object));
}

/**
 * Unbox: get pointer to value inside boxed object (unbox).
 * Returns a pointer to the in-place value.
 */
template<typename T>
inline T* unbox_ptr(Object* obj) {
    if (!obj) throw_null_reference();
    return reinterpret_cast<T*>(reinterpret_cast<char*>(obj) + sizeof(Object));
}

} // namespace cil2cpp
