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
        // TODO: throw ArgumentOutOfRangeException
        return nullptr;
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
    if (!obj) { throw_null_reference(); return 0; }
    if (is_mdarray(obj)) {
        return static_cast<MdArray*>(obj)->total_length;
    }
    return static_cast<Array*>(obj)->length;
}

Int32 array_get_rank(Object* obj) {
    if (!obj) { throw_null_reference(); return 0; }
    if (is_mdarray(obj)) {
        return static_cast<MdArray*>(obj)->rank;
    }
    return 1;
}

Int32 array_get_length_dim(Object* obj, Int32 dimension) {
    if (!obj) { throw_null_reference(); return 0; }
    if (is_mdarray(obj)) {
        return mdarray_get_length(static_cast<MdArray*>(obj), dimension);
    }
    // 1D array: only dimension 0 is valid
    if (dimension != 0) {
        throw_index_out_of_range();
        return 0;
    }
    return static_cast<Array*>(obj)->length;
}

void array_clear(Array* arr, Int32 index, Int32 length) {
    if (!arr) { throw_null_reference(); return; }
    if (index < 0 || length < 0 || index + length > arr->length) {
        throw_index_out_of_range();
        return;
    }
    if (length == 0) return;

    size_t elem_size = arr->element_type->element_size;
    if (elem_size == 0) elem_size = sizeof(void*);

    char* data = static_cast<char*>(array_data(arr)) + index * elem_size;
    std::memset(data, 0, length * elem_size);
}

void array_copy(Array* src, Int32 srcIndex, Array* dst, Int32 dstIndex, Int32 length) {
    if (!src || !dst) { throw_null_reference(); return; }
    if (srcIndex < 0 || dstIndex < 0 || length < 0 ||
        srcIndex + length > src->length || dstIndex + length > dst->length) {
        throw_index_out_of_range();
        return;
    }
    if (length == 0) return;

    size_t elem_size = src->element_type->element_size;
    if (elem_size == 0) elem_size = sizeof(void*);

    char* src_data = static_cast<char*>(array_data(src)) + srcIndex * elem_size;
    char* dst_data = static_cast<char*>(array_data(dst)) + dstIndex * elem_size;
    std::memmove(dst_data, src_data, length * elem_size);
}

} // namespace cil2cpp
