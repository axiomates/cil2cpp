/**
 * CIL2CPP Runtime - Type System Implementation
 */

#include <cil2cpp/type_info.h>
#include <cil2cpp/object.h>
#include <cil2cpp/array.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>

#include <unordered_map>
#include <mutex>
#include <string>
#include <cstring>
#include <cstdio>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <dbghelp.h>
#pragma comment(lib, "dbghelp.lib")
#endif

namespace cil2cpp {

// Type registry — protected by mutex for thread safety
static std::unordered_map<std::string, TypeInfo*> g_type_registry;
static std::mutex g_type_registry_mutex;

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
    // Pass 1: exact match (fast path — common case)
    TypeInfo* current = type;
    while (current) {
        for (UInt32 i = 0; i < current->interface_vtable_count; i++) {
            if (current->interface_vtables[i].interface_type == interface_type) {
                return &current->interface_vtables[i];
            }
        }
        current = current->base_type;
    }
    // Pass 2: variance-compatible match (ICovariant<string> satisfies ICovariant<object>, etc.)
    if (interface_type->generic_argument_count > 0) {
        current = type;
        while (current) {
            for (UInt32 i = 0; i < current->interface_vtable_count; i++) {
                if (type_is_variant_assignable(interface_type, current->interface_vtables[i].interface_type)) {
                    return &current->interface_vtables[i];
                }
            }
            current = current->base_type;
        }
    }
    // Array generic interface adapter: T[] implements ICollection<T>, IList<T>, etc.
    return array_get_generic_interface_vtable(type, interface_type);
}

InterfaceVTable* type_get_interface_vtable_checked(TypeInfo* type, TypeInfo* interface_type) {
    auto* result = type_get_interface_vtable(type, interface_type);
    if (!result) {
        fprintf(stderr, "[InvalidCast] type_get_interface_vtable_checked FAILED: type='%s' does not implement interface='%s'\n",
            type ? (type->full_name ? type->full_name : "?") : "null",
            interface_type ? (interface_type->full_name ? interface_type->full_name : "?") : "null");
#ifdef _WIN32
        {
            void* stack[32];
            USHORT frames = CaptureStackBackTrace(0, 32, stack, NULL);
            HANDLE process = GetCurrentProcess();
            SymInitialize(process, NULL, TRUE);
            for (USHORT i = 0; i < frames && i < 12; i++) {
                char buf[sizeof(SYMBOL_INFO) + 256];
                SYMBOL_INFO* sym = (SYMBOL_INFO*)buf;
                sym->SizeOfStruct = sizeof(SYMBOL_INFO);
                sym->MaxNameLen = 255;
                if (SymFromAddr(process, (DWORD64)stack[i], NULL, sym))
                    fprintf(stderr, "  [%u] %s\n", i, sym->Name);
            }
            SymCleanup(process);
        }
#endif
        fflush(stderr);
        throw_invalid_cast();
    }
    return result;
}

InterfaceVTable* obj_get_interface_vtable(Object* obj, TypeInfo* interface_type) {
    if (!obj) throw_null_reference();

    // Check if this is an array — detected two ways:
    // 1. Legacy: alloc_array set __type_info = element_type (element_type == __type_info)
    // 2. Proper: get_szarray_type_info creates array TypeInfo with TypeFlags::Array
    // Arrays must be checked FIRST because System.Array (base_type) has interface_vtables
    // with all-null method entries (it's a CoreRuntimeType). If we let type_get_interface_vtable
    // walk the base_type chain first, it finds those broken vtables and returns them,
    // causing null function pointer crashes. The array adapters provide correct implementations.
    auto* as_arr = reinterpret_cast<Array*>(obj);
    bool is_array = (as_arr->element_type == obj->__type_info && as_arr->length >= 0)
                 || (obj->__type_info->flags & TypeFlags::Array);
    if (is_array) {
        // Try array-specific adapters FIRST (generic + non-generic collection interfaces + ICloneable)
        auto* result = array_get_generic_interface_vtable(as_arr->element_type, interface_type);
        if (result) return result;
        result = array_get_nongeneric_interface_vtable(interface_type);
        if (result) return result;
        // Arrays only implement collection interfaces + ICloneable. Don't fall through to
        // type_get_interface_vtable which would walk base_type → System.Array's broken vtables.
    } else {
        // Normal lookup on the object's type (walks base_type chain)
        auto* result = type_get_interface_vtable(obj->__type_info, interface_type);
        if (result) return result;
    }

    // Not found — throw InvalidCastException with diagnostic info
    fprintf(stderr, "[InvalidCast] obj_get_interface_vtable FAILED: type='%s' does not implement interface='%s'\n",
        (obj->__type_info && obj->__type_info->full_name) ? obj->__type_info->full_name : "?",
        (interface_type && interface_type->full_name) ? interface_type->full_name : "?");
#ifdef _WIN32
    {
        void* stack[16];
        USHORT frames = CaptureStackBackTrace(0, 16, stack, NULL);
        HANDLE process = GetCurrentProcess();
        SymInitialize(process, NULL, TRUE);
        for (USHORT i = 0; i < frames && i < 8; i++) {
            char buf[sizeof(SYMBOL_INFO) + 256];
            SYMBOL_INFO* sym = (SYMBOL_INFO*)buf;
            sym->SizeOfStruct = sizeof(SYMBOL_INFO);
            sym->MaxNameLen = 255;
            if (SymFromAddr(process, (DWORD64)stack[i], NULL, sym))
                fprintf(stderr, "  [%u] %s\n", i, sym->Name);
        }
        SymCleanup(process);
    }
#endif
    fflush(stderr);
    throw_invalid_cast();
}

