/**
 * CIL2CPP Runtime - Managed Reflection Member Implementation
 *
 * Implements System.Reflection.MethodInfo and FieldInfo managed wrappers.
 */

#include <cil2cpp/memberinfo.h>
#include <cil2cpp/reflection.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/string.h>
#include <cil2cpp/array.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/boxing.h>

#include <cstring>
#include <string>

// Generated code defines System_RuntimeType_TypeInfo in global namespace.
extern cil2cpp::TypeInfo System_RuntimeType_TypeInfo;

namespace cil2cpp {

// ===== TypeInfo for managed reflection types =====

// Forward decl for vtable wrappers
static String* MethodInfo_ToString_vtable(Object* obj);
static Boolean MethodInfo_Equals_vtable(Object* obj, Object* other);
static Int32   MethodInfo_GetHashCode_vtable(Object* obj);

static void* System_Reflection_MethodInfo_vtable_methods[] = {
    reinterpret_cast<void*>(&MethodInfo_ToString_vtable),
    reinterpret_cast<void*>(&MethodInfo_Equals_vtable),
    reinterpret_cast<void*>(&MethodInfo_GetHashCode_vtable),
};
static VTable System_Reflection_MethodInfo_VTable = {
    &System_Reflection_MethodInfo_TypeInfo,
    System_Reflection_MethodInfo_vtable_methods,
    3,
};

TypeInfo System_Reflection_MethodInfo_TypeInfo = {
    .name = "MethodInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.MethodInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedMethodInfo), .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = &System_Reflection_MethodInfo_VTable,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static String* FieldInfo_ToString_vtable(Object* obj);
static Boolean FieldInfo_Equals_vtable(Object* obj, Object* other);
static Int32   FieldInfo_GetHashCode_vtable(Object* obj);

static void* System_Reflection_FieldInfo_vtable_methods[] = {
    reinterpret_cast<void*>(&FieldInfo_ToString_vtable),
    reinterpret_cast<void*>(&FieldInfo_Equals_vtable),
    reinterpret_cast<void*>(&FieldInfo_GetHashCode_vtable),
};
static VTable System_Reflection_FieldInfo_VTable = {
    &System_Reflection_FieldInfo_TypeInfo,
    System_Reflection_FieldInfo_vtable_methods,
    3,
};

TypeInfo System_Reflection_FieldInfo_TypeInfo = {
    .name = "FieldInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.FieldInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedFieldInfo), .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = &System_Reflection_FieldInfo_VTable,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_Reflection_ParameterInfo_TypeInfo = {
    .name = "ParameterInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.ParameterInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedParameterInfo), .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// ===== Helper: Create managed wrappers =====

static ManagedMethodInfo* create_managed_method_info(MethodInfo* native) {
    auto* mi = static_cast<ManagedMethodInfo*>(
        gc::alloc(sizeof(ManagedMethodInfo), &System_Reflection_MethodInfo_TypeInfo));
    mi->native_info = native;
    return mi;
}

static ManagedFieldInfo* create_managed_field_info(FieldInfo* native) {
    auto* fi = static_cast<ManagedFieldInfo*>(
        gc::alloc(sizeof(ManagedFieldInfo), &System_Reflection_FieldInfo_TypeInfo));
    fi->native_info = native;
    return fi;
}

// ===== Type → GetMethods/GetFields =====

Array* type_get_methods(Type* t) {
    if (!t || !t->type_info) throw_null_reference();

    auto* info = t->type_info;
    UInt32 count = info->method_count;

    // Create an array of ManagedMethodInfo* (pointer-sized elements)
    auto* arr = array_create(&System_Reflection_MethodInfo_TypeInfo,
                             static_cast<Int32>(count));
    auto** data = static_cast<ManagedMethodInfo**>(array_data(arr));
    for (UInt32 i = 0; i < count; i++) {
        data[i] = create_managed_method_info(&info->methods[i]);
    }
    return arr;
}

Array* type_get_fields(Type* t) {
    if (!t || !t->type_info) throw_null_reference();

    auto* info = t->type_info;
    UInt32 count = info->field_count;

    auto* arr = array_create(&System_Reflection_FieldInfo_TypeInfo,
                             static_cast<Int32>(count));
    auto** data = static_cast<ManagedFieldInfo**>(array_data(arr));
    for (UInt32 i = 0; i < count; i++) {
        data[i] = create_managed_field_info(&info->fields[i]);
    }
    return arr;
}

ManagedMethodInfo* type_get_method(Type* t, String* name) {
    if (!t || !t->type_info) throw_null_reference();
    if (!name) throw_null_reference();

    auto* info = t->type_info;
    auto* name_utf8 = string_to_utf8(name);

    for (UInt32 i = 0; i < info->method_count; i++) {
        if (std::strcmp(info->methods[i].name, name_utf8) == 0) {
            return create_managed_method_info(&info->methods[i]);
        }
    }
    return nullptr;
}

ManagedFieldInfo* type_get_field(Type* t, String* name) {
    if (!t || !t->type_info) throw_null_reference();
    if (!name) throw_null_reference();

    auto* info = t->type_info;
    auto* name_utf8 = string_to_utf8(name);

    for (UInt32 i = 0; i < info->field_count; i++) {
        if (std::strcmp(info->fields[i].name, name_utf8) == 0) {
            return create_managed_field_info(&info->fields[i]);
        }
    }
    return nullptr;
}

// ===== MethodInfo Property Accessors =====

String* methodinfo_get_name(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return string_literal(mi->native_info->name);
}

Type* methodinfo_get_declaring_type(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    if (!mi->native_info->declaring_type) return nullptr;
    return type_get_type_object(mi->native_info->declaring_type);
}

Type* methodinfo_get_return_type(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    if (!mi->native_info->return_type) return nullptr;
    return type_get_type_object(mi->native_info->return_type);
}

Boolean methodinfo_get_is_public(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return (mi->native_info->flags & 0x0007) == 0x0006; // MemberAccessMask == Public
}

Boolean methodinfo_get_is_static(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return (mi->native_info->flags & 0x0010) != 0; // Static
}

Boolean methodinfo_get_is_virtual(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return (mi->native_info->flags & 0x0040) != 0; // Virtual
}

Boolean methodinfo_get_is_abstract(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return (mi->native_info->flags & 0x0400) != 0; // Abstract
}

String* methodinfo_to_string(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    // Format: "ReturnType MethodName(ParamType1, ParamType2, ...)"
    auto* native = mi->native_info;
    std::string result;
    if (native->return_type)
        result += native->return_type->name;
    else
        result += "Void";
    result += " ";
    result += native->name;
    result += "(";
    for (UInt32 i = 0; i < native->parameter_count; i++) {
        if (i > 0) result += ", ";
        if (native->parameter_types && native->parameter_types[i])
            result += native->parameter_types[i]->name;
        else
            result += "?";
    }
    result += ")";
    return string_literal(result.c_str());
}

Array* methodinfo_get_parameters(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    auto* native = mi->native_info;
    auto* arr = array_create(&System_Reflection_ParameterInfo_TypeInfo,
                             static_cast<Int32>(native->parameter_count));
    auto** data = static_cast<ManagedParameterInfo**>(array_data(arr));
    for (UInt32 i = 0; i < native->parameter_count; i++) {
        auto* pi = static_cast<ManagedParameterInfo*>(
            gc::alloc(sizeof(ManagedParameterInfo), &System_Reflection_ParameterInfo_TypeInfo));
        pi->name = nullptr; // parameter names not stored in native MethodInfo
        pi->param_type = (native->parameter_types && native->parameter_types[i])
                         ? native->parameter_types[i] : nullptr;
        pi->position = static_cast<Int32>(i);
        data[i] = pi;
    }
    return arr;
}

Object* methodinfo_invoke(ManagedMethodInfo* mi, Object* obj, Array* parameters) {
    if (!mi || !mi->native_info) throw_null_reference();
    auto* native = mi->native_info;
    if (!native->method_pointer)
        throw_invalid_operation();

    // For simplicity, support up to 4 parameters for now
    // This covers the vast majority of reflection invocation use cases
    UInt32 param_count = native->parameter_count;
    bool is_static = (native->flags & 0x0010) != 0;

    // Collect parameter pointers from array
    Object** args = nullptr;
    if (parameters && param_count > 0) {
        args = static_cast<Object**>(array_data(parameters));
    }

    // Dispatch based on parameter count
    // Instance methods get 'obj' as first C++ parameter
    if (is_static) {
        switch (param_count) {
            case 0: {
                auto fn = reinterpret_cast<Object*(*)()>(native->method_pointer);
                return fn();
            }
            case 1: {
                auto fn = reinterpret_cast<Object*(*)(Object*)>(native->method_pointer);
                return fn(args ? args[0] : nullptr);
            }
            case 2: {
                auto fn = reinterpret_cast<Object*(*)(Object*, Object*)>(native->method_pointer);
                return fn(args ? args[0] : nullptr, args ? args[1] : nullptr);
            }
            default:
                throw_invalid_operation();
        }
    } else {
        if (!obj) throw_null_reference();
        switch (param_count) {
            case 0: {
                auto fn = reinterpret_cast<Object*(*)(Object*)>(native->method_pointer);
                return fn(obj);
            }
            case 1: {
                auto fn = reinterpret_cast<Object*(*)(Object*, Object*)>(native->method_pointer);
                return fn(obj, args ? args[0] : nullptr);
            }
            case 2: {
                auto fn = reinterpret_cast<Object*(*)(Object*, Object*, Object*)>(native->method_pointer);
                return fn(obj, args ? args[0] : nullptr, args ? args[1] : nullptr);
            }
            default:
                throw_invalid_operation();
        }
    }
}

// ===== FieldInfo Property Accessors =====

String* fieldinfo_get_name(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return string_literal(fi->native_info->name);
}

Type* fieldinfo_get_declaring_type(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    if (!fi->native_info->declaring_type) return nullptr;
    return type_get_type_object(fi->native_info->declaring_type);
}

Type* fieldinfo_get_field_type(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    if (!fi->native_info->field_type) return nullptr;
    return type_get_type_object(fi->native_info->field_type);
}

Boolean fieldinfo_get_is_public(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return (fi->native_info->flags & 0x0007) == 0x0006; // Public
}

Boolean fieldinfo_get_is_static(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return (fi->native_info->flags & 0x0010) != 0; // Static
}

Boolean fieldinfo_get_is_init_only(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return (fi->native_info->flags & 0x0020) != 0; // InitOnly
}

String* fieldinfo_to_string(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    auto* native = fi->native_info;
    std::string result;
    if (native->field_type)
        result += native->field_type->name;
    else
        result += "?";
    result += " ";
    result += native->name;
    return string_literal(result.c_str());
}

Object* fieldinfo_get_value(ManagedFieldInfo* fi, Object* obj) {
    if (!fi || !fi->native_info) throw_null_reference();
    auto* native = fi->native_info;
    bool is_static = (native->flags & 0x0010) != 0;

    if (!is_static && !obj) throw_null_reference();

    // For static fields, offset is 0 and we can't compute the address
    // without knowing the static storage location, which isn't stored in native FieldInfo.
    // For now, only support instance field access.
    if (is_static)
        throw_invalid_operation();

    // Compute field address from object base + offset
    auto* field_ptr = reinterpret_cast<char*>(obj) + native->offset;

    // Determine if field type is a reference type or value type
    if (native->field_type && (native->field_type->flags & TypeFlags::ValueType)) {
        // Value type: box it using box_raw
        return box_raw(field_ptr, native->field_type->instance_size, native->field_type);
    }

    // Reference type: just dereference the pointer
    return *reinterpret_cast<Object**>(field_ptr);
}

void fieldinfo_set_value(ManagedFieldInfo* fi, Object* obj, Object* value) {
    if (!fi || !fi->native_info) throw_null_reference();
    auto* native = fi->native_info;
    bool is_static = (native->flags & 0x0010) != 0;

    if (!is_static && !obj) throw_null_reference();

    if (is_static)
        throw_invalid_operation();

    auto* field_ptr = reinterpret_cast<char*>(obj) + native->offset;

    if (native->field_type && (native->field_type->flags & TypeFlags::ValueType)) {
        // Value type: unbox and copy — boxed data starts after Object header
        if (value) {
            auto size = native->field_type->instance_size;
            void* unboxed = reinterpret_cast<char*>(value) + sizeof(Object);
            std::memcpy(field_ptr, unboxed, size);
        }
    } else {
        // Reference type: just set the pointer
        *reinterpret_cast<Object**>(field_ptr) = value;
    }
}

// ===== ParameterInfo Property Accessors =====

String* parameterinfo_get_name(ManagedParameterInfo* pi) {
    if (!pi) throw_null_reference();
    return pi->name ? string_literal(pi->name) : string_literal("");
}

Type* parameterinfo_get_parameter_type(ManagedParameterInfo* pi) {
    if (!pi) throw_null_reference();
    if (!pi->param_type) return nullptr;
    return type_get_type_object(pi->param_type);
}

Int32 parameterinfo_get_position(ManagedParameterInfo* pi) {
    if (!pi) throw_null_reference();
    return pi->position;
}

// ===== VTable implementations =====

static String* MethodInfo_ToString_vtable(Object* obj) {
    return methodinfo_to_string(static_cast<ManagedMethodInfo*>(obj));
}

static Boolean MethodInfo_Equals_vtable(Object* obj, Object* other) {
    if (!other) return false;
    if (other->__type_info != &System_Reflection_MethodInfo_TypeInfo) return false;
    auto* a = static_cast<ManagedMethodInfo*>(obj);
    auto* b = static_cast<ManagedMethodInfo*>(other);
    return a->native_info == b->native_info;
}

static Int32 MethodInfo_GetHashCode_vtable(Object* obj) {
    auto* mi = static_cast<ManagedMethodInfo*>(obj);
    return mi->native_info
        ? static_cast<Int32>(reinterpret_cast<uintptr_t>(mi->native_info) >> 3) : 0;
}

static String* FieldInfo_ToString_vtable(Object* obj) {
    return fieldinfo_to_string(static_cast<ManagedFieldInfo*>(obj));
}

static Boolean FieldInfo_Equals_vtable(Object* obj, Object* other) {
    if (!other) return false;
    if (other->__type_info != &System_Reflection_FieldInfo_TypeInfo) return false;
    auto* a = static_cast<ManagedFieldInfo*>(obj);
    auto* b = static_cast<ManagedFieldInfo*>(other);
    return a->native_info == b->native_info;
}

static Int32 FieldInfo_GetHashCode_vtable(Object* obj) {
    auto* fi = static_cast<ManagedFieldInfo*>(obj);
    return fi->native_info
        ? static_cast<Int32>(reinterpret_cast<uintptr_t>(fi->native_info) >> 3) : 0;
}

// ===== Universal MemberInfo dispatchers =====

String* memberinfo_get_name(Object* obj) {
    if (!obj) throw_null_reference();
    if (obj->__type_info == &System_Type_TypeInfo ||
        obj->__type_info == &::System_RuntimeType_TypeInfo)
        return type_get_name(static_cast<Type*>(obj));
    if (obj->__type_info == &System_Reflection_MethodInfo_TypeInfo)
        return methodinfo_get_name(static_cast<ManagedMethodInfo*>(obj));
    if (obj->__type_info == &System_Reflection_FieldInfo_TypeInfo)
        return fieldinfo_get_name(static_cast<ManagedFieldInfo*>(obj));
    // Fallback: return type name
    return string_literal(obj->__type_info ? obj->__type_info->name : "?");
}

Type* memberinfo_get_declaring_type(Object* obj) {
    if (!obj) throw_null_reference();
    if (obj->__type_info == &System_Reflection_MethodInfo_TypeInfo)
        return methodinfo_get_declaring_type(static_cast<ManagedMethodInfo*>(obj));
    if (obj->__type_info == &System_Reflection_FieldInfo_TypeInfo)
        return fieldinfo_get_declaring_type(static_cast<ManagedFieldInfo*>(obj));
    // Type doesn't have DeclaringType in our model — return nullptr
    return nullptr;
}

} // namespace cil2cpp
