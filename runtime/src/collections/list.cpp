/**
 * CIL2CPP Runtime - List<T> implementation
 * Type-erased backing store using GC-allocated raw memory.
 */

#include <cil2cpp/collections.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cstring>

namespace cil2cpp {

// Check if type is a value type (uses TypeFlags, not element_size)
static bool is_value_type(TypeInfo* type) {
    return (static_cast<uint32_t>(type->flags) &
            static_cast<uint32_t>(TypeFlags::ValueType)) != 0;
}

// Get element size as stored in a collection slot
// Value types: use element_size from TypeInfo
// Reference types: always sizeof(void*) (pointer)
static size_t elem_size(TypeInfo* type) {
    if (!is_value_type(type)) return sizeof(void*);
    return type->element_size > 0 ? type->element_size : sizeof(void*);
}

// Ensure backing buffer has at least `min_capacity` slots
static void ensure_capacity(ListBase* list, Int32 min_capacity) {
    if (list->capacity >= min_capacity) return;

    Int32 new_cap = list->capacity == 0 ? 4 : list->capacity * 2;
    if (new_cap < min_capacity) new_cap = min_capacity;

    size_t es = elem_size(list->elem_type);
    size_t total = static_cast<size_t>(new_cap) * es;
    void* new_buf = gc::alloc(total, nullptr);
    std::memset(new_buf, 0, total);

    if (list->items && list->count > 0) {
        std::memcpy(new_buf, list->items,
                     static_cast<size_t>(list->count) * es);
    }
    list->items = new_buf;
    list->capacity = new_cap;
}

void* list_create(TypeInfo* list_type, TypeInfo* elem_type, Int32 capacity) {
    auto* list = static_cast<ListBase*>(gc::alloc(sizeof(ListBase), list_type));
    if (!list) return nullptr;
    list->items = nullptr;
    list->count = 0;
    list->version = 0;
    list->elem_type = elem_type;
    list->capacity = 0;
    if (capacity > 0) {
        ensure_capacity(list, capacity);
    }
    return list;
}

void list_add(void* raw, const void* element_ptr) {
    auto* list = static_cast<ListBase*>(raw);
    null_check(list);
    ensure_capacity(list, list->count + 1);

    size_t es = elem_size(list->elem_type);
    char* dst = static_cast<char*>(list->items) + static_cast<size_t>(list->count) * es;
    std::memcpy(dst, element_ptr, es);
    list->count++;
    list->version++;
}

void* list_get_ref(void* raw, Int32 index) {
    auto* list = static_cast<ListBase*>(raw);
    null_check(list);
    if (index < 0 || index >= list->count) throw_index_out_of_range();

    size_t es = elem_size(list->elem_type);
    return static_cast<char*>(list->items) + static_cast<size_t>(index) * es;
}

void list_set(void* raw, Int32 index, const void* element_ptr) {
    auto* list = static_cast<ListBase*>(raw);
    null_check(list);
    if (index < 0 || index >= list->count) throw_index_out_of_range();

    size_t es = elem_size(list->elem_type);
    char* dst = static_cast<char*>(list->items) + static_cast<size_t>(index) * es;
    std::memcpy(dst, element_ptr, es);
    list->version++;
}

Int32 list_get_count(void* raw) {
    auto* list = static_cast<ListBase*>(raw);
    return list ? list->count : 0;
}

void list_remove_at(void* raw, Int32 index) {
    auto* list = static_cast<ListBase*>(raw);
    null_check(list);
    if (index < 0 || index >= list->count) throw_index_out_of_range();

    size_t es = elem_size(list->elem_type);
    char* data = static_cast<char*>(list->items);

    // Shift elements left
    if (index < list->count - 1) {
        char* dst = data + static_cast<size_t>(index) * es;
        char* src = dst + es;
        size_t move_bytes = static_cast<size_t>(list->count - index - 1) * es;
        std::memmove(dst, src, move_bytes);
    }
    // Zero the last slot
    char* last = data + static_cast<size_t>(list->count - 1) * es;
    std::memset(last, 0, es);

    list->count--;
    list->version++;
}

void list_clear(void* raw) {
    auto* list = static_cast<ListBase*>(raw);
    if (!list) return;

    if (list->items && list->count > 0) {
        size_t es = elem_size(list->elem_type);
        std::memset(list->items, 0, static_cast<size_t>(list->count) * es);
    }
    list->count = 0;
    list->version++;
}

Boolean list_contains(void* raw, const void* element_ptr) {
    return list_index_of(raw, element_ptr) >= 0;
}

Int32 list_index_of(void* raw, const void* element_ptr) {
    auto* list = static_cast<ListBase*>(raw);
    if (!list || list->count == 0) return -1;

    size_t es = elem_size(list->elem_type);
    char* data = static_cast<char*>(list->items);

    for (Int32 i = 0; i < list->count; i++) {
        const void* slot = data + static_cast<size_t>(i) * es;
        if (element_equals(slot, element_ptr, list->elem_type))
            return i;
    }
    return -1;
}

void list_insert(void* raw, Int32 index, const void* element_ptr) {
    auto* list = static_cast<ListBase*>(raw);
    null_check(list);
    if (index < 0 || index > list->count) throw_index_out_of_range();

    ensure_capacity(list, list->count + 1);

    size_t es = elem_size(list->elem_type);
    char* data = static_cast<char*>(list->items);

    // Shift elements right
    if (index < list->count) {
        char* src = data + static_cast<size_t>(index) * es;
        char* dst = src + es;
        size_t move_bytes = static_cast<size_t>(list->count - index) * es;
        std::memmove(dst, src, move_bytes);
    }

    // Insert element
    char* slot = data + static_cast<size_t>(index) * es;
    std::memcpy(slot, element_ptr, es);
    list->count++;
    list->version++;
}

Boolean list_remove(void* raw, const void* element_ptr) {
    Int32 idx = list_index_of(raw, element_ptr);
    if (idx < 0) return false;
    list_remove_at(raw, idx);
    return true;
}

Int32 list_get_capacity(void* raw) {
    auto* list = static_cast<ListBase*>(raw);
    if (!list) return 0;
    return list->capacity;
}

// ===== Element comparison/hash helpers (shared by List and Dictionary) =====

Boolean element_equals(const void* a, const void* b, TypeInfo* type) {
    if (!is_value_type(type)) {
        // Reference type: compare via vtable Equals if available
        Object* objA = *static_cast<Object* const*>(a);
        Object* objB = *static_cast<Object* const*>(b);
        if (objA == objB) return true;
        if (!objA || !objB) return false;

        // Use vtable Equals if available
        if (objA->__type_info && objA->__type_info->vtable &&
            objA->__type_info->vtable->method_count > object_vtable::kEquals) {
            using EqualsFn = Boolean(*)(Object*, Object*);
            auto fn = reinterpret_cast<EqualsFn>(
                objA->__type_info->vtable->methods[object_vtable::kEquals]);
            return fn(objA, objB);
        }
        // Fallback to reference equality
        return false;
    }

    // Value type: byte comparison
    size_t es = elem_size(type);
    return std::memcmp(a, b, es) == 0;
}

Int32 element_hash(const void* element, TypeInfo* type) {
    if (!is_value_type(type)) {
        // Reference type: vtable GetHashCode if available
        Object* obj = *static_cast<Object* const*>(element);
        if (!obj) return 0;

        if (obj->__type_info && obj->__type_info->vtable &&
            obj->__type_info->vtable->method_count > object_vtable::kGetHashCode) {
            using HashFn = Int32(*)(Object*);
            auto fn = reinterpret_cast<HashFn>(
                obj->__type_info->vtable->methods[object_vtable::kGetHashCode]);
            return fn(obj);
        }
        return object_get_hash_code(obj);
    }

    // Value type: FNV-1a hash on raw bytes
    size_t es = elem_size(type);
    auto* data = static_cast<const uint8_t*>(element);
    uint32_t hash = 2166136261u;
    for (size_t i = 0; i < es; i++) {
        hash ^= data[i];
        hash *= 16777619u;
    }
    return static_cast<Int32>(hash);
}

} // namespace cil2cpp
