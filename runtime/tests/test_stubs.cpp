/**
 * CIL2CPP Runtime Tests - External Symbol Stubs
 *
 * Provides dummy definitions for symbols that are normally defined
 * by generated code but needed by the runtime library at link time.
 */

#include <cil2cpp/cil2cpp.h>

// System_RuntimeType_TypeInfo is defined as extern in type.cpp
// (normally provided by generated code). Tests need a stub.
cil2cpp::TypeInfo System_RuntimeType_TypeInfo = {
    .name = "RuntimeType", .namespace_name = "System", .full_name = "System.RuntimeType",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(cil2cpp::Object) + 24, .element_size = 0,
    .flags = cil2cpp::TypeFlags::Sealed, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};
