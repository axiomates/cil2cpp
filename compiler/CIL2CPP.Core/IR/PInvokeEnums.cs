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

/// <summary>
/// ECMA-335 II.15.5.4 — [MarshalAs] unmanaged type specifier.
/// Overrides the default marshaling behavior for P/Invoke parameters.
/// Values match System.Runtime.InteropServices.UnmanagedType enum.
/// </summary>
public enum MarshalAsType
{
    /// <summary>Win32 BOOL (4-byte int)</summary>
    Bool = 2,
    /// <summary>Signed 8-bit integer</summary>
    I1 = 3,
    /// <summary>Unsigned 8-bit integer</summary>
    U1 = 4,
    /// <summary>Signed 16-bit integer</summary>
    I2 = 5,
    /// <summary>Unsigned 16-bit integer</summary>
    U2 = 6,
    /// <summary>Signed 32-bit integer</summary>
    I4 = 7,
    /// <summary>Unsigned 32-bit integer</summary>
    U4 = 8,
    /// <summary>Signed 64-bit integer</summary>
    I8 = 9,
    /// <summary>Unsigned 64-bit integer</summary>
    U8 = 10,
    /// <summary>32-bit floating point</summary>
    R4 = 11,
    /// <summary>64-bit floating point</summary>
    R8 = 12,
    /// <summary>ANSI string (char*)</summary>
    LPStr = 20,
    /// <summary>Unicode string (wchar_t* / char16_t*)</summary>
    LPWStr = 21,
    /// <summary>Platform-dependent string</summary>
    LPTStr = 22,
    /// <summary>Fixed-size character array in struct</summary>
    ByValTStr = 23,
    /// <summary>IUnknown interface pointer</summary>
    IUnknown = 25,
    /// <summary>IDispatch interface pointer</summary>
    IDispatch = 26,
    /// <summary>Native platform integer (intptr_t)</summary>
    SysInt = 31,
    /// <summary>Native platform unsigned integer (uintptr_t)</summary>
    SysUInt = 32,
    /// <summary>C function pointer</summary>
    FunctionPtr = 38,
    /// <summary>Pointer to first array element</summary>
    LPArray = 42,
    /// <summary>Pointer to struct</summary>
    LPStruct = 43,
    /// <summary>UTF-8 string (char*)</summary>
    LPUtf8Str = 48,
}

/// <summary>
/// ECMA-335 II.15.5.4 — P/Invoke parameter direction.
/// Controls whether a parameter's value is copied back after the native call.
/// </summary>
public enum PInvokeParameterDirection
{
    /// <summary>Input only (default) — value passed to native, not copied back</summary>
    In,
    /// <summary>Output only — native writes result, copied back to managed</summary>
    Out,
    /// <summary>Bidirectional — value passed to native and copied back</summary>
    InOut,
}
