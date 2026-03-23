/**
 * CIL2CPP Runtime - GC-safe STL allocator
 *
 * STL containers (std::unordered_map, std::vector, etc.) use std::allocator
 * which calls new/malloc. BoehmGC does NOT scan new/malloc heap memory for
 * GC pointers. If a GC-allocated object is only referenced from inside an
 * STL container's internal storage, BoehmGC won't see it and may collect it.
 *
 * gc_allocator<T> wraps GC_MALLOC_UNCOLLECTABLE / GC_FREE:
 * - Memory IS scanned by BoehmGC for GC pointers (acts as root)
 * - Memory is NOT automatically collected (must be freed explicitly)
 *
 * Use this allocator for any STL container that stores GC-allocated pointers.
 */

#pragma once

#include <cstddef>
#include <cstring>
#include <new>
#include "gc.h"

namespace cil2cpp {

template<typename T>
struct gc_allocator {
    using value_type = T;

    gc_allocator() noexcept = default;
    template<typename U> gc_allocator(const gc_allocator<U>&) noexcept {}

    T* allocate(std::size_t n) {
        auto* p = static_cast<T*>(gc::alloc_uncollectable(n * sizeof(T)));
        if (!p) throw std::bad_alloc();
        return p;
    }

    void deallocate(T* p, std::size_t) noexcept {
        gc::free_uncollectable(p);
    }

    template<typename U>
    bool operator==(const gc_allocator<U>&) const noexcept { return true; }
    template<typename U>
    bool operator!=(const gc_allocator<U>&) const noexcept { return false; }
};

} // namespace cil2cpp
