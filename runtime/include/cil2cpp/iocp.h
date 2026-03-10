/**
 * CIL2CPP Runtime - I/O Completion Port Support
 * Bridges Windows overlapped I/O with the managed ThreadPoolBoundHandle system.
 */

#pragma once

#include "types.h"

namespace cil2cpp {
namespace iocp {

/** Initialize the global IOCP and start the polling thread. */
void init();

/** Shutdown the IOCP polling thread. */
void shutdown();

/**
 * Associate a Win32 HANDLE with the global IOCP.
 * Called from ThreadPool.BindHandlePortableCore ICall.
 * @return true on success, false on failure.
 */
bool bind_handle(void* os_handle);

} // namespace iocp
} // namespace cil2cpp
