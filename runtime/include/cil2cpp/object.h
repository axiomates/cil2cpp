/**
 * CIL2CPP Runtime - Base Object Type
 * All managed objects derive from this.
 */

#pragma once

#include "types.h"

namespace cil2cpp {

// Forward declaration
struct TypeInfo;

/**
 * Base class for all managed objects.
 * Corresponds to System.Object in .NET.
 */
struct Object {
    // Pointer to runtime type information
    TypeInfo* __type_info;

    // Sync block index (for threading/locking)
    UInt32 __sync_block;
};

/**
 * Object header size - used for memory allocation calculations.
 */
constexpr size_t OBJECT_HEADER_SIZE = sizeof(Object);

/**
 * Allocate a new object of the given type.
 */
Object* object_alloc(TypeInfo* type);

/**
 * Get the runtime type of an object.
 */
TypeInfo* object_get_type(Object* obj);

/**
 * Default implementation of Object.ToString()
 */
struct String* object_to_string(Object* obj);

/**
 * Default implementation of Object.GetHashCode()
 */
Int32 object_get_hash_code(Object* obj);

/**
 * Default implementation of Object.Equals(object)
 */
Boolean object_equals(Object* obj, Object* other);

/**
 * Check if an object is an instance of a type.
 * Used for 'is' operator.
 */
Boolean object_is_instance_of(Object* obj, TypeInfo* type);

/**
 * Cast an object to a type, returns null if not compatible.
 * Used for 'as' operator.
 */
Object* object_as(Object* obj, TypeInfo* type);

/**
 * Cast an object to a type, throws if not compatible.
 * Used for explicit cast.
 */
Object* object_cast(Object* obj, TypeInfo* type);

/**
 * Object.ReferenceEquals(object a, object b) - reference equality.
 */
Boolean object_reference_equals(Object* a, Object* b);

/**
 * Object.MemberwiseClone() - shallow copy of an object.
 */
Object* object_memberwise_clone(Object* obj);

} // namespace cil2cpp
