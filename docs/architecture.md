# Technical Architecture

> [中文版 (Chinese)](architecture.zh-CN.md)

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Compiler | C# / .NET | 8.0 |
| IL parsing | Mono.Cecil | Latest NuGet |
| Runtime | C++ | 20 |
| GC | BoehmGC (bdwgc) | v8.2.12 (FetchContent) |
| Build system | dotnet + CMake | CMake 3.20+ |
| Runtime distribution | CMake install + find_package | |
| Compiler tests | xUnit + coverlet | xUnit 2.9 |
| Runtime tests | Google Test | v1.15.2 (FetchContent) |
| Integration tests | Python (`tools/dev.py integration`) | stdlib only |
| Unicode/i18n | ICU4C | 78.2 |

## Project Layout

```
cil2cpp/
├── compiler/                   # C# compiler (.NET project)
│   ├── CIL2CPP.CLI/            #   CLI entry point (System.CommandLine)
│   ├── CIL2CPP.Core/           #   Core compilation logic
│   │   ├── IL/                 #     IL parsing (Mono.Cecil wrappers)
│   │   │   ├── AssemblyReader.cs
│   │   │   ├── TypeDefinitionInfo.cs
│   │   │   └── ILInstruction.cs
│   │   ├── IR/                 #     Intermediate representation
│   │   │   ├── IRBuilder.cs          # 8-pass build entry
│   │   │   ├── IRBuilder.Emit.cs     # IL → IR instruction translation
│   │   │   ├── IRBuilder.Methods.cs  # Method body construction
│   │   │   ├── IRModule.cs           # Module (type collection)
│   │   │   ├── IRType.cs             # Type
│   │   │   ├── IRMethod.cs           # Method
│   │   │   ├── IRInstruction.cs      # 25+ instruction subclasses
│   │   │   └── CppNameMapper.cs      # IL ↔ C++ name mapping
│   │   └── CodeGen/            #     C++ code generation
│   │       ├── CppCodeGenerator.cs       # Generation entry point
│   │       ├── CppCodeGenerator.Header.cs  # .h generation
│   │       ├── CppCodeGenerator.Source.cs  # .cpp generation
│   │       ├── CppCodeGenerator.KnownStubs.cs  # Hand-written stubs
│   │       └── StubAnalyzer.cs                 # Stub analysis tool (--analyze-stubs)
│   └── CIL2CPP.Tests/          #   Compiler unit tests (xUnit)
│       └── Fixtures/           #     Test fixture caching
├── tests/                      # Test C# projects (compiler input)
│   ├── HelloWorld/
│   ├── ArrayTest/
│   ├── FeatureTest/
│   └── MultiAssemblyTest/
├── runtime/                    # C++ runtime library (CMake)
│   ├── CMakeLists.txt
│   ├── cmake/                  #   CMake package config template
│   ├── include/cil2cpp/        #   Headers (32 files)
│   ├── src/                    #   Source (GC, type system, exception, BCL icall)
│   │   ├── gc/
│   │   ├── exception/
│   │   ├── type_system/
│   │   ├── io/
│   │   └── bcl/
│   └── tests/                  #   Runtime unit tests (Google Test)
├── tools/
│   └── dev.py                  # Developer CLI
└── docs/                       # Project documentation
```

## Compilation Pipeline Overview

