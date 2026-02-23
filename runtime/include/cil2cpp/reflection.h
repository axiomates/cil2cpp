/**
 * CIL2CPP Runtime - Reflection Support
 *
 * System.Type wrapper and reflection query API.
 * Implements core ECMA-335 reflection: typeof(T), GetType(), Type properties.
 */

#pragma once

#include "object.h"
#include "type_info.h"

namespace cil2cpp {

// Forward declaration
struct String;

/**
 * Managed System.Type object — wraps a TypeInfo pointer.
 * ECMA-335 IV.5.7: System.Type provides runtime type queries.
 */
struct Type : Object {
    TypeInfo* type_info;
    // BCL RuntimeType fields — needed because RuntimeType is aliased to cil2cpp::Type
    intptr_t f_m_handle = 0;    // RuntimeType.m_handle (RuntimeTypeHandle → IntPtr → MethodTable*)
    intptr_t f_m_cache = 0;     // RuntimeType.m_cache (GCHandle to RuntimeTypeCache)
};

// TypeInfo for runtime-provided BCL types (used by typeof() and GetType())
extern TypeInfo System_Object_TypeInfo;
extern TypeInfo System_String_TypeInfo;
extern TypeInfo System_Type_TypeInfo;

// ===== Core Type Object API =====

/**
 * Get or create the cached Type object for a TypeInfo.
 * Thread-safe. Returns the same Type* for the same TypeInfo* (reference equality).
 */
Type* type_get_type_object(TypeInfo* info);

/**
 * typeof(T) implementation: RuntimeTypeHandle → Type object.
 * Called by generated code via Type.GetTypeFromHandle icall.
 */
Type* type_get_type_from_handle(void* handle);

/**
 * obj.GetType() → Type object (managed wrapper around TypeInfo).
 */
Type* object_get_type_managed(Object* obj);

// ===== Type Property Accessors =====

String* type_get_name(Type* t);
String* type_get_full_name(Type* t);
String* type_get_namespace(Type* t);
Type*   type_get_base_type(Type* t);

Boolean type_get_is_value_type(Type* t);
Boolean type_get_is_interface(Type* t);
Boolean type_get_is_abstract(Type* t);
Boolean type_get_is_sealed(Type* t);
Boolean type_get_is_enum(Type* t);
Boolean type_get_is_array(Type* t);
Boolean type_get_is_class(Type* t);
Boolean type_get_is_primitive(Type* t);
Boolean type_get_is_generic_type(Type* t);

// ===== Type Methods =====

Boolean type_is_assignable_from_managed(Type* self, Type* other);
Boolean type_is_subclass_of_managed(Type* self, Type* other);
Boolean type_equals(Type* self, Object* other);
String* type_to_string(Type* t);

} // namespace cil2cpp

// Phase II.3: TypeInfo → cil2cpp::Type alias
using System_Reflection_TypeInfo = cil2cpp::Type;
