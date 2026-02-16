/**
 * CIL2CPP Runtime - Exception Handling Implementation
 */

#include <cil2cpp/exception.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/type_info.h>

#include <cstdio>
#include <cstdlib>

// Platform-specific headers for stack trace capture
#ifdef CIL2CPP_DEBUG
    #if defined(CIL2CPP_WINDOWS)
        #include <windows.h>
        #include <dbghelp.h>
    #elif defined(CIL2CPP_POSIX)
        #include <execinfo.h>
    #endif
    #include <string>
#endif

namespace cil2cpp {

// Thread-local exception context
thread_local ExceptionContext* g_exception_context = nullptr;

// Exception type infos (forward declarations)
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

[[noreturn]] void throw_exception(Exception* ex) {
    if (g_exception_context) {
        g_exception_context->current_exception = ex;
        longjmp(g_exception_context->jump_buffer, 1);
    } else {
        // No exception handler, terminate
        fprintf(stderr, "Unhandled exception: ");
        if (ex && ex->f_message) {
            // TODO: Convert string to UTF-8 and print
            fprintf(stderr, "(exception message)\n");
        } else {
            fprintf(stderr, "(no message)\n");
        }

        // Print stack trace if available
        if (ex && ex->f_stackTraceString) {
            fprintf(stderr, "Stack trace:\n");
            auto trace = string_to_utf8(ex->f_stackTraceString);
            if (trace) {
                fprintf(stderr, "%s", trace);
            }
        }

        std::abort();
    }
}

static Exception* create_exception(TypeInfo* type, const char* message) {
    Exception* ex = static_cast<Exception*>(gc::alloc(sizeof(Exception), type));
    if (message) {
        ex->f_message = string_literal(message);
    }
    ex->f_innerException = nullptr;
    ex->f_stackTraceString = capture_stack_trace();
    return ex;
}

[[noreturn]] void throw_null_reference() {
    Exception* ex = create_exception(&NullReferenceException_TypeInfo,
                                      "Object reference not set to an instance of an object.");
    throw_exception(ex);
}

[[noreturn]] void throw_index_out_of_range() {
    Exception* ex = create_exception(&IndexOutOfRangeException_TypeInfo,
                                      "Index was outside the bounds of the array.");
    throw_exception(ex);
}

[[noreturn]] void throw_invalid_cast() {
    Exception* ex = create_exception(&InvalidCastException_TypeInfo,
                                      "Specified cast is not valid.");
    throw_exception(ex);
}

[[noreturn]] void throw_invalid_operation() {
    Exception* ex = create_exception(&InvalidOperationException_TypeInfo,
                                      "Operation is not valid due to the current state of the object.");
    throw_exception(ex);
}

[[noreturn]] void throw_overflow() {
    Exception* ex = create_exception(&OverflowException_TypeInfo,
                                      "Arithmetic operation resulted in an overflow.");
    throw_exception(ex);
}

[[noreturn]] void throw_argument_null() {
    Exception* ex = create_exception(&ArgumentNullException_TypeInfo,
                                      "Value cannot be null.");
    throw_exception(ex);
}

[[noreturn]] void throw_argument() {
    Exception* ex = create_exception(&ArgumentException_TypeInfo,
                                      "Value does not fall within the expected range.");
    throw_exception(ex);
}

[[noreturn]] void throw_argument_out_of_range() {
    Exception* ex = create_exception(&ArgumentOutOfRangeException_TypeInfo,
                                      "Specified argument was out of the range of valid values.");
    throw_exception(ex);
}

[[noreturn]] void throw_not_supported() {
    Exception* ex = create_exception(&NotSupportedException_TypeInfo,
                                      "Specified method is not supported.");
    throw_exception(ex);
}

[[noreturn]] void throw_not_implemented() {
    Exception* ex = create_exception(&NotImplementedException_TypeInfo,
                                      "The method or operation is not implemented.");
    throw_exception(ex);
}

[[noreturn]] void throw_format() {
    Exception* ex = create_exception(&FormatException_TypeInfo,
                                      "Input string was not in a correct format.");
    throw_exception(ex);
}

[[noreturn]] void throw_divide_by_zero() {
    Exception* ex = create_exception(&DivideByZeroException_TypeInfo,
                                      "Attempted to divide by zero.");
    throw_exception(ex);
}

[[noreturn]] void throw_object_disposed() {
    Exception* ex = create_exception(&ObjectDisposedException_TypeInfo,
                                      "Cannot access a disposed object.");
    throw_exception(ex);
}

[[noreturn]] void throw_key_not_found() {
    Exception* ex = create_exception(&KeyNotFoundException_TypeInfo,
                                      "The given key was not present in the dictionary.");
    throw_exception(ex);
}

[[noreturn]] void throw_timeout() {
    Exception* ex = create_exception(&TimeoutException_TypeInfo,
                                      "The operation has timed out.");
    throw_exception(ex);
}

[[noreturn]] void throw_rank() {
    Exception* ex = create_exception(&RankException_TypeInfo,
                                      "Attempted to operate on an array with the wrong number of dimensions.");
    throw_exception(ex);
}

[[noreturn]] void throw_array_type_mismatch() {
    Exception* ex = create_exception(&ArrayTypeMismatchException_TypeInfo,
                                      "Attempted to access an element as a type incompatible with the array.");
    throw_exception(ex);
}

[[noreturn]] void throw_type_initialization(const char* type_name) {
    char buf[256];
    snprintf(buf, sizeof(buf), "The type initializer for '%s' threw an exception.",
             type_name ? type_name : "<unknown>");
    Exception* ex = create_exception(&TypeInitializationException_TypeInfo, buf);
    throw_exception(ex);
}

[[noreturn]] void throw_operation_canceled() {
    Exception* ex = create_exception(&OperationCanceledException_TypeInfo,
                                      "The operation was canceled.");
    throw_exception(ex);
}

[[noreturn]] void throw_platform_not_supported() {
    Exception* ex = create_exception(&PlatformNotSupportedException_TypeInfo,
                                      "Operation is not supported on this platform.");
    throw_exception(ex);
}

Exception* get_current_exception() {
    return g_exception_context ? g_exception_context->current_exception : nullptr;
}

String* capture_stack_trace() {
#ifndef CIL2CPP_DEBUG
    // In Release mode, skip stack trace capture for performance
    return string_literal("[Stack trace disabled in Release build]");
#else

#if defined(CIL2CPP_WINDOWS)
    // Windows: CaptureStackBackTrace + DbgHelp symbolication
    static bool sym_initialized = false;
    if (!sym_initialized) {
        HANDLE process = GetCurrentProcess();
        SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES);
        SymInitialize(process, NULL, TRUE);
        sym_initialized = true;
    }

