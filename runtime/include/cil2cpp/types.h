/**
 * CIL2CPP Runtime - Basic Type Definitions
 */

#pragma once

#include <cstdint>
#include <cstddef>

namespace cil2cpp {

// .NET primitive type mappings
using Boolean = bool;
using Byte = uint8_t;
using SByte = int8_t;
using Int16 = int16_t;
using UInt16 = uint16_t;
using Int32 = int32_t;
using UInt32 = uint32_t;
using Int64 = int64_t;
using UInt64 = uint64_t;
using Single = float;
using Double = double;
using Char = char16_t;      // UTF-16 character
using IntPtr = intptr_t;
using UIntPtr = uintptr_t;

// Forward declarations
struct Object;
struct String;
struct Array;
struct TypeInfo;
struct MethodInfo;
struct FieldInfo;

// Null reference
constexpr nullptr_t null = nullptr;

} // namespace cil2cpp
