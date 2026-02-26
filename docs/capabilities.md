# CIL2CPP Capabilities

> Last updated: 2026-02-25
>
> This document describes what CIL2CPP **can currently do**. For development plans and progress, see [roadmap.md](roadmap.md).
>
> [中文版 (Chinese)](capabilities.zh-CN.md)

## Overview

CIL2CPP is a C# → C++ AOT compiler (similar to Unity IL2CPP). Currently supports complete C# syntax (100% IL opcode coverage), BCL compiled from IL (Unity IL2CPP architecture), ~270 ICall entries. 1,240 C# + 591 C++ + 35 integration tests all passing.

## Key Metrics

| Metric | Count |
|--------|-------|
| IL opcode coverage | **100%** (all ~230 ECMA-335 opcodes) |
| ICallRegistry entries | **~270** (covering 30+ categories) |
| C# compiler tests | **~1,240** (xUnit) |
| C++ runtime tests | **591** (Google Test, 18 test files) |
| End-to-end integration tests | **35** (9 stages) |
| Runtime headers | **32** |

---

## C# Feature Support Table

> ✅ Supported ⚠️ Partial support (BCL/runtime limitation) ❌ Not supported (missing icall or AOT limitation)
>
> All C# syntax compiles to standard IL via Roslyn; the CIL instruction translation layer covers all opcodes.
> Status marks ⚠️/❌ reflect limitations at the BCL dependency chain or runtime icall level.

### Basic Types

| Feature | Status | Notes |
|---------|--------|-------|
| int, long, float, double | ✅ | Maps to C++ int32_t, int64_t, float, double |
| bool, byte, sbyte, short, ushort, uint, ulong | ✅ | Complete primitive type mapping |
| char | ✅ | UTF-16 (char16_t) |
| string | ✅ | Immutable, UTF-16 encoding, literal interning pool |
| IntPtr, UIntPtr | ✅ | intptr_t, uintptr_t |
| Type conversions (all Conv_*) | ✅ | 13 basic + 20 checked |
| struct (value types) | ✅ | initobj/ldobj/stobj + boxing/unboxing + copy + ldind/stind |
| enum | ✅ | typedef + constexpr constants + TypeInfo |
| Boxing/Unboxing | ✅ | box\<T\>() / unbox\<T\>(), Nullable box unwrapping |
| Nullable\<T\> | ✅ | BCL IL compiled + box unwrapping + generic monomorphization |
| ValueTuple | ✅ | BCL IL compiled, supports >7 nesting |
| record / record struct | ✅ | Method synthesis (ToString/Equals/GetHashCode/Clone), with expressions |

### Object-Oriented

| Feature | Status | Notes |
|---------|--------|-------|
| Class definition / constructors | ✅ | Instance fields + static fields + methods + newobj |
| Static constructors (.cctor) | ✅ | `_ensure_cctor()` once-guard |
| Inheritance (single) | ✅ | Base class field copying + VTable inheritance |
| Virtual methods / polymorphism | ✅ | VTable dispatch |
| Properties | ✅ | get_/set_ method calls |
| Type casting (is/as) | ✅ | isinst → object_as(), castclass → object_cast() |
| Abstract classes/methods | ✅ | VTable correctly allocates slots |
| Interfaces | ✅ | InterfaceVTable dispatch |
| Generic classes/methods | ✅ | Monomorphization |
| Operator overloading | ✅ | op_Addition etc. static method calls |
| Finalizers / destructors | ✅ | TypeInfo.finalizer + BoehmGC registration |
| Default interface methods (DIM) | ✅ | Interface default implementations as VTable fallback |
| Generic covariance/contravariance | ✅ | ECMA-335 variance-aware checking |

### Control Flow

| Feature | Status | Notes |
|---------|--------|-------|
| if/else, while/for, do-while | ✅ | All conditional branch instructions |
| switch (IL switch table) | ✅ | C++ switch/goto jump table |
| Pattern matching (switch expressions) | ✅ | Roslyn compiles to standard IL |
| Range / Index (..) | ✅ | Index/Range structs |
| Checked arithmetic | ✅ | OverflowException throwing |

### Arrays

| Feature | Status | Notes |
|---------|--------|-------|
| Single-dimensional arrays | ✅ | newarr + ldelem/stelem all types + bounds checking |
| Array initializers | ✅ | RuntimeHelpers.InitializeArray → memcpy |
| Multi-dimensional arrays (T[,]) | ✅ | MdArray runtime |
| Span\<T\> / ReadOnlySpan\<T\> | ✅ | BCL IL compiled, ref struct |

### Exception Handling

