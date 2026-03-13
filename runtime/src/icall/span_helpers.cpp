/**
 * CIL2CPP Runtime - SpanHelpers ICall implementations
 *
 * DontNegate/Negate are nested generic structs used as type parameters
 * for constrained static abstract calls in SpanHelpers.IndexOfAny etc.
 * The IL bodies are trivial but the generic nesting makes them hard to
 * compile from IL, so we provide ICalls with identical behavior.
 */

#include <cil2cpp/icall.h>

namespace cil2cpp {
namespace icall {

bool SpanHelpers_DontNegate_NegateIfNeeded(bool equals) {
    return equals;  // identity — DontNegate returns value unchanged
}

bool SpanHelpers_Negate_NegateIfNeeded(bool equals) {
    return !equals;  // negate — Negate returns logical negation
}

} // namespace icall
} // namespace cil2cpp
