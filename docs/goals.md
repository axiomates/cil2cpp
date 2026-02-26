# Project Goals & Positioning

> [中文版 (Chinese)](goals.zh-CN.md)

## What is CIL2CPP

CIL2CPP is a **C# → C++ AOT (Ahead-of-Time) compiler** that compiles .NET programs into native C++ code, which is then compiled by a C++ compiler (MSVC/GCC/Clang) into native executables.

The name CIL refers to Common Intermediate Language — the bytecode format that the .NET compiler (Roslyn) produces from C# source code. CIL2CPP reads this bytecode and translates each instruction into equivalent C++ code. Because of this approach, there's no need to individually "support" C# syntax features — as long as all IL instructions are covered, any C# code that compiles to those instructions automatically works.

## Comparable Projects

| Project | Positioning | Architecture | Target Platforms |
|---------|------------|-------------|-----------------|
| **Unity IL2CPP** | Unity engine-specific AOT | Mono BCL + custom GC | iOS/Android/WebGL/Console |
| **.NET NativeAOT** | Official general-purpose AOT | CoreCLR BCL + custom GC | All platforms |
| **CIL2CPP** | General-purpose AOT compiler | .NET 8 CoreCLR BCL + BoehmGC | Windows/Linux/macOS |

### Relationship to Unity IL2CPP

CIL2CPP borrows Unity IL2CPP's core architectural approach — **compiling BCL (Base Class Library) IL bytecode directly to C++**, with only the lowest-level hand-written `[InternalCall]` C++ implementations (icall) retained. However, it differs in the following ways:

- **Input**: Unity uses Mono BCL (older/smaller), CIL2CPP uses .NET 8 CoreCLR BCL (larger/more modern)
- **Scope**: Unity only needs to support game-related BCL subsets, CIL2CPP targets general-purpose coverage
- **GC**: Unity uses a custom GC, CIL2CPP uses BoehmGC (the same conservative GC as Mono)

### Relationship to .NET NativeAOT

.NET NativeAOT is Microsoft's official AOT solution, but it embeds a stripped-down CoreCLR runtime. CIL2CPP aims to match NativeAOT in feature coverage while using a completely independent, lightweight C++ runtime.

## Project Scope

**Positioning: General-purpose AOT compiler** (targeting .NET NativeAOT-level coverage).

The goal is not limited to the BCL subset Unity supports, but rather covers the full .NET BCL as comprehensively as possible:
- Console applications (Console I/O, environment variables, command-line arguments)
- File system operations (File/Path/Directory, future FileStream)
- Collections & LINQ (List/Dictionary/Where/Select etc.)
- Asynchronous programming (async/await, Task, thread pool)
- Reflection (typeof/GetType/GetMethods/GetFields/Invoke)
- Networking (future: Socket/HttpClient)
- Serialization (future: System.Text.Json)

## Core Design Principles

### 1. Unity IL2CPP Architecture: Full BCL IL Compilation

All BCL methods with IL bodies are **compiled directly from IL to C++**, following the exact same compilation path as user code. Only the lowest-level `[InternalCall]` methods have hand-written C++ implementations (~243 icall mappings).

```
Method call
  ↓
ICallRegistry lookup
  ├─ Hit → [InternalCall], no IL body
  │         → Call C++ runtime implementation
  └─ Miss → Normal IL compilation (BCL = same path as user code)
              → Generate C++ function
```

This means the complete call chain of `Console.WriteLine("Hello")` is entirely compiled from IL:
```
Console.WriteLine → TextWriter.WriteLine → StreamWriter.Write → Encoding.GetBytes → P/Invoke → WriteFile
```

### 2. Multi-Assembly + Tree Shaking

The compiler automatically loads user assemblies + third-party dependencies + BCL assemblies, and uses reachability analysis (ReachabilityAnalyzer) for tree shaking — only compiling types and methods actually used.

### 3. Cross-Platform

- **Compiler**: C# .NET 8, runs on any platform .NET supports
- **Runtime**: C++20, CMake build, supports Windows (MSVC), Linux (GCC/Clang), macOS (Apple Clang)
- **Generated code**: Platform-independent C++ code + CMake project

### 4. Multi-Layer Safety Net

Some BCL methods reference CLR internal types (RuntimeType, QCallTypeHandle, etc.) that cannot be compiled to C++. The compiler has 4 safety layers that automatically detect and stub these methods, ensuring compilation never fails:

1. HasClrInternalDependencies — IR-level detection
2. HasKnownBrokenPatterns — Pre-render detection
3. RenderedBodyHasErrors — Trial render detection
4. GenerateMissingMethodStubImpls — Catch-all stub

## Non-Goals / Fundamental AOT Limitations

The following features **cannot be supported** due to inherent constraints of the AOT compilation model. These are the same limitations as Unity IL2CPP and .NET NativeAOT:

| Feature | Reason |
|---------|--------|
| `System.Reflection.Emit` | Generates IL at runtime and executes it — no IL interpreter/JIT after AOT compilation |
| `DynamicMethod` | Creates methods at runtime and executes them |
| `Expression<T>.Compile()` | Compiles expression trees to executable code at runtime |
| `Assembly.Load()` / `Assembly.LoadFrom()` | Dynamically loads assemblies at runtime — AOT requires all code known at compile time |
| `Activator.CreateInstance(string typeName)` | Dynamic instantiation by name string — target type cannot be determined at compile time |
| `Type.MakeGenericType()` | Constructs generic types at runtime — monomorphization must happen at compile time |
| `ExpandoObject` / `dynamic` | DLR entirely depends on runtime binding |
| Runtime code hot-reloading | No JIT compiler, compiled machine code cannot be replaced |
