/**
 * CIL2CPP Runtime Library
 * Main header file - includes all runtime components
 */

#pragma once

#include "types.h"
#include "object.h"
#include "string.h"
#include "array.h"
#include "gc.h"
#include "exception.h"
#include "type_info.h"
#include "boxing.h"
#include "delegate.h"
#include "icall.h"
#include "checked.h"

// BCL types
#include "bcl/System.Object.h"
#include "bcl/System.String.h"
#include "bcl/System.Console.h"

namespace cil2cpp {

/**
 * Initialize the CIL2CPP runtime.
 * Must be called before any other runtime functions.
 */
void runtime_init();

/**
 * Shutdown the CIL2CPP runtime.
 * Performs final GC and cleanup.
 */
void runtime_shutdown();

} // namespace cil2cpp

// System.Object constructor (used by generated code)
void System_Object__ctor(void* obj);

// Entry point macro for generated code
#define CIL2CPP_MAIN(EntryClass, EntryMethod) \
    int main(int argc, char* argv[]) { \
        cil2cpp::runtime_init(); \
        EntryMethod(); \
        cil2cpp::runtime_shutdown(); \
        return 0; \
    }
