/**
 * CIL2CPP Runtime - Init/Shutdown
 */

#include <cil2cpp/cil2cpp.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif

namespace cil2cpp {

void runtime_init() {
    gc::init();
    threadpool::init();

#ifdef _WIN32
    // Force UTF-8 console mode. The BCL Console chain selects encoding based on
    // GetConsoleOutputCP(). With codepage 65001 (UTF-8), it uses UTF8Encoding
    // (pure managed IL) instead of OSEncoding (needs WideCharToMultiByte P/Invoke).
    SetConsoleOutputCP(65001);
    SetConsoleCP(65001);
#endif
}

void runtime_shutdown() {
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
