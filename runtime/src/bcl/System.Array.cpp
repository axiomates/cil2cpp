/**
 * CIL2CPP Runtime - System.Array Implementation
 */

#include <cil2cpp/array.h>
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

} // namespace cil2cpp
