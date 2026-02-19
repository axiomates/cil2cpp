/**
 * CIL2CPP Runtime - Init/Shutdown
 */

#include <cil2cpp/cil2cpp.h>
#include <cil2cpp/gchandle.h>
#include <cil2cpp/unicode.h>
#include <cil2cpp/globalization.h>

#ifdef CIL2CPP_WINDOWS
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

namespace cil2cpp {

// Global storage for command-line arguments
static int g_argc = 0;
static char** g_argv = nullptr;

int runtime_get_argc() { return g_argc; }
char** runtime_get_argv() { return g_argv; }

void runtime_init() {
    gc::init();
    gchandle_init();
    threadpool::init();
    unicode::init();
    globalization::init();

#ifdef CIL2CPP_WINDOWS
    // Force UTF-8 console mode. The BCL Console chain selects encoding based on
    // GetConsoleOutputCP(). With codepage 65001 (UTF-8), it uses UTF8Encoding
    // (pure managed IL) instead of OSEncoding (needs WideCharToMultiByte P/Invoke).
    SetConsoleOutputCP(65001);
    SetConsoleCP(65001);
#endif
}

void runtime_set_args(int argc, char** argv) {
    g_argc = argc;
    g_argv = argv;
}

void runtime_shutdown() {
    globalization::shutdown();
    threadpool::shutdown();
    gc::collect();
    gc::shutdown();
}

} // namespace cil2cpp

// System.Object constructor - no-op for base class
void System_Object__ctor(void* obj) {
    // Base object constructor does nothing
    (void)obj;
}
