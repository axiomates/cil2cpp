/**
 * CIL2CPP Runtime - P/Invoke Interop Support
 *
 * ECMA-335 II.15.5 — Platform Invoke error tracking and marshaling helpers.
 */

#pragma once

#include <cil2cpp/types.h>

namespace cil2cpp {

/**
 * Set the last P/Invoke error code (TLS).
 * Called by generated P/Invoke wrappers before the native call (to clear)
 * and by capture_last_pinvoke_error() after the native call.
 */
void set_last_pinvoke_error(Int32 error);

/**
 * Get the last P/Invoke error code (TLS).
 * Maps to System.Runtime.InteropServices.Marshal.GetLastPInvokeError().
 */
Int32 get_last_pinvoke_error();

/**
 * Capture the platform-specific error code after a P/Invoke call.
 * On Windows: calls GetLastError(). On POSIX: reads errno.
 * Stores the result in TLS for later retrieval via get_last_pinvoke_error().
 */
void capture_last_pinvoke_error();

} // namespace cil2cpp