| Feature | Status | Notes |
|---------|--------|-------|
| throw / try / catch / finally | ✅ | setjmp/longjmp macros |
| rethrow | ✅ | CIL2CPP_RETHROW |
| Exception filters (catch when) | ✅ | ECMA-335 Filter handler |
| Nested try/catch/finally | ✅ | Full multi-level nesting support |
| Custom exception types | ✅ | Inheriting Exception |
| Stack traces | ✅ | Windows: DbgHelp, POSIX: backtrace (Debug only) |
| using statements | ✅ | try/finally + IDisposable interface dispatch |

### Standard Library (BCL)

| Feature | Status | Notes |
|---------|--------|-------|
| System.Object | ✅ | ToString/GetHashCode/Equals/GetType |
| System.String | ✅ | Layout icall + BCL IL compiled (Concat/Format/Join/Split etc.) |
| Console.WriteLine/Write/ReadLine | ✅ | Full BCL IL chain compiled |
| System.Math / MathF | ✅ | ~40 icalls |
| List\<T\> / Dictionary\<K,V\> | ✅ | BCL IL compiled |
| LINQ | ✅ | Where/Select/OrderBy etc., BCL IL compiled |
| yield return / IEnumerable | ✅ | Iterator state machines |
| IAsyncEnumerable\<T\> | ✅ | await foreach |
| System.IO (File/Path/Directory) | ✅ | 22 ICalls, C++17 filesystem |
| System.Net | ❌ | Low-level icall not implemented |

### Delegates & Events

| Feature | Status | Notes |
|---------|--------|-------|
| Delegates / multicast delegates | ✅ | delegate_create / Combine / Remove |
| Events | ✅ | add_/remove_ + Delegate.Combine |
| Lambda / closures | ✅ | Compiler-generated DisplayClass |

### Advanced Features

| Feature | Status | Notes |
|---------|--------|-------|
| async / await | ✅ | Thread pool + continuation + Task combinators |
| CancellationToken | ✅ | BCL IL compiled |
| Multithreading | ✅ | Thread/Monitor/Interlocked/lock/volatile |
| Reflection | ✅ | typeof/GetType/GetMethods/GetFields/MethodInfo.Invoke |
| Attributes | ✅ | Metadata storage + runtime query |
| unsafe (pointers/fixed/stackalloc) | ✅ | Pointer types + BoehmGC conservative scanning |
| P/Invoke / DllImport | ✅ | extern "C" + type marshaling + SetLastError |
| Span\<T\> | ✅ | ref struct + BCL IL compiled |

---

## ICallRegistry Breakdown (~270 entries)

| Category | Count | Description |
|----------|-------|-------------|
| System.Math | 28 | Sqrt/Sin/Cos/Pow/Log/Floor/Ceiling etc. double versions |
| System.MathF | 20 | Corresponding float versions |
| System.ThrowHelper | 17 | Various exception throw helpers |
| System.Char | 16 | IsLetter/IsDigit/IsUpper/ToUpper/ToLower etc. |
| System.Threading.Interlocked | 14 | Increment/Decrement/Exchange/CompareExchange |
| System.Array | 13 | Copy/Clear/GetLength/GetLowerBound/Reverse/Sort |
| System.IO.File | 12 | Exists/ReadAllText/WriteAllText/ReadAllBytes/Delete/Copy/Move |
| System.String | 12 | FastAllocateString/get_Length/get_Chars/Comparison |
| System.Globalization.CompareInfo | 11 | Culture-aware string comparison |
| System.GC | 11 | Collect/WaitForPendingFinalizers/GetTotalMemory |
| System.IO.Path | 8 | GetFullPath/GetDirectoryName/GetFileName/GetExtension/GetTempPath |
| System.Threading.Monitor | 8 | Enter/Exit/TryEnter/Wait/Pulse/PulseAll |
| System.Environment | 8 | Exit/GetEnvironmentVariable/GetCommandLineArgs/ProcessorCount |
| System.Object | 6 | GetType/ToString/GetHashCode/Equals/MemberwiseClone |
| System.Threading.Thread | 6 | Start/Join/Sleep/CurrentThread/ManagedThreadId |
| System.Runtime.InteropServices.Marshal | 6 | AllocHGlobal/FreeHGlobal/AllocCoTaskMem/FreeCoTaskMem/GetLastPInvokeError |
| System.Buffer | 5 | BlockCopy/MemoryCopy/ByteLength |
| System.Delegate/MulticastDelegate | 5 | Combine/Remove/GetInvocationList |
| System.RuntimeHelpers | 4 | InitializeArray/IsReferenceOrContainsReferences |
| System.Runtime.InteropServices.GCHandle | 4 | Alloc/Free/Target/IsAllocated |
| System.ArgIterator | 4 | Varargs support |
| System.Globalization.OrdinalCasing | 3 | Ordinal case conversion |
| System.IO.Directory | 2 | Exists/CreateDirectory |
| System.Runtime.InteropServices.SafeHandle | 8 | .ctor/DangerousGetHandle/SetHandle/DangerousAddRef/DangerousRelease/IsClosed/SetHandleAsInvalid/Dispose |
| Other (Volatile, Enum, Type, HashCode, Marvin, NativeLibrary, ...) | ~18 | 1-3 entries each |