    // 64 frames is sufficient for most managed call stacks; deeper recursion
    // will simply have the oldest frames truncated from the trace.
    constexpr int MAX_FRAMES = 64;
    // Skip the 2 internal frames: capture_stack_trace() and create_exception()
    constexpr int FRAMES_TO_SKIP = 2;
    void* frames[MAX_FRAMES];
    USHORT frame_count = CaptureStackBackTrace(
        FRAMES_TO_SKIP,
        MAX_FRAMES,
        frames,
        NULL
    );

    HANDLE process = GetCurrentProcess();
    std::string result;

    // Symbol buffer
    alignas(SYMBOL_INFO) char symbol_buffer[sizeof(SYMBOL_INFO) + MAX_SYM_NAME * sizeof(TCHAR)];
    SYMBOL_INFO* symbol = reinterpret_cast<SYMBOL_INFO*>(symbol_buffer);
    symbol->SizeOfStruct = sizeof(SYMBOL_INFO);
    symbol->MaxNameLen = MAX_SYM_NAME;

    IMAGEHLP_LINE64 line_info;
    line_info.SizeOfStruct = sizeof(IMAGEHLP_LINE64);

    for (USHORT i = 0; i < frame_count; i++) {
        DWORD64 address = reinterpret_cast<DWORD64>(frames[i]);

        result += "   at ";

        if (SymFromAddr(process, address, 0, symbol)) {
            result += symbol->Name;
        } else {
            char addr_buf[32];
            snprintf(addr_buf, sizeof(addr_buf), "0x%llX", (unsigned long long)address);
            result += addr_buf;
        }

        DWORD displacement = 0;
        if (SymGetLineFromAddr64(process, address, &displacement, &line_info)) {
            result += " in ";
            result += line_info.FileName;
            result += ":line ";
            result += std::to_string(line_info.LineNumber);
        }

        result += "\n";
    }

