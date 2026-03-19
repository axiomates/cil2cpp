# Development Roadmap

> Last updated: 2026-03-19
>
> [中文版 (Chinese)](roadmap.zh-CN.md)

## Design Principles

### Core Goal

**Only content that truly cannot be compiled from IL uses C++ runtime mappings; everything else is translated from BCL IL to C++.**

Using Unity IL2CPP as a reference architecture, but not blindly copying — IL2CPP uses Mono BCL (with far simpler dependency chains than .NET 8), and its source comes from community decompilation (may be incomplete). We perform first-principles analysis based on .NET 8 BCL's actual dependency chains.

### Six Guidelines

1. **IL-first**: Everything that can be compiled from BCL IL should be compiled — including FileStream, Socket, HttpClient, JSON, etc. These BCL types have complete IL implementations and ultimately call OS APIs via P/Invoke. **The way to improve capability is to fix compiler bugs to let BCL IL compile, not to reimplement functionality with ICalls.**
2. **ICall as bridge**: C++ runtime only exposes low-level primitives via ICall (Monitor, Thread, Interlocked, GC); BCL IL calls these primitives. **Don't write ICall replacements for high-level BCL functionality.**
3. **Compiler quality-driven**: The way to improve IL translation rate is to fix compiler bugs, not add RuntimeProvided types
4. **First-principles judgment**: Every RuntimeProvided type must have a clear technical reason (runtime directly accesses fields / BCL IL references CLR internal types / embeds C++ types)
5. **Native library integration**: .NET BCL calls platform native libraries via P/Invoke (kernel32, ws2_32, System.Native, OpenSSL, etc.). CIL2CPP integrates these libraries (similar to BoehmGC/ICU FetchContent pattern), letting BCL P/Invoke link naturally rather than bypassing with ICalls.
6. **Windows-first, cross-platform ready**: Primary development targets Windows (x64). Platform abstraction macros and conditional branches are written from the start to facilitate future cross-platform support, but Linux/macOS/32-bit are deferred until core goals are met.

### Goal Classification

#### Must-Implement (Core NativeAOT compatibility, Windows x64)

These are required before CIL2CPP can claim "compiles .NET NativeAOT projects":

| Goal | Phase | Description |
|------|-------|-------------|
| Full HTTP GET | C.6 ✅ | `HttpClient.GetStringAsync("http://...")` async request/response chain — **working** |
| NativeAOT metadata | D | `[DynamicallyAccessedMembers]`, ILLink feature switches, NuGet package validation |
| JSON serialization (SG) | D.5 ✅ | System.Text.Json source generator + Newtonsoft.Json 13.0.3 (NuGet) — **both validated end-to-end** |
| MarshalAs P/Invoke | C.7 | `[MarshalAs]`, `[Out]`/`[In]`, array marshaling — needed by NuGet ecosystem |
| SChannel TLS (Windows) | E.win ✅ | HTTPS via `secur32.dll`/`schannel.dll` P/Invoke — **working** (HttpsGetTest passes) |
| Compression | E.2 | zlib via System.IO.Compression.Native |
| RenderedBodyError → 0 | H.2 | Fix remaining codegen bugs (17 RE stubs in HelloWorld baseline, not blocking NuGet) |
| SIMD scalar completion | F.1 | Eliminate remaining SIMD stubs via complete scalar fallback paths |
| 10 NuGet package validation | G.2 | Prove real-world packages compile and run (2/10: Newtonsoft.Json ✅, DI+Logging+Console ✅) |

#### Deferred (after Must-Implement is complete)

Only begin after core Windows NativeAOT compatibility is achieved. Platform abstraction code (macros, `#ifdef`, branch scaffolding) is written NOW to enable these later.

| Goal | Phase | Description |
|------|-------|-------------|
| Linux support (System.Native) | B.5 | Extract ~30 .c files from dotnet/runtime, FetchContent compile |
| macOS support | — | System.Native + Objective-C bridge for platform APIs |
| OpenSSL TLS (Linux) | E.linux | FetchContent + link OpenSSL for Linux HTTPS |
| 32-bit targets (ARM/x86) | — | Pointer size assumptions in IRBuilder.Types.cs, TypeInfo layout |
| Task struct refactoring | F.2 | Reduce RuntimeProvided 32→25 (internal quality, no user-facing impact) |
| Incremental compilation | F.3 | IR/codegen caching (performance optimization) |
| Full reflection model | F.4 | QCall alternatives for reflection metadata |

#### Architecturally Impossible (AOT-incompatible)

These .NET features are fundamentally incompatible with AOT compilation and will **never** be supported:

| Feature | Reason |
|---------|--------|
| `Assembly.Load` / dynamic assembly loading | Requires JIT to compile new IL at runtime |
| `Reflection.Emit` / dynamic code generation | Creates new types/methods at runtime — no C++ equivalent |
| `DynamicMethod` / expression tree compilation | Generates IL dynamically, requires JIT |
| Tiered JIT compilation / ReadyToRun | JIT-specific runtime optimization |
| `Type.MakeGenericType` with runtime types | AOT cannot monomorphize types unknown at compile time |
| Dynamic COM interop (`IDispatch`) | Requires runtime type discovery |
| QCall / CLR internal types | CLR JIT-specific bridges (QCallTypeHandle, MetadataImport, MethodTable) — permanently retained as stubs (~96 stubs) |