---

## System.IO Implementation Details

### Architecture

System.IO uses ICall interception at the public API level, intercepting File/Path/Directory calls and using C++17 `<filesystem>` for cross-platform support.

### Implemented ICalls (22)

**File (12)**: Exists, ReadAllText (1/2 params), WriteAllText (1/2 params), ReadAllBytes, WriteAllBytes, Delete, Copy, Move, ReadAllLines, AppendAllText

**Path (8)**: GetFullPath, GetDirectoryName, GetFileName, GetFileNameWithoutExtension, GetExtension, GetTempPath, Combine (2 params), Combine (3 params)

**Directory (2)**: Exists, CreateDirectory

### Not Implemented

| Feature | Description |
|---------|-------------|
| FileStream / StreamReader / StreamWriter | No streaming I/O |
| Directory enumeration | No GetFiles / EnumerateFiles / Delete |
| File info | No FileInfo / DirectoryInfo, no timestamps/attributes |
| Encoding parameter | ReadAllText/WriteAllText Encoding parameter is ignored (FIXME) |

---

## P/Invoke Implementation Details

### Supported

- DllImport declarations (extern "C", auto-filters .NET internal modules)
- Basic type marshaling (int/long/float/double/IntPtr passed directly)
- String marshaling (Ansi: UTF-8, Unicode: zero-copy UTF-16)
- Boolean marshaling (C# bool ↔ Win32 BOOL)
- Blittable struct marshaling (SequentialLayout value types passed directly)
- Callback delegates (function pointers: extract method_ptr → C function pointer)
- SetLastError (TLS storage, clear before + capture after call)
- Marshal.AllocHGlobal/FreeHGlobal/AllocCoTaskMem/FreeCoTaskMem

### FIXME / Not Implemented

| Feature | Status | Description |
|---------|--------|-------------|
| Calling conventions | ✅ | StdCall/FastCall/ThisCall emitted to extern declarations |
| CharSet.Auto | ⚠️ | Hard-coded to Unicode |
| SafeHandle methods | ⚠️ | 8 ICalls (.ctor/DangerousGetHandle/SetHandle/DangerousAddRef/DangerousRelease/IsClosed/SetHandleAsInvalid/Dispose), missing ReleaseHandle virtual dispatch |
| MarshalAs attribute | ❌ | Not parsed |
| Out/In attributes | ❌ | Parameter direction not distinguished |
| Array marshaling / Ref String | ❌ | Not supported |

---

## Known Limitations

| Limitation | Description |
|-----------|-------------|
| CLR internal type dependencies | BCL IL references QCallTypeHandle / MetadataImport etc. → method bodies auto-stubbed |
| BCL deep dependency chains | Middle layers stubbed → upper-level methods unavailable |
| System.Net | Network layer low-level icall not implemented |
| Regex internals | Depends on CLR internal RegexCache etc. |
| SIMD | Requires platform-specific intrinsics, currently uses scalar fallback structs |

---

## Test Coverage

### C++ Runtime Tests (591, 18 files)

| Module | Test Count |
|--------|-----------|
| Exception | 71 |
| String | 52 |
| Type System | 48 |
| Checked | 47 |
| Reflection | 46 |
| Unicode | 40 |
| IO | 34 |
| Array | 31 |
| Collections | 31 |
| Object | 28 |
| MemberInfo | 28 |
| Boxing | 26 |
| Globalization | 24 |
| Async | 23 |
| Delegate | 18 |
| Threading | 17 |
| GC | 16 |
| TypedReference | 11 |
| **Total** | **591** |

### End-to-End Integration Tests (35, 9 stages)

| Stage | Test Content | Count |
|-------|-------------|-------|
| Prerequisites | dotnet, CMake, runtime installation | 3 |
| HelloWorld | codegen → build → run → verify output | 5 |
| Library project | No entry point → add_library | 4 |
| Debug configuration | #line directives, IL comments | 4 |
| String literals | string_literal, __init_string_literals | 2 |
| Multi-assembly | Cross-assembly types/methods | 5 |
| ArglistTest | Varargs | 5 |
| FeatureTest | Comprehensive language features codegen-only | 3 |
| SystemIOTest | System.IO end-to-end | 4 |
| **Total** | | **35** |
