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

// ECMA-335 II.23.1.5 — Field attributes
enum class FieldAttributeFlags : UInt32 {
    FieldAccessMask = 0x0007,
    Private         = 0x0001,
    FamANDAssem     = 0x0002,
    Assembly        = 0x0003,
    Family          = 0x0004,
    FamORAssem      = 0x0005,
    Public          = 0x0006,
    Static          = 0x0010,
    InitOnly        = 0x0020,
    Literal         = 0x0040,
    NotSerialized   = 0x0080,
    HasFieldRVA     = 0x0100,
};

inline FieldAttributeFlags operator|(FieldAttributeFlags a, FieldAttributeFlags b) {
    return static_cast<FieldAttributeFlags>(static_cast<UInt32>(a) | static_cast<UInt32>(b));
}

inline bool operator&(FieldAttributeFlags a, FieldAttributeFlags b) {
    return (static_cast<UInt32>(a) & static_cast<UInt32>(b)) != 0;
}

// ECMA-335 II.23.1.10 — Method attributes
enum class MethodAttributeFlags : UInt32 {
    MemberAccessMask = 0x0007,
    Private          = 0x0001,
    FamANDAssem      = 0x0002,
    Assembly         = 0x0003,
    Family           = 0x0004,
    FamORAssem       = 0x0005,
    Public           = 0x0006,
    Static           = 0x0010,
    Final            = 0x0020,
    Virtual          = 0x0040,
    HideBySig        = 0x0080,
    NewSlot          = 0x0100,
    Abstract         = 0x0400,
    SpecialName      = 0x0800,
    RTSpecialName    = 0x1000,
};

inline MethodAttributeFlags operator|(MethodAttributeFlags a, MethodAttributeFlags b) {
    return static_cast<MethodAttributeFlags>(static_cast<UInt32>(a) | static_cast<UInt32>(b));
}

inline bool operator&(MethodAttributeFlags a, MethodAttributeFlags b) {
    return (static_cast<UInt32>(a) & static_cast<UInt32>(b)) != 0;
}

/**
 * Custom attribute constructor argument value.
 */
struct CustomAttributeArg {
    const char* type_name;
    union {
        Int64 int_val;
        double float_val;
        const char* string_val;
    };
    CustomAttributeArg* array_elements;  // non-null for array arguments
    UInt32 array_count;                  // number of elements in array_elements
};

/**
 * Custom attribute metadata.
 */
struct CustomAttributeInfo {
    const char* attribute_type_name;
    CustomAttributeArg* args;
    UInt32 arg_count;
};

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
    CustomAttributeInfo* custom_attributes;
    UInt32 custom_attribute_count;
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
    CustomAttributeInfo* custom_attributes;
    UInt32 custom_attribute_count;
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
 * Interface virtual method table - maps an interface to method pointers.
 */
struct InterfaceVTable {
    TypeInfo* interface_type;
    void** methods;
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

    // Interface dispatch tables
    InterfaceVTable* interface_vtables;
    UInt32 interface_vtable_count;

    // Custom attributes
    CustomAttributeInfo* custom_attributes;
    UInt32 custom_attribute_count;

    // Generic variance data (for variance-aware type assignability)
    // For generic instances: concrete argument TypeInfos + variance flags from open type
    TypeInfo** generic_arguments;        // nullptr for non-generic types
    uint8_t* generic_variances;           // 0=invariant, 1=covariant, 2=contravariant
    UInt32 generic_argument_count;
    const char* generic_definition_name; // Open type's full_name, or nullptr
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
 * Get interface vtable for a type (for interface dispatch).
 */
InterfaceVTable* type_get_interface_vtable(TypeInfo* type, TypeInfo* interface_type);

/**
 * Get interface vtable for a type, throwing InvalidCastException if not found.
 */
InterfaceVTable* type_get_interface_vtable_checked(TypeInfo* type, TypeInfo* interface_type);

/**
 * Get type by full name (for reflection).
 */
TypeInfo* type_get_by_name(const char* full_name);

/**
 * Register a type with the runtime.
 */
void type_register(TypeInfo* type);

/**
 * Check if a type has a specific custom attribute.
 */
Boolean type_has_attribute(TypeInfo* type, const char* attr_type_name);

/**
 * Get a custom attribute from a type (returns nullptr if not found).
 */
CustomAttributeInfo* type_get_attribute(TypeInfo* type, const char* attr_type_name);

/**
 * Check if a method has a specific custom attribute.
 */
Boolean method_has_attribute(MethodInfo* method, const char* attr_type_name);

/**
 * Check if a field has a specific custom attribute.
 */
Boolean field_has_attribute(FieldInfo* field, const char* attr_type_name);

} // namespace cil2cpp
