# CLAUDE.md — CIL2CPP Project Instructions

## What is this project?

CIL2CPP is a C# → C++ AOT compiler (similar to Unity IL2CPP).
Pipeline: `.csproj` → `dotnet build` → .NET DLL (IL) → Mono.Cecil → IR → C++ source + CMakeLists.txt

Two halves:
- **Compiler** (`compiler/`): C# .NET 8 tool that reads IL and emits C++
- **Runtime** (`runtime/`): C++ 20 static library (BoehmGC, type system, BCL stubs)

## Project layout

```
compiler/
  CIL2CPP.CLI/          # Command-line entry point (System.CommandLine)
  CIL2CPP.Core/
    IL/                  # Mono.Cecil wrappers (AssemblyReader, TypeDefinitionInfo, ILInstruction)
    IR/                  # Intermediate representation (IRBuilder, IRModule, IRType, IRMethod, CppNameMapper)
    CodeGen/             # C++ code generator (CppCodeGenerator)
  CIL2CPP.Tests/         # xUnit tests (1,291 tests)
    Fixtures/            # SampleAssemblyFixture (builds sample DLLs once per run)
tests/                   # Test C# projects (compiler input): HelloWorld, ArrayTest, FeatureTest
runtime/
  include/cil2cpp/       # Public headers (types.h, object.h, string.h, array.h, gc.h, exception.h, boxing.h, type_info.h)
  src/                   # Implementation (gc/, exception/, type_system/, bcl/)
  tests/                 # Google Test (595 tests)
  cmake/                 # cil2cppConfig.cmake.in
tools/
  dev.py                 # Developer CLI (build/test/coverage/codegen/integration)
  integration_defs.py    # Data-driven integration test definitions (34 tests)
  integration_runner.py  # Parallel/sequential integration test executor
```

## Build commands

```bash
# Compiler (C#)
dotnet build compiler/CIL2CPP.CLI
dotnet build compiler/CIL2CPP.Core

# Runtime (C++)
cmake -B runtime/build -S runtime
cmake --build runtime/build --config Release
cmake --build runtime/build --config Debug
cmake --install runtime/build --config Release --prefix C:/cil2cpp
cmake --install runtime/build --config Debug --prefix C:/cil2cpp

# Code generation
dotnet run --project compiler/CIL2CPP.CLI -- codegen -i tests/HelloWorld/HelloWorld.csproj -o output
dotnet run --project compiler/CIL2CPP.CLI -- codegen -i tests/HelloWorld/HelloWorld.csproj -o output -c Debug

# One-step compile: .csproj → native executable
python tools/dev.py compile HelloWorld
python tools/dev.py compile HelloWorld --run         # compile and run
python tools/dev.py compile -i path/to/App.csproj    # arbitrary project
```

## Test commands

```bash
# C# unit tests (1,291 tests)
dotnet test compiler/CIL2CPP.Tests

# C# tests + coverage
dotnet test compiler/CIL2CPP.Tests --collect:"XPlat Code Coverage"

# C++ runtime tests (595 tests)
cmake -B runtime/tests/build -S runtime/tests
cmake --build runtime/tests/build --config Debug
ctest --test-dir runtime/tests/build -C Debug --output-on-failure

# Integration tests (204 tests across 34 projects) — full pipeline: csproj → codegen → cmake → build → run → verify
# Pre-builds CIL2CPP.CLI once, then workers use --no-build to avoid MSBuild lock contention
python tools/dev.py integration                    # parallel (auto-detect workers from CPU+RAM, ~6 min)
python tools/dev.py integration --sequential       # sequential (~21 min)
python tools/dev.py integration -j 2               # 2 parallel workers
python tools/dev.py integration --filter HelloWorld # run only matching tests

# All tests at once
python tools/dev.py test --all

# Coverage report (C# + C++ unified HTML)
python tools/dev.py test --coverage
```

## Architecture: Compiler pipeline

