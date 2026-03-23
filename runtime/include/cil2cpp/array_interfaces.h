/**
 * CIL2CPP Runtime - Array Interface Name Constants
 *
 * Canonical IL type names for interfaces that System.Array implements.
 * Shared between type_info.cpp (isinst/castclass) and System.Array.cpp (dispatch).
 * These are ECMA-335 standard interface names — stable across all .NET versions.
 */

#pragma once

#include <cstddef>
#include <string_view>

namespace cil2cpp {
namespace array_interfaces {

// ===== Non-generic interfaces (System.Array always implements these) =====

constexpr const char* kIList                  = "System.Collections.IList";
constexpr const char* kICollection            = "System.Collections.ICollection";
constexpr const char* kIEnumerable            = "System.Collections.IEnumerable";
constexpr const char* kIStructuralComparable  = "System.Collections.IStructuralComparable";
constexpr const char* kIStructuralEquatable   = "System.Collections.IStructuralEquatable";
constexpr const char* kICloneable             = "System.ICloneable";

// ===== Generic interface definitions (T[] implements these for element type T) =====

constexpr const char* kIList_1                = "System.Collections.Generic.IList`1";
constexpr const char* kICollection_1          = "System.Collections.Generic.ICollection`1";
constexpr const char* kIEnumerable_1          = "System.Collections.Generic.IEnumerable`1";
constexpr const char* kIReadOnlyList_1        = "System.Collections.Generic.IReadOnlyList`1";
constexpr const char* kIReadOnlyCollection_1  = "System.Collections.Generic.IReadOnlyCollection`1";

// ===== Generic interface prefixes (for full_name prefix matching on concrete types) =====
// Lengths are compile-time computed from the string literals via string_view.

constexpr std::string_view kICollection_1_prefix_sv         = "System.Collections.Generic.ICollection`1<";
constexpr const char*      kICollection_1_prefix            = kICollection_1_prefix_sv.data();
constexpr size_t           kICollection_1_prefix_len        = kICollection_1_prefix_sv.size();

constexpr std::string_view kIList_1_prefix_sv               = "System.Collections.Generic.IList`1<";
constexpr const char*      kIList_1_prefix                  = kIList_1_prefix_sv.data();
constexpr size_t           kIList_1_prefix_len              = kIList_1_prefix_sv.size();

constexpr std::string_view kIReadOnlyCollection_1_prefix_sv = "System.Collections.Generic.IReadOnlyCollection`1<";
constexpr const char*      kIReadOnlyCollection_1_prefix    = kIReadOnlyCollection_1_prefix_sv.data();
constexpr size_t           kIReadOnlyCollection_1_prefix_len = kIReadOnlyCollection_1_prefix_sv.size();

constexpr std::string_view kIReadOnlyList_1_prefix_sv       = "System.Collections.Generic.IReadOnlyList`1<";
constexpr const char*      kIReadOnlyList_1_prefix          = kIReadOnlyList_1_prefix_sv.data();
constexpr size_t           kIReadOnlyList_1_prefix_len      = kIReadOnlyList_1_prefix_sv.size();

constexpr std::string_view kIEnumerable_1_prefix_sv         = "System.Collections.Generic.IEnumerable`1<";
constexpr const char*      kIEnumerable_1_prefix            = kIEnumerable_1_prefix_sv.data();
constexpr size_t           kIEnumerable_1_prefix_len        = kIEnumerable_1_prefix_sv.size();

} // namespace array_interfaces
} // namespace cil2cpp
