/**
 * CIL2CPP Runtime - P/Invoke Interop Implementation
 *
 * ECMA-335 II.15.5 — Platform-specific error tracking for SetLastError.
 */

#include <cil2cpp/interop.h>

#ifdef CIL2CPP_WINDOWS
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <cerrno>
#endif

namespace cil2cpp {

static thread_local Int32 g_last_pinvoke_error = 0;

void set_last_pinvoke_error(Int32 error) {
    g_last_pinvoke_error = error;
}

Int32 get_last_pinvoke_error() {
    return g_last_pinvoke_error;
}

void capture_last_pinvoke_error() {
#ifdef CIL2CPP_WINDOWS
    g_last_pinvoke_error = static_cast<Int32>(GetLastError());
#else
    g_last_pinvoke_error = errno;
#endif
}

} // namespace cil2cpp