> **Note**: Libraries that use these features (gRPC dynamic proxies, some ORMs like Dapper's dynamic queries, Reflection.Emit-based serializers) cannot be supported. Source-generator equivalents (gRPC code-first, System.Text.Json SG) should be used instead.

---

## RuntimeProvided Type Classification (First Principles)

### Decision Criteria

A type needs to be RuntimeProvided if and only if it meets any of these conditions:
1. C++ runtime needs to **directly access the type's fields** (GC, exceptions, delegate dispatch, etc.)
2. BCL IL method bodies reference **CLR internal types that cannot be AOT compiled** (QCall, MetadataImport, etc.)
3. Struct embeds **C++-specific data types** (e.g., std::mutex*)

### Types That Must Be C++ Runtime (32 = RuntimeProvidedTypes entry count in code)

| Type | Count | Technical Reason |
|------|-------|-----------------|
| Object / ValueType / Enum | 3 | GC header + type system foundation, root of every managed object |
| String | 1 | Inline UTF-16 buffer + GC special handling (string_create/concat/intern) |
| Array | 1 | Variable-length layout + bounds + GC (array_create/get/set) |
| Exception | 1 | setjmp/longjmp exception mechanism, runtime directly accesses message/inner/trace |
| Delegate / MulticastDelegate | 2 | Function pointer + dispatch chain (delegate_invoke/combine) |
| Type / RuntimeType | 2 | Type metadata system, typeof() → TypeInfo* → Type* cache |
| Thread | 1 | TLS + OS thread management, runtime directly accesses thread state |
| Reflection struct + alias ×12 | 12 | MemberInfo/MethodBase/MethodInfo/FieldInfo/ParameterInfo (5 real) + RuntimeXxx alias (7) |
| TypedReference / ArgIterator | 2 | Varargs mechanism, compiler special handling |
| Task + async non-generic ×6 | 6 | Task (4 custom fields + std::mutex*) + TaskAwaiter/Builder/ValueTask/ValueTaskAwaiter/AsyncIteratorBuilder |
| CancellationTokenSource | 1 | Depends on ITimer + ManualResetEvent + Registrations chain |

### Types Migrated to IL (8, Phase 4 ✅)

| Type | Count | Status | Description |
|------|-------|--------|-------------|
| IAsyncStateMachine | 1 | ✅ Done | Pure interface, removed RuntimeProvided |
| CancellationToken | 1 | ✅ Done | Only has f_source pointer, struct generated from Cecil |
| WaitHandle hierarchy | 6 | ✅ Done | struct from Cecil, TypeInfo from IL, WaitOneCore ICall retained; POSIX Mutex/Semaphore have TODO (currently stub impl) |

### Long-Term Migratable Types (7, requires Task architectural refactoring)

| Type | Count | Issue |
|------|-------|-------|
| Task | 1 | 4 custom fields + std::mutex* + MSVC padding |
| TaskAwaiter / AsyncTaskMethodBuilder | 2 | Only have f_task field, but depend on Task struct layout |
| ValueTask / ValueTaskAwaiter / AsyncIteratorMethodBuilder | 3 | Depend on Task + BCL dependency chain (ThreadPool, ExecutionContext) |
| CancellationTokenSource | 1 | Depends on ITimer + ManualResetEvent + Registrations chain |

**Long-term vision**: Rewrite async runtime architecture, migrating Task from custom C++ implementation to BCL IL implementation. This requires the entire TPL dependency chain (ThreadPool, TaskScheduler, ExecutionContext, SynchronizationContext) to compile from IL.

### RuntimeProvided Goals

- **Current**: 32 entries (Phase 4 complete: removed IAsyncStateMachine + CancellationToken + WaitHandle×6 = -8)
- **Short-term target achieved**: 40 → 32 (-8 RuntimeProvided types)
- **Long-term**: 32 → 25 (after Task architectural refactoring, remove Task+async deps+CTS = 7)

### Unity IL2CPP Reference (comparison only, not blindly copied)

> **Note**: IL2CPP uses Mono BCL; .NET 8 BCL's dependency chains are far more complex than Mono's. The following comparison is for reference only.
> IL2CPP source comes from community-decompiled libil2cpp headers, may be incomplete.

IL2CPP Runtime struct: `Il2CppObject` / `Il2CppString` / `Il2CppArray` / `Il2CppException` / `Il2CppDelegate` / `Il2CppMulticastDelegate` / `Il2CppThread` / `Il2CppReflectionType` / `Il2CppReflectionMethod` / `Il2CppReflectionField` / `Il2CppReflectionProperty` (~12)

IL2CPP compiles from IL: Task/async entire family, CancellationToken/Source, WaitHandle hierarchy — but this is based on Mono's simpler BCL, not directly comparable to .NET 8.

---

## Current Status

### RuntimeProvided Types: 32 entries (Phase 4 complete -8, long-term target 25)

See "RuntimeProvided Type Classification" section above.

### Stub Distribution (HelloWorld, 1,280 stubs, ~95%+ translation rate)

> Historical baseline from commit 3f93840 (2026-03-05). `stub_budget.json` was removed in Phase X cleanup.
> Assembly: HelloWorld (~26k methods after demand-driven generics + specialized method reachability).
> To get current counts, run codegen with `--analyze` flag or inspect generated `*_stubs.cpp` files.

| Category | Count | % | Nature |
|----------|-------|---|--------|
| MissingBody | 604 | 47.2% | No IL body (abstract/extern/JIT intrinsic) — most are legitimate |
| KnownBrokenPattern | 458 | 35.8% | SIMD dead-code branches + TypeHandle/MethodTable |
| ClrInternalType | 96 | 7.5% | QCall/MetadataImport CLR JIT-specific types (permanent) |
| UndeclaredFunction | 95 | 7.4% | Cascade from MissingBody/KBP — generic specialization gaps |
| RenderedBodyError | 17 | 1.3% | Codegen bugs — Phase H.2 target: 0 |
| UnknownParameterTypes | 10 | 0.8% | Method parameters reference unknown types |
| UnknownBodyReferences | 0 | 0% | Resolved |

**Unfixable or deferred**: SIMD dead-code branches are handled by FeatureSwitchResolver (IsSupported=false dead-branch elimination). CLR internal types (~96) are permanently retained.

**IL translation rate**: ~95%+. History: Phase A: 2,777 → 1,478; Phase B: 1,478 → 1,537; Phase C: → 1,666; Phase X + demand-driven generics: → 1,280 (method count also reduced ~26k from ~31k via specialized method reachability).
**Tests**: 1,291 C# + 576 C++ + 93 integration (15 test projects) — all passing.

### Implemented Architecture Capabilities

- **Multi-project compilation**: AssemblySet + deps.json + CIL2CPPAssemblyResolver on-demand loading for ProjectReference assemblies (MultiAssemblyTest verified)
- **Source Generator awareness**: SG output is standard IL, dotnet build already compiles it, CIL2CPP reads directly
- **P/Invoke complete**: Declaration generation + calling conventions + CharSet marshaling; OS APIs like kernel32/user32 can be called directly
- **100% IL opcode coverage**: All ~230 ECMA-335 opcodes implemented

### Distance to Final Goal (Windows x64)

| Project Type | Est. Completion | Key Blockers |
|-------------|----------------|-------------|
| Simple console apps | ~95% | Reflection stubs may cause runtime surprises |
| Library projects | ~85% | NuGet PackageReference fully validated (Newtonsoft.Json); DAM parsing complete |
| File I/O apps | ~90% | FileStream/StreamReader working; File.ReadAllBytes hangs (B.6) |
| Network apps (HTTP) | ~90% | HTTP GET ✅, HTTPS GET ✅ (Windows). Complex scenarios (redirects, auth) not yet validated |
| Network apps (HTTPS) | ~85% | SChannel TLS working for basic HTTPS GET; edge cases pending |
| JSON serialization | ~85% | System.Text.Json SG ✅ + Newtonsoft.Json 13.0.3 ✅ — both proven end-to-end |
| REST client (HTTP+JSON) | ~80% | HTTP + JSON both working end-to-end; needs more validation |
| NuGet packages (simple) | ~75% | Newtonsoft.Json ✅ + DI+Logging+Console ✅ (3 NuGet packages in one project). M4 achieved. |
| Production-grade apps | ~40% | DI + logging ecosystem working (DITest). Config + compression pending |
| Arbitrary NativeAOT .csproj | **~55%** | Large assembly scale proven (3.1M lines, 103s codegen), DI ecosystem working, stubs remain, `[MarshalAs]` partial |

> **Linux/macOS**: Deferred. All percentages above are Windows-only. Linux requires System.Native integration (Phase B.5, deferred) + OpenSSL (Phase E.linux, deferred). Current Linux support: ~5% (console-only, no file I/O or networking).

**What moves the needle** (cumulative, Windows):
- **50%→60%**: ✅ Codegen performance (361s→103s, -72%) + more NuGet packages (target: 10)
- **60%→75%**: NuGet package validation cycle (Serilog, Polly, etc.) + stub reduction + compression (zlib) + MarshalAs completion
- **75%→85%**: Complex HTTP scenarios + config ecosystem + more reflection completeness
- **85%→95%**: 10 NuGet package validation + comprehensive testing + edge case polish

**Implementation gaps** (2026-03-18 audit):
- `[DynamicallyAccessedMembers]` — **complete (validated)**: 13 DamFlags, field/method/parameter scanning, CLI `--rdxml` wired, 7 DAM reachability tests + 14 rd.xml parser tests
- ILLink feature switches — **active**: FeatureSwitchResolver substitutes 10+ AOT defaults at compile time. SIMD IsSupported=false dead-branch elimination via brfalse pattern detection + 4-layer SIMD dead-code elimination.
- `[MarshalAs]` attribute — **implemented** (C.7.1): Cecil parsing + 21 type mappings. Missing: `[Out]`/`[In]` copy-back (C.7.2), LPArray runtime marshaling (C.7.3)
- NuGet PackageReference — **✅ fully validated** (Phase 14+15): NuGetSimpleTest (Newtonsoft.Json 13.0.3) + DITest (DI+Logging+Console, 3 NuGet packages) both compile and run end-to-end. OOM issue resolved via demand-driven generic discovery + specialized method reachability.
- DI ecosystem — **✅ validated** (Phase 15): DITest with Microsoft.Extensions.DependencyInjection — constructor injection, singleton/transient lifetimes, reflection-based service resolution. M4 milestone achieved.
- Source generator output — **✅ fully validated** (D.5): JsonSGTest with `[JsonSerializable]` compiles and runs end-to-end through CIL2CPP.
- Codegen performance — **✅ optimized** (2026-03-19): NuGetSimpleTest 361s→103s (-72%). Four optimizations: CppNameMapper caching, incremental CreateGenericSpecializations, Parallel.For method generation, O(n²) BuildTypeInfoExprLookup elimination. Two correctness bugs found and fixed (fixpoint termination + duplicate pending keys).

---

## Phase 1: Foundation ✅

- RuntimeType = Type alias (matching `Il2CppReflectionType`)
- Handle type removal (RuntimeTypeHandle/MethodHandle/FieldHandle → intptr_t)
- AggregateException / SafeHandle / Thread.CurrentThread TLS / GCHandle weak reference

## Phase 2: Middle Layer Unlock ✅

- CalendarId/EraInfo/DefaultBinder/DBNull etc. CLR internal type removal (27 → 6)
- Reflection type aliases (RuntimeMethodInfo → ManagedMethodInfo etc.)
- WaitHandle OS primitives / P/Invoke calling conventions / SafeHandle ICall completion

## Phase 3: Compiler Pipeline Quality ✅

**Goal**: Improve IL translation rate — fix root causes blocking BCL IL compilation

**Results**: 4,402 → 2,860 stubs (-1,542, -35.1%), IL translation rate 88.8%. Further optimization moved to Phase A.

| # | Task | Impact | Status | Description |
|---|------|--------|--------|-------------|
| 3.1 | SIMD scalar fallback | ✅ Done | ✅ | Vector64/128/256/512 struct definitions |
| 3.2a | ExternalEnumTypes fix | -386 | ✅ | External assembly enum types registered to knownTypeNames |
| 3.2b | Ldelem_Any/Stelem_Any fix | -114 | ✅ | Generic array element access instruction support |
| 3.2c | IsResolvedValueType correction | Correctness | ✅ | IsPrimitive includes String/Object → changed to IsValueType |
| 3.2d | IsValidMergeVariable correction | -45 | ✅ | Forbid &expr as branch merge assignment target |
| 3.2e | DetermineTempVarTypes improvement | -17 | ✅ | IRBinaryOp type inference + IRRawCpp pattern inference |
| 3.2f | Stub classification improvement | Diagnostics | ✅ | GetBrokenPatternDetail covers all HasKnownBrokenPatterns patterns |
| 3.2g | Integration test fixes | 69/69 | ✅ | Fixed 7 C++ compilation error patterns (void ICall/TypeInfo/ctor/Span/pointer types) |
| 3.2h | StackEntry typed stack + IRRawCpp type annotation | -98 | ✅ | Stack\<StackEntry\> type tracking + IRRawCpp ResultVar/ResultTypeCpp completion |
| 3.3 | UnknownBodyReferences fix | 506→285 | ✅ | Gate reordering + knownTypeNames sync + opaque stubs + SIMD/array type detection |
| 3.4 | UndeclaredFunction fix | 222→151 | ✅ | Broadened calledFunctions scan + multi-pass discovery + diagnostic filter fix (remaining 151 are generic specialization gaps) |
| 3.5 | FilteredGenericNamespaces relaxation | Cascade unlock | Deferred | Gradually relax safe namespaces (System.Diagnostics etc.) |
| 3.6 | KnownBrokenPattern refinement + unbox fix | 637→604 | ✅ | Classification improvement + array type fix + self-recursion false positive removal + unbox generic trailing underscore fix |
| 3.7 | Nested generic type specialization | -26 | ✅ | CreateNestedGenericSpecializations: Dictionary.Entry, List.Enumerator etc. |
| 3.8 | Pointer local fix + opaque stubs | -46 | ✅ | HasUnknownBodyReferences dead code fix + opaque struct generation for value type locals + pointer local forward declaration |
| 3.9 | Nested-nested type fixpoint iteration + param/return type stubs | -46 | ✅ | CreateNestedGenericSpecializations fixpoint loop + opaque struct scan for method params and return types |
| 3.10 | Generic specialization param resolution + stub gate correction | -84 | ✅ | ResolveRemainingGenericParams extended to all instruction types + func ptr false positive fix + delegate arg type conversion |
| 3.11 | Scalar alias + Numerics DIM + TimeZoneInfo | -80 | ✅ | m_value scalar interception + primitive Numerics DIM passthrough + TimeZoneInfo false positive removal |
| 3.12 | Generic specialization mangled name resolution | -49 | ✅ | Arity-prefixed mangled name resolution (_N_TKey → _N_System_String), 29 unresolved generic → 0 |
| 3.13 | Transitive generic discovery | +1319 compiled | ✅ | Fixpoint loop discovers 207 new types 1393 methods, gate hardening 5 patterns (Object*→f_, MdArray**, MdArray*→typed, FINALLY without END_TRY, delegate invoke Object*→typed) |
| 3.14 | Delegate invoke typed pointer cast | -28 | ✅ | IRDelegateInvoke adds (void*) intermediate cast for all typed pointer args, fixing Object*→String* C2664 |
| 3.15 | RenderedBodyError false positives + ldind.ref type tracking | RE -41 | ✅ | 5 fixes: non-pointer void* cast RHS check, static_cast skip, TypeHandle→KnownBroken, ldind.ref StackEntry typed deref, Span byref detection |
| 3.15b | IntPtr/UIntPtr ICall + intptr_t casting + RE reclassification | RE 113→0 | ✅ | IntPtr/UIntPtr ctor ICall + intptr_t arg/return casting + 113 RE→KBP method-level reclassification (stopgap, root cause pending) |
| 3.16 | Fix reclassified RE root causes | -10 | ✅ | GuidResult/RuntimeType.SplitName RE root cause fix + KBP false positive removal (TimeSpanFormat/Number/GuidResult/SplitName) |
| 3.17 | IRBuilder generic specialization completion | -57 | ✅ | Nested type method body compilation + DiscoverTransitiveGenericTypesFromMethods + GIM argument scanning |
| 3.18 | ICall-mapped method body skip + ThreadPool ICall | -143 | ✅ | HasICallMapping methods skip body conversion (-124 MissingBody) + ThreadPool/Interlocked ICall (-19) |
| 3.19 | Stub budget ratchet update | Diagnostics | ✅ | stub_budget.json baseline from 3,310 → 3,147 |
| 3.20 | KBP false positive audit | -287 | ✅ | Removed 30+ overly broad method-level KBP checks (Numerics DIM -60, DISH -35, Span/IAsyncLocal -58, CWT -23, P/Invoke/Buffers/Reflection -68 etc.). RenderedBodyError 0→90 (real codegen bugs correctly exposed) |

### Phase X: Compromise Code Removal ✅ (2026-03)

> **All stub/gate/workaround infrastructure has been removed.** ~5,500 lines of compromise code deleted.

**Deleted systems**:
- **Gate system** (5 pre-render gates, trial render, post-render validation) from Header.cs/Source.cs
- **StubAnalyzer.cs** (634 lines) — root-cause analysis, call graph, budget checking
- **KnownStubs.cs** (521 lines) — hand-written AOT replacement bodies
- **`__SIMD_STUB__` sentinel** (~20 checks in IRBuilder.Emit/Methods) — dead-code propagation workaround
- **Namespace blacklists** (non-AOT-incompatible exclusions like System.Data, Interop/*) — removed
- **Generic filter blacklists** (FilteredGenericNamespaces, VectorScalarFallbackTypes) — deleted
- **`--analyze-stubs` / `--stub-budget` CLI options** — deleted

**Replaced by**:
- **Demand-driven generic discovery**: types discovered on-demand when method bodies are compiled (no bulk pre-scanning)
- **Specialized method reachability**: `_calledSpecializedMethods` HashSet tracks which methods on which generic specializations are actually called — 77% generic methods skipped
- **Interface-dispatch-aware pruning**: non-dispatched generic interfaces not materialized — -311 types (-7.5%)
- **FeatureSwitchResolver**: SIMD IsSupported=false dead-branch elimination via brfalse patterns
- **Self-referential generic detection**: replaces arbitrary MaxGenericNestingDepth=5

**Impact**: HelloWorld -21% codegen, 27799 → 26248 methods, 315K → 260K lines.

---

## Phase 4: Viable Types Return to IL (40 → 32) ✅

**Goal**: Remove 8 RuntimeProvided types — **Complete**

| # | Task | Removed | Feasibility | Description |
|---|------|---------|-------------|-------------|
| 4.1 | IAsyncStateMachine → IL | 1 | ✅ Done | Pure interface, removed RuntimeProvided + deleted task.h alias |
| 4.2 | CancellationToken → IL | 1 | ✅ Done | Only has f_source pointer, struct generated from Cecil |
| 4.3-7 | WaitHandle hierarchy ×6 → IL | 6 | ✅ Done | struct from Cecil, TypeInfo from IL, WaitOneCore ICall retained; POSIX Mutex/Semaphore have TODO (currently stub impl) |

**Prerequisite**: Phase 3 compiler quality sufficient for BCL WaitHandle/CancellationToken IL to compile correctly.

## Phase 5: Async Architecture Refactoring (Long-term, 32 → 25) — Downgraded to Phase F.2

> **Downgrade reason**: async/await already works (true concurrency + thread pool + combinators). Task struct refactoring is internal quality optimization with almost no contribution to "compile any project".
> See [phase_v1_analysis.md](phase_v1_analysis.md)

**5.1 Complete** ✅: TplEventSource no-op ICall + ThreadPool ICall + Interlocked.ExchangeAdd — 66 methods compile from IL, 65 stubbed
**5.2-5.5 Deferred**: Task struct refactoring → downgraded to Phase F.2 (performance optimization phase)

---

## Future Phases (Phase A-G)

> **Core approach**: Not "write ICall for every feature", but "fix the compiler to let BCL IL compile".
> FileStream / Socket / HttpClient / JSON have complete IL implementations in .NET BCL, ultimately calling OS APIs via P/Invoke. P/Invoke is already available. The problem is compiler bugs preventing intermediate BCL methods from being translated from IL.

### Phase A: Compiler Finalization — Fix Stub Root Causes ✅

**Goal**: Translation rate > 92%, stubs < 2,000 — **Achieved** (1,478 at Phase A end; currently 1,280 after Phase X cleanup + demand-driven generics)

**Results**: 2,777 → 1,478 stubs (-1,299, -46.8%) at Phase A completion

| # | Task | Impact | Status |
|---|------|--------|--------|
| A.1 | Fix RenderedBodyError | RE 90→36 | ✅ |
| A.2 | Broader EventSource no-op ICall + modreq/modopt | -137 | ✅ |
| A.3 | Fix UndeclaredFunction (generic discovery, ICalls) | UF 287→66 | ✅ |
| A.4 | KBP audit + false positive removal | KBP 611→654 (correct) | ✅ |
| A.5 | Fix UBR + UP | UBR 41→0, UP 22→0 | ✅ |
| A.6 | Fix stub double-counting (gate-failed MissingBody) | -698 MissingBody | ✅ |
| A.7 | Callee scan optimization (gated method skip + UF prediction) | -40 cascade | ✅ |

**Output**: M1 milestone achieved. BCL intermediate method chains significantly unlocked.

### Phase B: BCL Chain Validation — Streaming I/O ✅ (Windows)

**Goal**: `File.OpenRead(path)` / `new StreamReader(path)` compile from BCL IL and run

**Strategy**: **Don't write new ICalls**. Start from FileStream BCL IL chain, find break points top-down, fix compiler.

**Platform differences**:
- **Windows**: FileStream → P/Invoke to **kernel32.dll** (CreateFile/ReadFile/WriteFile) — P/Invoke already available
- **Linux**: FileStream → P/Invoke to **System.Native** (.NET's official POSIX wrapper layer, ~30 .c files, [open source in dotnet/runtime](https://github.com/dotnet/runtime)) — needs native library integration

| # | Task | Estimate | Status | Description |
|---|------|----------|--------|-------------|
| B.1 | Trace FileStream IL dependency chain | Medium | ✅ | FileStream → FileStreamStrategy → SafeFileHandle → Interop.Kernel32 — full chain already compiled from IL |
| B.2 | Fix stubs found in chain | High | ✅ | KBP Pattern 8 fix (-376 stubs), Buffer.Memmove sizeof fix, SafeFileHandle/ThreadPool/ASCII ICalls |
| B.3 | SpanHelpers scalar search interception | Medium | ✅ | BCL SIMD branches → AOT scalar templates (IndexOfAny/IndexOf/LastIndexOf/IndexOfAnyExcept) |
| B.4 | End-to-end FileStreamTest | Low | ✅ | FileStream Write/Read, StreamWriter, StreamReader.ReadLine — all pass (Windows) |
| B.5 | System.Native native library integration (Linux) | Medium | **Deferred** | Like BoehmGC/ICU: extract ~30 .c files from dotnet/runtime, FetchContent compile. *Deferred until must-implement goals are complete.* |
| B.6 | Remove File.ReadAllText/WriteAllText ICall bypass | Low | Blocked | HACK cleanup: File.ReadAllText works via BCL IL, but File.ReadAllBytes hangs (FileStream.Read(byte[]) code path bug). Need to fix before full removal. |
| B.7 | Integration test suite + baselines | Low | ✅ | FileStreamTest added as Phase 9 (39/39 integration tests pass) + UF/RE stub reduction (-94 stubs) |

**Prerequisites**: Phase A ✅
**Output**: FileStream end-to-end compiled from BCL IL — **Windows working** ✅, Linux needs System.Native

### Phase C: BCL Chain Extension — Networking ✅

**Goal**: `HttpClient.GetStringAsync("http://...")` compiles from BCL IL

**Strategy**: Same as Phase B — trace BCL IL chain, fix break points.

**Platform**: Windows → P/Invoke to **ws2_32.dll** (Winsock2). Linux deferred (needs System.Native, Phase B.5).

| # | Task | Estimate | Status | Description |
|---|------|----------|--------|-------------|
| C.1 | Trace Socket BCL IL chain | Medium | ✅ | Socket → SafeSocketHandle → Interop.Winsock P/Invoke (Windows). RuntimeType fix + ObjectHasComponentSize ICall |
| C.2 | TCP socket lifecycle | High | ✅ | Full TCP loopback: bind/listen/connect/accept/send/recv via Winsock P/Invoke. Gate patterns for pointer locals, Array ref/out, delegate cross-scope |
| C.3 | HttpClient construction | High | ✅ | HttpClient → SocketsHttpHandler → HttpConnectionSettings → TimeSpan/Int128. 5 compiler/runtime fixes: signed comparison (`clt`/`cgt` → `signed_lt/gt`), Exception.GetType ICall, SR resource string ICall, RunClassConstructor stub, generic nested type name mangling (boundary-aware regex) |
| C.4 | DNS resolution | Low | ✅ | `Dns.GetHostAddresses` via Winsock GetAddrInfoW P/Invoke. `dup` opcode decoupling fix for `a[i++]` patterns |
| C.5 | Integration tests | Low | ✅ | SocketTest (TCP loopback + DNS) + HttpTest (HttpClient construction) — all integration tests pass |
| C.6 | Full HTTP GET (plaintext) | High | ✅ | HttpGetTest: full `HttpClient.GetStringAsync("http://...")` async request/response chain working. HttpsGetTest: HTTPS via SChannel (secur32/sspicli) also working. Both pass as integration tests. |

**Prerequisites**: Phase B ✅
**Output**: Socket + DNS + HttpClient HTTP GET + HTTPS GET all working (Windows). 93/93 integration tests pass.

### ThreadPool Architecture Assessment (2026-03-02)

> **Decision**: No restructure needed short-term. Current implementation is **correct for scope**; limitations are performance, not correctness.

**Current architecture** (`runtime/src/async/threadpool.cpp`, ~125 lines):
- Fixed-size worker pool (`std::thread` × `hardware_concurrency`)
- Single global FIFO queue (`std::queue` + `std::mutex` + `std::condition_variable`)
- All async/await, Task combinators, continuations work with true concurrency
- BCL ThreadPool ICalls (9 entries) are intentional no-ops — CIL2CPP routes work through its own C++ pool

**What works** (verified by 576 runtime tests + 93 integration tests):
- `queue_work()` executes on worker threads (100 concurrent items ✅)
- Task.Run / task_delay / task_when_all / task_when_any ✅
- Continuations: thread-safe linked list, 400 concurrent registrations ✅
- Async/await state machines from BCL IL ✅

**Missing .NET features** (performance, not correctness):
- Hill climbing (dynamic thread count) — fixed count is adequate for <100 concurrent tasks
- Work stealing (per-thread LIFO queues) — single queue has higher contention but works
- Thread injection (`RequestWorkerThread` is no-op) — nested `Task.Run` deadlock risk under extreme load, mitigated by BCL synchronous fallback
- Config/metrics feedback loops — all no-ops, BCL hardcoded defaults survive

**When to restructure** (Phase F.2):
- If Task struct migrates to BCL IL (requires full TPL dependency chain: ThreadPool + TaskScheduler + ExecutionContext + SynchronizationContext)
- Or if performance benchmarks show contention on 4+ cores
- Intermediate step: implement work-stealing queues (~200 lines) before full Task migration

### Phase H: Quality Convergence (parallel with C.6/D)

**Goal**: Make existing capabilities predictable, explainable, regression-testable. Motivated by external code review identifying "compilable but behaviorally divergent" icall simplifications and undocumented limitations.

**Strategy**: NOT a "stop and clean" phase. Runs in parallel with feature work (C.6/D), prioritized by user-perceivable impact.

| # | Task | Priority | Status | Description |
|---|------|----------|--------|-------------|
| H.1 | TypeCode ICall fix + IsPublic/IsNestedPublic | High | ✅ | Map TypeInfo name → TypeCode enum (17 primitives) + TypeFlags::Public/NestedPublic from Cecil metadata. Fixes `Convert.*`, `String.Format`, serializer type switches, `Type.IsPublic` |
| H.2 | RenderedBodyError reduction | High | ~85% done | 116 → 17 RE stubs. Remaining 17 are real codegen edge cases. Target: 0. |
| H.3 | Remove File ICall bypass (B.6) | High | Blocked | Debug `FileStream.Read(byte[])` hang, then delete 12 File ICalls. BCL IL handles all encoding correctly via StreamReader |
| H.4 | Platform compatibility docs | Medium | ✅ | Support matrix with promise levels: Full / Functional / Stub / Not implemented |
| H.5 | Reflection status docs | Medium | ✅ | Expected-vs-actual table for all 23 reflection icalls + prerequisite phase for full fix |
| H.6 | Codegen bug reproduction tests | Low | Pending | Minimal C# test cases for each FIXME gate pattern (regression anchors) |

**Prerequisites**: None (runs in parallel)
**Output**: Documented behavioral boundaries, reduced silent degrade risk, RenderedBodyError reduction

### Phase D: NativeAOT Metadata & Ecosystem Validation (START IMMEDIATELY — parallel with C.6)

**Goal**: Support trimming annotations + validate NuGet ecosystem — **this is the single biggest jump toward "compile any NativeAOT project"**

**Why D is critical now**: Without `[DynamicallyAccessedMembers]` support, ReachabilityAnalyzer silently tree-shakes types that NuGet packages need at runtime. Without ILLink feature switches, System.Text.Json source generator paths don't activate. Every project with NuGet dependencies hits these issues. D unlocks the entire NuGet ecosystem; C.6 only unlocks HTTP.

| # | Task | Estimate | Status | Description |
|---|------|----------|--------|-------------|
| D.0 | NuGet package integration tests | Medium | ✅ Complete | NuGetSimpleTest (Phase 14): Newtonsoft.Json 13.0.3 (3.1M lines, 103s after optimization). DITest (Phase 15): DI+Logging+Console (3 NuGet packages, constructor injection, singleton/transient). JsonSGTest (Phase 13): System.Text.Json SG. 93/93 integration tests. |
| D.1 | `[DynamicallyAccessedMembers]` parsing | Medium | ✅ Complete (validated) | ReachabilityAnalyzer.cs — full 13-flag DAM parsing + SeedDynamicallyAccessedMembers(). Includes DAM on generic method/type parameters (critical for DI: `AddSingleton<TService, [DAM] TImpl>()` preserves TImpl constructors). CLI `--rdxml`. 7 DAM tests + 14 rd.xml tests. |
| D.2 | rd.xml parser | Low | ✅ Complete (validated) | RdXmlParser.cs — full XML parsing + PreservationRule mapping. CLI `--rdxml` option wired in Program.cs (codegen + analyze commands). |
| D.3 | ILLink feature switch substitution | Medium | ✅ Active | FeatureSwitchResolver.cs (10 AOT defaults) + IRBuilder.Methods.cs:1372-1386 (Ldsfld substitution). Automatically active in all builds. |
| D.4 | AOT compatibility warnings | Low | Pending | Report `[RequiresUnreferencedCode]` call chains |
| D.5 | Source generator validation | Medium | ✅ Complete (runs) | JsonSGTest (Phase 13): `[JsonSerializable]` + AppJsonContext SG output compiles and runs through CIL2CPP end-to-end. NuGetSimpleTest (Phase 14): Newtonsoft.Json 13.0.3 also fully validated. |

**Prerequisites**: None (parallelizable with C.6)
**Output**: DI + JSON (SG) + Logging compilable; NuGet packages work correctly with tree-shaking

### Phase C.7: P/Invoke Marshaling Completeness (after C.6)

**Goal**: `[MarshalAs]` + `[Out]` + array marshaling for NuGet ecosystem and System.Native

**Why needed**: P/Invoke has CharSet/CallingConvention/SetLastError. `[MarshalAs]` attribute parsing and type mapping are now complete (C.7.1). Remaining: `[Out]`/`[In]` copy-back semantics and array marshaling for System.Native and NuGet native interop packages.

| # | Task | Estimate | Status | Description |
|---|------|----------|--------|-------------|
| C.7.1 | `[MarshalAs]` attribute parsing | Medium | ✅ Done | Cecil MarshalInfo parsing (IRBuilder.Methods.cs:123-143), 21-type MarshalAsType enum (PInvokeEnums.cs), full type mapping in GetPInvokeNativeType() (Source.cs:3024-3094). LPStr/LPWStr/Bool/integers all working. |
| C.7.2 | `[Out]`/`[In]` parameter direction | Low | Pending | Distinguish parameter direction for correct copy-back semantics |
| C.7.3 | Array marshaling | Medium | In progress | SizeParamIndex parsed (IRMethod.cs:136) but codegen incomplete. Fixed-size arrays in structs, `[MarshalAs(UnmanagedType.LPArray)]` runtime marshaling pending. |

**Prerequisites**: Phase C ✅
**Output**: P/Invoke compatible with System.Native declarations and NuGet native interop packages

### Phase E: Native Library Integration — TLS/zlib (link only, don't rewrite)

**Goal**: HTTPS + Compression

**Note**: .NET BCL's TLS/zlib have complete IL, calling .NET-specific native libraries via P/Invoke (same pattern as System.Native, all extracted from [dotnet/runtime](https://github.com/dotnet/runtime)):
- TLS → SChannel (Windows, `secur32.dll`/`schannel.dll` P/Invoke — **no FetchContent needed**) / `System.Security.Cryptography.Native.OpenSsl` (Linux)
- zlib → `System.IO.Compression.Native` (.NET's zlib wrapper)

**Platform strategy**: Windows TLS uses SChannel (already part of OS, same pattern as kernel32/ws2_32). Linux TLS uses OpenSSL (requires FetchContent). Split E.win vs E.linux to ship Windows HTTPS earlier.

| # | Task | Estimate | Description |
|---|------|----------|-------------|
| E.win | SChannel TLS (Windows) | Medium | ✅ SslStream → P/Invoke to `secur32.dll`/`schannel.dll`. HttpsGetTest passes as integration test. |
| E.linux | OpenSSL TLS (Linux) | High | **Deferred.** Extract `System.Security.Cryptography.Native.OpenSsl` from dotnet/runtime, FetchContent + link OpenSSL |
| E.2 | System.IO.Compression.Native integration | Low | Extract from dotnet/runtime, embed zlib |
| E.3 | Remove corresponding InternalPInvokeModules | Low | Let BCL P/Invoke declarations generate normally |
| E.4 | Regex interpreter BCL IL validation | Medium | Non-Compiled mode doesn't depend on Reflection.Emit |
| E.5 | End-to-end tests | Low | HTTPS GET + JSON deserialization |

**Prerequisites**: Phase C (TLS needs Socket) + Phase D (JSON needs metadata awareness) + Phase C.7 (MarshalAs needed for native lib P/Invoke)
**Output**: `HttpClient.GetStringAsync("https://...")` + `JsonSerializer.Deserialize<T>()` available

### Phase F: Performance & Advanced

**Goal**: Translation rate > 95%

| # | Task | Estimate | Description |
|---|------|----------|-------------|
| F.1 | SIMD scalar fallback path completion | High | ✅ Substantial. 4-layer dead-code elimination (FeatureSwitchResolver + IR constant propagation + container type leak fix + render-time replacement). HttpsGetTest SIMD errors 303→0. Remaining SIMD stubs are KBP dead-branch residuals, not blocking. |
| F.2 | Task struct refactoring (from Phase 5.2-5.5) | High | **Technical debt.** Reduce RuntimeProvided 32→25. Async works correctly, but 7 types (Task + 6 async deps) remain as C++ runtime structs. Current test coverage may not exercise all edge cases — expanding NuGet validation may surface issues. Address when test coverage is broad enough to validate the migration. |
| F.3 | Incremental compilation | Medium | **Deferred.** IR/codegen caching (performance optimization) |
| F.4 | Reflection model evaluation (from Phase 6) | Medium | **Deferred.** Evaluate QCall alternatives |

**Prerequisites**: Phase A-E core functionality complete
**Output**: Translation rate > 95%

### Phase G: Productization

**Goal**: Shippable tool

| # | Task | Estimate |
|---|------|----------|
| G.1 | CI/CD (GitHub Actions: Win + Linux) | Medium |
| G.2 | 10+ real NuGet package compilation validation | High |
| G.3 | Self-contained deployment mode + RID detection | Medium |
| G.4 | Documentation completion | Medium |

---

## Dependency Graph

### Must-Implement (Windows x64)

```
Phase 1-4 ✅  →  Phase A ✅  →  Phase B ✅ (Windows)
       ↓
   ┌── Phase C.6 ✅ (Full HTTP GET) ────────────┐
   │   Phase E.win ✅ (SChannel TLS)             │ ← parallel
   │      ↓                                     │
   │   Phase C.7.2/C.7.3 ([Out]/[In] + array)  Phase D ✅ (NativeAOT metadata + NuGet)
   │      ↓                                     │    D.4 AOT warnings (low priority)
   │   Phase E.2 (zlib compression)              │
   │      ↓                                     │
   └──────┴─────── convergence ─────────────────┘
                        ↓
              Phase H.2 (RenderedBodyError → 0) — continuous
              Phase F.1 ✅ (SIMD: 4-layer elimination complete)
                        ↓
              Phase G.2 (10 NuGet packages — PRIMARY DRIVER)
                        ↓
              Phase G (Productization: CI/CD + deployment)
```

### Deferred (after Must-Implement)

```
Phase B.5  (System.Native — Linux/macOS I/O + network)
Phase E.linux (OpenSSL TLS — Linux HTTPS)
Phase F.2  (Task struct refactoring — internal quality)
Phase F.3  (Incremental compilation — performance)
Phase F.4  (Full reflection model — QCall alternatives)
32-bit targets (ARM/x86 pointer size)
macOS support (Objective-C bridge)
```

---

## Milestones

| Milestone | Criteria | Phase | Status |
|-----------|---------|-------|--------|
| **M1: Compiler Maturity** | stubs < 2,000, translation rate > 92% | A | ✅ (1,280 stubs, ~95%+) |
| **M2: File I/O** | FileStream/StreamReader compile from BCL IL and run | B | ✅ Windows |
| **M3: Networked Apps** | HttpClient HTTP GET compiles from BCL IL and runs | C.6 | ✅ HTTP + HTTPS GET working |
| **M3.5: REST Client** | HTTP GET + JSON serialization end-to-end | C.6+D | ✅ JsonSGTest + NuGetSimpleTest (Newtonsoft.Json) |
| **M4: Library Ecosystem** | Project with 3+ NuGet PackageReferences compiles and runs | D+G.2 | ✅ DITest (DI+Logging+Console = 3 NuGet packages) |
| **M5: Production-Grade** | HTTPS + Compression + DI/logging ecosystem | E+F | In progress (HTTPS ✅, DI/logging ✅, SIMD ✅, codegen perf ✅, compression pending) |
| **M6: Release** | CI/CD + 10 real NuGet package validation | G | Not started |

## Metric Definitions

### Compilation Metrics

| Metric | Definition | Current | Phase A Target | Long-term Target |
|--------|-----------|---------|---------------|-----------------|
| IL translation rate | (total_methods - stubs) / total_methods | **~95%+** (1,280 stubs / ~26k methods) | >92% ✅ | >95% |
| RuntimeProvided count | RuntimeProvidedTypes entries | **32** (was 40, -8) | ~32 | ~25 (Phase F.2) |
| CoreRuntime count | Methods fully provided by C++ | 22 | ~22 | ~10 (Phase F.4) |
| ICall count | C++ internal calls | **~490** | ~400 | Stabilize (features come from BCL IL, not ICall) |

### Metric Schema (for consistent reporting)

| Dimension | Value | Notes |
|-----------|-------|-------|
| **Assembly** | HelloWorld (primary), SocketTest (secondary) | All headline metrics use HelloWorld unless stated otherwise |
| **Source of truth** | Generated `*_stubs.cpp` files | `stub_budget.json` was removed in Phase X |
| **Categories** | 7 stub root cause categories | MissingBody, KnownBrokenPattern, RenderedBodyError, ClrInternalType, UndeclaredFunction, UnknownParameterTypes, UnknownBodyReferences |
| **Translation rate** | `1 - (stub_total / total_methods)` | `total_methods` from IRModule after all passes |
| **Commit binding** | Include commit hash + date when citing metrics in docs | Prevents stale numbers persisting across phases |

> **Note**: Stub counts may temporarily increase when compiler improvements expand compilation scope (e.g., fixing C2362 switch gate made more methods compilable, exposing their callees as new stubs). This is positive progress — more methods compile — even though the headline number rises.

---

## Key Decision Summary

| Decision | Result | Rationale |
|----------|--------|-----------|
| RuntimeType | Type alias | Matches IL2CPP `Il2CppReflectionType` |
| Reflection types | Keep CoreRuntime | .NET 8 BCL reflection IL deeply depends on QCall/MetadataImport, cannot IL-compile short-term |
| Task | Keep RuntimeProvided (short-term) | 4 custom runtime fields + std::mutex* + MSVC padding, long-term needs architectural refactoring |
| ThreadPool | Keep custom C++ impl (short-term) | Fixed-size pool + global FIFO queue is correct for current scope. BCL ThreadPool ICalls are intentional no-ops (config/metrics/injection). Missing: hill climbing, work stealing, per-thread queues — performance optimization only, not correctness. Restructure deferred to Phase F.2 alongside Task refactoring. |
| WaitHandle | Target IL + ICall (Phase 4) | Simple struct, BCL IL compilable, needs 8 OS primitive ICall registrations |
| SIMD | Scalar fallback struct + IsSupported=false | BCL has non-SIMD fallback paths |
| File I/O ICall | HACK — remove after Phase B | File.ReadAllText etc. 12 ICalls bypass FileStream IL chain, violates IL-first |
| Network layer | BCL IL natural compilation | BCL has built-in cross-platform branching |
| Regex | Interpreter mode + source generator | Compiled mode uses Reflection.Emit → AOT incompatible |
| IL2CPP reference | Reference but don't blindly copy | IL2CPP based on Mono BCL, .NET 8 dependency chains differ significantly |

## CLR Internal Types (Permanently Retained as Stubs)

| Type | Description |
|------|-------------|
| QCallTypeHandle / QCallAssembly / ObjectHandleOnStack / MethodTable | CLR JIT-specific bridges |
| MetadataImport / RuntimeCustomAttributeData | CLR internal metadata access |

## Filtered P/Invoke Modules (InternalPInvokeModules)

> These modules are in the `CppCodeGenerator.Source.cs` `InternalPInvokeModules` blacklist; P/Invoke declarations are not generated.
> Integration approach: Extract .c source from [dotnet/runtime](https://github.com/dotnet/runtime), FetchContent compile, then remove from blacklist.

| Module | Function | Unlock Phase | Integration Method |
|--------|----------|-------------|-------------------|
| `System.Native` | POSIX file/process/network (~30 .c) | Phase B | FetchContent from dotnet/runtime |
| `System.IO.Compression.Native` | zlib wrapper | Phase E | FetchContent + embedded zlib |
| `System.Globalization.Native` | ICU wrapper | ✅ Already have ICU integration | — |
| `System.Security.Cryptography.Native.OpenSsl` | OpenSSL/TLS | Phase E | FetchContent + link OpenSSL |
| `System.Net.Security.Native` | GSSAPI/TLS | Phase E | FetchContent |
| `QCall` / `QCall.dll` | CLR internal bridge | Permanently retained | Cannot integrate (CLR JIT-specific) |
| `ucrtbase` / `ucrtbase.dll` | CRT | ✅ Already linked | — |
