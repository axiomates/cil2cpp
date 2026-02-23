/**
 * CIL2CPP Runtime - ThrowHelper ICalls
 *
 * System.ThrowHelper is a BCL internal class that centralizes exception
 * throwing for common validation failures. It uses ExceptionArgument and
 * ExceptionResource enums for messages.
 */

#include <cil2cpp/icall.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/string.h>
#include <cil2cpp/gc.h>

namespace cil2cpp {

// ===== ExceptionArgument enum → name table =====
// Mirrors System.ExceptionArgument from dotnet/runtime ThrowHelper.cs
static const char* const g_argument_names[] = {
    "obj",              // 0
    "dictionary",       // 1
    "array",            // 2
    "info",             // 3
    "key",              // 4
    "text",             // 5
    "values",           // 6
    "value",            // 7
    "startIndex",       // 8
    "task",             // 9
    "bytes",            // 10
    "byteIndex",        // 11
    "byteCount",        // 12
    "ch",               // 13
    "chars",            // 14
    "charIndex",        // 15
    "charCount",        // 16
    "s",                // 17
    "input",            // 18
    "ownedMemory",      // 19
    "list",             // 20
    "index",            // 21
    "capacity",         // 22
    "collection",       // 23
    "item",             // 24
    "converter",        // 25
    "match",            // 26
    "count",            // 27
    "action",           // 28
    "comparison",       // 29
    "exceptions",       // 30
    "exception",        // 31
    "pointer",          // 32
    "start",            // 33
    "format",           // 34
    "culture",          // 35
    "comparer",         // 36
    "comparable",       // 37
    "source",           // 38
    "state",            // 39
    "length",           // 40
    "comparisonType",   // 41
    "manager",          // 42
    "sourceBytesToCopy",// 43
    "callBack",         // 44
    "creationOptions",  // 45
    "function",         // 46
    "scheduler",        // 47
    "continuationAction",// 48
    "continuationFunction",// 49
    "tasks",            // 50
    "asyncResult",      // 51
    "beginMethod",      // 52
    "endMethod",        // 53
    "endFunction",      // 54
    "cancellationToken",// 55
    "continuationOptions",// 56
    "delay",            // 57
    "millisecondsDelay",// 58
    "millisecondsTimeout",// 59
    "stateMachine",     // 60
    "timeout",          // 61
    "type",             // 62
    "sourceIndex",      // 63
    "sourceArray",      // 64
    "destinationIndex", // 65
    "destinationArray", // 66
    "pHandle",          // 67
    "other",            // 68
    "newSize",          // 69
    "lowerBounds",      // 70
    "lengths",          // 71
    "len",              // 72
    "keys",             // 73
    "indices",          // 74
    "endIndex",         // 75
    "elementType",      // 76
    "arrayIndex",       // 77
    "year",             // 78
    "codePoint",        // 79
    "str",              // 80
    "options",          // 81
    "prefix",           // 82
    "suffix",           // 83
    "buffer",           // 84
    "buffers",          // 85
    "offset",           // 86
    "stream",           // 87
    "anyOf",            // 88
    "overlapped",       // 89
    "minimumBytes",     // 90
    "arrayType",        // 91
    "divisor",          // 92
    "factor",           // 93
    "owner",            // 94
    "body",             // 95
    "name",             // 96
    "mode",             // 97
    "encoding",         // 98
    "updateValueFactory",// 99
};
static constexpr int g_argument_names_count =
    static_cast<int>(sizeof(g_argument_names) / sizeof(g_argument_names[0]));

// ===== ExceptionResource enum → message table =====
// Mirrors System.ExceptionResource from dotnet/runtime ThrowHelper.cs
static const char* const g_resource_strings[] = {
    "Index was out of range. Must be non-negative and less than or equal to the size of the collection.", // 0
    "Index was out of range. Must be non-negative and less than the size of the collection.",             // 1
    "Index and count must refer to a location within the string.",                                        // 2
    "Index and count must refer to a location within the buffer.",                                        // 3
    "Count must be positive and count must refer to a valid location in the buffer.",                     // 4
    "Year, Month, and Day parameters describe an un-representable DateTime.",                             // 5
    "Destination array is not long enough.",                                                              // 6
    "The byte array is too small for the specified value.",                                               // 7
    "Collection is read-only.",                                                                          // 8
    "Only single dimensional arrays are supported for the requested action.",                             // 9
    "The lower bound of target array must be zero.",                                                     // 10
    "The output char buffer is too small to contain the decoded characters.",                             // 11
    "Index was out of range. Must be non-negative and less than the size of the collection.",             // 12 (ListInsert)
    "Non-negative number required.",                                                                     // 13
    "Not a valid buffer length.",                                                                        // 14
    "capacity was less than the current size.",                                                           // 15
    "Offset and length were out of bounds for the array or count is greater than the number of elements.",// 16
    "Cannot extract a Unicode scalar value from the specified index in the input.",                       // 17
    "Larger than collection size.",                                                                      // 18
    "The keys for this dictionary are missing.",                                                         // 19
    "The serialization info has a null key.",                                                            // 20
    "Mutating a key collection derived from a dictionary is not allowed.",                                // 21
    "Mutating a value collection derived from a dictionary is not allowed.",                              // 22
    "The given array is null.",                                                                          // 23
    "An attempt was made to transition a task to a final state when it had already completed.",           // 24
    "An attempt was made to set an exception on a TCS with a null exception.",                           // 25
    "An attempt was made to set exceptions on a TCS with an empty exception collection.",                // 26
    "The string comparison type passed in is currently not supported.",                                   // 27
    "ConcurrentCollection_SyncRoot_NotSupported",                                                        // 28
    "The tasks argument included a null value.",                                                         // 29
};
static constexpr int g_resource_strings_count =
    static_cast<int>(sizeof(g_resource_strings) / sizeof(g_resource_strings[0]));

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

cil2cpp::Object* ThrowHelper_GetArgumentException(Int32 resource) {
    auto* ex = static_cast<ArgumentException*>(
        gc::alloc(sizeof(ArgumentException), &ArgumentException_TypeInfo));
    if (resource >= 0 && resource < g_resource_strings_count) {
        ex->f_message = string_create_utf8(g_resource_strings[resource]);
    }
    return reinterpret_cast<Object*>(ex);
}

cil2cpp::Object* ThrowHelper_GetArgumentException2(Int32 resource, Int32 argument) {
    auto* ex = static_cast<ArgumentException*>(
        gc::alloc(sizeof(ArgumentException), &ArgumentException_TypeInfo));
    if (resource >= 0 && resource < g_resource_strings_count) {
        ex->f_message = string_create_utf8(g_resource_strings[resource]);
    }
    if (argument >= 0 && argument < g_argument_names_count) {
        ex->f_paramName = string_create_utf8(g_argument_names[argument]);
    }
    return reinterpret_cast<Object*>(ex);
}

cil2cpp::Object* ThrowHelper_GetArgumentOutOfRangeException(Int32 argument) {
    auto* ex = static_cast<ArgumentOutOfRangeException*>(
        gc::alloc(sizeof(ArgumentOutOfRangeException), &ArgumentOutOfRangeException_TypeInfo));
    if (argument >= 0 && argument < g_argument_names_count) {
        ex->f_paramName = string_create_utf8(g_argument_names[argument]);
    }
    return reinterpret_cast<Object*>(ex);
}

cil2cpp::Object* ThrowHelper_GetInvalidOperationException(Int32 resource) {
    auto* ex = static_cast<InvalidOperationException*>(
        gc::alloc(sizeof(InvalidOperationException), &InvalidOperationException_TypeInfo));
    if (resource >= 0 && resource < g_resource_strings_count) {
        ex->f_message = string_create_utf8(g_resource_strings[resource]);
    }
    return reinterpret_cast<Object*>(ex);
}

cil2cpp::String* ThrowHelper_GetResourceString(Int32 resource) {
    if (resource >= 0 && resource < g_resource_strings_count) {
        return string_create_utf8(g_resource_strings[resource]);
    }
    return string_create_utf8("(unknown resource)");
}

cil2cpp::String* ThrowHelper_GetArgumentName(Int32 argument) {
    if (argument >= 0 && argument < g_argument_names_count) {
        return string_create_utf8(g_argument_names[argument]);
    }
    return string_create_utf8("(unknown argument)");
}

} // namespace icall
} // namespace cil2cpp