TypeInfo* type_get_by_name(const char* full_name) {
    std::lock_guard<std::mutex> lock(g_type_registry_mutex);
    auto it = g_type_registry.find(full_name);
    if (it != g_type_registry.end()) {
        return it->second;
    }
    return nullptr;
}

void type_register(TypeInfo* type) {
    if (type && type->full_name) {
        std::lock_guard<std::mutex> lock(g_type_registry_mutex);
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

    // Array structural check: detect arrays and check against System.Array
    // and its interfaces. Arrays are detected two ways:
    // 1. Legacy: alloc_array sets __type_info = element_type (element_type == __type_info)
    // 2. Proper: get_szarray_type_info creates array TypeInfo with TypeFlags::Array
    auto* as_arr = reinterpret_cast<Array*>(obj);
    bool is_array = (as_arr->element_type == obj->__type_info && as_arr->length >= 0)
                 || (obj->__type_info->flags & TypeFlags::Array);
    if (is_array) {
        if (type->full_name &&
            std::strcmp(type->full_name, "System.Array") == 0) {
            return true;
        }
        // Also check interfaces that System.Array implements (IList, ICollection, etc.)
        if (type->flags & TypeFlags::Interface) {
            if (type->full_name) {
                const char* name = type->full_name;
                // Non-generic interfaces
                if (std::strcmp(name, "System.Collections.IList") == 0 ||
                    std::strcmp(name, "System.Collections.ICollection") == 0 ||
                    std::strcmp(name, "System.Collections.IEnumerable") == 0 ||
                    std::strcmp(name, "System.Collections.IStructuralComparable") == 0 ||
                    std::strcmp(name, "System.Collections.IStructuralEquatable") == 0 ||
                    std::strcmp(name, "System.ICloneable") == 0) {
                    return true;
                }
            }
            // Generic interfaces: T[] implements IList<T>, ICollection<T>, IEnumerable<T>,
            // IReadOnlyList<T>, IReadOnlyCollection<T>
            // First try structured generic metadata (if available)
            if (type->generic_definition_name && type->generic_argument_count == 1
                && type->generic_arguments && type->generic_arguments[0]) {
                const char* genDef = type->generic_definition_name;
                bool isArrayInterface =
                    std::strcmp(genDef, "System_Collections_Generic_IList_1") == 0 ||
                    std::strcmp(genDef, "System_Collections_Generic_ICollection_1") == 0 ||
                    std::strcmp(genDef, "System_Collections_Generic_IEnumerable_1") == 0 ||
                    std::strcmp(genDef, "System_Collections_Generic_IReadOnlyList_1") == 0 ||
                    std::strcmp(genDef, "System_Collections_Generic_IReadOnlyCollection_1") == 0;
                if (isArrayInterface) {
                    auto* elemType = as_arr->element_type;
                    auto* interfaceArgType = type->generic_arguments[0];
                    if (elemType == interfaceArgType ||
                        type_is_assignable_from(interfaceArgType, elemType)) {
                        return true;
                    }
                }
            }
            // Fallback: use full_name prefix matching for concrete generic interface TypeInfos
            // that lack generic_definition_name (common in AOT-compiled code)
            if (type->full_name) {
                const char* fn = type->full_name;
                static const char* prefixes[] = {
                    "System.Collections.Generic.IList`1<",
                    "System.Collections.Generic.ICollection`1<",
                    "System.Collections.Generic.IEnumerable`1<",
                    "System.Collections.Generic.IReadOnlyList`1<",
                    "System.Collections.Generic.IReadOnlyCollection`1<",
                };
                for (auto* prefix : prefixes) {
                    size_t plen = std::strlen(prefix);
                    if (std::strncmp(fn, prefix, plen) == 0) {
                        // Extract type argument from full_name: "prefix<TypeArg>"
                        // Compare with the array element type's full_name
                        auto* elemType = as_arr->element_type;
                        if (elemType && elemType->full_name) {
                            size_t argLen = std::strlen(fn) - plen - 1; // -1 for trailing '>'
                            if (argLen > 0 && std::strncmp(fn + plen, elemType->full_name, argLen) == 0
                                && std::strlen(elemType->full_name) == argLen) {
                                return true;
                            }
                        }
                        break;
                    }
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
    fprintf(stderr, "[InvalidCast] object_cast FAILED: obj type='%s' cannot cast to '%s'\n",
        (obj && obj->__type_info && obj->__type_info->full_name) ? obj->__type_info->full_name : "?",
        (type && type->full_name) ? type->full_name : "?");
    fflush(stderr);
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
    if (!type || !attr_type_name || type->custom_attribute_count == 0 || !type->custom_attributes) return nullptr;
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
