/**
 * CIL2CPP Runtime - Assembly & PropertyInfo TypeInfo definitions
 *
 * Phase II.4: Minimal TypeInfo structs for reflection types.
 */

#include <cil2cpp/assembly.h>
#include <cil2cpp/reflection.h>

namespace cil2cpp {

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
    .flags = TypeFlags::Abstract,
    .vtable = nullptr,
    .fields = nullptr, .field_count = 0,
    .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

} // namespace cil2cpp