    return string_create_utf8(result.c_str());

#elif defined(CIL2CPP_POSIX)
    // Linux/macOS: backtrace + backtrace_symbols
    // 64 frames is sufficient for most managed call stacks; deeper recursion
    // will simply have the oldest frames truncated from the trace.
    constexpr int MAX_FRAMES = 64;
    // Skip the 2 internal frames: capture_stack_trace() and create_exception()
    constexpr int FRAMES_TO_SKIP = 2;
    void* frames[MAX_FRAMES];
    int frame_count = backtrace(frames, MAX_FRAMES);

    int skip = FRAMES_TO_SKIP;
    if (frame_count <= skip) {
        return string_literal("[No stack frames captured]");
    }

    char** symbols = backtrace_symbols(frames + skip, frame_count - skip);
    if (!symbols) {
        return string_literal("[Failed to resolve stack symbols]");
    }

    std::string result;
    for (int i = 0; i < frame_count - skip; i++) {
        result += "   at ";
        result += symbols[i];
        result += "\n";
    }

    free(symbols);
    return string_create_utf8(result.c_str());

#else
    return string_literal("[Stack trace not supported on this platform]");
#endif

#endif  // CIL2CPP_DEBUG
}

// Exception type infos
TypeInfo Exception_TypeInfo = {
    .name = "Exception",
    .namespace_name = "System",
    .full_name = "System.Exception",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Exception),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TypeInfo NullReferenceException_TypeInfo = {
    .name = "NullReferenceException",
    .namespace_name = "System",
    .full_name = "System.NullReferenceException",
    .base_type = &Exception_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(NullReferenceException),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TypeInfo IndexOutOfRangeException_TypeInfo = {
    .name = "IndexOutOfRangeException",
    .namespace_name = "System",
    .full_name = "System.IndexOutOfRangeException",
    .base_type = &Exception_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(IndexOutOfRangeException),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TypeInfo InvalidCastException_TypeInfo = {
    .name = "InvalidCastException",
    .namespace_name = "System",
    .full_name = "System.InvalidCastException",
    .base_type = &Exception_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(InvalidCastException),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TypeInfo InvalidOperationException_TypeInfo = {
    .name = "InvalidOperationException",
    .namespace_name = "System",
    .full_name = "System.InvalidOperationException",
    .base_type = &Exception_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(InvalidOperationException),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TypeInfo ArgumentException_TypeInfo = {
    .name = "ArgumentException",
    .namespace_name = "System",
    .full_name = "System.ArgumentException",
    .base_type = &Exception_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(ArgumentException),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TypeInfo ArgumentNullException_TypeInfo = {
    .name = "ArgumentNullException",
    .namespace_name = "System",
    .full_name = "System.ArgumentNullException",
    .base_type = &ArgumentException_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(ArgumentNullException),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

TypeInfo OverflowException_TypeInfo = {
    .name = "OverflowException",
    .namespace_name = "System",
    .full_name = "System.OverflowException",
    .base_type = &ArithmeticException_TypeInfo,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(OverflowException),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

// --- New exception TypeInfos ---

#define EXCEPTION_TYPEINFO(CppName, Ns, FullName, BaseName) \
TypeInfo CppName##_TypeInfo = { \
    .name = #CppName, \
    .namespace_name = Ns, \
    .full_name = FullName, \
    .base_type = &BaseName##_TypeInfo, \
    .interfaces = nullptr, \
    .interface_count = 0, \
    .instance_size = sizeof(CppName), \
    .element_size = 0, \
    .flags = TypeFlags::None, \
    .vtable = nullptr, \
    .fields = nullptr, \
    .field_count = 0, \
    .methods = nullptr, \
    .method_count = 0, \
    .default_ctor = nullptr, \
    .finalizer = nullptr, \
    .interface_vtables = nullptr, \
    .interface_vtable_count = 0, \
};

EXCEPTION_TYPEINFO(ArithmeticException,             "System", "System.ArithmeticException",             Exception)
EXCEPTION_TYPEINFO(DivideByZeroException,           "System", "System.DivideByZeroException",           ArithmeticException)
EXCEPTION_TYPEINFO(NotSupportedException,           "System", "System.NotSupportedException",           Exception)
EXCEPTION_TYPEINFO(PlatformNotSupportedException,   "System", "System.PlatformNotSupportedException",   NotSupportedException)
EXCEPTION_TYPEINFO(NotImplementedException,         "System", "System.NotImplementedException",         Exception)
EXCEPTION_TYPEINFO(ObjectDisposedException,         "System", "System.ObjectDisposedException",         InvalidOperationException)
EXCEPTION_TYPEINFO(ArgumentOutOfRangeException,     "System", "System.ArgumentOutOfRangeException",     ArgumentException)
EXCEPTION_TYPEINFO(FormatException,                 "System", "System.FormatException",                 Exception)
EXCEPTION_TYPEINFO(RankException,                   "System", "System.RankException",                   Exception)
EXCEPTION_TYPEINFO(ArrayTypeMismatchException,      "System", "System.ArrayTypeMismatchException",      Exception)
EXCEPTION_TYPEINFO(TypeInitializationException,     "System", "System.TypeInitializationException",     Exception)
EXCEPTION_TYPEINFO(TimeoutException,                "System", "System.TimeoutException",                Exception)
EXCEPTION_TYPEINFO(AggregateException,              "System", "System.AggregateException",              Exception)
EXCEPTION_TYPEINFO(OperationCanceledException,      "System", "System.OperationCanceledException",      Exception)
EXCEPTION_TYPEINFO(TaskCanceledException,           "System.Threading.Tasks", "System.Threading.Tasks.TaskCanceledException", OperationCanceledException)
EXCEPTION_TYPEINFO(KeyNotFoundException,            "System.Collections.Generic", "System.Collections.Generic.KeyNotFoundException", Exception)
EXCEPTION_TYPEINFO(IOException,                    "System.IO", "System.IO.IOException",                    Exception)
EXCEPTION_TYPEINFO(FileNotFoundException,          "System.IO", "System.IO.FileNotFoundException",          IOException)
EXCEPTION_TYPEINFO(DirectoryNotFoundException,     "System.IO", "System.IO.DirectoryNotFoundException",     IOException)

#undef EXCEPTION_TYPEINFO

[[noreturn]] void throw_io_exception(const char* message) {
    Exception* ex = create_exception(&IOException_TypeInfo, message ? message : "I/O error occurred.");
    throw_exception(ex);
}

[[noreturn]] void throw_file_not_found(const char* path) {
    char buf[512];
    snprintf(buf, sizeof(buf), "Could not find file '%s'.", path ? path : "");
    Exception* ex = create_exception(&FileNotFoundException_TypeInfo, buf);
    throw_exception(ex);
}

[[noreturn]] void throw_directory_not_found(const char* path) {
    char buf[512];
    snprintf(buf, sizeof(buf), "Could not find a part of the path '%s'.", path ? path : "");
    Exception* ex = create_exception(&DirectoryNotFoundException_TypeInfo, buf);
    throw_exception(ex);
}

} // namespace cil2cpp
