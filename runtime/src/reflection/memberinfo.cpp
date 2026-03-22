/**
 * CIL2CPP Runtime - Managed Reflection Member Implementation
 *
 * Implements System.Reflection.MethodInfo and FieldInfo managed wrappers.
 */

#include <cil2cpp/memberinfo.h>
#include <cil2cpp/assembly.h>
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

// ===== Fallback TypeInfo for reflection types =====
// These are used when the runtime creates reflection objects.
// Generated code defines its own TypeInfo with proper BCL vtables in global namespace.
// At init time, reflection_set_typeinfos() patches these to use the generated vtables.

static TypeInfo MethodInfo_TypeInfo_Fallback = {
    .name = "MethodInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.MethodInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedMethodInfo), .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo FieldInfo_TypeInfo_Fallback = {
    .name = "FieldInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.FieldInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedFieldInfo), .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo ParameterInfo_TypeInfo_Fallback = {
    .name = "ParameterInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.ParameterInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedParameterInfo), .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

static TypeInfo PropertyInfo_TypeInfo_Fallback = {
    .name = "PropertyInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.PropertyInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedPropertyInfo), .element_size = 0,
    .flags = TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// Pointers start at fallback, patched to generated TypeInfo at init time
static TypeInfo* s_methodinfo_ti = &MethodInfo_TypeInfo_Fallback;
static TypeInfo* s_fieldinfo_ti = &FieldInfo_TypeInfo_Fallback;
static TypeInfo* s_parameterinfo_ti = &ParameterInfo_TypeInfo_Fallback;
static TypeInfo* s_propertyinfo_ti = &PropertyInfo_TypeInfo_Fallback;

// Public definitions to satisfy extern declarations in headers.
// NOT used for allocation — s_*_ti pointers are used instead.
TypeInfo System_Reflection_MethodInfo_TypeInfo = MethodInfo_TypeInfo_Fallback;
TypeInfo System_Reflection_FieldInfo_TypeInfo = FieldInfo_TypeInfo_Fallback;
TypeInfo System_Reflection_ParameterInfo_TypeInfo = ParameterInfo_TypeInfo_Fallback;
TypeInfo System_Reflection_PropertyInfo_TypeInfo = PropertyInfo_TypeInfo_Fallback;

void reflection_set_typeinfos(TypeInfo* method_ti, TypeInfo* field_ti, TypeInfo* param_ti, TypeInfo* prop_ti) {
    if (method_ti) s_methodinfo_ti = method_ti;
    if (field_ti)  s_fieldinfo_ti = field_ti;
    if (param_ti)  s_parameterinfo_ti = param_ti;
    if (prop_ti)   s_propertyinfo_ti = prop_ti;
}

// Forward declarations for extern "C" functions in core_methods.cpp
extern "C" void* System_Reflection_RuntimePropertyInfo_get_PropertyType(void*);
extern "C" void* System_Reflection_RuntimePropertyInfo_GetIndexParameters(void*);
extern "C" bool  System_Reflection_RuntimePropertyInfo_get_CanRead(void*);
extern "C" bool  System_Reflection_RuntimePropertyInfo_get_CanWrite(void*);
extern "C" void* System_Reflection_RuntimePropertyInfo_GetGetMethod(void*, bool);
extern "C" void* System_Reflection_RuntimePropertyInfo_GetSetMethod(void*, bool);
extern "C" void* System_Reflection_RuntimePropertyInfo_GetValue_5(void*, void*, int32_t, void*, void*, void*);
extern "C" void  System_Reflection_RuntimePropertyInfo_SetValue_6(void*, void*, void*, int32_t, void*, void*, void*);

// Forward declarations for MemberInfo ICalls used in ICustomAttributeProvider interface patching
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Boolean(void*, bool);
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Type_System_Boolean(void*, void*, bool);
extern "C" bool System_Reflection_MemberInfo_IsDefined(void*, void*, bool);

void reflection_patch_property_vtables(TypeInfo* prop_ti, TypeInfo* runtime_prop_ti) {
    // Patch null vtable slots for RuntimePropertyInfo methods.
    // Since RuntimePropertyInfo is ReflectionAliased, the codegen blocks all instance methods,
    // leaving these vtable slots as nullptr. We patch them at runtime.
    // Slot mapping (from generated vtable order):
    //   18: get_PropertyType      21: get_CanRead           25: GetGetMethod(bool)
    //   19: GetIndexParameters     22: get_CanWrite          27: GetSetMethod(bool)
    //   32: GetValue(5-param)      36: SetValue(6-param)
    auto patch = [](TypeInfo* ti, const char* label) {
        if (!ti || !ti->vtable) {
            return;
        }
        auto** m = ti->vtable->methods;
        auto count = ti->vtable->method_count;
        if (count > 18 && !m[18]) m[18] = (void*)System_Reflection_RuntimePropertyInfo_get_PropertyType;
        if (count > 19 && !m[19]) m[19] = (void*)System_Reflection_RuntimePropertyInfo_GetIndexParameters;
        if (count > 21 && !m[21]) m[21] = (void*)System_Reflection_RuntimePropertyInfo_get_CanRead;
        if (count > 22 && !m[22]) m[22] = (void*)System_Reflection_RuntimePropertyInfo_get_CanWrite;
        if (count > 25 && !m[25]) m[25] = (void*)System_Reflection_RuntimePropertyInfo_GetGetMethod;
        if (count > 27 && !m[27]) m[27] = (void*)System_Reflection_RuntimePropertyInfo_GetSetMethod;
        if (count > 32 && !m[32]) m[32] = (void*)System_Reflection_RuntimePropertyInfo_GetValue_5;
        if (count > 36 && !m[36]) m[36] = (void*)System_Reflection_RuntimePropertyInfo_SetValue_6;

        // Patch ICustomAttributeProvider interface vtable.
        // RuntimePropertyInfo is blanket-gated, leaving ICustomAttributeProvider methods as nullptr.
        // Patch them to use the MemberInfo ICalls which support attribute construction.
        if (ti->interface_vtables) {
            for (UInt32 i = 0; i < ti->interface_count; i++) {
                auto& ivt = ti->interface_vtables[i];
                if (ivt.interface_type && ivt.interface_type->full_name &&
                    std::strcmp(ivt.interface_type->full_name, "System.Reflection.ICustomAttributeProvider") == 0) {
                    if (ivt.method_count >= 3) {
                        if (!ivt.methods[0]) ivt.methods[0] = (void*)System_Reflection_MemberInfo_GetCustomAttributes__System_Boolean;
                        if (!ivt.methods[1]) ivt.methods[1] = (void*)System_Reflection_MemberInfo_GetCustomAttributes__System_Type_System_Boolean;
                        if (!ivt.methods[2]) ivt.methods[2] = (void*)System_Reflection_MemberInfo_IsDefined;
                    }
                    break;
                }
            }
        }
    };
    patch(prop_ti, "PropertyInfo");
    patch(runtime_prop_ti, "RuntimePropertyInfo");
}

// Forward declarations for MethodBase Equals/GetHashCode in core_methods.cpp
extern "C" bool System_Reflection_MethodBase_Equals(void*, void*);
extern "C" int32_t System_Reflection_MethodBase_GetHashCode(void*);

void reflection_patch_method_vtables(TypeInfo* methodbase_ti, TypeInfo* methodinfo_ti) {
    // Patch vtable slots 1 (Equals) and 2 (GetHashCode) for MethodBase/MethodInfo.
    // MethodBase is ReflectionAliased → Equals/GetHashCode overrides never compiled
    // → vtable retains Object defaults (pointer equality). We patch in the semantic
    // implementations that compare native_info pointers.
    auto patch = [](TypeInfo* ti) {
        if (!ti || !ti->vtable) return;
        auto** m = ti->vtable->methods;
        auto count = ti->vtable->method_count;
        // Slot 1 = Equals (Object vtable layout)
        if (count > 1 && !m[1]) m[1] = (void*)System_Reflection_MethodBase_Equals;
        // Slot 2 = GetHashCode (Object vtable layout)
        if (count > 2 && !m[2]) m[2] = (void*)System_Reflection_MethodBase_GetHashCode;
    };
    patch(methodbase_ti);
    patch(methodinfo_ti);
}

// ===== Helper: Create managed wrappers =====

// Cache managed MethodInfo wrappers so the same native MethodInfo always
// returns the same managed object. Critical for reference-equality checks
// in Expression.CheckMethod (System.Linq.Expressions).
static constexpr size_t METHODINFO_CACHE_SIZE = 128;
static MethodInfo* s_methodinfo_cache_keys[METHODINFO_CACHE_SIZE] = {};
static ManagedMethodInfo* s_methodinfo_cache_values[METHODINFO_CACHE_SIZE] = {};

ManagedMethodInfo* create_managed_method_info(MethodInfo* native) {
    auto slot = reinterpret_cast<uintptr_t>(native) / sizeof(void*) % METHODINFO_CACHE_SIZE;
    if (s_methodinfo_cache_keys[slot] == native && s_methodinfo_cache_values[slot]) {
        return s_methodinfo_cache_values[slot];
    }
    auto* mi = static_cast<ManagedMethodInfo*>(
        gc::alloc(sizeof(ManagedMethodInfo), s_methodinfo_ti));
    mi->native_info = native;
    s_methodinfo_cache_keys[slot] = native;
    s_methodinfo_cache_values[slot] = mi;
    return mi;
}

static ManagedFieldInfo* create_managed_field_info(FieldInfo* native) {
    auto* fi = static_cast<ManagedFieldInfo*>(
        gc::alloc(sizeof(ManagedFieldInfo), s_fieldinfo_ti));
    fi->native_info = native;
    return fi;
}

// Cache managed PropertyInfo wrappers so the same native PropertyInfo always
// returns the same managed object. This is critical for reference-equality checks
// in List<MemberInfo>.Contains() used by Newtonsoft.Json's GetSerializableMembers.
static constexpr size_t PROPINFO_CACHE_SIZE = 64;
static PropertyInfo* s_propinfo_cache_keys[PROPINFO_CACHE_SIZE] = {};
static ManagedPropertyInfo* s_propinfo_cache_values[PROPINFO_CACHE_SIZE] = {};

static ManagedPropertyInfo* create_managed_property_info(PropertyInfo* native) {
    // Check cache first
    auto slot = reinterpret_cast<uintptr_t>(native) / sizeof(void*) % PROPINFO_CACHE_SIZE;
    if (s_propinfo_cache_keys[slot] == native && s_propinfo_cache_values[slot]) {
        return s_propinfo_cache_values[slot];
    }

    auto* pi = static_cast<ManagedPropertyInfo*>(
        gc::alloc(sizeof(ManagedPropertyInfo), s_propertyinfo_ti));
    pi->native_info = native;

    // Store in cache
    s_propinfo_cache_keys[slot] = native;
    s_propinfo_cache_values[slot] = pi;
    return pi;
}

// ===== Type → GetMethods/GetFields =====

Array* type_get_methods(Type* t) {
    if (!t || !t->type_info) throw_null_reference();

    auto* info = t->type_info;
    // Reflection metadata may be stripped for types not accessed via reflection
    UInt32 count = info->methods ? info->method_count : 0;

    // Create an array of ManagedMethodInfo* (pointer-sized elements)
    auto* arr = array_create(s_methodinfo_ti,
                             static_cast<Int32>(count));
    auto** data = static_cast<ManagedMethodInfo**>(array_data(arr));
    for (UInt32 i = 0; i < count; i++) {
        data[i] = create_managed_method_info(&info->methods[i]);
    }
    return arr;
}

Array* type_get_methods_by_name(Type* t, const char* name, int listType) {
    if (!t || !t->type_info) throw_null_reference();
    auto* info = t->type_info;
    UInt32 count = info->methods ? info->method_count : 0;
    if (!name || listType == 0) return type_get_methods(t);

    UInt32 matchCount = 0;
    for (UInt32 i = 0; i < count; i++) {
        if (!info->methods[i].name) continue;
        bool match = (listType == 1)
            ? (std::strcmp(info->methods[i].name, name) == 0)
            : (_stricmp(info->methods[i].name, name) == 0);
        if (match) matchCount++;
    }

    auto* arr = array_create(s_methodinfo_ti, static_cast<Int32>(matchCount));
    auto** data = static_cast<ManagedMethodInfo**>(array_data(arr));
    UInt32 idx = 0;
    for (UInt32 i = 0; i < count && idx < matchCount; i++) {
        if (!info->methods[i].name) continue;
        bool match = (listType == 1)
            ? (std::strcmp(info->methods[i].name, name) == 0)
            : (_stricmp(info->methods[i].name, name) == 0);
        if (match) data[idx++] = create_managed_method_info(&info->methods[i]);
    }
    return arr;
}

Array* type_get_fields(Type* t) {
    if (!t || !t->type_info) throw_null_reference();

    auto* info = t->type_info;
    // Reflection metadata may be stripped for types not accessed via reflection
    UInt32 count = info->fields ? info->field_count : 0;

    auto* arr = array_create(s_fieldinfo_ti,
                             static_cast<Int32>(count));
    auto** data = static_cast<ManagedFieldInfo**>(array_data(arr));
    for (UInt32 i = 0; i < count; i++) {
        data[i] = create_managed_field_info(&info->fields[i]);
    }
    return arr;
}

Array* type_get_fields_by_name(Type* t, const char* name, int listType) {
    if (!t || !t->type_info) throw_null_reference();
    auto* info = t->type_info;
    UInt32 count = info->fields ? info->field_count : 0;
    if (!name || listType == 0) return type_get_fields(t);

    // Count matches
    UInt32 matchCount = 0;
    for (UInt32 i = 0; i < count; i++) {
        if (!info->fields[i].name) continue;
        bool match = (listType == 1)
            ? (std::strcmp(info->fields[i].name, name) == 0)
            : (_stricmp(info->fields[i].name, name) == 0);
        if (match) matchCount++;
    }

    auto* arr = array_create(s_fieldinfo_ti, static_cast<Int32>(matchCount));
    auto** data = static_cast<ManagedFieldInfo**>(array_data(arr));
    UInt32 idx = 0;
    for (UInt32 i = 0; i < count && idx < matchCount; i++) {
        if (!info->fields[i].name) continue;
        bool match = (listType == 1)
            ? (std::strcmp(info->fields[i].name, name) == 0)
            : (_stricmp(info->fields[i].name, name) == 0);
        if (match) data[idx++] = create_managed_field_info(&info->fields[i]);
    }
    return arr;
}

MethodInfo* find_method_info(TypeInfo* type_info, const char* name, uint32_t param_count) {
    if (!type_info || !type_info->methods) return nullptr;
    for (UInt32 i = 0; i < type_info->method_count; i++) {
        if (std::strcmp(type_info->methods[i].name, name) == 0
            && type_info->methods[i].parameter_count == param_count) {
            return &type_info->methods[i];
        }
    }
    return nullptr;
}

ManagedMethodInfo* type_get_method(Type* t, String* name) {
    if (!t || !t->type_info) throw_null_reference();
    if (!name) throw_null_reference();

    auto* info = t->type_info;
    if (!info->methods) return nullptr;  // Reflection metadata stripped
    auto* name_utf8 = string_to_utf8(name);

    for (UInt32 i = 0; i < info->method_count; i++) {
        if (std::strcmp(info->methods[i].name, name_utf8) == 0) {
            // .NET GetMethod(string) doesn't return constructors
            if (std::strcmp(name_utf8, ".ctor") == 0 || std::strcmp(name_utf8, ".cctor") == 0)
                return nullptr;
            return create_managed_method_info(&info->methods[i]);
        }
    }
    return nullptr;
}

ManagedFieldInfo* type_get_field(Type* t, String* name) {
    if (!t || !t->type_info) throw_null_reference();
    if (!name) throw_null_reference();

    auto* info = t->type_info;
    if (!info->fields) return nullptr;  // Reflection metadata stripped
    auto* name_utf8 = string_to_utf8(name);

    for (UInt32 i = 0; i < info->field_count; i++) {
        if (std::strcmp(info->fields[i].name, name_utf8) == 0) {
            // .NET GetField(string) without BindingFlags returns public fields only
            if (!metadata::field_is_public(info->fields[i].flags)) continue;
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
    return metadata::method_is_public(mi->native_info->flags);
}

Boolean methodinfo_get_is_static(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return metadata::method_is_static(mi->native_info->flags);
}

Boolean methodinfo_get_is_virtual(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return metadata::method_is_virtual(mi->native_info->flags);
}

Boolean methodinfo_get_is_abstract(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return metadata::method_is_abstract(mi->native_info->flags);
}

Boolean methodinfo_get_is_final(ManagedMethodInfo* mi) {
    if (!mi || !mi->native_info) throw_null_reference();
    return metadata::method_is_final(mi->native_info->flags);
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
    auto* arr = array_create(s_parameterinfo_ti,
                             static_cast<Int32>(native->parameter_count));
    auto** data = static_cast<ManagedParameterInfo**>(array_data(arr));
    for (UInt32 i = 0; i < native->parameter_count; i++) {
        auto* pi = static_cast<ManagedParameterInfo*>(
            gc::alloc(sizeof(ManagedParameterInfo), s_parameterinfo_ti));
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
    bool is_static = metadata::method_is_static(native->flags);

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
    return metadata::field_is_public(fi->native_info->flags);
}

Boolean fieldinfo_get_is_static(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return metadata::field_is_static(fi->native_info->flags);
}

Boolean fieldinfo_get_is_init_only(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return metadata::field_is_init_only(fi->native_info->flags);
}

Boolean fieldinfo_get_is_literal(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return metadata::field_is_literal(fi->native_info->flags);
}

Boolean fieldinfo_get_is_private(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return metadata::field_is_private(fi->native_info->flags);
}

Boolean fieldinfo_get_is_special_name(ManagedFieldInfo* fi) {
    if (!fi || !fi->native_info) throw_null_reference();
    return metadata::field_is_special_name(fi->native_info->flags);
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
    bool is_static = metadata::field_is_static(native->flags);

    if (!is_static && !obj) throw_null_reference();

    // For literal static fields (enum constants, const), box the constant_value
    if (is_static) {
        bool is_literal = metadata::field_is_literal(native->flags);
        if (is_literal && native->field_type) {
            auto elem_size = native->field_type->element_size;
            if (elem_size == 0) elem_size = native->field_type->instance_size;
            return box_raw(&native->constant_value, elem_size, native->field_type);
        }
        // Non-literal static fields: offset-based access not supported yet
        throw_invalid_operation();
    }

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
    bool is_static = metadata::field_is_static(native->flags);

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

// ===== Type → GetProperties =====

Array* type_get_properties(Type* t) {
    if (!t || !t->type_info) throw_null_reference();

    auto* info = t->type_info;
    UInt32 count = info->properties ? info->property_count : 0;

    auto* arr = array_create(s_propertyinfo_ti,
                             static_cast<Int32>(count));
    auto** data = static_cast<ManagedPropertyInfo**>(array_data(arr));
    for (UInt32 i = 0; i < count; i++) {
        data[i] = create_managed_property_info(&info->properties[i]);
    }
    return arr;
}

Array* type_get_properties_by_name(Type* t, const char* name, int listType) {
    if (!t || !t->type_info) throw_null_reference();
    auto* info = t->type_info;
    UInt32 count = info->properties ? info->property_count : 0;
    if (!name || listType == 0) return type_get_properties(t);

    UInt32 matchCount = 0;
    for (UInt32 i = 0; i < count; i++) {
        if (!info->properties[i].name) continue;
        bool match = (listType == 1)
            ? (std::strcmp(info->properties[i].name, name) == 0)
            : (_stricmp(info->properties[i].name, name) == 0);
        if (match) matchCount++;
    }

    auto* arr = array_create(s_propertyinfo_ti, static_cast<Int32>(matchCount));
    auto** data = static_cast<ManagedPropertyInfo**>(array_data(arr));
    UInt32 idx = 0;
    for (UInt32 i = 0; i < count && idx < matchCount; i++) {
        if (!info->properties[i].name) continue;
        bool match = (listType == 1)
            ? (std::strcmp(info->properties[i].name, name) == 0)
            : (_stricmp(info->properties[i].name, name) == 0);
        if (match) data[idx++] = create_managed_property_info(&info->properties[i]);
    }
    return arr;
}

ManagedPropertyInfo* type_get_property(Type* t, String* name) {
    if (!t || !t->type_info) throw_null_reference();
    if (!name) throw_null_reference();

    auto* info = t->type_info;
    if (!info->properties) return nullptr;
    auto* name_utf8 = string_to_utf8(name);

    for (UInt32 i = 0; i < info->property_count; i++) {
        if (std::strcmp(info->properties[i].name, name_utf8) == 0) {
            return create_managed_property_info(&info->properties[i]);
        }
    }
    return nullptr;
}

// ===== PropertyInfo Property Accessors =====

String* propertyinfo_get_name(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    return string_literal(pi->native_info->name);
}

Type* propertyinfo_get_declaring_type(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    if (!pi->native_info->declaring_type) return nullptr;
    return type_get_type_object(pi->native_info->declaring_type);
}

Type* propertyinfo_get_property_type(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    if (!pi->native_info->property_type) return nullptr;
    return type_get_type_object(pi->native_info->property_type);
}

ManagedMethodInfo* propertyinfo_get_get_method(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    if (!pi->native_info->getter) return nullptr;
    // Find the MethodInfo in declaring type's methods that matches the getter pointer
    auto* decl = pi->native_info->declaring_type;
    if (decl && decl->methods) {
        for (UInt32 i = 0; i < decl->method_count; i++) {
            if (decl->methods[i].method_pointer == pi->native_info->getter) {
                return create_managed_method_info(&decl->methods[i]);
            }
        }
    }
    // Fallback: property has a getter pointer but the declaring type's methods array
    // doesn't include it. Create a GC-allocated MethodInfo with proper naming.
    auto* stub = static_cast<MethodInfo*>(gc::alloc(sizeof(MethodInfo), nullptr));
    std::memset(stub, 0, sizeof(MethodInfo));
    static thread_local std::string getter_name_buf;
    getter_name_buf = std::string("get_") + (pi->native_info->name ? pi->native_info->name : "");
    stub->name = getter_name_buf.c_str();
    stub->method_pointer = pi->native_info->getter;
    stub->declaring_type = pi->native_info->declaring_type;
    return create_managed_method_info(stub);
}

ManagedMethodInfo* propertyinfo_get_set_method(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    if (!pi->native_info->setter) return nullptr;
    auto* decl = pi->native_info->declaring_type;
    if (decl && decl->methods) {
        for (UInt32 i = 0; i < decl->method_count; i++) {
            if (decl->methods[i].method_pointer == pi->native_info->setter) {
                return create_managed_method_info(&decl->methods[i]);
            }
        }
    }
    // Fallback: property has a setter pointer but the declaring type's methods array
    // doesn't include it. Create a GC-allocated MethodInfo with proper naming.
    auto* stub = static_cast<MethodInfo*>(gc::alloc(sizeof(MethodInfo), nullptr));
    std::memset(stub, 0, sizeof(MethodInfo));
    static thread_local std::string setter_name_buf;
    setter_name_buf = std::string("set_") + (pi->native_info->name ? pi->native_info->name : "");
    stub->name = setter_name_buf.c_str();
    stub->method_pointer = pi->native_info->setter;
    stub->declaring_type = pi->native_info->declaring_type;
    return create_managed_method_info(stub);
}

Boolean propertyinfo_can_read(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    return pi->native_info->getter != nullptr;
}

Boolean propertyinfo_can_write(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    return pi->native_info->setter != nullptr;
}

Object* propertyinfo_get_value(ManagedPropertyInfo* pi, Object* obj, Array* /*index*/) {
    if (!pi || !pi->native_info) throw_null_reference();
    if (!pi->native_info->getter)
        throw_invalid_operation();

    auto* prop_type = pi->native_info->property_type;
    auto getter = pi->native_info->getter;

    // Reference types: call directly, return pointer
    if (!prop_type || !(prop_type->flags & TypeFlags::ValueType)) {
        auto fn = reinterpret_cast<Object*(*)(Object*)>(getter);
        return fn(obj);
    }

    // Value types: call with correct ABI return type, then box
    // x64 ABI: integer types return in RAX, float/double return in XMM0
    switch (prop_type->cor_element_type) {
        case 0x02: { // Boolean
            auto fn = reinterpret_cast<bool(*)(Object*)>(getter);
            bool val = fn(obj);
            return box(val, prop_type);
        }
        case 0x04: case 0x05: { // SByte, Byte
            auto fn = reinterpret_cast<uint8_t(*)(Object*)>(getter);
            uint8_t val = fn(obj);
            return box_raw(&val, 1, prop_type);
        }
        case 0x03: case 0x06: case 0x07: { // Char, Int16, UInt16
            auto fn = reinterpret_cast<uint16_t(*)(Object*)>(getter);
            uint16_t val = fn(obj);
            return box_raw(&val, 2, prop_type);
        }
        case 0x08: case 0x09: { // Int32, UInt32
            auto fn = reinterpret_cast<int32_t(*)(Object*)>(getter);
            int32_t val = fn(obj);
            return box(val, prop_type);
        }
        case 0x0A: case 0x0B: { // Int64, UInt64
            auto fn = reinterpret_cast<int64_t(*)(Object*)>(getter);
            int64_t val = fn(obj);
            return box(val, prop_type);
        }
        case 0x0C: { // Single (float)
            auto fn = reinterpret_cast<float(*)(Object*)>(getter);
            float val = fn(obj);
            return box(val, prop_type);
        }
        case 0x0D: { // Double
            auto fn = reinterpret_cast<double(*)(Object*)>(getter);
            double val = fn(obj);
            return box(val, prop_type);
        }
        default: {
            // Enum or struct value type: use instance_size for value size
            auto value_size = prop_type->instance_size > sizeof(Object)
                ? prop_type->instance_size - sizeof(Object) : sizeof(int64_t);
            if (value_size <= sizeof(int64_t)) {
                auto fn = reinterpret_cast<int64_t(*)(Object*)>(getter);
                int64_t val = fn(obj);
                return box_raw(&val, value_size, prop_type);
            }
            // Large struct (>8 bytes): x64 returns via hidden pointer param
            auto* result = static_cast<Object*>(gc::alloc(sizeof(Object) + value_size, prop_type));
            auto fn = reinterpret_cast<void*(*)(void*, Object*)>(getter);
            fn(reinterpret_cast<char*>(result) + sizeof(Object), obj);
            return result;
        }
    }
}

void propertyinfo_set_value(ManagedPropertyInfo* pi, Object* obj, Object* value, Array* /*index*/) {
    if (!pi || !pi->native_info) throw_null_reference();
    if (!pi->native_info->setter)
        throw_invalid_operation();

    auto* prop_type = pi->native_info->property_type;
    auto setter = pi->native_info->setter;

    // Reference types: pass directly
    if (!prop_type || !(prop_type->flags & TypeFlags::ValueType)) {
        auto fn = reinterpret_cast<void(*)(Object*, Object*)>(setter);
        fn(obj, value);
        return;
    }

    // Value types: unbox and pass with correct ABI
    if (!value) throw_null_reference();
    void* unboxed = reinterpret_cast<char*>(value) + sizeof(Object);
    switch (prop_type->cor_element_type) {
        case 0x02: { // Boolean
            auto fn = reinterpret_cast<void(*)(Object*, bool)>(setter);
            fn(obj, *reinterpret_cast<bool*>(unboxed));
            return;
        }
        case 0x04: case 0x05: { // SByte, Byte
            auto fn = reinterpret_cast<void(*)(Object*, uint8_t)>(setter);
            fn(obj, *reinterpret_cast<uint8_t*>(unboxed));
            return;
        }
        case 0x03: case 0x06: case 0x07: { // Char, Int16, UInt16
            auto fn = reinterpret_cast<void(*)(Object*, uint16_t)>(setter);
            fn(obj, *reinterpret_cast<uint16_t*>(unboxed));
            return;
        }
        case 0x08: case 0x09: { // Int32, UInt32
            auto fn = reinterpret_cast<void(*)(Object*, int32_t)>(setter);
            fn(obj, *reinterpret_cast<int32_t*>(unboxed));
            return;
        }
        case 0x0A: case 0x0B: { // Int64, UInt64
            auto fn = reinterpret_cast<void(*)(Object*, int64_t)>(setter);
            fn(obj, *reinterpret_cast<int64_t*>(unboxed));
            return;
        }
        case 0x0C: { // Single (float)
            auto fn = reinterpret_cast<void(*)(Object*, float)>(setter);
            fn(obj, *reinterpret_cast<float*>(unboxed));
            return;
        }
        case 0x0D: { // Double
            auto fn = reinterpret_cast<void(*)(Object*, double)>(setter);
            fn(obj, *reinterpret_cast<double*>(unboxed));
            return;
        }
        default: {
            // Struct value type: pass as int64_t for small structs
            auto value_size = prop_type->instance_size > sizeof(Object)
                ? prop_type->instance_size - sizeof(Object) : sizeof(int64_t);
            if (value_size <= sizeof(int64_t)) {
                int64_t raw = 0;
                std::memcpy(&raw, unboxed, value_size);
                auto fn = reinterpret_cast<void(*)(Object*, int64_t)>(setter);
                fn(obj, raw);
            } else {
                // Large struct: passed by pointer on x64
                auto fn = reinterpret_cast<void(*)(Object*, void*)>(setter);
                fn(obj, unboxed);
            }
            return;
        }
    }
}

String* propertyinfo_to_string(ManagedPropertyInfo* pi) {
    if (!pi || !pi->native_info) throw_null_reference();
    auto* native = pi->native_info;
    std::string result;
    if (native->property_type)
        result += native->property_type->name;
    else
        result += "?";
    result += " ";
    result += native->name;
    return string_literal(result.c_str());
}

// ===== Universal MemberInfo dispatchers =====

// Check TypeInfo by full_name to handle linker ODR (both runtime and generated code define TypeInfo symbols)
static bool is_type(TypeInfo* ti, const char* full_name) {
    return ti && ti->full_name && std::strcmp(ti->full_name, full_name) == 0;
}

static bool is_method_info(TypeInfo* ti) {
    return is_type(ti, "System.Reflection.MethodInfo")
        || is_type(ti, "System.Reflection.RuntimeMethodInfo")
        || is_type(ti, "System.Reflection.MethodBase")
        || is_type(ti, "System.Reflection.ConstructorInfo")
        || is_type(ti, "System.Reflection.RuntimeConstructorInfo");
}

static bool is_field_info(TypeInfo* ti) {
    return is_type(ti, "System.Reflection.FieldInfo")
        || is_type(ti, "System.Reflection.RuntimeFieldInfo");
}

static bool is_property_info(TypeInfo* ti) {
    return is_type(ti, "System.Reflection.PropertyInfo")
        || is_type(ti, "System.Reflection.RuntimePropertyInfo");
}

String* memberinfo_get_name(Object* obj) {
    if (!obj) throw_null_reference();
    auto* ti = obj->__type_info;
    if (ti == &System_Type_TypeInfo || ti == &::System_RuntimeType_TypeInfo
        || is_type(ti, "System.RuntimeType") || is_type(ti, "System.Type")) {
        auto* t = static_cast<Type*>(obj);
        return type_get_name(t);
    }
    if (is_method_info(ti))
        return methodinfo_get_name(static_cast<ManagedMethodInfo*>(obj));
    if (is_field_info(ti))
        return fieldinfo_get_name(static_cast<ManagedFieldInfo*>(obj));
    if (is_property_info(ti)) {
        return propertyinfo_get_name(static_cast<ManagedPropertyInfo*>(obj));
    }
    // Fallback: return type name
    return string_literal(ti ? ti->name : "?");
}

Type* memberinfo_get_declaring_type(Object* obj) {
    if (!obj) throw_null_reference();
    auto* ti = obj->__type_info;
    if (is_method_info(ti))
        return methodinfo_get_declaring_type(static_cast<ManagedMethodInfo*>(obj));
    if (is_field_info(ti))
        return fieldinfo_get_declaring_type(static_cast<ManagedFieldInfo*>(obj));
    if (is_property_info(ti))
        return propertyinfo_get_declaring_type(static_cast<ManagedPropertyInfo*>(obj));
    // Type doesn't have DeclaringType in our model — return nullptr
    return nullptr;
}

} // namespace cil2cpp