```
 C# source code          CIL2CPP compiler (C#)                                Native compilation
──────────────    ┌─────────────────────────────────────────────────────┐    ──────────────────
                  │                                                     │
 .csproj ──────→  │ dotnet build ──→ .NET DLL (IL)                      │
                  │       ↓                                             │
                  │ Mono.Cecil ──→ Read IL bytecode + type metadata      │
                  │       ↓         (Debug: also read PDB source maps)  │
                  │ AssemblySet ──→ Load user + third-party + BCL       │
                  │       ↓         assemblies (deps.json discovery)    │
                  │ ReachabilityAnalyzer ──→ Reachability analysis      │
                  │       ↓                 (tree shaking from entry    │
                  │       ↓                  point / public types)      │
                  │ IRBuilder.Build() ──→ IR (8 passes, see below)      │
                  │       ↓                                             │
                  │ CppCodeGenerator ──→ C++ headers + multi-source     │
                  │                      + CMake (file splitting +      │     .h / *_data.cpp
                  │                      trial render + auto stub)      │ ──→ *_methods_N.cpp
                  └─────────────────────────────────────────────────────┘     CMakeLists.txt
                                                                                  ↓
                                                                          cmake + C++ compiler
                                                                                  ↓
     C++ runtime (cil2cpp::runtime) ──────────────────────── find_package ──→ link
     BoehmGC / type system / exception / thread pool                          ↓
                                                                        native executable
```

## IR Build: 8-Pass Pipeline

The compiler core is `IRBuilder.Build()`, which transforms Mono.Cecil IL data into an intermediate representation (IR) over 8 passes:

| Pass | Name | What it does | Why this ordering |
|------|------|-------------|-------------------|
| 1 | Type shells | Create all `IRType` (names, flags, namespaces) | Later passes need type lookup by name |
| 2 | Fields & base types | Fill fields, base type refs, interface lists, static constructors | VTable construction needs inheritance chain |
| 3 | Method shells | Create `IRMethod` (signatures, parameters), no bodies | Method bodies need call targets resolved |
| 4 | Generic monomorphization | Collect all generic instantiations → generate concrete types/methods | `List<int>` → `List_1_System_Int32` as standalone type |
| 5 | VTable | Recursively build virtual method tables along inheritance chain | callvirt in method bodies needs VTable slot numbers |
| 6 | Interface mapping | Build InterfaceVTable (interface → implementation method mapping) | Interface dispatch needs implementation method addresses |
| 7 | Method bodies | IL stack simulation → variable assignments, generate `IRInstruction` | Depends on prior passes: call resolution, VTable slots, interface mapping |
| 8 | Method synthesis | record ToString/Equals/GetHashCode/Clone | Replace compiler-generated bodies that reference EqualityComparer |

### Key Classes

- `IRModule` — Contains `List<IRType>`, entry point, array initialization data, primitive type info
- `IRType` — Contains fields, static fields, methods, VTable entries, base type ref
- `IRMethod` — Contains `List<IRBasicBlock>`, each basic block has `List<IRInstruction>`
- `IRInstruction` — 25+ concrete subclasses (IRBinaryOp, IRCall, IRCast, IRBox, etc.)
- `CppNameMapper` — Bidirectional mapping between IL type names ↔ C++ type names with identifier mangling

## BCL Compilation Strategy

### Unity IL2CPP Model

CIL2CPP adopts the same strategy as Unity IL2CPP: **all BCL methods with IL bodies are compiled directly from IL to C++**, following the exact same compilation path as user code. Only the lowest-level `[InternalCall]` methods have hand-written C++ implementations (icall).

```
Method call
  ↓
ICallRegistry lookup (~270 mappings)
  ├─ Hit → [InternalCall] method, no IL body
  │         GC / Monitor / Interlocked / Buffer / Math / IO / Globalization etc.
  │         → Call C++ runtime implementation
  │
  └─ Miss → Normal IL compilation (BCL methods follow same path as user code)
              → Generate C++ function
```

### RuntimeProvidedTypes (Runtime-provided types)

The following types have their **struct definitions** provided by the C++ runtime (not compiled from BCL IL). Based on first-principles analysis: C++ runtime directly accesses fields / BCL IL references CLR internal types / embeds C++ types.

