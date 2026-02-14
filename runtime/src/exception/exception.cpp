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
extern TypeInfo NullReferenceException_TypeInfo;
extern TypeInfo IndexOutOfRangeException_TypeInfo;
extern TypeInfo InvalidCastException_TypeInfo;
extern TypeInfo InvalidOperationException_TypeInfo;
extern TypeInfo OverflowException_TypeInfo;

[[noreturn]] void throw_exception(Exception* ex) {
    if (g_exception_context) {
        g_exception_context->current_exception = ex;
        longjmp(g_exception_context->jump_buffer, 1);
    } else {
        // No exception handler, terminate
        fprintf(stderr, "Unhandled exception: ");
        if (ex && ex->message) {
            // TODO: Convert string to UTF-8 and print
            fprintf(stderr, "(exception message)\n");
        } else {
            fprintf(stderr, "(no message)\n");
        }

        // Print stack trace if available
        if (ex && ex->stack_trace) {
            fprintf(stderr, "Stack trace:\n");
            auto trace = string_to_utf8(ex->stack_trace);
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
        ex->message = string_literal(message);
    }
    ex->inner_exception = nullptr;
    ex->stack_trace = capture_stack_trace();
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
TypeInfo NullReferenceException_TypeInfo = {
    .name = "NullReferenceException",
    .namespace_name = "System",
    .full_name = "System.NullReferenceException",
    .base_type = nullptr,  // TODO: link to Exception type
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
    .base_type = nullptr,
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
    .base_type = nullptr,
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
    .base_type = nullptr,
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

TypeInfo OverflowException_TypeInfo = {
    .name = "OverflowException",
    .namespace_name = "System",
    .full_name = "System.OverflowException",
    .base_type = nullptr,
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

} // namespace cil2cpp
