/**
 * CIL2CPP Runtime - Assembly & PropertyInfo TypeInfo definitions
 *
 * Phase II.4: Minimal TypeInfo structs for reflection types.
 */

#include <cil2cpp/assembly.h>
#include <cil2cpp/memberinfo.h>
#include <cil2cpp/reflection.h>
#include <cil2cpp/string.h>

namespace cil2cpp {

// Forward declarations for PropertyInfo VTable
static String* PropertyInfo_ToString_vtable(Object* obj) {
    return propertyinfo_to_string(static_cast<ManagedPropertyInfo*>(obj));
}
static Boolean PropertyInfo_Equals_vtable(Object* obj, Object* other) {
    if (!other) return false;
    if (other->__type_info != &System_Reflection_PropertyInfo_TypeInfo) return false;
    auto* a = static_cast<ManagedPropertyInfo*>(obj);
    auto* b = static_cast<ManagedPropertyInfo*>(other);
    return a->native_info == b->native_info;
}
static Int32 PropertyInfo_GetHashCode_vtable(Object* obj) {
    auto* pi = static_cast<ManagedPropertyInfo*>(obj);
    return pi->native_info
        ? static_cast<Int32>(reinterpret_cast<uintptr_t>(pi->native_info) >> 3) : 0;
}
static void* System_Reflection_PropertyInfo_vtable_methods[] = {
    reinterpret_cast<void*>(&PropertyInfo_ToString_vtable),
    reinterpret_cast<void*>(&PropertyInfo_Equals_vtable),
    reinterpret_cast<void*>(&PropertyInfo_GetHashCode_vtable),
};
static VTable System_Reflection_PropertyInfo_VTable = {
    &System_Reflection_PropertyInfo_TypeInfo,
    System_Reflection_PropertyInfo_vtable_methods,
    3,
};

TypeInfo System_Reflection_Assembly_TypeInfo = {
    .name = "Assembly",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.Assembly",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedAssembly), .element_size = 0,
    .flags = TypeFlags::Abstract,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

TypeInfo System_Reflection_PropertyInfo_TypeInfo = {
    .name = "PropertyInfo",
    .namespace_name = "System.Reflection",
    .full_name = "System.Reflection.PropertyInfo",
    .base_type = &System_Object_TypeInfo,
    .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(ManagedPropertyInfo), .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = &System_Reflection_PropertyInfo_VTable,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

} // namespace cil2cpp
