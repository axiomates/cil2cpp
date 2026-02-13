/**
 * CIL2CPP Runtime - System.Object Implementation
 */

#include <cil2cpp/bcl/System.Object.h>
#include <cil2cpp/string.h>
#include <cil2cpp/type_info.h>

namespace cil2cpp {
namespace System {

// System.Object type info
TypeInfo Object_TypeInfo = {
    .name = "Object",
    .namespace_name = "System",
    .full_name = "System.Object",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
};

} // namespace System

// Object method implementations
String* object_to_string(Object* obj) {
    if (!obj) {
        return string_literal("null");
    }

    if (obj->__type_info && obj->__type_info->full_name) {
        return string_literal(obj->__type_info->full_name);
    }

    return string_literal("System.Object");
}

} // namespace cil2cpp
