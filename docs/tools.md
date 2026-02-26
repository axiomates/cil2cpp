# Development Tools & Environment Setup

> [中文版 (Chinese)](tools.zh-CN.md)

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET 8 SDK | 8.0+ | Build compiler + compile input C# projects |
| CMake | 3.20+ | Build runtime and generated C++ code |
| C++ compiler | C++20 | Compile runtime and generated code |
| Python | 3.8+ | Developer CLI tool (stdlib only) |

### Supported C++ Compilers

| Platform | Compiler | Minimum Version |
|----------|----------|----------------|
| Windows | MSVC 2022 | Visual Studio 17.0+ |
| Linux | GCC | 12+ |
| Linux | Clang | 15+ |
| macOS | Apple Clang | 14+ (Xcode 14+) |

### Optional Dependencies (Coverage Reports)

> Quick install: `python tools/dev.py setup` auto-detects and installs

- **[OpenCppCoverage](https://github.com/OpenCppCoverage/OpenCppCoverage)** — C++ coverage (Windows)
  ```bash
  winget install OpenCppCoverage.OpenCppCoverage
  ```
- **[ReportGenerator](https://github.com/danielpalme/ReportGenerator)** — Merged coverage HTML reports
  ```bash
  dotnet tool install -g dotnet-reportgenerator-globaltool
  ```
- **lcov + lcov_cobertura** — C++ coverage (Linux, alternative to OpenCppCoverage)

---

## Quick Start (4 Steps)

### Step 1: Build and Install Runtime (One-Time)

```bash
# Configure
cmake -B build -S runtime

# Compile (Release + Debug)
cmake --build build --config Release
cmake --build build --config Debug

# Install to specified path
cmake --install build --config Release --prefix C:/cil2cpp
cmake --install build --config Debug --prefix C:/cil2cpp
```

> BoehmGC is auto-downloaded via FetchContent, cached in `runtime/.deps/`. Deleting `build/` won't re-download.

> **Shortcut**: Steps 2-4 can be done in one step with `python tools/dev.py compile HelloWorld`.

### Step 2: Generate C++ Code

```bash
# Release (default)
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output

# Debug (#line directives + IL offset comments + stack traces)
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output -c Debug
```

| Option | Description | Default |
|--------|-------------|---------|
| `-i, --input` | Input .csproj file (required) | — |
| `-o, --output` | Output directory (required) | — |
| `-c, --configuration` | Build configuration | `Release` |

### Step 3: Compile to Native Executable

```bash
cmake -B build_output -S output -DCMAKE_PREFIX_PATH=C:/cil2cpp
cmake --build build_output --config Release
```

### Step 4: Run

```bash
# Windows
build_output\Release\HelloWorld.exe

# Linux / macOS
./build_output/HelloWorld
```

---

## Generated Files

| File | Contents | Condition |
|------|----------|-----------|
| `<Name>.h` | Struct declarations, method signatures, TypeInfo, static fields | Always |
| `<Name>_data.cpp` | TypeInfo definitions, VTable, string literals, P/Invoke | Always |
| `<Name>_methods_N.cpp` | Method implementations (partitioned by IR instruction count, ~20000/partition) | Always |
| `<Name>_stubs.cpp` | Default stubs for unimplemented methods | When stubs exist |
| `main.cpp` | Runtime init → entry method → runtime shutdown | Executable only |
| `CMakeLists.txt` | CMake configuration | Always |
| `stubbed_methods.txt` | Stub diagnostic report | When stubs exist |

## Executable vs Library

| C# Project | Detection | Generated Result |
|------------|-----------|-----------------|
| Has `static void Main()` | Executable | `main.cpp` + `add_executable` |
| No entry point (library) | Static library | No `main.cpp`, `add_library(STATIC)` |

## Debug vs Release Configuration

| Feature | Release | Debug |
|---------|---------|-------|
| `#line` directives (map back to C# source) | — | Yes |
| `/* IL_XXXX */` offset comments | — | Yes |
| PDB symbol reading | — | Yes |
| Runtime stack traces | Disabled | Windows: DbgHelp, POSIX: backtrace |
| `CIL2CPP_DEBUG` define | — | Yes |
| C++ optimization | MSVC: `/O2`, GCC: `-O2` | MSVC: `/Zi /Od /RTC1`, GCC: `-g -O0` |

In Debug mode, when debugging with Visual Studio, `#line` directives make breakpoints and stepping navigate to original C# source files.

---

## Developer CLI (`tools/dev.py`)

Python interactive CLI (stdlib only), unifying all build/test/coverage/code generation operations.

### Subcommand Mode

```bash
python tools/dev.py build                  # Compile compiler + runtime
python tools/dev.py build --compiler       # Compiler only
python tools/dev.py build --runtime        # Runtime only
python tools/dev.py test --all             # Run all tests
python tools/dev.py test --compiler        # Compiler tests only (xUnit)
python tools/dev.py test --runtime         # Runtime tests only (GTest)
python tools/dev.py test --coverage        # Tests + coverage HTML report
python tools/dev.py test --compiler --filter ILOpcode  # Filter tests
python tools/dev.py install                # Install runtime (Debug + Release)
python tools/dev.py codegen HelloWorld     # Quick code generation test
python tools/dev.py compile HelloWorld     # One-step compile: codegen → cmake → build
python tools/dev.py compile HelloWorld --run  # Compile and run
python tools/dev.py compile -i myapp.csproj   # Compile arbitrary project
python tools/dev.py integration            # Integration tests
python tools/dev.py setup                  # Check prerequisites + install optional deps
```

### Interactive Menu

Run without arguments for interactive menu:

```bash
python tools/dev.py
```

### Coverage Reports

```bash
python tools/dev.py test --coverage
# 1. C# coverage (coverlet) — dotnet test --collect:"XPlat Code Coverage"
# 2. C++ coverage (OpenCppCoverage) — collect runtime test coverage
# 3. Merge two Cobertura XMLs → ReportGenerator → unified HTML report
# → Auto-opens browser to view report
# → Report path: CoverageResults/CoverageReport/index.html
```

---

## Test System

The project has three layers of tests: compiler unit tests, runtime unit tests, and end-to-end integration tests.

### C# Compiler Tests (~1240, xUnit)

```bash
dotnet test compiler/CIL2CPP.Tests
```

**Fixture caching mechanism**: `SampleAssemblyFixture` uses xUnit's `ICollectionFixture<T>` pattern — each sample's (HelloWorld, ArrayTest, FeatureTest, etc.) compilation result is created once and cached for the entire test run.

> **Important**: When adding tests, you must use `GetXxxReleaseContext()` / `GetXxxReleaseModule()` to access cached compilation results. Never directly `new AssemblySet()` + `new ReachabilityAnalyzer()` in test methods (~12 seconds each).

### C++ Runtime Tests (591, Google Test)

```bash
cmake -B runtime/tests/build -S runtime/tests
cmake --build runtime/tests/build --config Debug
ctest --test-dir runtime/tests/build -C Debug --output-on-failure
```

### End-to-End Integration Tests (35)

Full compilation pipeline: C# `.csproj` → codegen → CMake configure → C++ build → run → verify output.

```bash
python tools/dev.py integration
```

### All Tests

```bash
# Recommended
python tools/dev.py test --all

# Or manually
dotnet test compiler/CIL2CPP.Tests
cmake --build runtime/tests/build --config Debug && ctest --test-dir runtime/tests/build -C Debug --output-on-failure
python tools/dev.py integration
```

---

## Install Path Conventions

| Usage | Path | Description |
|-------|------|-------------|
| Development | `C:/cil2cpp` | Manual cmake --install |
| Integration tests | `C:/cil2cpp_test` | dev.py integration auto-installs |
| Consumer | Custom | `find_package(cil2cpp REQUIRED)` → `cil2cpp::runtime` |
