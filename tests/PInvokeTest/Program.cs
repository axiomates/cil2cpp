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

    // Test 6: LPArray typed pointer — char[] passed as char16_t*
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetTempPathW(
        uint nBufferLength,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] char[] lpBuffer);

    // Test 7: ByValTStr struct layout — RtlGetVersion fills OSVERSIONINFOEXW
    [DllImport("ntdll.dll")]
    static extern int RtlGetVersion(ref OSVERSIONINFOEXW versionInfo);

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

    // ByValTStr struct: OSVERSIONINFOEXW has a char16_t[128] inline field
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OSVERSIONINFOEXW
    {
        public uint dwOSVersionInfoSize;
        public uint dwMajorVersion;
        public uint dwMinorVersion;
        public uint dwBuildNumber;
        public uint dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    // Test 8: LayoutKind.Explicit — union-style struct
    [StructLayout(LayoutKind.Explicit)]
    struct ExplicitUnion
    {
        [FieldOffset(0)] public uint LowPart;
        [FieldOffset(4)] public int HighPart;
        [FieldOffset(0)] public long QuadPart;
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

        // Test 6: LPArray typed pointer — char[] to wchar_t*
        char[] tempBuf = new char[260];
        uint tempLen = GetTempPathW(260, tempBuf);
        bool validTempPath = tempLen > 0 && tempBuf[0] != '\0';
        Console.WriteLine(validTempPath ? "LPArray: OK" : "LPArray: FAIL");

        // Test 7: ByValTStr struct layout — fields after inline char16_t[128] at correct offset
        var osInfo = new OSVERSIONINFOEXW();
        osInfo.dwOSVersionInfoSize = 284; // sizeof(OSVERSIONINFOEXW) = 20 + 256 + 8 = 284
        int ntStatus = RtlGetVersion(ref osInfo);
        bool validOS = ntStatus == 0 && osInfo.dwMajorVersion >= 10 && osInfo.dwBuildNumber > 0;
        Console.WriteLine(validOS ? "ByValTStr: OK" : "ByValTStr: FAIL");

        // Test 8: LayoutKind.Explicit — union overlapping fields at same offset
        var u = new ExplicitUnion();
        u.QuadPart = 0x0000000200000001L; // HighPart=2, LowPart=1
        bool validExplicit = u.LowPart == 1 && u.HighPart == 2;
        Console.WriteLine(validExplicit ? "ExplicitLayout: OK" : "ExplicitLayout: FAIL");

        Console.WriteLine("=== Done ===");
    }
}
