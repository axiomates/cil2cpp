/**
 * CIL2CPP Runtime - System.Array Implementation
 */

#include <cil2cpp/array.h>
#include <cil2cpp/array_interfaces.h>
#include <cil2cpp/mdarray.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/boxing.h>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

namespace cil2cpp {

// Cache of element_type → SZArray TypeInfo. Protected by mutex for thread safety.
static std::mutex g_szarray_cache_mutex;
static std::unordered_map<TypeInfo*, TypeInfo*> g_szarray_cache;

// The generated System.Array TypeInfo — set by __init_runtime_vtables() via
// array_set_system_array_typeinfo(). Used as base_type/vtable source for SZArray TypeInfos.
static TypeInfo* g_system_array_typeinfo = nullptr;

TypeInfo* get_szarray_type_info(TypeInfo* element_type) {
    if (!element_type) return nullptr;

    {
        std::lock_guard<std::mutex> lock(g_szarray_cache_mutex);
        auto it = g_szarray_cache.find(element_type);
        if (it != g_szarray_cache.end()) return it->second;
    }

    // Build "ElementFullName[]" string
    std::string name;
    if (element_type->full_name) {
        name = std::string(element_type->full_name) + "[]";
    } else if (element_type->name) {
        name = std::string(element_type->name) + "[]";
    } else {
        name = "?[]";
    }

    // Build short name
    std::string short_name;
    if (element_type->name) {
        short_name = std::string(element_type->name) + "[]";
    } else {
        short_name = "?[]";
    }

    // Allocate persistent copies of the name strings
    char* full_name_copy = new char[name.size() + 1];
    std::memcpy(full_name_copy, name.c_str(), name.size() + 1);
    char* short_name_copy = new char[short_name.size() + 1];
    std::memcpy(short_name_copy, short_name.c_str(), short_name.size() + 1);

    // Allocate the TypeInfo (not GC-managed — lives for process lifetime).
    auto* ti = new TypeInfo{};
    ti->name = short_name_copy;
    ti->namespace_name = element_type->namespace_name;
    ti->full_name = full_name_copy;
    ti->instance_size = sizeof(Array);
    ti->element_size = element_type->element_size;
    ti->flags = TypeFlags::Array | TypeFlags::Sealed | TypeFlags::Public;
    ti->cor_element_type = cor_element_type::SZARRAY;
    ti->array_rank = 1;
    ti->element_type_info = element_type;

    // T[] inherits from System.Array.
    // Copy base_type, vtable, and interface LIST (for type_implements_interface),
    // but NOT interface_vtables — System.Array's interface vtables have null method
    // entries (CoreRuntimeType). The array adapter (array_get_generic_interface_vtable /
    // array_get_nongeneric_interface_vtable) handles all array interface dispatch.
    if (g_system_array_typeinfo) {
        ti->base_type = g_system_array_typeinfo;
        ti->vtable = g_system_array_typeinfo->vtable;
        ti->interfaces = g_system_array_typeinfo->interfaces;
        ti->interface_count = g_system_array_typeinfo->interface_count;
        // Deliberately NOT copying interface_vtables — forces obj_get_interface_vtable
        // to fall through to the array-specific adapter which provides correct methods.
    }

    std::lock_guard<std::mutex> lock(g_szarray_cache_mutex);
    auto [it, inserted] = g_szarray_cache.emplace(element_type, ti);
    if (!inserted) {
        // Another thread beat us — free our copy and return theirs
        delete[] full_name_copy;
        delete[] short_name_copy;
        delete ti;
    }
    return it->second;
}

void array_set_system_array_typeinfo(TypeInfo* system_array_ti) {
    if (!system_array_ti) return;

    std::lock_guard<std::mutex> lock(g_szarray_cache_mutex);
    g_system_array_typeinfo = system_array_ti;

    // Patch all already-cached SZArray TypeInfos — base_type = System.Array itself
    // Same as get_szarray_type_info: copy interfaces but NOT interface_vtables.
    for (auto& [elem, ti] : g_szarray_cache) {
        ti->base_type = system_array_ti;
        ti->vtable = system_array_ti->vtable;
        ti->interfaces = system_array_ti->interfaces;
        ti->interface_count = system_array_ti->interface_count;
    }
}

Array* array_create(TypeInfo* element_type, Int32 length) {
    if (length < 0) {
        throw_argument_out_of_range();
    }

    return static_cast<Array*>(gc::alloc_array(element_type, static_cast<size_t>(length)));
}

void* array_get_element_ptr(Array* arr, Int32 index) {
    array_bounds_check(arr, index);

    size_t element_size = arr->element_type->element_size;
    if (element_size == 0) {
        element_size = sizeof(void*);  // Reference type
    }

    char* data = static_cast<char*>(array_data(arr));
    return data + (index * element_size);
}

Array* array_get_subarray(Array* source, Int32 start, Int32 length) {
    if (!source) {
        throw_null_reference();
    }
    if (start < 0 || length < 0 || start + length > source->length) {
        throw_index_out_of_range();
    }

    auto result = array_create(source->element_type, length);
    if (length > 0) {
        size_t elem_size = source->element_type->element_size;
        if (elem_size == 0) {
            elem_size = sizeof(void*);  // Reference type
        }
        auto* src_data = static_cast<char*>(array_data(source)) + start * elem_size;
        auto* dst_data = static_cast<char*>(array_data(result));
        std::memcpy(dst_data, src_data, length * elem_size);
    }
    return result;
}

void array_bounds_check(Array* arr, Int32 index) {
    if (!arr) {
        throw_null_reference();
    }

    if (index < 0 || index >= arr->length) {
        throw_index_out_of_range();
    }
}

// ===== ICall functions for System.Array (work with both 1D and multi-dim) =====

Int32 array_get_length(Object* obj) {
    if (!obj) throw_null_reference();
    if (is_mdarray(obj)) {
        return static_cast<MdArray*>(obj)->total_length;
    }
    return static_cast<Array*>(obj)->length;
}

Int32 array_get_rank(Object* obj) {
    if (!obj) throw_null_reference();
    if (is_mdarray(obj)) {
        return static_cast<MdArray*>(obj)->rank;
    }
    return 1;
}

Int32 array_get_length_dim(Object* obj, Int32 dimension) {
    if (!obj) throw_null_reference();
    if (is_mdarray(obj)) {
        return mdarray_get_length(static_cast<MdArray*>(obj), dimension);
    }
    // 1D array: only dimension 0 is valid
    if (dimension != 0) throw_index_out_of_range();
    return static_cast<Array*>(obj)->length;
}

void array_clear(Array* arr, Int32 index, Int32 length) {
    if (!arr) throw_null_reference();
    if (index < 0 || length < 0 || index + length > arr->length) throw_index_out_of_range();
    if (length == 0) return;

    size_t elem_size = arr->element_type->element_size;
    if (elem_size == 0) elem_size = sizeof(void*);

    char* data = static_cast<char*>(array_data(arr)) + index * elem_size;
    std::memset(data, 0, length * elem_size);
}

void array_clear_all(void* raw) {
    auto* arr = static_cast<Array*>(raw);
    if (!arr) throw_null_reference();
    array_clear(arr, 0, arr->length);
}

void array_copy(Array* src, Int32 srcIndex, Array* dst, Int32 dstIndex, Int32 length) {
    if (!src || !dst) throw_null_reference();
    if (srcIndex < 0 || dstIndex < 0 || length < 0 ||
        srcIndex + length > src->length || dstIndex + length > dst->length) throw_index_out_of_range();
    if (length == 0) return;

    size_t elem_size = src->element_type->element_size;
    if (elem_size == 0) elem_size = sizeof(void*);

    char* src_data = static_cast<char*>(array_data(src)) + srcIndex * elem_size;
    char* dst_data = static_cast<char*>(array_data(dst)) + dstIndex * elem_size;
    std::memmove(dst_data, src_data, length * elem_size);
}

void array_copy_simple(void* raw_src, void* raw_dst, Int32 length) {
    auto* src = static_cast<Array*>(raw_src);
    auto* dst = static_cast<Array*>(raw_dst);
    array_copy(src, 0, dst, 0, length);
}

Object* array_clone(void* raw) {
    auto* arr = static_cast<Array*>(raw);
    if (!arr) throw_null_reference();

    auto* result = array_create(arr->element_type, arr->length);
    if (arr->length > 0) {
        size_t elem_size = arr->element_type->element_size;
        if (elem_size == 0) elem_size = sizeof(void*);
        std::memcpy(array_data(result), array_data(arr), arr->length * elem_size);
    }
    return reinterpret_cast<Object*>(result);
}

void array_reverse(void* raw, Int32 index, Int32 length) {
    auto* arr = static_cast<Array*>(raw);
    if (!arr) throw_null_reference();
    if (index < 0 || length < 0 || index + length > arr->length) throw_index_out_of_range();
    if (length <= 1) return;

    size_t elem_size = arr->element_type->element_size;
    if (elem_size == 0) elem_size = sizeof(void*);

    char* data = static_cast<char*>(array_data(arr));
    char* lo = data + index * elem_size;
    char* hi = data + (index + length - 1) * elem_size;

    // Swap elements from both ends toward center
    // Use a small stack buffer for element swap
    char tmp[64]; // handles elements up to 64 bytes
    while (lo < hi) {
        if (elem_size <= sizeof(tmp)) {
            std::memcpy(tmp, lo, elem_size);
            std::memcpy(lo, hi, elem_size);
            std::memcpy(hi, tmp, elem_size);
        } else {
            // Fallback for very large elements: byte-by-byte swap
            for (size_t b = 0; b < elem_size; ++b) {
                char t = lo[b]; lo[b] = hi[b]; hi[b] = t;
            }
        }
        lo += elem_size;
        hi -= elem_size;
    }
}

uintptr_t array_get_native_length(void* raw) {
    auto* arr = static_cast<Array*>(raw);
    if (!arr) throw_null_reference();
    return static_cast<uintptr_t>(arr->length);
}

void* array_get_value(void* raw, Int32 index) {
    auto* arr = static_cast<Array*>(raw);
    if (!arr) throw_null_reference();
    array_bounds_check(arr, index);

    size_t elem_size = arr->element_type->element_size;
    if (elem_size == 0) {
        // Reference type: return the pointer directly
        auto** data = static_cast<void**>(array_data(arr));
        return data[index];
    }
    // Value type: box the element
    char* data = static_cast<char*>(array_data(arr));
    return box_raw(data + index * elem_size, elem_size, arr->element_type);
}

void* array_internal_get_value(void* raw, intptr_t flattenedIndex) {
    // Same as array_get_value but with intptr_t index (used by BCL internal paths)
    return array_get_value(raw, static_cast<Int32>(flattenedIndex));
}

void array_internal_set_value(void* raw, void* value, intptr_t flattenedIndex) {
    auto* arr = static_cast<Array*>(raw);
    if (!arr) throw_null_reference();
    auto index = static_cast<Int32>(flattenedIndex);
    array_bounds_check(arr, index);

    size_t elem_size = arr->element_type->element_size;
    if (elem_size == 0) {
        // Reference type
        auto** data = static_cast<void**>(array_data(arr));
        data[index] = value;
    } else {
        // Value type — unbox the value and copy
        char* data = static_cast<char*>(array_data(arr));
        if (value) {
            char* unboxed = static_cast<char*>(value) + sizeof(Object);
            std::memcpy(data + index * elem_size, unboxed, elem_size);
        } else {
            std::memset(data + index * elem_size, 0, elem_size);
        }
    }
}

void array_copy_impl(void* raw_src, Int32 srcIndex, void* raw_dst, Int32 dstIndex, Int32 length) {
    array_copy(static_cast<Array*>(raw_src), srcIndex,
               static_cast<Array*>(raw_dst), dstIndex, length);
}

Int32 array_get_cor_element_type(void* arr) {
    auto* a = static_cast<Array*>(arr);
    if (a && a->element_type) {
        return static_cast<Int32>(a->element_type->cor_element_type);
    }
    return 0; // ELEMENT_TYPE_END — indicates unknown
}

void array_copy_to(void* raw_this, void* raw_dest, Int32 index) {
    auto* src = static_cast<Array*>(raw_this);
    if (!src) throw_null_reference();
    if (!raw_dest) throw_argument_null();
    array_copy(src, 0, static_cast<Array*>(raw_dest), index, src->length);
}

Boolean array_is_value_of_element_type(void* raw_this, void* raw_value) {
    auto* arr = static_cast<Array*>(raw_this);
    auto* obj = static_cast<Object*>(raw_value);
    if (!arr || !obj) return false;
    return obj->__type_info == arr->element_type;
}

// ===== Array Generic Interface VTable Adapter =====
//
// In .NET, T[] implements IList<T>, ICollection<T>, IEnumerable<T>,
// IReadOnlyList<T>, IReadOnlyCollection<T>. The CLR provides these at runtime.
// In AOT, we synthesize interface vtable entries when dispatch is attempted.

namespace {

// ICollection<T> adapter methods (void* __this is an Array*)
int32_t array_iface_get_count(void* __this) {
    return static_cast<cil2cpp::Array*>(__this)->length;
}

bool array_iface_get_is_readonly(void* /*__this*/) {
    return true;  // Arrays are fixed-size
}

void array_iface_add(void* /*__this*/, void* /*item*/) {
    cil2cpp::throw_not_supported();
}

void array_iface_clear(void* /*__this*/) {
    cil2cpp::throw_not_supported();
}

bool array_iface_contains(void* __this, void* item) {
    auto* arr = static_cast<cil2cpp::Array*>(__this);
    auto** data = static_cast<void**>(cil2cpp::array_data(arr));
    for (int32_t i = 0; i < arr->length; i++) {
        if (data[i] == item) return true;  // Reference equality for objects
    }
    return false;
}

void array_iface_copy_to(void* __this, void* dest_array, int32_t arrayIndex) {
    auto* src = static_cast<cil2cpp::Array*>(__this);
    if (!dest_array) cil2cpp::throw_argument_null();
    cil2cpp::array_copy(src, 0, static_cast<cil2cpp::Array*>(dest_array), arrayIndex, src->length);
}

bool array_iface_remove(void* /*__this*/, void* /*item*/) {
    cil2cpp::throw_not_supported();
}

// IList<T> adapter methods
void* array_iface_get_item(void* __this, int32_t index) {
    auto* arr = static_cast<cil2cpp::Array*>(__this);
    cil2cpp::array_bounds_check(arr, index);
    auto** data = static_cast<void**>(cil2cpp::array_data(arr));
    return data[index];
}

void array_iface_set_item(void* __this, int32_t index, void* value) {
    auto* arr = static_cast<cil2cpp::Array*>(__this);
    cil2cpp::array_bounds_check(arr, index);
    auto** data = static_cast<void**>(cil2cpp::array_data(arr));
    data[index] = value;
}

int32_t array_iface_index_of(void* __this, void* item) {
    auto* arr = static_cast<cil2cpp::Array*>(__this);
    auto** data = static_cast<void**>(cil2cpp::array_data(arr));
    for (int32_t i = 0; i < arr->length; i++) {
        if (data[i] == item) return i;
    }
    return -1;
}

void array_iface_insert(void* /*__this*/, int32_t /*index*/, void* /*item*/) {
    cil2cpp::throw_not_supported();
}

void array_iface_remove_at(void* /*__this*/, int32_t /*index*/) {
    cil2cpp::throw_not_supported();
}

// Non-generic ICollection/IList adapter methods specific to arrays.
// These differ from the generic adapters in return type or semantics.
void* array_iface_get_sync_root(void* __this) {
    return __this;  // Arrays use themselves as sync root (same as CLR)
}

bool array_iface_get_is_synchronized(void* /*__this*/) {
    return false;  // Arrays are not synchronized
}

bool array_iface_get_is_fixed_size(void* /*__this*/) {
    return true;  // Arrays are fixed-size
}

int32_t array_iface_ilist_add(void* /*__this*/, void* /*item*/) {
    cil2cpp::throw_not_supported();
}

void array_iface_ilist_remove(void* /*__this*/, void* /*item*/) {
    cil2cpp::throw_not_supported();
}

// Layout shared by all SZGenericArrayEnumerator<T> specializations.
// All monomorphized versions have identical field layout, only TypeInfo differs.
struct SZGenericArrayEnumeratorLayout {
    cil2cpp::TypeInfo* __type_info;
    cil2cpp::UInt32 __sync_block;
    int32_t f__index;
    int32_t f__endIndex;
    cil2cpp::Array* f__array;
};

// IEnumerable<T>.GetEnumerator — creates SZGenericArrayEnumerator<T> for this array.
void* array_iface_get_enumerator(void* raw_this) {
    auto* arr = static_cast<cil2cpp::Array*>(raw_this);
    if (!arr) cil2cpp::throw_null_reference();

    // Look up SZGenericArrayEnumerator<ElementType> from the type registry.
    const char* elem_name = arr->element_type ? arr->element_type->full_name : nullptr;
    cil2cpp::TypeInfo* enum_type = nullptr;
    if (elem_name) {
        std::string type_name = "System.SZGenericArrayEnumerator`1<";
        type_name += elem_name;
        type_name += ">";
        enum_type = cil2cpp::type_get_by_name(type_name.c_str());
    }
    // Fallback for element types without a specialized enumerator:
    // use SZGenericArrayEnumerator<Object> (works for all reference-type arrays).
    if (!enum_type) {
        enum_type = cil2cpp::type_get_by_name("System.SZGenericArrayEnumerator`1<System.Object>");
    }
    if (!enum_type) {
        cil2cpp::throw_not_supported();
    }

    auto* obj = cil2cpp::object_alloc(enum_type);
    auto* layout = reinterpret_cast<SZGenericArrayEnumeratorLayout*>(obj);
    layout->f__index = -1;
    layout->f__endIndex = arr->length;
    layout->f__array = arr;
    return obj;
}

// Static vtable data for array interface adapters.
// Method ordering must match the interface method order in generated code.

// ICollection<T>: get_Count, get_IsReadOnly, Add, Clear, Contains, CopyTo, Remove
static void* g_array_icollection_methods[] = {
    (void*)&array_iface_get_count,
    (void*)&array_iface_get_is_readonly,
    (void*)&array_iface_add,
    (void*)&array_iface_clear,
    (void*)&array_iface_contains,
    (void*)&array_iface_copy_to,
    (void*)&array_iface_remove,
};

// IList<T>: get_Item, set_Item, IndexOf, Insert, RemoveAt
static void* g_array_ilist_methods[] = {
    (void*)&array_iface_get_item,
    (void*)&array_iface_set_item,
    (void*)&array_iface_index_of,
    (void*)&array_iface_insert,
    (void*)&array_iface_remove_at,
};

// IReadOnlyCollection<T>: get_Count
static void* g_array_ireadonlycollection_methods[] = {
    (void*)&array_iface_get_count,
};

// IReadOnlyList<T>: get_Item
static void* g_array_ireadonlylist_methods[] = {
    (void*)&array_iface_get_item,
};

// IEnumerable<T>: GetEnumerator
static void* g_array_ienumerable_methods[] = {
    (void*)&array_iface_get_enumerator,
};

// Non-generic ICollection: CopyTo, get_Count, get_SyncRoot, get_IsSynchronized
static void* g_array_nongeric_icollection_methods[] = {
    (void*)&array_iface_copy_to,
    (void*)&array_iface_get_count,
    (void*)&array_iface_get_sync_root,
    (void*)&array_iface_get_is_synchronized,
};

// Non-generic IEnumerable: GetEnumerator
static void* g_array_nongeneric_ienumerable_methods[] = {
    (void*)&array_iface_get_enumerator,
};

// ICloneable: Clone (returns shallow copy of the array)
void* array_iface_clone(void* __this) {
    return cil2cpp::array_clone(__this);
}

static void* g_array_icloneable_methods[] = {
    (void*)&array_iface_clone,
};

// Non-generic IList: get_Item, set_Item, Add, Contains, Clear, get_IsReadOnly, get_IsFixedSize,
//                     IndexOf, Insert, Remove, RemoveAt
static void* g_array_nongeneric_ilist_methods[] = {
    (void*)&array_iface_get_item,
    (void*)&array_iface_set_item,
    (void*)&array_iface_ilist_add,
    (void*)&array_iface_contains,
    (void*)&array_iface_clear,
    (void*)&array_iface_get_is_readonly,
    (void*)&array_iface_get_is_fixed_size,
    (void*)&array_iface_index_of,
    (void*)&array_iface_insert,
    (void*)&array_iface_ilist_remove,
    (void*)&array_iface_remove_at,
};

// Cache: one InterfaceVTable per (interface_type) that we've synthesized.
// Thread-safe access: single-threaded initialization is fine since .NET doesn't
// guarantee thread safety for array interface dispatch in practice.
static std::unordered_map<cil2cpp::TypeInfo*, cil2cpp::InterfaceVTable> g_array_iface_cache;
static std::mutex g_array_iface_mutex;

// Check if interface is one of the array-compatible generic collection interfaces.
// Uses full_name prefix matching since Tier-2 interface TypeInfos may not have generic_definition_name.
bool is_array_generic_collection_interface(cil2cpp::TypeInfo* iface_type, void**& methods, uint32_t& count) {
    if (!iface_type || !iface_type->full_name) return false;

    const char* name = iface_type->full_name;
    if (std::strncmp(name, cil2cpp::array_interfaces::kICollection_1_prefix, cil2cpp::array_interfaces::kICollection_1_prefix_len) == 0) {
        methods = g_array_icollection_methods; count = 7; return true;
    }
    if (std::strncmp(name, cil2cpp::array_interfaces::kIList_1_prefix, cil2cpp::array_interfaces::kIList_1_prefix_len) == 0) {
        methods = g_array_ilist_methods; count = 5; return true;
    }
    if (std::strncmp(name, cil2cpp::array_interfaces::kIReadOnlyCollection_1_prefix, cil2cpp::array_interfaces::kIReadOnlyCollection_1_prefix_len) == 0) {
        methods = g_array_ireadonlycollection_methods; count = 1; return true;
    }
    if (std::strncmp(name, cil2cpp::array_interfaces::kIReadOnlyList_1_prefix, cil2cpp::array_interfaces::kIReadOnlyList_1_prefix_len) == 0) {
        methods = g_array_ireadonlylist_methods; count = 1; return true;
    }
    if (std::strncmp(name, cil2cpp::array_interfaces::kIEnumerable_1_prefix, cil2cpp::array_interfaces::kIEnumerable_1_prefix_len) == 0) {
        methods = g_array_ienumerable_methods; count = 1; return true;
    }
    return false;
}

// Check if interface is one of the non-generic interfaces that arrays implement.
bool is_array_nongeneric_collection_interface(cil2cpp::TypeInfo* iface_type, void**& methods, uint32_t& count) {
    if (!iface_type || !iface_type->full_name) return false;

    const char* name = iface_type->full_name;
    if (std::strcmp(name, cil2cpp::array_interfaces::kICollection) == 0) {
        methods = g_array_nongeric_icollection_methods; count = 4; return true;
    }
    if (std::strcmp(name, cil2cpp::array_interfaces::kIEnumerable) == 0) {
        methods = g_array_nongeneric_ienumerable_methods; count = 1; return true;
    }
    if (std::strcmp(name, cil2cpp::array_interfaces::kIList) == 0) {
        methods = g_array_nongeneric_ilist_methods; count = 11; return true;
    }
    if (std::strcmp(name, cil2cpp::array_interfaces::kICloneable) == 0) {
        methods = g_array_icloneable_methods; count = 1; return true;
    }
    return false;
}

} // anonymous namespace

InterfaceVTable* array_get_generic_interface_vtable(TypeInfo* type, TypeInfo* interface_type) {
    // Check if 'type' is actually an array element type (arrays have element_type == __type_info)
    // This function is only called when normal interface vtable lookup fails.
    // For arrays, the __type_info IS the element TypeInfo, and we need to check if the
    // interface matches T[] → ICollection<T>, IList<T>, etc.

    if (!interface_type) return nullptr;

    void** methods = nullptr;
    uint32_t count = 0;
    if (!is_array_generic_collection_interface(interface_type, methods, count))
        return nullptr;

    // Verify the interface's type argument matches the array's element type.
    // The array's __type_info is the element TypeInfo, but here 'type' is the calling
    // object's TypeInfo. We can't verify element type here since we don't have the
    // object pointer — trust that object_is_instance_of already verified compatibility.

    std::lock_guard lock(g_array_iface_mutex);
    auto it = g_array_iface_cache.find(interface_type);
    if (it != g_array_iface_cache.end())
        return &it->second;

    auto& vtable = g_array_iface_cache[interface_type];
    vtable.interface_type = interface_type;
    vtable.methods = methods;
    vtable.method_count = count;
    return &vtable;
}

InterfaceVTable* array_get_nongeneric_interface_vtable(TypeInfo* interface_type) {
    if (!interface_type) return nullptr;

    void** methods = nullptr;
    uint32_t count = 0;
    if (!is_array_nongeneric_collection_interface(interface_type, methods, count))
        return nullptr;

    std::lock_guard lock(g_array_iface_mutex);
    auto it = g_array_iface_cache.find(interface_type);
    if (it != g_array_iface_cache.end())
        return &it->second;

    auto& vtable = g_array_iface_cache[interface_type];
    vtable.interface_type = interface_type;
    vtable.methods = methods;
    vtable.method_count = count;
    return &vtable;
}

} // namespace cil2cpp
