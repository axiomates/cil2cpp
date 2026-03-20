/**
 * CIL2CPP Runtime Tests - External Symbol Stubs
 *
 * Provides dummy definitions for symbols that are normally defined
 * by generated code but needed by the runtime library at link time.
 */

#include <cil2cpp/cil2cpp.h>
#include <cil2cpp/task.h>
#include <cil2cpp/memberinfo.h>

// System_RuntimeType_TypeInfo is defined as extern in type.cpp
// (normally provided by generated code). Tests need a stub.
cil2cpp::TypeInfo System_RuntimeType_TypeInfo = {
    .name = "RuntimeType", .namespace_name = "System", .full_name = "System.RuntimeType",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = sizeof(cil2cpp::Object) + 24, .element_size = 0,
    .flags = cil2cpp::TypeFlags::Sealed, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
   .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// Task_FinishContinuations is defined in generated code (BCL IL).
// Tests need a no-op stub.
void System_Threading_Tasks_Task_FinishContinuations(cil2cpp::Task*) {}

// ThreadPool DispatchWorkItem — defined in generated code (BCL IL).
void System_Threading_ThreadPoolWorkQueue_DispatchWorkItem(cil2cpp::Object*, cil2cpp::ManagedThread*) {}

// AttributeUsageAttribute TypeInfo — defined in generated code.
cil2cpp::TypeInfo System_AttributeUsageAttribute_TypeInfo = {
    .name = "AttributeUsageAttribute", .namespace_name = "System",
    .full_name = "System.AttributeUsageAttribute",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = 24, .element_size = 0,
    .flags = cil2cpp::TypeFlags::Sealed, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// RuntimeType.RuntimeTypeCache TypeInfo — defined in generated code.
// instance_size = 176: Object header (16) + 20 pointer/enum fields (160) on x64.
// In production, codegen sets this via sizeof(System_RuntimeType_RuntimeTypeCache).
cil2cpp::TypeInfo System_RuntimeType_RuntimeTypeCache_TypeInfo = {
    .name = "RuntimeTypeCache", .namespace_name = "System",
    .full_name = "System.RuntimeType+RuntimeTypeCache",
    .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
    .instance_size = 176, .element_size = 0,
    .flags = cil2cpp::TypeFlags::None, .vtable = nullptr,
    .fields = nullptr, .field_count = 0, .methods = nullptr, .method_count = 0,
    .properties = nullptr, .property_count = 0,
    .default_ctor = nullptr, .finalizer = nullptr,
    .interface_vtables = nullptr, .interface_vtable_count = 0,
};

// IOCP CompletionCallback — defined in generated code (BCL IL).
struct System_Threading_NativeOverlapped;
void System_Threading_ThreadPoolBoundHandleOverlapped_CompletionCallback(
    uint32_t, uint32_t, System_Threading_NativeOverlapped*) {}
