/**
 * CIL2CPP Runtime - System.String
 * TypeInfo extern declaration (used by runtime .cpp files).
 * String methods compile from BCL IL — no inline wrappers needed.
 */

#pragma once

#include "../string.h"
#include "../type_info.h"

namespace cil2cpp {
namespace System {

// System.String type info (defined in System.String.cpp)
extern TypeInfo String_TypeInfo;

} // namespace System
} // namespace cil2cpp
