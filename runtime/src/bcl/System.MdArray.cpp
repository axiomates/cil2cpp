/**
 * CIL2CPP Runtime - Multi-Dimensional Array Implementation
 */

#include <cil2cpp/mdarray.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/type_info.h>
#include <cstring>

namespace cil2cpp {

MdArray* mdarray_create(TypeInfo* element_type, Int32 rank, const Int32* lengths) {
    // Compute total element count (product of all dimensions)
    Int32 total = 1;
    for (Int32 i = 0; i < rank; i++) {
        if (lengths[i] < 0) {
            throw_overflow(); // ArgumentOutOfRangeException
        }
        total *= lengths[i];
    }

    // Compute element size
    size_t elem_size = element_type->element_size;
    if (elem_size == 0) {
        elem_size = sizeof(void*); // Reference type
    }

    // Allocation: header + lengths[rank] + lower_bounds[rank] + data[total * elem_size]
    size_t metadata_size = static_cast<size_t>(rank) * 2 * sizeof(Int32);
    size_t data_size = static_cast<size_t>(total) * elem_size;
    size_t alloc_size = sizeof(MdArray) + metadata_size + data_size;

    auto* arr = static_cast<MdArray*>(gc::alloc(alloc_size, element_type));
    if (!arr) return nullptr;

    // Mark as multi-dimensional array
    arr->__sync_block |= MDARRAY_FLAG;
    arr->element_type = element_type;
    arr->rank = rank;
    arr->total_length = total;

    // Fill lengths
    Int32* lens = mdarray_lengths(arr);
    std::memcpy(lens, lengths, static_cast<size_t>(rank) * sizeof(Int32));

    // Zero-fill lower bounds (C# multi-dim arrays always start at 0)
    Int32* lbs = mdarray_lower_bounds(arr);
    std::memset(lbs, 0, static_cast<size_t>(rank) * sizeof(Int32));

    return arr;
}

void* mdarray_get_element_ptr(MdArray* arr, const Int32* indices) {
    if (!arr) throw_null_reference();

    Int32* lens = mdarray_lengths(arr);

    // Bounds check + compute linear index (row-major)
    Int32 linear = 0;
    for (Int32 d = 0; d < arr->rank; d++) {
        if (indices[d] < 0 || indices[d] >= lens[d]) throw_index_out_of_range();
        linear = linear * lens[d] + indices[d];
    }

    size_t elem_size = arr->element_type->element_size;
    if (elem_size == 0) {
        elem_size = sizeof(void*);
    }

    return static_cast<char*>(mdarray_data(arr)) + linear * elem_size;
}

Int32 mdarray_get_length(MdArray* arr, Int32 dimension) {
    if (!arr) throw_null_reference();
    if (dimension < 0 || dimension >= arr->rank) throw_index_out_of_range();
    return mdarray_lengths(arr)[dimension];
}

} // namespace cil2cpp