```
AssemblyReader (Mono.Cecil)
  → IRBuilder.Build() — 7 passes:
      Pass 1: Type shells (names, flags)
      Pass 2: Fields, base types, static constructors
      Pass 3: Method shells (signatures, no bodies)
      Pass 3.5: Generic method specializations (monomorphization)
      Pass 4: VTable construction
      Pass 5: Interface implementation mapping
      Pass 6: Method bodies (IL stack simulation → variable assignments)
      Pass 7: Record method synthesis (replace compiler-generated bodies)
  → CppCodeGenerator.Generate() — produces .h, .cpp, main.cpp, CMakeLists.txt
```

Key classes:
- `IRModule` contains `List<IRType>`, entry point, array init data, primitive type infos
- `IRType` contains fields, static fields, methods, VTable entries, base type ref
- `IRMethod` contains `List<IRBasicBlock>`, each block has `List<IRInstruction>`
- 25+ concrete `IRInstruction` subclasses (IRBinaryOp, IRCall, IRCast, IRBox, etc.)
- `CppNameMapper` maps IL type names ↔ C++ type names and mangles identifiers

## Coding conventions

### C# (compiler)
- .NET 8, C# latest, nullable enabled, implicit usings
- No .sln file — use `dotnet` commands directly with project paths
- xUnit for tests: `[Fact]`, `[Theory]`/`[InlineData]`, `[Collection("SampleAssembly")]`
- `SampleAssemblyFixture` builds sample DLLs once per test run via `ICollectionFixture<T>`
- **Tests MUST use fixture cache** (`GetXxxReleaseContext()` / `GetXxxReleaseModule()`) — never `new AssemblySet()` + `new ReachabilityAnalyzer()` in test methods (each costs ~12s)
- Mono.Cecil objects are owned by `AssemblyDefinition` — disposing `AssemblyReader` invalidates all Cecil-backed objects

### C++ (runtime)
- C++20, CMake 3.20+
- Headers in `include/cil2cpp/`, sources in `src/`
- BoehmGC (bdwgc v8.2.12) via FetchContent, cached in `runtime/.deps/`
- **MSVC critical**: consumers MUST define `GC_NOT_DLL` to avoid `__imp_` linker errors
- Google Test v1.15.2 via FetchContent for runtime tests
- Multi-config generators (Visual Studio): use `DEBUG_POSTFIX d` on all targets

