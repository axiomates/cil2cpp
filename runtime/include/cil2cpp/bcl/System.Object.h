/**
 * CIL2CPP Runtime - System.Object
 */

#pragma once

#include "../object.h"
#include "../type_info.h"

namespace cil2cpp {
namespace System {

// System.Object type info (defined in System.Object.cpp)
extern TypeInfo Object_TypeInfo;

// System.Object methods (static wrappers for virtual calls)
inline String* Object_ToString(Object* obj) {
    return object_to_string(obj);
}

inline Int32 Object_GetHashCode(Object* obj) {
    return object_get_hash_code(obj);
}

inline Boolean Object_Equals(Object* obj, Object* other) {
    return object_equals(obj, other);
}

inline Boolean Object_ReferenceEquals(Object* a, Object* b) {
    return a == b;
}

inline TypeInfo* Object_GetType(Object* obj) {
    return object_get_type(obj);
}

} // namespace System
} // namespace cil2cpp
