/**
 * CIL2CPP Runtime — TypedReference + VarArgs support
 *
 * TypedReference: runtime representation of __makeref / __refvalue / __reftype
 * VarArgs: runtime support for __arglist / ArgIterator
 */

#pragma once

#include "type_info.h"
#include <cstdint>

namespace cil2cpp {

/// TypedReference — ECMA-335 managed reference with type information.
/// Created by mkrefany IL instruction (__makeref in C#).
struct TypedReference {
    void*     value;  // Managed pointer to the value
    TypeInfo* type;   // Type information
};

/// Entry in a varargs argument list.
struct VarArgEntry {
    void*     ptr;    // Pointer to argument value
    TypeInfo* type;   // Argument type info
};

/// Handle to a packed varargs argument list.
/// Constructed at call sites and passed as intptr_t to varargs methods.
struct VarArgHandle {
    VarArgEntry* entries;
    int32_t      count;
};

/// ArgIterator — runtime iterator over varargs.
/// Mirrors System.ArgIterator layout for icall support.
struct ArgIterator {
    VarArgEntry* entries;
    int32_t      count;
    int32_t      index;
};

// ArgIterator icall functions
void argiterator_init(ArgIterator* self, intptr_t handle);
int32_t argiterator_get_remaining_count(ArgIterator* self);
TypedReference argiterator_get_next_arg(ArgIterator* self);
void argiterator_end(ArgIterator* self);

} // namespace cil2cpp

// Type aliases for generated code (matches IL type names)
using System_TypedReference = cil2cpp::TypedReference;
using System_ArgIterator = cil2cpp::ArgIterator;

