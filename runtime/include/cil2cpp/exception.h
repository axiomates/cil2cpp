/**
 * CIL2CPP Runtime - Exception Handling
 */

#pragma once

#include "object.h"
#include "string.h"
#include <setjmp.h>

namespace cil2cpp {

/**
 * Base exception type.
 * Corresponds to System.Exception.
 */
struct Exception : Object {
    String* message;
    Exception* inner_exception;
    String* stack_trace;
};

/**
 * Null reference exception.
 */
struct NullReferenceException : Exception {};

/**
 * Index out of range exception.
 */
struct IndexOutOfRangeException : Exception {};

/**
 * Invalid cast exception.
 */
struct InvalidCastException : Exception {};

/**
 * Invalid operation exception.
 */
struct InvalidOperationException : Exception {};

/**
 * Argument exception.
 */
struct ArgumentException : Exception {};

/**
 * Argument null exception.
 */
struct ArgumentNullException : ArgumentException {};

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

#define CIL2CPP_RETHROW \
    do { \
        cil2cpp::Exception* __rethrow_ex = cil2cpp::g_exception_context->current_exception; \
        cil2cpp::g_exception_context = cil2cpp::g_exception_context->previous; \
        cil2cpp::throw_exception(__rethrow_ex); \
    } while(0)
