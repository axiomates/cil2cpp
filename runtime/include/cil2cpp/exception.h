/**
 * CIL2CPP Runtime - Exception Handling
 */

#pragma once

#include "object.h"
#include "string.h"
#include <setjmp.h>

namespace cil2cpp {

struct Array; // forward declaration for Exception fields

/**
 * Base exception type.
 * Corresponds to System.Exception.
 */
struct Exception : Object {
    // All BCL fields in declaration order (must match System.Exception field layout exactly
    // for compatibility with AOT-compiled IL that inherits from Exception)
    Object* f_exceptionMethod;           // System.Exception._exceptionMethod (MethodBase*)
    String* f_message;                   // System.Exception._message
    Object* f_data;                      // System.Exception._data (IDictionary*)
    Exception* f_innerException;         // System.Exception._innerException
    String* f_helpURL;                   // System.Exception._helpURL
    Array* f_stackTrace;                 // System.Exception._stackTrace (Object[])
    Array* f_watsonBuckets;              // System.Exception._watsonBuckets (byte[])
    String* f_stackTraceString;          // System.Exception._stackTraceString
    String* f_remoteStackTraceString;    // System.Exception._remoteStackTraceString
    Array* f_dynamicMethods;             // System.Exception._dynamicMethods (Object[])
    String* f_source;                    // System.Exception._source
    UIntPtr f_ipForWatsonBuckets;        // System.Exception._ipForWatsonBuckets
    IntPtr f_xptrs;                      // System.Exception._xptrs
    Int32 f_xcode;                       // System.Exception._xcode
    Int32 f_HResult;                     // System.Exception._HResult

    // Convenience aliases for runtime C++ code
    String*& message() { return f_message; }
    Exception*& inner_exception() { return f_innerException; }
    String*& stack_trace() { return f_stackTraceString; }
};

// --- SystemException hierarchy ---
struct NullReferenceException : Exception {};
struct IndexOutOfRangeException : Exception {};
struct InvalidCastException : Exception {};
struct InvalidOperationException : Exception {};
struct ObjectDisposedException : InvalidOperationException {
    String* f_objectName;  // System.ObjectDisposedException._objectName
};
struct NotSupportedException : Exception {};
struct PlatformNotSupportedException : NotSupportedException {};
struct NotImplementedException : Exception {};
struct ArgumentException : Exception {
    String* f_paramName;   // System.ArgumentException._paramName
};
struct ArgumentNullException : ArgumentException {};
struct ArgumentOutOfRangeException : ArgumentException {
    Object* f_actualValue;
};
struct ArithmeticException : Exception {};
struct OverflowException : ArithmeticException {};
struct DivideByZeroException : ArithmeticException {};
struct FormatException : Exception {};
struct RankException : Exception {};
struct ArrayTypeMismatchException : Exception {};
struct TypeInitializationException : Exception {
    String* f_typeName;  // System.TypeInitializationException._typeName
};
struct TimeoutException : Exception {};

// --- Task-related exceptions ---
struct AggregateException : Exception {};
struct OperationCanceledException : Exception {
    void* f_cancellationToken;  // System.OperationCanceledException._cancellationToken (CancellationToken value)
};
struct TaskCanceledException : OperationCanceledException {
    void* f_canceledTask;  // System.Threading.Tasks.TaskCanceledException._canceledTask
};

// --- IO ---
struct IOException : Exception {};
struct FileNotFoundException : IOException {};
struct DirectoryNotFoundException : IOException {};

// --- Collections ---
struct KeyNotFoundException : Exception {};

/**
 * Exception handling context.
 */
struct ExceptionContext {
    jmp_buf jump_buffer;
    ExceptionContext* previous;
    Exception* current_exception;
    int state;  // 0 = try, 1 = catch, 2 = finally
};

/**
 * Thread-local exception context stack.
 */
extern thread_local ExceptionContext* g_exception_context;

/**
 * Throw an exception.
 */
[[noreturn]] void throw_exception(Exception* ex);

/**
 * Create and throw a NullReferenceException.
 */
[[noreturn]] void throw_null_reference();

/**
 * Create and throw an IndexOutOfRangeException.
 */
[[noreturn]] void throw_index_out_of_range();

/**
 * Create and throw an InvalidCastException.
 */
[[noreturn]] void throw_invalid_cast();

/**
 * Create and throw an InvalidOperationException.
 */
[[noreturn]] void throw_invalid_operation();

/**
 * Create and throw an OverflowException.
 */
[[noreturn]] void throw_overflow();

/**
 * Create and throw an ArgumentNullException.
 */
[[noreturn]] void throw_argument_null();

[[noreturn]] void throw_argument();
[[noreturn]] void throw_argument_out_of_range();
[[noreturn]] void throw_not_supported();
[[noreturn]] void throw_not_implemented();
[[noreturn]] void throw_format();
[[noreturn]] void throw_divide_by_zero();
[[noreturn]] void throw_arithmetic();
[[noreturn]] void throw_object_disposed();
[[noreturn]] void throw_key_not_found();
[[noreturn]] void throw_timeout();
[[noreturn]] void throw_rank();
[[noreturn]] void throw_array_type_mismatch();
[[noreturn]] void throw_type_initialization(const char* type_name);
[[noreturn]] void throw_operation_canceled();
[[noreturn]] void throw_platform_not_supported();
[[noreturn]] void throw_io_exception(const char* message);
[[noreturn]] void throw_file_not_found(const char* message);
[[noreturn]] void throw_io_exception(const char* message);
[[noreturn]] void throw_file_not_found(const char* path);
[[noreturn]] void throw_directory_not_found(const char* path);

/**
 * Get current exception (in catch block).
 */
Exception* get_current_exception();

/**
 * Capture current stack trace.
 */
String* capture_stack_trace();

/**
 * Null check - throws NullReferenceException if null.
 */
inline void null_check(void* ptr) {
    if (ptr == nullptr) {
        throw_null_reference();
    }
}

// Exception TypeInfo extern declarations (defined in exception.cpp)
extern TypeInfo Exception_TypeInfo;
extern TypeInfo NullReferenceException_TypeInfo;
extern TypeInfo IndexOutOfRangeException_TypeInfo;
extern TypeInfo InvalidCastException_TypeInfo;
extern TypeInfo InvalidOperationException_TypeInfo;
extern TypeInfo ObjectDisposedException_TypeInfo;
extern TypeInfo NotSupportedException_TypeInfo;
extern TypeInfo PlatformNotSupportedException_TypeInfo;
extern TypeInfo NotImplementedException_TypeInfo;
extern TypeInfo ArgumentException_TypeInfo;
extern TypeInfo ArgumentNullException_TypeInfo;
extern TypeInfo ArgumentOutOfRangeException_TypeInfo;
extern TypeInfo ArithmeticException_TypeInfo;
extern TypeInfo OverflowException_TypeInfo;
extern TypeInfo DivideByZeroException_TypeInfo;
extern TypeInfo FormatException_TypeInfo;
extern TypeInfo RankException_TypeInfo;
extern TypeInfo ArrayTypeMismatchException_TypeInfo;
extern TypeInfo TypeInitializationException_TypeInfo;
extern TypeInfo TimeoutException_TypeInfo;
extern TypeInfo AggregateException_TypeInfo;
extern TypeInfo OperationCanceledException_TypeInfo;
extern TypeInfo TaskCanceledException_TypeInfo;
extern TypeInfo KeyNotFoundException_TypeInfo;
extern TypeInfo IOException_TypeInfo;
extern TypeInfo FileNotFoundException_TypeInfo;
extern TypeInfo DirectoryNotFoundException_TypeInfo;

} // namespace cil2cpp

