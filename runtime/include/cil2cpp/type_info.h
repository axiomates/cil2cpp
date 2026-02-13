/**
 * CIL2CPP Runtime - Runtime Type Information
 */

#pragma once

#include "types.h"

namespace cil2cpp {

// Type flags
enum class TypeFlags : UInt32 {
    None = 0,
    ValueType = 1 << 0,
    Interface = 1 << 1,
    Abstract = 1 << 2,
    Sealed = 1 << 3,
    Enum = 1 << 4,
    Array = 1 << 5,
    Primitive = 1 << 6,
    Generic = 1 << 7,
};

inline TypeFlags operator|(TypeFlags a, TypeFlags b) {
    return static_cast<TypeFlags>(static_cast<UInt32>(a) | static_cast<UInt32>(b));
}

inline bool operator&(TypeFlags a, TypeFlags b) {
    return (static_cast<UInt32>(a) & static_cast<UInt32>(b)) != 0;
}

/**
 * Method information for reflection and virtual dispatch.
 */
struct MethodInfo {
    const char* name;
    TypeInfo* declaring_type;
    TypeInfo* return_type;
    TypeInfo** parameter_types;
    UInt32 parameter_count;
    void* method_pointer;       // Actual function pointer
    UInt32 flags;
    Int32 vtable_slot;          // -1 if not virtual
};

/**
 * Field information for reflection.
 */
struct FieldInfo {
    const char* name;
    TypeInfo* declaring_type;
    TypeInfo* field_type;
    UInt32 offset;              // Offset in object
    UInt32 flags;
};

/**
 * Virtual method table.
 */
struct VTable {
    TypeInfo* type;
    void** methods;             // Array of method pointers
    UInt32 method_count;
};

/**
 * Runtime type information.
 */
struct TypeInfo {
    // Basic info
    const char* name;
    const char* namespace_name;
    const char* full_name;

    // Type hierarchy
    TypeInfo* base_type;
    TypeInfo** interfaces;
    UInt32 interface_count;

    // Size and layout
    UInt32 instance_size;
    UInt32 element_size;        // For arrays: size of element

    // Flags
    TypeFlags flags;

    // Virtual table
    VTable* vtable;

    // Reflection data
    FieldInfo* fields;
    UInt32 field_count;
    MethodInfo* methods;
    UInt32 method_count;

    // Constructor
    void (*default_ctor)(Object*);
    void (*finalizer)(Object*);
};

/**
 * Check if type is assignable from another type.
 */
Boolean type_is_assignable_from(TypeInfo* target, TypeInfo* source);

/**
 * Check if type is a subclass of another type.
 */
Boolean type_is_subclass_of(TypeInfo* type, TypeInfo* base_type);

/**
 * Check if type implements an interface.
 */
Boolean type_implements_interface(TypeInfo* type, TypeInfo* interface_type);

/**
 * Get type by full name (for reflection).
 */
TypeInfo* type_get_by_name(const char* full_name);

/**
 * Register a type with the runtime.
 */
void type_register(TypeInfo* type);

} // namespace cil2cpp
