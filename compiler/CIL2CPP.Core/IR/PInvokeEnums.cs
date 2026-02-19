namespace CIL2CPP.Core.IR;

/// <summary>
/// ECMA-335 II.15.5.2 — P/Invoke character set marshaling.
/// Determines how System.String is marshaled to native code.
/// </summary>
public enum PInvokeCharSet
{
    /// <summary>Marshal String as const char* (UTF-8 / ANSI)</summary>
    Ansi,

    /// <summary>Marshal String as const char16_t* (UTF-16)</summary>
    Unicode,

    /// <summary>Platform-dependent: Unicode on Windows, Ansi on Unix</summary>
    Auto,
}

/// <summary>
/// ECMA-335 II.15.5.1 — P/Invoke calling convention.
/// On x64, only one calling convention exists. On x86, stdcall is used for Win32 APIs.
/// </summary>
public enum PInvokeCallingConvention
{
    /// <summary>C default calling convention (__cdecl)</summary>
    Cdecl,

    /// <summary>Win32 API calling convention (__stdcall, x86 only)</summary>
    StdCall,

    /// <summary>C++ member function calling convention (__thiscall)</summary>
    ThisCall,

    /// <summary>Fast calling convention (__fastcall)</summary>
    FastCall,
}
