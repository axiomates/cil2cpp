/**
 * CIL2CPP Runtime - I/O Completion Port Support (Windows)
 *
 * Provides a global IOCP and a polling thread that dispatches completions
 * to the managed ThreadPoolBoundHandleOverlapped.CompletionCallback.
 *
 * Flow:
 * 1. BindHandle() associates a Win32 HANDLE with the global IOCP
 * 2. Overlapped I/O operations (ConnectEx, WSARecv, etc.) complete on the IOCP
 * 3. The IOCP thread picks up the completion and calls CompletionCallback
 * 4. CompletionCallback (compiled from IL) dispatches to the user callback
 */

#include <cil2cpp/iocp.h>

#ifdef _WIN32
#include <windows.h>
#include <atomic>
#include <thread>

// Forward declaration for the generated NativeOverlapped struct type
struct System_Threading_NativeOverlapped;

// Forward declaration: compiled from IL, dispatches IOCP completions to managed callbacks.
void System_Threading_ThreadPoolBoundHandleOverlapped_CompletionCallback(
    uint32_t errorCode, uint32_t numBytes, System_Threading_NativeOverlapped* nativeOverlapped);

namespace cil2cpp {
namespace iocp {

static HANDLE g_iocp = nullptr;
static std::atomic<bool> g_running{false};
static std::thread g_iocp_thread;

static void iocp_thread_func() {
    while (g_running.load(std::memory_order_relaxed)) {
        DWORD bytes_transferred = 0;
        ULONG_PTR completion_key = 0;
        LPOVERLAPPED overlapped = nullptr;

        // Wait up to 100ms for a completion (allows periodic shutdown check)
        BOOL result = GetQueuedCompletionStatus(
            g_iocp, &bytes_transferred, &completion_key, &overlapped, 100);

        if (!overlapped) {
            // Timeout or error with no overlapped — just loop
            continue;
        }

        // Determine the error code
        DWORD error_code = 0;
        if (!result) {
            error_code = GetLastError();
        }

        // Dispatch to managed CompletionCallback
        System_Threading_ThreadPoolBoundHandleOverlapped_CompletionCallback(
            error_code, bytes_transferred,
            reinterpret_cast<System_Threading_NativeOverlapped*>(overlapped));
    }
}

void init() {
    if (g_iocp) return; // Already initialized

    // Create a global IOCP with concurrency = number of processors
    g_iocp = CreateIoCompletionPort(INVALID_HANDLE_VALUE, nullptr, 0, 0);
    if (!g_iocp) return;

    g_running.store(true, std::memory_order_relaxed);
    g_iocp_thread = std::thread(iocp_thread_func);
}

void shutdown() {
    g_running.store(false, std::memory_order_relaxed);
    if (g_iocp_thread.joinable()) {
        // Post a dummy completion to wake the thread from GetQueuedCompletionStatus
        PostQueuedCompletionStatus(g_iocp, 0, 0, nullptr);
        g_iocp_thread.join();
    }
    if (g_iocp) {
        CloseHandle(g_iocp);
        g_iocp = nullptr;
    }
}

bool bind_handle(void* os_handle) {
    if (!g_iocp) {
        init(); // Lazy initialization
    }
    if (!g_iocp || !os_handle || os_handle == INVALID_HANDLE_VALUE) {
        return false;
    }

    // Associate the handle with the global IOCP
    HANDLE result = CreateIoCompletionPort(static_cast<HANDLE>(os_handle), g_iocp, 0, 0);
    return result != nullptr;
}

} // namespace iocp
} // namespace cil2cpp

#else
// Non-Windows: IOCP not supported
namespace cil2cpp {
namespace iocp {
void init() {}
void shutdown() {}
bool bind_handle(void*) { return false; }
} // namespace iocp
} // namespace cil2cpp
#endif
