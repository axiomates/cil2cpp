/**
 * CIL2CPP Runtime - Runtime Type Information
 */

#pragma once

#include "types.h"

namespace cil2cpp {

// ECMA-335 II.23.1.16 — CorElementType constants
namespace cor_element_type {
    constexpr uint8_t VOID       = 0x01;
    constexpr uint8_t BOOLEAN    = 0x02;
    constexpr uint8_t CHAR       = 0x03;
    constexpr uint8_t I1         = 0x04;  // SByte
    constexpr uint8_t U1         = 0x05;  // Byte
    constexpr uint8_t I2         = 0x06;  // Int16
    constexpr uint8_t U2         = 0x07;  // UInt16
    constexpr uint8_t I4         = 0x08;  // Int32
    constexpr uint8_t U4         = 0x09;  // UInt32
    constexpr uint8_t I8         = 0x0A;  // Int64
    constexpr uint8_t U8         = 0x0B;  // UInt64
    constexpr uint8_t R4         = 0x0C;  // Single
    constexpr uint8_t R8         = 0x0D;  // Double
    constexpr uint8_t STRING     = 0x0E;
    constexpr uint8_t PTR        = 0x0F;
    constexpr uint8_t BYREF      = 0x10;
    constexpr uint8_t VALUETYPE  = 0x11;
    constexpr uint8_t CLASS      = 0x12;
    constexpr uint8_t VAR        = 0x13;  // generic type parameter
    constexpr uint8_t ARRAY      = 0x14;  // multi-dimensional array
    constexpr uint8_t GENERICINST = 0x15;
    constexpr uint8_t TYPEDBYREF = 0x16;
    constexpr uint8_t I          = 0x18;  // IntPtr
    constexpr uint8_t U          = 0x19;  // UIntPtr
    constexpr uint8_t FNPTR      = 0x1B;
    constexpr uint8_t OBJECT     = 0x1C;
    constexpr uint8_t SZARRAY    = 0x1D;  // single-dimensional zero-indexed array
    constexpr uint8_t MVAR       = 0x1E;  // generic method parameter
} // namespace cor_element_type

// ECMA-335 II.23.1.7 — System.TypeCode values
// Prefixed with TC_ to avoid name collisions with cil2cpp types (e.g., cil2cpp::String)
namespace type_code {
    constexpr Int32 TC_Empty    = 0;
    constexpr Int32 TC_Object   = 1;
    constexpr Int32 TC_DBNull   = 2;
    constexpr Int32 TC_Boolean  = 3;
    constexpr Int32 TC_Char     = 4;
    constexpr Int32 TC_SByte    = 5;
    constexpr Int32 TC_Byte     = 6;
    constexpr Int32 TC_Int16    = 7;
    constexpr Int32 TC_UInt16   = 8;
    constexpr Int32 TC_Int32    = 9;
    constexpr Int32 TC_UInt32   = 10;
    constexpr Int32 TC_Int64    = 11;
    constexpr Int32 TC_UInt64   = 12;
    constexpr Int32 TC_Single   = 13;
    constexpr Int32 TC_Double   = 14;
    constexpr Int32 TC_Decimal  = 15;
    constexpr Int32 TC_DateTime = 16;
    constexpr Int32 TC_String   = 18;
} // namespace type_code

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
    Public = 1 << 8,
    NestedPublic = 1 << 9,
    IsByRefLike = 1 << 10,
    Nullable = 1 << 11,
    MultiDimensionalArray = 1 << 12,
    NotPublic = 1 << 13,
    NestedAssembly = 1 << 14,
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
 * Property information for reflection.
 */
struct PropertyInfo {
    const char* name;
    TypeInfo* declaring_type;
    TypeInfo* property_type;
    void* getter;               // Function pointer to get method (nullptr if write-only)
    void* setter;               // Function pointer to set method (nullptr if read-only)
    UInt32 flags;               // ECMA-335 PropertyAttributes
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
    PropertyInfo* properties;
    UInt32 property_count;

    // Constructor
    void (*default_ctor)(Object*);
    void (*finalizer)(Object*);

    // Interface dispatch tables
    InterfaceVTable* interface_vtables;
    UInt32 interface_vtable_count;

    // Custom attributes
    CustomAttributeInfo* custom_attributes;
    UInt32 custom_attribute_count;

    // ECMA-335 CorElementType code — packed after UInt32 to minimize padding
    uint8_t cor_element_type;

    // Array rank (0 for non-array, 1 for T[], 2 for T[,], etc.)
    uint8_t array_rank;

    // Enum underlying type (e.g., Int32 for most enums)
    TypeInfo* underlying_type;           // nullptr for non-enum types

    // Enum name/value metadata (for Enum.ToString)
    const char** enum_names;             // nullptr for non-enum types
    Int64* enum_values;                  // enum constant values (widened to Int64)
    UInt32 enum_count;                   // number of enum constants

    // Generic variance data (for variance-aware type assignability)
    // For generic instances: concrete argument TypeInfos + variance flags from open type
    TypeInfo** generic_arguments;        // nullptr for non-generic types
    uint8_t* generic_variances;           // 0=invariant, 1=covariant, 2=contravariant
    UInt32 generic_argument_count;
    const char* generic_definition_name; // Open type's full_name, or nullptr

    // Element type info (for arrays: element TypeInfo, for pointers: pointed-to TypeInfo)
    TypeInfo* element_type_info;         // nullptr for non-array/non-pointer types

    // ECMA-335 metadata token
    UInt32 metadata_token;              // 0 if not available
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
 * Object-aware interface vtable lookup. Handles array generic interface dispatch
 * (T[] implements IList<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>).
 * Falls back to type_get_interface_vtable_checked for non-array objects.
 */
InterfaceVTable* obj_get_interface_vtable(Object* obj, TypeInfo* interface_type);

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
