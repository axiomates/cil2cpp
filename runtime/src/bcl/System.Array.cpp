/**
 * CIL2CPP Runtime - System.Array Implementation
 */

#include <cil2cpp/array.h>
#include <cil2cpp/mdarray.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/type_info.h>
#include <cstring>

namespace cil2cpp {

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

void* array_clone(void* raw) {
    auto* arr = static_cast<Array*>(raw);
    if (!arr) throw_null_reference();

    auto* result = array_create(arr->element_type, arr->length);
    if (arr->length > 0) {
        size_t elem_size = arr->element_type->element_size;
        if (elem_size == 0) elem_size = sizeof(void*);
        std::memcpy(array_data(result), array_data(arr), arr->length * elem_size);
    }
    return result;
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
    // Value type: would need boxing — FIXME: return nullptr for now
    return nullptr;
}

void array_copy_impl(void* raw_src, Int32 srcIndex, void* raw_dst, Int32 dstIndex, Int32 length) {
    array_copy(static_cast<Array*>(raw_src), srcIndex,
               static_cast<Array*>(raw_dst), dstIndex, length);
}

Int32 array_get_cor_element_type(void* /*arr*/) {
    // FIXME: would need TypeInfo to carry CorElementType metadata
    return 0; // ELEMENT_TYPE_END — indicates unknown
}

} // namespace cil2cpp
