/**
 * CIL2CPP Runtime - Managed Reflection Member Types
 *
 * System.Reflection.MethodInfo and System.Reflection.FieldInfo wrappers.
 * These managed objects wrap native MethodInfo/FieldInfo metadata structs
 * and expose them through the .NET reflection API.
 */

#pragma once

#include "object.h"
#include "type_info.h"

namespace cil2cpp {

// Forward declarations
struct Type;
struct Array;
struct String;

// TypeInfo for reflection member types
extern TypeInfo System_Reflection_MethodInfo_TypeInfo;
extern TypeInfo System_Reflection_FieldInfo_TypeInfo;
extern TypeInfo System_Reflection_ParameterInfo_TypeInfo;

/**
 * Managed System.Reflection.MethodInfo — wraps a native MethodInfo pointer.
 */
struct ManagedMethodInfo : Object {
    MethodInfo* native_info;
};

/**
 * Managed System.Reflection.FieldInfo — wraps a native FieldInfo pointer.
 */
struct ManagedFieldInfo : Object {
    FieldInfo* native_info;
};

/**
 * Managed System.Reflection.ParameterInfo — wraps parameter metadata.
 */
struct ManagedParameterInfo : Object {
    const char* name;       // Parameter name (nullptr if unknown)
    TypeInfo* param_type;   // Parameter type
    Int32 position;         // 0-based position
};

// ===== Type → GetMethods/GetFields API =====

/**
 * Type.GetMethods() → MethodInfo[] (public instance + static methods)
 */
Array* type_get_methods(Type* t);

/**
 * Type.GetFields() → FieldInfo[] (public instance + static fields)
 */
Array* type_get_fields(Type* t);

/**
 * Type.GetMethod(string name) → MethodInfo or nullptr
 */
ManagedMethodInfo* type_get_method(Type* t, String* name);

/**
 * Type.GetField(string name) → FieldInfo or nullptr
 */
ManagedFieldInfo* type_get_field(Type* t, String* name);

// ===== MethodInfo Property Accessors =====

String*  methodinfo_get_name(ManagedMethodInfo* mi);
Type*    methodinfo_get_declaring_type(ManagedMethodInfo* mi);
Type*    methodinfo_get_return_type(ManagedMethodInfo* mi);
Boolean  methodinfo_get_is_public(ManagedMethodInfo* mi);
Boolean  methodinfo_get_is_static(ManagedMethodInfo* mi);
Boolean  methodinfo_get_is_virtual(ManagedMethodInfo* mi);
Boolean  methodinfo_get_is_abstract(ManagedMethodInfo* mi);
String*  methodinfo_to_string(ManagedMethodInfo* mi);
Array*   methodinfo_get_parameters(ManagedMethodInfo* mi);

/**
 * MethodInfo.Invoke(object obj, object[] parameters) → object
 * Invokes the method via its stored function pointer.
 * For value type returns, boxes the result.
 */
Object* methodinfo_invoke(ManagedMethodInfo* mi, Object* obj, Array* parameters);

// ===== FieldInfo Property Accessors =====

String*  fieldinfo_get_name(ManagedFieldInfo* fi);
Type*    fieldinfo_get_declaring_type(ManagedFieldInfo* fi);
Type*    fieldinfo_get_field_type(ManagedFieldInfo* fi);
Boolean  fieldinfo_get_is_public(ManagedFieldInfo* fi);
Boolean  fieldinfo_get_is_static(ManagedFieldInfo* fi);
Boolean  fieldinfo_get_is_init_only(ManagedFieldInfo* fi);
String*  fieldinfo_to_string(ManagedFieldInfo* fi);

/**
 * FieldInfo.GetValue(object obj) → object
 * Returns the field value. For value types, boxes the result.
 */
Object* fieldinfo_get_value(ManagedFieldInfo* fi, Object* obj);

/**
 * FieldInfo.SetValue(object obj, object value)
 * Sets the field value. For value types, unboxes the value.
 */
void fieldinfo_set_value(ManagedFieldInfo* fi, Object* obj, Object* value);

// ===== ParameterInfo Property Accessors =====

String* parameterinfo_get_name(ManagedParameterInfo* pi);
Type*   parameterinfo_get_parameter_type(ManagedParameterInfo* pi);
Int32   parameterinfo_get_position(ManagedParameterInfo* pi);

// ===== Universal MemberInfo dispatchers =====
// Called when the IL declaring type is System.Reflection.MemberInfo and the
// receiver could be Type, MethodInfo, or FieldInfo. Dispatch at runtime based on TypeInfo.

String* memberinfo_get_name(Object* obj);
Type*   memberinfo_get_declaring_type(Object* obj);

} // namespace cil2cpp

// Type aliases used by generated code (matches mangled IL type names)
using System_Reflection_MethodInfo = cil2cpp::ManagedMethodInfo;
using System_Reflection_MethodBase = cil2cpp::ManagedMethodInfo;
using System_Reflection_FieldInfo = cil2cpp::ManagedFieldInfo;
using System_Reflection_ParameterInfo = cil2cpp::ManagedParameterInfo;
using System_Reflection_MemberInfo = cil2cpp::Object;
// Phase II.3: Runtime reflection subtypes → existing runtime structs
using System_Reflection_RuntimeMethodInfo = cil2cpp::ManagedMethodInfo;
using System_Reflection_RuntimeFieldInfo = cil2cpp::ManagedFieldInfo;
using System_Reflection_RuntimeConstructorInfo = cil2cpp::ManagedMethodInfo;
