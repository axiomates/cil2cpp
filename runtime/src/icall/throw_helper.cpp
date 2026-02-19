/**
 * CIL2CPP Runtime - ThrowHelper ICalls
 *
 * System.ThrowHelper is a BCL internal class that centralizes exception
 * throwing for common validation failures. It uses ExceptionArgument and
 * ExceptionResource enums for messages. Our AOT runtime maps these to
 * our existing exception types without full resource string support.
 */

#include <cil2cpp/icall.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/string.h>

namespace cil2cpp {
namespace icall {

// ===== Throwing helpers =====

void ThrowHelper_ThrowArgumentOutOfRangeException(Int32 /*argument*/) {
    throw_argument_out_of_range();
}

void ThrowHelper_ThrowArgumentOutOfRangeException2(Int32 /*argument*/, Int32 /*resource*/) {
    throw_argument_out_of_range();
}

void ThrowHelper_ThrowArgumentNullException(Int32 /*argument*/) {
    throw_argument_null();
}

void ThrowHelper_ThrowArgumentNullException2(cil2cpp::String* /*paramName*/) {
    throw_argument_null();
}

void ThrowHelper_ThrowArgumentException(Int32 /*resource*/) {
    throw_argument();
}

void ThrowHelper_ThrowArgumentException2(Int32 /*resource*/, Int32 /*argument*/) {
    throw_argument();
}

void ThrowHelper_ThrowInvalidOperationException(Int32 /*resource*/) {
    throw_invalid_operation();
}

void ThrowHelper_ThrowInvalidOperationException0() {
    throw_invalid_operation();
}

void ThrowHelper_ThrowNotSupportedException(Int32 /*resource*/) {
    throw_not_supported();
}

void ThrowHelper_ThrowNotSupportedException0() {
    throw_not_supported();
}

void ThrowHelper_ThrowFormatInvalidString(Int32 /*offset*/, Int32 /*reason*/) {
    throw_format();
}

void ThrowHelper_ThrowUnexpectedStateForKnownCallback(Int32 /*state*/) {
    throw_invalid_operation();
}

// ===== Exception factory helpers (GetXxx pattern) =====
// The BCL pattern: throw ThrowHelper.GetArgumentException(ExceptionResource.Xxx);
// We return an Object* that can be passed to throw_exception()

cil2cpp::Object* ThrowHelper_GetArgumentException(Int32 /*resource*/) {
    // Return a pre-allocated ArgumentException
    throw_argument();  // FIXME: should return, not throw. But callers immediately throw anyway.
}

cil2cpp::Object* ThrowHelper_GetArgumentException2(Int32 /*resource*/, Int32 /*argument*/) {
    throw_argument();
}

cil2cpp::Object* ThrowHelper_GetArgumentOutOfRangeException(Int32 /*argument*/) {
    throw_argument_out_of_range();
}

cil2cpp::Object* ThrowHelper_GetInvalidOperationException(Int32 /*resource*/) {
    throw_invalid_operation();
}

cil2cpp::String* ThrowHelper_GetResourceString(Int32 /*resource*/) {
    // FIXME: would need full resource string table to return meaningful messages
    return string_create_utf16(u"(resource string)", 17);
}

cil2cpp::String* ThrowHelper_GetArgumentName(Int32 /*argument*/) {
    // FIXME: would need ExceptionArgument enum name table
    return string_create_utf16(u"(argument)", 10);
}

} // namespace icall
} // namespace cil2cpp