**Must be RuntimeProvided (26 types)**:
- **Object model**: Object, ValueType, Enum — GC header + type system foundation
- **String/Array**: String, Array — Variable-length layout + GC interop
- **Exception**: Exception — setjmp/longjmp mechanism
- **Delegate**: Delegate, MulticastDelegate — Function pointer dispatch
- **Type system**: Type, RuntimeType — Type metadata
- **Thread**: Thread — TLS + OS thread management
- **Reflection structs ×12**: MemberInfo, MethodInfo, FieldInfo, ParameterInfo + Runtime* subtypes + Assembly — .NET 8 BCL reflection IL deeply depends on QCall/MetadataImport, cannot AOT compile
- **Task**: 4 custom runtime fields + f_lock (std::mutex*) + MSVC padding issue
- **Varargs**: TypedReference, ArgIterator

**Short-term migratable to IL (8 types, Phase IV)**:
- **IAsyncStateMachine** — Pure interface, no struct needed
- **CancellationToken** — Only has f_source pointer
- **WaitHandle hierarchy ×6** — BCL IL compilable, needs OS primitive ICall registration

**Long-term requires architectural refactoring (6 types, Phase V)**:
- **TaskAwaiter / AsyncTaskMethodBuilder / ValueTask / ValueTaskAwaiter / AsyncIteratorMethodBuilder** — Depend on Task struct layout
- **CancellationTokenSource** — Depends on ITimer + ManualResetEvent chain

See [roadmap.md](roadmap.md) Phase IV-V for details.

### Compiler Built-in Intrinsics

A few JIT intrinsic methods are inlined by the compiler (not going through ICallRegistry):

- `Unsafe.SizeOf<T>` / `Unsafe.As<T>` / `Unsafe.Add<T>` → C++ sizeof / reinterpret_cast / pointer arithmetic
- `Array.Empty<T>()` → Static empty array instance
- `RuntimeHelpers.InitializeArray` → memcpy static data

