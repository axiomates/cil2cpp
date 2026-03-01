/**
 * CIL2CPP Runtime - System.Type Implementation
 *
 * Implements the managed Type wrapper for TypeInfo.
 * Type objects are cached per TypeInfo for reference equality (typeof(T) == typeof(T)).
 */

#include <cil2cpp/reflection.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/string.h>
#include <cil2cpp/exception.h>

#include <unordered_map>
#include <mutex>

// Generated code defines System_RuntimeType_TypeInfo in global namespace.
// BCL code expects all Type objects to be RuntimeType instances (object_as checks).
extern cil2cpp::TypeInfo System_RuntimeType_TypeInfo;

namespace cil2cpp {

// TypeInfo for runtime-provided BCL types (needed for typeof() and GetType())
TypeInfo System_Object_TypeInfo = {
    .name = "Object", .namespace_name = "System", .full_name = "System.Object",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(Object), .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_String_TypeInfo = {
    .name = "String", .namespace_name = "System", .full_name = "System.String",
    .base_type = &System_Object_TypeInfo, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(String), .element_size = 0,  // Reference type: arrays use pointer-sized elements
    .flags = TypeFlags::Sealed, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// Forward declarations needed for vtable setup
extern TypeInfo System_Type_TypeInfo;
String* type_to_string(Type* t);
Boolean type_equals(Type* self, Object* other);

// VTable wrappers for System.Type — match the Object vtable layout:
//   Slot 0: ToString, Slot 1: Equals, Slot 2: GetHashCode
static String* Type_ToString_vtable(Object* obj) {
    return type_to_string(static_cast<Type*>(obj));
}
static Boolean Type_Equals_vtable(Object* obj, Object* other) {
    return type_equals(static_cast<Type*>(obj), other);
}
static Int32 Type_GetHashCode_vtable(Object* obj) {
    auto* t = static_cast<Type*>(obj);
    return t->type_info ? static_cast<Int32>(reinterpret_cast<uintptr_t>(t->type_info) >> 3) : 0;
}
static void* System_Type_vtable_methods[] = {
    reinterpret_cast<void*>(&Type_ToString_vtable),
    reinterpret_cast<void*>(&Type_Equals_vtable),
    reinterpret_cast<void*>(&Type_GetHashCode_vtable),
};
static VTable System_Type_VTable = {
    &System_Type_TypeInfo,
    System_Type_vtable_methods,
    3,
};

// TypeInfo for System.Type itself
TypeInfo System_Type_TypeInfo = {
    .name = "Type",
    .namespace_name = "System",
    .full_name = "System.Type",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Type),
    .element_size = 0,
    .flags = TypeFlags::Sealed,
    .vtable = &System_Type_VTable,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

// Thread-safe Type object cache: TypeInfo* → Type*
static std::unordered_map<TypeInfo*, Type*> g_type_cache;
static std::mutex g_type_cache_mutex;

Type* type_get_type_object(TypeInfo* info) {
    if (!info) return nullptr;

    std::lock_guard<std::mutex> lock(g_type_cache_mutex);

    auto it = g_type_cache.find(info);
    if (it != g_type_cache.end()) {
        return it->second;
    }

    // Allocate as RuntimeType — BCL code (Type.op_Equality etc.) expects all Type objects
    // to be RuntimeType instances (checks via object_as(obj, &System_RuntimeType_TypeInfo)).
    // System_RuntimeType_TypeInfo is defined in generated code with full vtable (136+ methods).
    auto* type_obj = static_cast<Type*>(gc::alloc(sizeof(Type), &::System_RuntimeType_TypeInfo));
    type_obj->type_info = info;

    g_type_cache[info] = type_obj;
    return type_obj;
}

Type* type_get_type_from_handle(void* handle) {
    if (!handle) return nullptr;
    return type_get_type_object(static_cast<TypeInfo*>(handle));
}

Type* object_get_type_managed(Object* obj) {
    if (!obj) throw_null_reference();
    return type_get_type_object(obj->__type_info);
}

// ===== Property Accessors =====

String* type_get_name(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return string_literal(t->type_info->name);
}

String* type_get_full_name(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return string_literal(t->type_info->full_name);
}

String* type_get_namespace(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return string_literal(t->type_info->namespace_name);
}

Type* type_get_base_type(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    if (!t->type_info->base_type) return nullptr;
    return type_get_type_object(t->type_info->base_type);
}

Boolean type_get_is_value_type(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::ValueType;
}

Boolean type_get_is_interface(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::Interface;
}

Boolean type_get_is_abstract(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::Abstract;
}

Boolean type_get_is_sealed(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::Sealed;
}

Boolean type_get_is_enum(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::Enum;
}

Boolean type_get_is_array(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::Array;
}

Boolean type_get_is_class(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    // ECMA-335: IsClass = not value type and not interface
    return !(t->type_info->flags & TypeFlags::ValueType)
        && !(t->type_info->flags & TypeFlags::Interface);
}

Boolean type_get_is_primitive(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::Primitive;
}

Boolean type_get_is_generic_type(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return t->type_info->flags & TypeFlags::Generic;
}

// ===== Type Methods =====

Boolean type_is_assignable_from_managed(Type* self, Type* other) {
    if (!self || !self->type_info) throw_null_reference();
    if (!other || !other->type_info) return false;
    return type_is_assignable_from(self->type_info, other->type_info);
}

Boolean type_is_subclass_of_managed(Type* self, Type* other) {
    if (!self || !self->type_info) throw_null_reference();
    if (!other || !other->type_info) return false;
    return type_is_subclass_of(self->type_info, other->type_info);
}

Boolean type_equals(Type* self, Object* other) {
    if (!self) throw_null_reference();
    if (!other) return false;
    // Check if other is a Type object by checking its TypeInfo
    // Accept both System_Type_TypeInfo and System_RuntimeType_TypeInfo (RuntimeType inherits Type)
    if (other->__type_info != &System_Type_TypeInfo &&
        other->__type_info != &::System_RuntimeType_TypeInfo) return false;
    auto* other_type = static_cast<Type*>(other);
    return self->type_info == other_type->type_info;
}

String* type_to_string(Type* t) {
    if (!t || !t->type_info) throw_null_reference();
    return string_literal(t->type_info->full_name);
}

} // namespace cil2cpp