### Generated C++ code
- `#line N "file"` directives in Debug mode (NOT `#line default` — that's C#-only, MSVC error C2005)
- Struct layout: `__type_info` pointer + `__sync_block` + fields (inherited fields first, from furthest ancestor)
- Instance methods become C functions with explicit `this` as first parameter
- Static fields stored in `<Type>_statics` global structs with `_ensure_cctor()` guards
- BCL methods compile from IL (Unity IL2CPP architecture). Only `[InternalCall]` methods use icall (~395 entries in ICallRegistry). Console compiles from BCL IL (full chain).

## Important gotchas

- `CppNameMapper.GetDefaultValue`: must handle both IL names (System.Int32) AND C++ names (int32_t)
- Float/double literals: use `InvariantCulture` formatting + handle NaN/Infinity via `std::numeric_limits`
- Inheritance field collection: must walk full ancestor chain (A:B:C needs C's fields too, not just B's)
- `AddAutoDeclarations` detects first-use of temp vars (`__tN`) and prepends `auto` — no artificial length limits
- `EmitMethodCall`: always use `IRCall` (never `IRRawCpp`) for method calls, even with return values
- Cecil's `IsClass` returns true for structs — check `BaseTypeName == "System.ValueType"` instead
- Hidden sequence points have `StartLine == 0xFEEFEE`

## Dispatch architecture (7 layers)

The compiler/runtime uses 7 categories of special-case dispatch. This is standard for AOT compilers (Unity IL2CPP, CoreRT/NativeAOT have similar patterns).

1. **ICallRegistry** (`compiler/CIL2CPP.Core/IR/ICallRegistry.cs`) — `[InternalCall]` method whitelist (~395 entries) redirected to C++ icall implementations. All other BCL methods compile from IL.
2. **RuntimeProvidedTypes / CoreRuntimeTypes** (`IRBuilder.cs`, `CppCodeGenerator.*`) — BCL types (Object, Exception, Type, Reflection, Task) have runtime-provided struct layouts and partial method ownership.
3. **Compiler intrinsics** (`IRBuilder.Emit.cs`) — `Unsafe.*`, `Array.Empty<T>()`, `RuntimeHelpers.InitializeArray` etc. map directly to IR without going through normal method translation.
4. **Reflection reachability patterns** (`ReachabilityAnalyzer.cs`) — Heuristic recognition of reflection call patterns (e.g. `ldtoken` → `GetTypeFromHandle`); dynamic patterns may be missed.
5. **Runtime init patching** (`CppCodeGenerator.Source.cs`) — `__init_runtime_vtables()` patches String, Task, Reflection TypeInfos at startup to bridge runtime and generated code.
6. **Data structure reuse** (`mdarray.h`, `monitor.cpp`) — MdArray stores rank/bounds via `__sync_block` bits; same field reused by Monitor for lock state.
7. **Auto-stub fallback** (`CppCodeGenerator.Source.cs`, `runtime/src/bcl/core_methods.cpp`) — BCL methods that are referenced but unreachable get default-return stubs for linkability. Debug builds warn via `stub_called()`.

When adding new features, check all 7 layers for interactions.

## Runtime install paths

- All (dev + tests): `C:/cil2cpp`
- Consumer usage: `find_package(cil2cpp REQUIRED)` → link `cil2cpp::runtime`

## Current status

- **Phase 1-3 complete**: arrays, exceptions, boxing, enums, vtable, interfaces, generics, delegates, events, lambda/closures
- **Phase 4 complete**: cross-assembly references, tree-shaking, assembly resolution, deps.json parsing
- **Phase 4.5-4.6 complete**: ldind/stind, checked arithmetic, struct copy, Nullable\<T\>, ValueTuple, record synthesis
- **Phase 5a complete**: async/await with true concurrency (thread pool, continuations, combinators)
- **Phase 6 complete**: incremental GC, BCL interface proxies, exception filters, attributes, multi-dim arrays, stackalloc, unsafe/fixed, P/Invoke, DIM, Span\<T\>, generic variance, LINQ/yield, collections, CancellationToken
- **Phase 7 complete**: reflection (GetMethods/GetFields/Invoke), IAsyncEnumerable, System.IO
- **Phase 8 complete**: 100% IL opcode coverage, custom exception types, attribute complex params, P/Invoke struct marshaling + callback delegate, generic constraint validation
- **Phase 9A complete**: ICU globalization integration — CompareInfo, String comparison, Ordinal, OrdinalCasing, TextInfo, GlobalizationMode
- **Phase G complete**: System.IO (File 12 + Path 8 + Directory 2 ICalls), P/Invoke SetLastError + Marshal
- **Phase X complete**: removed all compromise/workaround code (~5,500 lines deleted) — gate system, StubAnalyzer, KnownStubs, SIMD stub sentinel, namespace/generic blacklists
- **Phase 10-11 complete**: HttpTest + HttpsGetTest — HTTP/HTTPS client, TLS/SChannel, async networking
- **Phase 12 complete**: DirTest — Directory.Exists, CreateDirectory
- **Phase 13 complete**: JsonSGTest — System.Text.Json source-generated serialization/deserialization
- **Phase 14 complete**: NuGetSimpleTest — Newtonsoft.Json 13.0.3 (first NuGet package), 3.1M lines generated
- **Phase 15 complete**: DITest — Microsoft.Extensions.DependencyInjection (3 NuGet PackageReferences: DI + Logging + Console), constructor injection, singleton/transient lifetimes
- **Phase 16 complete**: HumanizerTest — Humanizer 2.14.1 (Humanize/Dehumanize, number to words)
- **Phase 17 complete**: PollyTest — Polly 8.5.2 (resilience pipeline, basic Execute)
- **Phase 18 complete**: PInvokeTest — P/Invoke [Out]/[In] marshaling (GetSystemInfo, QueryPerformanceCounter, GetDiskFreeSpaceExW, LPArray, ByValTStr, ExplicitLayout, [Out] LPArray with SizeParamIndex)
- **Phase 19 complete**: SerilogTest — Serilog 4.2.0 + Console sink (structured logging, template parsing)
- **Phase 20 complete**: ConfigTest — Microsoft.Extensions.Configuration + Binder (in-memory config, GetValue, nested keys)
- **Phase 21 complete**: CompressionTest — System.IO.Compression (GZipStream/DeflateStream round-trip via zlib)
- **Phase 22 complete**: ValidationApp — feature composition stress test (10 sections: generics+LINQ+collections, custom exceptions, events+delegates, async/await, GroupBy+Dictionary, StringBuilder, Nullable+pattern matching, IDisposable, interface covariance)
- **Phase 23 complete**: RegexTest — System.Text.RegularExpressions interpreter mode (IsMatch/Match/Replace/Split, named groups, RegexOptions, timeout)
- **Phase 24 complete**: DateTimeTest — DateTime.Now/UtcNow/Parse/ToString, TimeSpan arithmetic, DateTimeOffset, formatting
- **Phase 25 complete**: DecimalTest — Decimal arithmetic, Parse/TryParse, ToString, Math.Round/Floor/Ceiling
- **Phase 26 complete**: HashidsTest — Hashids.net 1.7.0 (encode/decode integers, hex encoding, LINQ Intersect)
- **Phase 27 complete**: GuardClausesTest — Ardalis.GuardClauses 4.6.0 (Guard.Against.Null/NullOrEmpty/OutOfRange/Zero/NegativeOrZero)
- **Phase 28 complete**: SlugifyTest — Slugify.Core 4.0.1 (GenerateSlug, special characters, custom config)
- **Phase 29 complete**: StatelessTest — Stateless 5.16.0 (state machine library, generic interface dispatch, 6 tests, 100% match with .NET reference)
- **Phase 30 complete**: MiniCsvTool — real project validation (~400 lines, CSV processing + LINQ composition, 100% output match)
- **Phase 31 complete**: TodoManager — real project validation (~550 lines, domain model + GuardClauses + validation + LINQ + enum OrderBy). Fixed EnumComparer\`1 missing constructed-type marking (no VTable/instance_size). 66/70 lines match (3 BCL gaps: resource strings, DateTime format strings)
- **Phase 32 complete**: HealthChecker — real project validation (~500 lines, Config + Serilog + async/await + LINQ aggregation + 7 simulated endpoints). 100% output match, zero compiler fixes needed
- **Phase 33 complete**: FluentValidationTest — FluentValidation 11.11.0 (expression tree compilation via LightCompiler interpreter, 6 tests). Fixed: Expression<T>.Compile() AOT intrinsic (LambdaCompiler→LightCompiler redirect), CanEmitObjectArrayDelegate ICall (force DynamicDelegateLightup), Assembly→RuntimeAssembly TypeInfo, checked_mul integral promotion, ManagedMethodInfo interning cache, MethodBase reflection ICalls. 12/13 lines match (1 BCL locale resource string gap)
- **Phase 34 complete**: MiniServiceApp — multi-package composition (~365 lines, DI + Config + Serilog + Humanizer + LINQ). 100% output match. Exercises: DI singleton/transient, config binding, structured logging, pluralization/ordinalization/ToWords, LINQ GroupBy/OrderBy/Average/Sum, domain modeling, custom exceptions, decimal arithmetic
- **Compiler optimizations**: SIMD dead-code elimination (4-layer), demand-driven generic discovery, specialized method reachability (77% generic methods skipped), interface-dispatch-aware specialization, IsValueType fix for `<PrivateImplementationDetails>` generics, O(n²) fixpoint optimization (NuGetSimpleTest 196s→89s), __Canon generic sharing, parallel Pass 6 body compilation, parallel header generation, precompiled headers (PCH), incremental VTable, IR peephole optimizer (-6.6% codegen + -3.1% IRCall/IRBox inlining), mangled-name O(1) index (Pass 6 ScanExternalEnums 12.7s→0.7s), deferred method spec body compilation (sequential→parallel, Pass 3.3b-3.4 37s→23s), incremental callee tracking (NuGetSimpleTest IRBuilder 62s→38s, 39% reduction)
- **BCL architecture**: Unity IL2CPP model — all BCL IL compiled to C++, only [InternalCall] methods use icall (~395 entries).
- **Strategy**: Windows-first, cross-platform ready. Linux/macOS/32-bit deferred until core Windows NativeAOT goals are met.
- **Architecturally impossible**: Assembly.Load, Reflection.Emit, DynamicMethod, Type.MakeGenericType with runtime types — AOT-incompatible, never supported.
- **Test coverage**: 34 test projects, 204 integration tests, 1,291 C# + 595 C++ unit tests
- **NuGet packages validated**: 15 packages. M6 Phase 2 deepened 6 test suites (NuGetSimpleTest 2→10, PollyTest 4→6, DITest 3→9, ConfigTest 6→10, FluentValidationTest 6→11, HumanizerTest 6→10). MiniServiceApp composes DI+Config+Serilog+Humanizer+LINQ.
- **Current milestone**: M6 — Ecosystem Depth (in progress). M1-M5 all complete. Regex/DateTime/Decimal validated, 15 NuGet packages.
- **Overall maturity**: ~25-35% (see docs/roadmap.md "Maturity Assessment" section). Feature breadth ~65-75%, but feature depth ~20-30%. ValidationApp proves feature composition works.
- **Next priorities**:
  1. **More NuGet validation** (M6 target) — validate more packages, deeper API coverage, target 50+
  2. **Real application validation** — compile 3+ real .NET NativeAOT projects >1000 lines
  3. **Compiler robustness** — goal: new NuGet packages compile without manual fixes
  4. **Stub reduction** — root-cause highest-frequency stub categories (currently ~1000 unreachable stubs per project)
  5. **Linux/macOS port** — deferred until M6+ complete. System.Native P/Invoke via FetchContent, POSIX runtime layer

## Rules
 - 禁止在commit生成 Co-Authored-By 的段落，commit用英文
 - 我们的开发环境，这是一台 Windows 电脑，linux 命令不会被很好的执行，不要用 python3，直接用 python 命令。但我们要做的项目是跨平台的。
 - 拆分模块，方便维护，不要制造太大的单文件代码
 - **禁止妥协实现** — 不允许引入 stub/gate/workaround 代码来掩盖根因：
   - IL 依赖 JIT → 直接报错（AOT 不兼容）
   - 类型被 tree-shaking 剪掉后引用失败 → 修复 tree-shaking
   - IR/CodeGen 生成了非法 C++ → 修复 IR 或 CodeGen
   - 缺少编译器功能（函数指针、泛型特化等）→ 实现该功能
   - 有问题就暴露，让 C++ 编译失败，然后从根因修复
 - 修复优先级：tree-shaking > codegen bug > 新功能
 - 所有操作都需要针对架构，而不是测试，从最佳工程实践的角度，用符合il2cpp架构的正确方式解决，不要引入stub
 - python tools/dev.py integration 是非常耗时的昂贵操作，与其频繁在之上用管道命令调查输出，不如一次把它的结果完全输出到文件里，然后调查文件
 - **禁止在防御机制上开发功能或修复问题** — CallsUndeclaredFunction、GenerateMissingMethodStubImpls、ClassifyDeadCode 等渲染时过滤器是安全网，不是功能层。修复 RenderedBodyError 必须在 IR 或 CodeGen 层面解决根因（修复签名生成、修复泛型特化、修复 tree-shaking），而不是在这些过滤器中添加特殊处理或例外。同样，不要在 HasInvalidCppSignature / HasGenericBodyTypeConflict 中添加新的豁免条件来"修复"跳过的方法——而是修复它们被跳过的根因。