## Three-Layer Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Layer 1: CIL Instruction Translation                    │
│  ConvertInstruction() switch — ~230 opcodes              │
│  Coverage: 100%                                          │
│  This layer determines: can an IL method body be         │
│  translated to C++                                       │
├─────────────────────────────────────────────────────────┤
│  Layer 2: BCL Method Compilation                         │
│  All BCL methods with IL bodies compiled from IL to C++  │
│  Limitation: CLR internal type deps → auto stubbed       │
│  This layer determines: which standard library methods   │
│  are available                                           │
├─────────────────────────────────────────────────────────┤
│  Layer 3: Runtime ICall                                  │
│  ~270 [InternalCall] methods → C++ runtime               │
│  implementation                                          │
│  Limitation: unimplemented icall → feature unavailable   │
│  This layer determines: GC, threading, string layout,    │
│  IO and other low-level capabilities                     │
└─────────────────────────────────────────────────────────┘
```

## C++ Code Generation Strategy

Generated C++ code maps each .NET type to a C++ struct, and each method to a standalone C function:

- **Reference types** → `struct` + `__type_info` pointer + `__sync_block` + user fields, heap-allocated via `gc::alloc()`
- **Value types** → Plain `struct` (no object header), stack-allocated, passed by value
- **Instance methods** → `RetType FuncName(ThisType* __this, ...)` — explicit this parameter
- **Static fields** → `<Type>_statics` global struct + `_ensure_cctor()` initialization guard
- **Virtual method calls** → `obj->__type_info->vtable->methods[slot]` function pointer call
- **Interface dispatch** → `type_get_interface_vtable()` lookup for interface implementation table

### Multi-File Splitting & Parallel Compilation

Generated C++ code is automatically split into multiple compilation units, leveraging MSVC `/MP` for parallel compilation:

```
output/
├── HelloWorld.h              ← Unified header (all type declarations + forward refs)
├── HelloWorld_data.cpp       ← Static data (TypeInfo, VTable, string literals, P/Invoke)
├── HelloWorld_methods_0.cpp  ← Method implementation partition 0
├── HelloWorld_methods_1.cpp  ← Method implementation partition 1
├── ...                       ← Auto-partitioned by IR instruction count (~20000/partition)
├── HelloWorld_stubs.cpp      ← Default stubs for unimplemented methods
├── main.cpp                  ← Entry point (executable projects)
└── CMakeLists.txt            ← Auto-lists all source files + /MP compile options
```

Partitioning strategy: evenly split by IR instruction count (`MinInstructionsPerPartition = 20000`), each `.cpp` file is approximately 13k-17k lines of C++ code.

### Multi-Layer Safety Net: Auto Stub Mechanism

Some BCL methods' IL references CLR internal types that cannot be compiled to C++. The compiler has 4 safety layers:

1. **HasClrInternalDependencies** — IR level: method references CLR internal type → replaced with default-value-returning stub
2. **HasKnownBrokenPatterns** — Pre-render: JIT intrinsics, known issues → skip
3. **RenderedBodyHasErrors** — Trial render: render method body to C++, detect compilation error patterns → stub
4. **GenerateMissingMethodStubImpls** — Catch-all: all declared but undefined functions → generate default stub

Each codegen run produces a `stubbed_methods.txt` report listing all stubbed methods and their reasons.

## Garbage Collector (GC)

The runtime uses [BoehmGC (bdwgc)](https://github.com/ivmai/bdwgc) as its garbage collector — the same conservative GC used by Mono.

### Why BoehmGC

BoehmGC's **conservative scanning** automatically solves all root tracking problems:

| Scenario | Custom GC (deprecated) | BoehmGC |
|----------|----------------------|---------|
| Stack local variables | Requires shadow stack | Auto-scans stack |
| Reference type fields | Requires reference bitmap | Auto-scans heap |
| Reference elements in arrays | Requires manual marking | Auto-scans |
| Static fields (globals) | Requires add_root | Auto-scans global area |
| Reference fields in value types | Requires precise layout | Auto-scans |

### Dependency Management

```
runtime/CMakeLists.txt
├── FetchContent: bdwgc v8.2.12        ← Auto-download
├── FetchContent: libatomic_ops v7.10.0 ← Required for MSVC
└── Cache: runtime/.deps/              ← gitignored, survives build/ deletion
```

- bdwgc compiled as static library, PRIVATE linked to cil2cpp_runtime
- gc.lib / gcd.lib installed separately to `lib/`
- Consumers auto-link via `find_package(cil2cpp)`

### GC Feature Status

| Feature | Status |
|---------|--------|
| Object allocation + TypeInfo | `gc::alloc()` → `GC_MALLOC()` + set `__type_info` |
| Array allocation | `alloc_array()` → `GC_MALLOC()` + element type + length |
| Auto root scanning | BoehmGC conservative scan, no shadow stack needed |
| Finalizer registration | `GC_register_finalizer_no_order()` |
| Finalizer detection | Compiler detects `Finalize()` → TypeInfo.finalizer |
| GC statistics | `GC_get_heap_size()` / `GC_get_total_bytes()` |
| Incremental collection | `GC_enable_incremental()` + `gc::collect_a_little()` |

## Runtime Installation & CMake Package

The runtime is compiled as a static library and distributed via CMake install + find_package:

```cmake
# Consumer CMakeLists.txt
find_package(cil2cpp REQUIRED)
target_link_libraries(MyApp PRIVATE cil2cpp::runtime)
```

Installed directory structure:

```
<prefix>/
├── include/cil2cpp/    # 32 headers
├── lib/
│   ├── cil2cpp_runtime.lib   # Release
│   ├── cil2cpp_runtimed.lib  # Debug (DEBUG_POSTFIX "d")
│   ├── gc.lib                # BoehmGC Release
│   ├── gcd.lib               # BoehmGC Debug
│   └── cmake/cil2cpp/        # CMake package config
│       ├── cil2cppConfig.cmake
│       ├── cil2cppTargets.cmake
│       └── ...
```

Install path conventions:
- Development: `C:/cil2cpp`
- Integration tests: `C:/cil2cpp_test`
