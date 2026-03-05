/**
 * CIL2CPP Runtime - Dictionary<K,V> implementation
 * Separate-chaining hash table with packed entries.
 *
 * Entry layout (each entry_stride bytes):
 *   [Int32 hash_code] [Int32 next] [key_bytes...] [value_bytes...]
 *   offset 0           offset 4     offset 8       offset 8+key_size
 *
 * hash_code: -1 = free entry, >=0 = active entry
 * next: index of next entry in same bucket chain, -1 = end
 */

#include <cil2cpp/collections.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cstring>

namespace cil2cpp {

// Prime table for bucket sizes
static const Int32 s_primes[] = {
    3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239,
    293, 353, 431, 521, 631, 761, 919, 1103, 1327, 1597, 1931, 2333,
    2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
    17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431,
    90523, 108631, 130363, 156437, 187751, 225307, 270371, 324449,
};
static constexpr int s_prime_count = sizeof(s_primes) / sizeof(s_primes[0]);

static Int32 get_prime(Int32 min) {
    for (int i = 0; i < s_prime_count; i++) {
        if (s_primes[i] >= min) return s_primes[i];
    }
    // Fallback: next odd >= min
    Int32 candidate = min | 1;
    return candidate;
}

// Check if type is a value type (uses TypeFlags, not element_size)
static bool is_value_type(TypeInfo* type) {
    return (static_cast<uint32_t>(type->flags) &
            static_cast<uint32_t>(TypeFlags::ValueType)) != 0;
}

// Get element size as stored in a collection slot
static size_t type_elem_size(TypeInfo* type) {
    if (!is_value_type(type)) return sizeof(void*);
    return type->element_size > 0 ? type->element_size : sizeof(void*);
}

// Entry access helpers
static inline char* entry_at(DictBase* d, Int32 index) {
    return static_cast<char*>(d->entries) + static_cast<size_t>(index) * d->entry_stride;
}

static inline Int32& entry_hash(char* entry) {
    return *reinterpret_cast<Int32*>(entry);
}

static inline Int32& entry_next(char* entry) {
    return *reinterpret_cast<Int32*>(entry + 4);
}

static inline void* entry_key(char* entry) {
    return entry + 8;
}

static inline void* entry_value(char* entry, Int32 key_size) {
    return entry + 8 + key_size;
}

// Bucket access (int32_t array stored as raw memory)
static inline Int32& bucket_at(DictBase* d, Int32 index) {
    return static_cast<Int32*>(static_cast<void*>(array_data(d->buckets)))[index];
}

// Shared Int32 TypeInfo for bucket arrays
static TypeInfo& get_int32_type() {
    static TypeInfo s_int32_elem = {
        .name = "Int32", .namespace_name = "System", .full_name = "System.Int32",
        .base_type = nullptr, .interfaces = nullptr, .interface_count = 0,
        .instance_size = sizeof(Int32), .element_size = sizeof(Int32),
        .flags = TypeFlags::ValueType | TypeFlags::Primitive,
        .vtable = nullptr, .fields = nullptr, .field_count = 0,
        .methods = nullptr, .method_count = 0,
        .properties = nullptr, .property_count = 0,
        .default_ctor = nullptr, .finalizer = nullptr,
        .interface_vtables = nullptr, .interface_vtable_count = 0,
    };
    return s_int32_elem;
}

// Initialize buckets and entries for given capacity
static void init_storage(DictBase* d, Int32 capacity) {
    Int32 prime = get_prime(capacity);

    // Allocate bucket array (Int32[])
    d->buckets = array_create(&get_int32_type(), prime);
    // Fill buckets with -1
    Int32* bdata = static_cast<Int32*>(array_data(d->buckets));
    for (Int32 i = 0; i < prime; i++) bdata[i] = -1;

    // Allocate entries as raw GC memory
    size_t total = static_cast<size_t>(prime) * d->entry_stride;
    d->entries = gc::alloc(total, nullptr);
    std::memset(d->entries, 0, total);
    // Mark all entries as free
    for (Int32 i = 0; i < prime; i++) {
        entry_hash(entry_at(d, i)) = -1;
    }

    d->capacity = prime;
}

// Resize: rehash all entries into new storage
static void resize(DictBase* d) {
    Int32 new_cap = get_prime(d->capacity * 2);

    // Save old data
    void* old_entries = d->entries;
    Int32 old_count = d->count;

    // Allocate new storage
    d->buckets = array_create(&get_int32_type(), new_cap);
    Int32* bdata = static_cast<Int32*>(array_data(d->buckets));
    for (Int32 i = 0; i < new_cap; i++) bdata[i] = -1;

    size_t total = static_cast<size_t>(new_cap) * d->entry_stride;
    d->entries = gc::alloc(total, nullptr);
    std::memset(d->entries, 0, total);
    for (Int32 i = 0; i < new_cap; i++) {
        entry_hash(entry_at(d, i)) = -1;
    }

    d->capacity = new_cap;
    d->count = 0;
    d->free_list = -1;
    d->free_count = 0;

    // Re-insert active entries from old storage
    for (Int32 i = 0; i < old_count; i++) {
        char* old_e = static_cast<char*>(old_entries) + static_cast<size_t>(i) * d->entry_stride;
        Int32 h = entry_hash(old_e);
        if (h < 0) continue; // skip free entries

        // Allocate new slot
        Int32 new_idx = d->count;
        d->count++;

        char* new_e = entry_at(d, new_idx);
        entry_hash(new_e) = h;
        std::memcpy(entry_key(new_e), entry_key(old_e), d->key_size);
        std::memcpy(entry_value(new_e, d->key_size),
                     entry_value(old_e, d->key_size), d->value_size);

        // Link into bucket chain
        Int32 bucket = h % new_cap;
        entry_next(new_e) = bucket_at(d, bucket);
        bucket_at(d, bucket) = new_idx;
    }
}

// Find entry index for key, or -1 if not found
static Int32 find_entry(DictBase* d, const void* key) {
    if (!d->buckets) return -1;

    Int32 hash = element_hash(key, d->key_type) & 0x7FFFFFFF;
    Int32 bucket = hash % d->buckets->length;
    Int32 i = bucket_at(d, bucket);

    while (i >= 0) {
        char* e = entry_at(d, i);
        if (entry_hash(e) == hash &&
            element_equals(entry_key(e), key, d->key_type)) {
            return i;
        }
        i = entry_next(e);
    }
    return -1;
}

void* dict_create(TypeInfo* dict_type, TypeInfo* key_type, TypeInfo* value_type) {
    auto* d = static_cast<DictBase*>(gc::alloc(sizeof(DictBase), dict_type));
    if (!d) return nullptr;

    d->buckets = nullptr;
    d->entries = nullptr;
    d->count = 0;
    d->capacity = 0;
    d->free_list = -1;
    d->free_count = 0;
    d->key_type = key_type;
    d->value_type = value_type;
    d->key_size = static_cast<Int32>(type_elem_size(key_type));
    d->value_size = static_cast<Int32>(type_elem_size(value_type));
    d->entry_stride = 8 + d->key_size + d->value_size;

    return d;
}

void dict_set(void* raw, const void* key, const void* value) {
    auto* d = static_cast<DictBase*>(raw);
    null_check(d);

    // Lazy init
    if (!d->buckets) init_storage(d, 3);

    Int32 hash = element_hash(key, d->key_type) & 0x7FFFFFFF;

    // Check for existing entry
    Int32 bucket = hash % d->buckets->length;
    Int32 i = bucket_at(d, bucket);
    while (i >= 0) {
        char* e = entry_at(d, i);
        if (entry_hash(e) == hash &&
            element_equals(entry_key(e), key, d->key_type)) {
            // Update existing value
            std::memcpy(entry_value(e, d->key_size), value, d->value_size);
            return;
        }
        i = entry_next(e);
    }

    // Add new entry
    Int32 new_idx;
    if (d->free_count > 0) {
        new_idx = d->free_list;
        char* free_e = entry_at(d, new_idx);
        d->free_list = entry_next(free_e);
        d->free_count--;
    } else {
        if (d->count >= d->capacity) {
            resize(d);
            bucket = hash % d->buckets->length;
        }
        new_idx = d->count;
        d->count++;
    }

    char* e = entry_at(d, new_idx);
    entry_hash(e) = hash;
    entry_next(e) = bucket_at(d, bucket);
    std::memcpy(entry_key(e), key, d->key_size);
    std::memcpy(entry_value(e, d->key_size), value, d->value_size);
    bucket_at(d, bucket) = new_idx;
}

void* dict_get_ref(void* raw, const void* key) {
    auto* d = static_cast<DictBase*>(raw);
    null_check(d);

    Int32 i = find_entry(d, key);
    if (i < 0) {
        // KeyNotFoundException — use InvalidOperationException as closest available
        throw_invalid_operation();
    }
    return entry_value(entry_at(d, i), d->key_size);
}

Boolean dict_try_get_value(void* raw, const void* key, void* value_out) {
    auto* d = static_cast<DictBase*>(raw);
    if (!d) return false;

    Int32 i = find_entry(d, key);
    if (i < 0) {
        // Zero the output
        if (value_out) std::memset(value_out, 0, d->value_size);
        return false;
    }
    if (value_out) {
        std::memcpy(value_out, entry_value(entry_at(d, i), d->key_size), d->value_size);
    }
    return true;
}

Boolean dict_contains_key(void* raw, const void* key) {
    auto* d = static_cast<DictBase*>(raw);
    if (!d) return false;
    return find_entry(d, key) >= 0;
}

Boolean dict_remove(void* raw, const void* key) {
    auto* d = static_cast<DictBase*>(raw);
    if (!d || !d->buckets) return false;

    Int32 hash = element_hash(key, d->key_type) & 0x7FFFFFFF;
    Int32 bucket = hash % d->buckets->length;
    Int32 prev = -1;
    Int32 i = bucket_at(d, bucket);

    while (i >= 0) {
        char* e = entry_at(d, i);
        if (entry_hash(e) == hash &&
            element_equals(entry_key(e), key, d->key_type)) {
            // Unlink from bucket chain
            if (prev < 0) {
                bucket_at(d, bucket) = entry_next(e);
            } else {
                entry_next(entry_at(d, prev)) = entry_next(e);
            }

            // Mark as free
            entry_hash(e) = -1;
            entry_next(e) = d->free_list;
            std::memset(entry_key(e), 0, d->key_size + d->value_size);
            d->free_list = i;
            d->free_count++;
            return true;
        }
        prev = i;
        i = entry_next(e);
    }
    return false;
}

Int32 dict_get_count(void* raw) {
    auto* d = static_cast<DictBase*>(raw);
    if (!d) return 0;
    return d->count - d->free_count;
}

void dict_clear(void* raw) {
    auto* d = static_cast<DictBase*>(raw);
    if (!d) return;

    if (d->buckets) {
        Int32* bdata = static_cast<Int32*>(array_data(d->buckets));
        for (Int32 i = 0; i < d->buckets->length; i++) bdata[i] = -1;
    }
    if (d->entries && d->capacity > 0) {
        std::memset(d->entries, 0, static_cast<size_t>(d->capacity) * d->entry_stride);
        for (Int32 i = 0; i < d->capacity; i++) {
            entry_hash(entry_at(d, i)) = -1;
        }
    }
    d->count = 0;
    d->free_list = -1;
    d->free_count = 0;
}

} // namespace cil2cpp
