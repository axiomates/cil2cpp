/**
 * CIL2CPP Runtime - Type System Implementation
 */

#include <cil2cpp/type_info.h>
#include <cil2cpp/object.h>
#include <cil2cpp/array.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>

#include <unordered_map>
#include <string>
#include <cstring>

namespace cil2cpp {

// Type registry
static std::unordered_map<std::string, TypeInfo*> g_type_registry;

// Forward declaration for variance check
static Boolean type_is_variant_assignable(TypeInfo* target, TypeInfo* source);

Boolean type_is_assignable_from(TypeInfo* target, TypeInfo* source) {
    if (!target || !source) {
        return false;
    }

    // Same type
    if (target == source) {
        return true;
    }

    // Check inheritance chain
    if (type_is_subclass_of(source, target)) {
        return true;
    }

    // Check interfaces (exact match)
    if (target->flags & TypeFlags::Interface) {
        if (type_implements_interface(source, target)) {
            return true;
        }
    }

    // Variance-aware check: if both are generic instances of the same open type,
    // check if assignment is valid considering co/contravariance
    if (type_is_variant_assignable(target, source)) {
        return true;
    }

    // Check if source implements a variant-compatible interface
    if ((target->flags & TypeFlags::Interface) && target->generic_definition_name) {
        // Walk source's interfaces and check variant compatibility
        TypeInfo* current = source;
        while (current) {
            for (UInt32 i = 0; i < current->interface_count; i++) {
                if (type_is_variant_assignable(target, current->interfaces[i])) {
                    return true;
                }
            }
            current = current->base_type;
        }
    }

    return false;
}

/// Check if two generic instances of the same open type are variant-compatible.
static Boolean type_is_variant_assignable(TypeInfo* target, TypeInfo* source) {
    if (!target || !source) return false;
    if (target->generic_argument_count == 0 || source->generic_argument_count == 0) return false;
    if (target->generic_argument_count != source->generic_argument_count) return false;
    if (!target->generic_definition_name || !source->generic_definition_name) return false;
    if (std::strcmp(target->generic_definition_name, source->generic_definition_name) != 0) return false;

    for (UInt32 i = 0; i < target->generic_argument_count; i++) {
        auto* t_arg = target->generic_arguments[i];
        auto* s_arg = source->generic_arguments[i];
        if (t_arg == s_arg) continue;

        uint8_t variance = target->generic_variances ? target->generic_variances[i] : 0;
        if (variance == 1) {
            // Covariant (out T): source arg must be assignable TO target arg
            if (!type_is_assignable_from(t_arg, s_arg)) return false;
        } else if (variance == 2) {
            // Contravariant (in T): target arg must be assignable TO source arg
            if (!type_is_assignable_from(s_arg, t_arg)) return false;
        } else {
            // Invariant: must be identical
            return false;
        }
    }
    return true;
}

Boolean type_is_subclass_of(TypeInfo* type, TypeInfo* base_type) {
    if (!type || !base_type) {
        return false;
    }

    TypeInfo* current = type->base_type;
    while (current) {
        if (current == base_type) {
            return true;
        }
        current = current->base_type;
    }

    return false;
}

Boolean type_implements_interface(TypeInfo* type, TypeInfo* interface_type) {
    if (!type || !interface_type) {
        return false;
    }

    // Check this type's interfaces
    for (UInt32 i = 0; i < type->interface_count; i++) {
        if (type->interfaces[i] == interface_type) {
            return true;
        }
    }

    // Check base type's interfaces
    if (type->base_type) {
        return type_implements_interface(type->base_type, interface_type);
    }

    return false;
}

InterfaceVTable* type_get_interface_vtable(TypeInfo* type, TypeInfo* interface_type) {
    TypeInfo* current = type;
    while (current) {
        for (UInt32 i = 0; i < current->interface_vtable_count; i++) {
            if (current->interface_vtables[i].interface_type == interface_type) {
                return &current->interface_vtables[i];
            }
        }
        current = current->base_type;
    }
    return nullptr;
}

InterfaceVTable* type_get_interface_vtable_checked(TypeInfo* type, TypeInfo* interface_type) {
    auto* result = type_get_interface_vtable(type, interface_type);
    if (!result) {
        throw_invalid_cast();
    }
    return result;
}

TypeInfo* type_get_by_name(const char* full_name) {
    auto it = g_type_registry.find(full_name);
    if (it != g_type_registry.end()) {
        return it->second;
    }
    return nullptr;
}

void type_register(TypeInfo* type) {
    if (type && type->full_name) {
        g_type_registry[type->full_name] = type;
    }
}

// Object method implementations
Object* object_alloc(TypeInfo* type) {
    if (!type) {
        return nullptr;
    }
    return static_cast<Object*>(gc::alloc(type->instance_size, type));
}

TypeInfo* object_get_type(Object* obj) {
    return obj ? obj->__type_info : nullptr;
}

Int32 object_get_hash_code(Object* obj) {
    if (!obj) return 0;
    // Default: use object address
    return static_cast<Int32>(reinterpret_cast<IntPtr>(obj));
}

Boolean object_equals(Object* obj, Object* other) {
    // Default: reference equality
    return obj == other;
}

Boolean object_is_instance_of(Object* obj, TypeInfo* type) {
    if (!obj || !type) {
        return false;
    }
    if (type_is_assignable_from(type, obj->__type_info)) {
        return true;
    }

    // Array structural check: alloc_array sets __type_info = element_type,
    // so arrays' __type_info doesn't have System.Array in its hierarchy.
    // Detect arrays structurally (element_type == __type_info) and check
    // if the target type is System.Array or one of its ancestors/interfaces.
    auto* as_arr = reinterpret_cast<Array*>(obj);
    if (as_arr->element_type == obj->__type_info && as_arr->length >= 0) {
        if (type->full_name &&
            std::strcmp(type->full_name, "System.Array") == 0) {
            return true;
        }
        // Also check interfaces that System.Array implements (IList, ICollection, etc.)
        if (type->flags & TypeFlags::Interface) {
            // Walk the target's name to check common array interfaces
            if (type->full_name) {
                const char* name = type->full_name;
                if (std::strcmp(name, "System.Collections.IList") == 0 ||
                    std::strcmp(name, "System.Collections.ICollection") == 0 ||
                    std::strcmp(name, "System.Collections.IEnumerable") == 0 ||
                    std::strcmp(name, "System.Collections.IStructuralComparable") == 0 ||
                    std::strcmp(name, "System.Collections.IStructuralEquatable") == 0 ||
                    std::strcmp(name, "System.ICloneable") == 0) {
                    return true;
                }
            }
        }
    }

    return false;
}

Object* object_as(Object* obj, TypeInfo* type) {
    if (object_is_instance_of(obj, type)) {
        return obj;
    }
    return nullptr;
}

Object* object_cast(Object* obj, TypeInfo* type) {
    if (!obj) return nullptr;  // ECMA-335: castclass on null returns null
    if (object_is_instance_of(obj, type)) {
        return obj;
    }
    throw_invalid_cast();
}

Boolean object_reference_equals(Object* a, Object* b) {
    return a == b;
}

Object* object_memberwise_clone(Object* obj) {
    if (!obj) throw_null_reference();
    auto* type = obj->__type_info;
    auto* clone = static_cast<Object*>(gc::alloc(type->instance_size, type));
    std::memcpy(clone, obj, type->instance_size);
    return clone;
}

// ===== Custom Attribute Queries =====

Boolean type_has_attribute(TypeInfo* type, const char* attr_type_name) {
    return type_get_attribute(type, attr_type_name) != nullptr;
}

CustomAttributeInfo* type_get_attribute(TypeInfo* type, const char* attr_type_name) {
    if (!type || !attr_type_name || type->custom_attribute_count == 0) return nullptr;
    for (UInt32 i = 0; i < type->custom_attribute_count; i++) {
        if (std::strcmp(type->custom_attributes[i].attribute_type_name, attr_type_name) == 0) {
            return &type->custom_attributes[i];
        }
    }
    return nullptr;
}

Boolean method_has_attribute(MethodInfo* method, const char* attr_type_name) {
    if (!method || !attr_type_name || method->custom_attribute_count == 0) return false;
    for (UInt32 i = 0; i < method->custom_attribute_count; i++) {
        if (std::strcmp(method->custom_attributes[i].attribute_type_name, attr_type_name) == 0) {
            return true;
        }
    }
    return false;
}

Boolean field_has_attribute(FieldInfo* field, const char* attr_type_name) {
    if (!field || !attr_type_name || field->custom_attribute_count == 0) return false;
    for (UInt32 i = 0; i < field->custom_attribute_count; i++) {
        if (std::strcmp(field->custom_attributes[i].attribute_type_name, attr_type_name) == 0) {
            return true;
        }
    }
    return false;
}

} // namespace cil2cpp
