using System;
using System.Runtime.InteropServices;

/// <summary>
/// Tests P/Invoke [Out]/[In] parameter direction (C.7.2).
/// Validates that blittable byref parameters work correctly through
/// the CIL2CPP P/Invoke wrapper generation.
/// </summary>
public class Program
{
    // --- P/Invoke declarations ---

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll")]
    static extern bool QueryPerformanceCounter(out long value);

    [DllImport("kernel32.dll")]
    static extern bool QueryPerformanceFrequency(out long frequency);

    [DllImport("kernel32.dll")]
    static extern void GetSystemInfo(out SYSTEM_INFO info);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetDiskFreeSpaceExW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_INFO
    {
        public ushort wProcessorArchitecture;
        public ushort wReserved;
        public uint dwPageSize;
        public IntPtr lpMinimumApplicationAddress;
        public IntPtr lpMaximumApplicationAddress;
        public IntPtr dwActiveProcessorMask;
        public uint dwNumberOfProcessors;
        public uint dwProcessorType;
        public uint dwAllocationGranularity;
        public ushort wProcessorLevel;
        public ushort wProcessorRevision;
    }

    public static void Main()
    {
        Console.WriteLine("=== PInvokeTest ===");

        // Test 1: Baseline — no marshaling, just return value
        uint pid = GetCurrentProcessId();
        Console.WriteLine(pid > 0 ? "ProcessId: OK" : "ProcessId: FAIL");

        // Test 2: [Out] blittable int64* — QueryPerformanceCounter
        bool ok1 = QueryPerformanceCounter(out long counter);
        Console.WriteLine(ok1 && counter > 0 ? "PerfCounter: OK" : "PerfCounter: FAIL");

        // Test 3: [Out] blittable int64* — QueryPerformanceFrequency
        bool ok2 = QueryPerformanceFrequency(out long freq);
        Console.WriteLine(ok2 && freq > 0 ? "PerfFrequency: OK" : "PerfFrequency: FAIL");

        // Test 4: [Out] blittable struct* — GetSystemInfo
        GetSystemInfo(out SYSTEM_INFO sysInfo);
        bool validSysInfo = sysInfo.dwPageSize > 0
                         && sysInfo.dwNumberOfProcessors > 0
                         && sysInfo.dwAllocationGranularity > 0;
        Console.WriteLine(validSysInfo ? "SystemInfo: OK" : "SystemInfo: FAIL");

        // Test 5: Multiple [Out] params + SetLastError — GetDiskFreeSpaceExW
        bool ok3 = GetDiskFreeSpaceExW("C:\\",
            out ulong freeBytesAvailable,
            out ulong totalBytes,
            out ulong totalFreeBytes);
        bool validDisk = ok3 && totalBytes > 0 && freeBytesAvailable <= totalBytes;
        Console.WriteLine(validDisk ? "DiskSpace: OK" : "DiskSpace: FAIL");

        Console.WriteLine("=== Done ===");
    }
}
