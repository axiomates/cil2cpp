/**
 * CIL2CPP Runtime - Type System Implementation
 */

#include <cil2cpp/type_info.h>
#include <cil2cpp/object.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>

#include <unordered_map>
#include <string>
#include <cstring>

namespace cil2cpp {

// Type registry
static std::unordered_map<std::string, TypeInfo*> g_type_registry;

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

    // Check interfaces
    if (target->flags & TypeFlags::Interface) {
        return type_implements_interface(source, target);
    }

    return false;
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
    return type_is_assignable_from(type, obj->__type_info);
}

Object* object_as(Object* obj, TypeInfo* type) {
    if (object_is_instance_of(obj, type)) {
        return obj;
    }
    return nullptr;
}

Object* object_cast(Object* obj, TypeInfo* type) {
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

} // namespace cil2cpp