// Exception handling macros for generated code
#define CIL2CPP_TRY \
    { \
        cil2cpp::ExceptionContext __exc_ctx; \
        bool __exc_caught = false; \
        __exc_ctx.previous = cil2cpp::g_exception_context; \
        __exc_ctx.current_exception = nullptr; \
        __exc_ctx.state = 0; \
        cil2cpp::g_exception_context = &__exc_ctx; \
        if (setjmp(__exc_ctx.jump_buffer) == 0) {

#define CIL2CPP_CATCH_ALL \
        } else { \
            __exc_ctx.state = 1; \
            __exc_caught = true;

#define CIL2CPP_CATCH(ExceptionType) \
        } else if (cil2cpp::object_is_instance_of( \
            reinterpret_cast<cil2cpp::Object*>(__exc_ctx.current_exception), \
            &ExceptionType##_TypeInfo)) { \
            __exc_ctx.state = 1; \
            __exc_caught = true;

#define CIL2CPP_FINALLY \
        } \
        __exc_ctx.state = 2; \
        {

#define CIL2CPP_END_TRY \
        } \
        { \
            cil2cpp::Exception* __pending = __exc_ctx.current_exception; \
            cil2cpp::g_exception_context = __exc_ctx.previous; \
            if (__pending && !__exc_caught) { \
                cil2cpp::throw_exception(__pending); \
            } \
        } \
    }

// Filter begin: like CATCH_ALL but does NOT set __exc_caught.
// The endfilter instruction decides whether to accept (set __exc_caught) or reject (rethrow).
#define CIL2CPP_FILTER_BEGIN \
        } else { \
            __exc_ctx.state = 1;

#define CIL2CPP_RETHROW \
    do { \
        cil2cpp::Exception* __rethrow_ex = cil2cpp::g_exception_context->current_exception; \
        cil2cpp::g_exception_context = cil2cpp::g_exception_context->previous; \
        cil2cpp::throw_exception(__rethrow_ex); \
    } while(0)
