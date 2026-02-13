/**
 * CIL2CPP Runtime - Init/Shutdown
 */

#include <cil2cpp/cil2cpp.h>

namespace cil2cpp {

void runtime_init() {
    gc::init();
}

void runtime_shutdown() {
    gc::collect();
    gc::shutdown();
}

} // namespace cil2cpp

// System.Object constructor - no-op for base class
void System_Object__ctor(void* obj) {
    // Base object constructor does nothing
    (void)obj;
}
