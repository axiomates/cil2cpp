# Development Roadmap

> Last updated: 2026-02-28
>
> [中文版 (Chinese)](roadmap.zh-CN.md)

## Design Principles

### Core Goal

**Only content that truly cannot be compiled from IL uses C++ runtime mappings; everything else is translated from BCL IL to C++.**

Using Unity IL2CPP as a reference architecture, but not blindly copying — IL2CPP uses Mono BCL (with far simpler dependency chains than .NET 8), and its source comes from community decompilation (may be incomplete). We perform first-principles analysis based on .NET 8 BCL's actual dependency chains.

### Five Guidelines

1. **IL-first**: Everything that can be compiled from BCL IL should be compiled — including FileStream, Socket, HttpClient, JSON, etc. These BCL types have complete IL implementations and ultimately call OS APIs via P/Invoke. **The way to improve capability is to fix compiler bugs to let BCL IL compile, not to reimplement functionality with ICalls.**
2. **ICall as bridge**: C++ runtime only exposes low-level primitives via ICall (Monitor, Thread, Interlocked, GC); BCL IL calls these primitives. **Don't write ICall replacements for high-level BCL functionality.**
3. **Compiler quality-driven**: The way to improve IL translation rate is to fix compiler bugs, not add RuntimeProvided types
4. **First-principles judgment**: Every RuntimeProvided type must have a clear technical reason (runtime directly accesses fields / BCL IL references CLR internal types / embeds C++ types)
5. **Native library integration**: .NET BCL calls platform native libraries via P/Invoke (kernel32, ws2_32, System.Native, OpenSSL, etc.). CIL2CPP integrates these libraries (similar to BoehmGC/ICU FetchContent pattern), letting BCL P/Invoke link naturally rather than bypassing with ICalls.

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

### Types Migrated to IL (8, Phase IV ✅)

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

- **Current**: 32 entries (Phase IV complete: removed IAsyncStateMachine + CancellationToken + WaitHandle×6 = -8)
- **Short-term target achieved**: 40 → 32 (-8 RuntimeProvided types)
- **Long-term**: 32 → 25 (after Task architectural refactoring, remove Task+async deps+CTS = 7)

### Unity IL2CPP Reference (comparison only, not blindly copied)

> **Note**: IL2CPP uses Mono BCL; .NET 8 BCL's dependency chains are far more complex than Mono's. The following comparison is for reference only.
> IL2CPP source comes from community-decompiled libil2cpp headers, may be incomplete.

IL2CPP Runtime struct: `Il2CppObject` / `Il2CppString` / `Il2CppArray` / `Il2CppException` / `Il2CppDelegate` / `Il2CppMulticastDelegate` / `Il2CppThread` / `Il2CppReflectionType` / `Il2CppReflectionMethod` / `Il2CppReflectionField` / `Il2CppReflectionProperty` (~12)

IL2CPP compiles from IL: Task/async entire family, CancellationToken/Source, WaitHandle hierarchy — but this is based on Mono's simpler BCL, not directly comparable to .NET 8.

---

## Current Status

### RuntimeProvided Types: 32 entries (Phase IV complete -8, long-term target 25)

See "RuntimeProvided Type Classification" section above.

### Stub Distribution (HelloWorld, 1,478 stubs, ~95.2% translation rate)

| Category | Count | % | Nature |
|----------|-------|---|--------|
| MissingBody | 666 | 45.1% | No IL body (abstract/extern/JIT intrinsic) — most are legitimate |
| KnownBrokenPattern | 620 | 41.9% | SIMD 333 + line-level body scan patterns + TypeHandle/MethodTable |
| UndeclaredFunction | 68 | 4.6% | Generic specialization missing (IRBuilder didn't create specialization type) |
| ClrInternalType | 96 | 6.5% | QCall/MetadataImport CLR JIT-specific types |
| RenderedBodyError | 28 | 1.9% | Codegen bugs (PInvoke callbacks, reflection aliases, pointer casts) |

**Unfixable or deferred**: SIMD (333+) needs intrinsics support or runtime fallback. CLR internal types (96) are permanently retained.

**IL translation rate**: ~95.2% (29,249 compiled / 30,727 total methods). Phase A: 2,777 → 1,537; Phase B: 1,537 → 1,478.
**Tests**: 1,240 C# + 592 C++ + 39 integration — all passing.

### Implemented Architecture Capabilities

- **Multi-project compilation**: AssemblySet + deps.json + CIL2CPPAssemblyResolver on-demand loading for ProjectReference assemblies (MultiAssemblyTest verified)
- **Source Generator awareness**: SG output is standard IL, dotnet build already compiles it, CIL2CPP reads directly
- **P/Invoke complete**: Declaration generation + calling conventions + CharSet marshaling; OS APIs like kernel32/user32 can be called directly
- **100% IL opcode coverage**: All ~230 ECMA-335 opcodes implemented

### Distance to Final Goal

| Project Type | Est. Completion | Blockers |
|-------------|----------------|----------|
| Simple console apps | ~95% | Basic BCL chain works, 95.2% translation rate |
| Library projects | ~85% | Collections, generics, async, reflection all available |
| File I/O apps | ~85% | FileStream/StreamReader BCL IL chain end-to-end (Windows ✅), Linux pending System.Native |
| Network apps | ~10% | Socket/HttpClient BCL IL chain not verified, System.Native not integrated |
| Production-grade apps | ~5% | Needs TLS + JSON + DI etc. complete BCL chains |
| Arbitrary NativeAOT .csproj | ~35% | Compiler mature (1,478 stubs) but native lib integration + NativeAOT metadata pending |

---

## Phase I: Foundation ✅

- Stub dependency analysis tool (`--analyze-stubs`)
- RuntimeType = Type alias (matching `Il2CppReflectionType`)
- Handle type removal (RuntimeTypeHandle/MethodHandle/FieldHandle → intptr_t)
- AggregateException / SafeHandle / Thread.CurrentThread TLS / GCHandle weak reference

## Phase II: Middle Layer Unlock ✅

- CalendarId/EraInfo/DefaultBinder/DBNull etc. CLR internal type removal (27 → 6)
- Reflection type aliases (RuntimeMethodInfo → ManagedMethodInfo etc.)
- WaitHandle OS primitives / P/Invoke calling conventions / SafeHandle ICall completion

## Phase III: Compiler Pipeline Quality ✅

**Goal**: Improve IL translation rate — fix root causes blocking BCL IL compilation

**Results**: 4,402 → 2,860 stubs (-1,542, -35.1%), IL translation rate 88.8%. Further optimization moved to Phase A.

| # | Task | Impact | Status | Description |
|---|------|--------|--------|-------------|
| III.1 | SIMD scalar fallback | ✅ Done | ✅ | Vector64/128/256/512 struct definitions |
| III.2a | ExternalEnumTypes fix | -386 | ✅ | External assembly enum types registered to knownTypeNames |
| III.2b | Ldelem_Any/Stelem_Any fix | -114 | ✅ | Generic array element access instruction support |
| III.2c | IsResolvedValueType correction | Correctness | ✅ | IsPrimitive includes String/Object → changed to IsValueType |
| III.2d | IsValidMergeVariable correction | -45 | ✅ | Forbid &expr as branch merge assignment target |
| III.2e | DetermineTempVarTypes improvement | -17 | ✅ | IRBinaryOp type inference + IRRawCpp pattern inference |
| III.2f | Stub classification improvement | Diagnostics | ✅ | GetBrokenPatternDetail covers all HasKnownBrokenPatterns patterns |
| III.2g | Integration test fixes | 35/35 | ✅ | Fixed 7 C++ compilation error patterns (void ICall/TypeInfo/ctor/Span/pointer types) |
| III.2h | StackEntry typed stack + IRRawCpp type annotation | -98 | ✅ | Stack\<StackEntry\> type tracking + IRRawCpp ResultVar/ResultTypeCpp completion |
| III.3 | UnknownBodyReferences fix | 506→285 | ✅ | Gate reordering + knownTypeNames sync + opaque stubs + SIMD/array type detection |
| III.4 | UndeclaredFunction fix | 222→151 | ✅ | Broadened calledFunctions scan + multi-pass discovery + diagnostic filter fix (remaining 151 are generic specialization gaps) |
| III.5 | FilteredGenericNamespaces relaxation | Cascade unlock | Deferred | Gradually relax safe namespaces (System.Diagnostics etc.) |
| III.6 | KnownBrokenPattern refinement + unbox fix | 637→604 | ✅ | Classification improvement + array type fix + self-recursion false positive removal + unbox generic trailing underscore fix |
| III.7 | Nested generic type specialization | -26 | ✅ | CreateNestedGenericSpecializations: Dictionary.Entry, List.Enumerator etc. |
| III.8 | Pointer local fix + opaque stubs | -46 | ✅ | HasUnknownBodyReferences dead code fix + opaque struct generation for value type locals + pointer local forward declaration |
| III.9 | Nested-nested type fixpoint iteration + param/return type stubs | -46 | ✅ | CreateNestedGenericSpecializations fixpoint loop + opaque struct scan for method params and return types |
| III.10 | Generic specialization param resolution + stub gate correction | -84 | ✅ | ResolveRemainingGenericParams extended to all instruction types + func ptr false positive fix + delegate arg type conversion |
| III.11 | Scalar alias + Numerics DIM + TimeZoneInfo | -80 | ✅ | m_value scalar interception + primitive Numerics DIM passthrough + TimeZoneInfo false positive removal |
| III.12 | Generic specialization mangled name resolution | -49 | ✅ | Arity-prefixed mangled name resolution (_N_TKey → _N_System_String), 29 unresolved generic → 0 |
| III.13 | Transitive generic discovery | +1319 compiled | ✅ | Fixpoint loop discovers 207 new types 1393 methods, gate hardening 5 patterns (Object*→f_, MdArray**, MdArray*→typed, FINALLY without END_TRY, delegate invoke Object*→typed) |
| III.14 | Delegate invoke typed pointer cast | -28 | ✅ | IRDelegateInvoke adds (void*) intermediate cast for all typed pointer args, fixing Object*→String* C2664 |
| III.15 | RenderedBodyError false positives + ldind.ref type tracking | RE -41 | ✅ | 5 fixes: non-pointer void* cast RHS check, static_cast skip, TypeHandle→KnownBroken, ldind.ref StackEntry typed deref, Span byref detection |
| III.15b | IntPtr/UIntPtr ICall + intptr_t casting + RE reclassification | RE 113→0 | ✅ | IntPtr/UIntPtr ctor ICall + intptr_t arg/return casting + 113 RE→KBP method-level reclassification (stopgap, root cause pending) |
| III.16 | Fix reclassified RE root causes | -10 | ✅ | GuidResult/RuntimeType.SplitName RE root cause fix + KBP false positive removal (TimeSpanFormat/Number/GuidResult/SplitName) |
| III.17 | IRBuilder generic specialization completion | -57 | ✅ | Nested type method body compilation + DiscoverTransitiveGenericTypesFromMethods + GIM argument scanning |
| III.18 | ICall-mapped method body skip + ThreadPool ICall | -143 | ✅ | HasICallMapping methods skip body conversion (-124 MissingBody) + ThreadPool/Interlocked ICall (-19) |
| III.19 | Stub budget ratchet update | Diagnostics | ✅ | stub_budget.json baseline from 3,310 → 3,147 |
| III.20 | KBP false positive audit | -287 | ✅ | Removed 30+ overly broad method-level KBP checks (Numerics DIM -60, DISH -35, Span/IAsyncLocal -58, CWT -23, P/Invoke/Buffers/Reflection -68 etc.). RenderedBodyError 0→90 (real codegen bugs correctly exposed) |

---

## Phase IV: Viable Types Return to IL (40 → 32) ✅

**Goal**: Remove 8 RuntimeProvided types — **Complete**

| # | Task | Removed | Feasibility | Description |
|---|------|---------|-------------|-------------|
| IV.1 | IAsyncStateMachine → IL | 1 | ✅ Done | Pure interface, removed RuntimeProvided + deleted task.h alias |
| IV.2 | CancellationToken → IL | 1 | ✅ Done | Only has f_source pointer, struct generated from Cecil |
| IV.3-7 | WaitHandle hierarchy ×6 → IL | 6 | ✅ Done | struct from Cecil, TypeInfo from IL, WaitOneCore ICall retained; POSIX Mutex/Semaphore have TODO (currently stub impl) |

**Prerequisite**: Phase III compiler quality sufficient for BCL WaitHandle/CancellationToken IL to compile correctly.

## Phase V: Async Architecture Refactoring (Long-term, 32 → 25) — Downgraded to Phase F.2

> **Downgrade reason**: async/await already works (true concurrency + thread pool + combinators). Task struct refactoring is internal quality optimization with almost no contribution to "compile any project".
> See [phase_v1_analysis.md](phase_v1_analysis.md)

**V.1 Complete** ✅: TplEventSource no-op ICall + ThreadPool ICall + Interlocked.ExchangeAdd — 66 methods compile from IL, 65 stubbed
**V.2-V.5 Deferred**: Task struct refactoring → downgraded to Phase F.2 (performance optimization phase)

---

## Future Phases (Phase A-G)

> **Core approach**: Not "write ICall for every feature", but "fix the compiler to let BCL IL compile".
> FileStream / Socket / HttpClient / JSON have complete IL implementations in .NET BCL, ultimately calling OS APIs via P/Invoke. P/Invoke is already available. The problem is compiler bugs preventing intermediate BCL methods from being translated from IL.

### Phase A: Compiler Finalization — Fix Stub Root Causes ✅

**Goal**: Translation rate > 92%, stubs < 2,000 — **Achieved: 1,478 stubs, ~95.2% translation rate**

**Results**: 2,777 → 1,478 stubs (-1,299, -46.8%)

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

### Phase B: BCL Chain Validation — Streaming I/O (in progress)

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
| B.5 | System.Native native library integration (Linux) | Medium | Pending | Like BoehmGC/ICU: extract ~30 .c files from dotnet/runtime, FetchContent compile |
| B.6 | Remove File.ReadAllText/WriteAllText ICall bypass | Low | Blocked | HACK cleanup: File.ReadAllText works via BCL IL, but File.ReadAllBytes hangs (FileStream.Read(byte[]) code path bug). Need to fix before full removal. |
| B.7 | Integration test suite + baselines | Low | ✅ | FileStreamTest added as Phase 9 (39/39 integration tests pass) + UF/RE stub reduction (-94 stubs) |

**Prerequisites**: Phase A ✅
**Output**: FileStream end-to-end compiled from BCL IL — **Windows working** ✅, Linux needs System.Native

### Phase C: BCL Chain Extension — Networking

**Goal**: `HttpClient.GetStringAsync("http://...")` compiles from BCL IL

**Strategy**: Same as Phase B — trace BCL IL chain, fix break points.

**Platform differences**:
- **Windows**: Socket → P/Invoke to **ws2_32.dll** (Winsock2)
- **Linux**: Socket → P/Invoke to **System.Native** (already integrated in Phase B.3)

| # | Task | Estimate | Description |
|---|------|----------|-------------|
| C.1 | Trace Socket BCL IL chain | Medium | Socket → SafeSocketHandle → Interop.Winsock (Win) / System.Native (Linux) |
| C.2 | Fix stubs found in chain | High | Same as B.2 |
| C.3 | Trace HttpClient BCL IL chain | High | SocketsHttpHandler → Socket → MemoryPool → HPack |
| C.4 | DNS P/Invoke verification | Low | getaddrinfo is P/Invoke, should work directly |
| C.5 | End-to-end integration tests | Low | HTTP GET (plaintext) |

**Prerequisites**: Phase B
**Output**: HttpClient plaintext HTTP available

### Phase D: NativeAOT Metadata (parallelizable with Phase C)

**Goal**: Support trimming annotations for reflection-dependent libraries

| # | Task | Estimate | Description |
|---|------|----------|-------------|
| D.1 | `[DynamicallyAccessedMembers]` parsing | Medium | ReachabilityAnalyzer reads custom attributes, preserves annotated members |
| D.2 | rd.xml parser | Low | XML format preservation rules |
| D.3 | ILLink feature switch substitution | Medium | Compile-time constants (`IsDynamicCodeSupported = false`) |
| D.4 | AOT compatibility warnings | Low | Report `[RequiresUnreferencedCode]` call chains |

**Prerequisites**: None
**Output**: DI + JSON (SG) + Logging compilable

### Phase E: Native Library Integration — TLS/zlib (link only, don't rewrite)

**Goal**: HTTPS + Compression

**Note**: .NET BCL's TLS/zlib have complete IL, calling .NET-specific native libraries via P/Invoke (same pattern as System.Native, all extracted from [dotnet/runtime](https://github.com/dotnet/runtime)):
- TLS → `System.Security.Cryptography.Native.OpenSsl` (Linux) / SChannel (Windows, kernel32 P/Invoke)
- zlib → `System.IO.Compression.Native` (.NET's zlib wrapper)

| # | Task | Estimate | Description |
|---|------|----------|-------------|
| E.1 | System.Security.Cryptography.Native integration | High | Extract from dotnet/runtime, link OpenSSL (Linux) / SChannel (Win) |
| E.2 | System.IO.Compression.Native integration | Low | Extract from dotnet/runtime, embed zlib |
| E.3 | Remove corresponding InternalPInvokeModules | Low | Let BCL P/Invoke declarations generate normally |
| E.4 | Regex interpreter BCL IL validation | Medium | Non-Compiled mode doesn't depend on Reflection.Emit |
| E.5 | End-to-end tests | Low | HTTPS GET + JSON deserialization |

**Prerequisites**: Phase C (TLS needs Socket) + Phase D (JSON needs metadata awareness)
**Output**: `HttpClient.GetStringAsync("https://...")` + `JsonSerializer.Deserialize<T>()` available

### Phase F: Performance & Advanced

**Goal**: Translation rate > 95%

| # | Task | Estimate | Description |
|---|------|----------|-------------|
| F.1 | SIMD scalar fallback path completion | High | Eliminate 333 SIMD stubs |
| F.2 | Task struct refactoring (from Phase V.2-V.5) | High | Reduce RuntimeProvided 32→25 |
| F.3 | Incremental compilation | Medium | IR/codegen caching |
| F.4 | Reflection model evaluation (from Phase VI) | Medium | Evaluate QCall alternatives |

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

```
Phase I   (Foundation) ✅
Phase II  (Middle layer unlock) ✅
Phase III (Compiler pipeline quality) ✅ — 4,402→2,860, -35.1%, 88.8%
Phase IV  (Viable types→IL) 40→32 ✅
Phase V.1 (Task dependency analysis) ✅
       ↓
Phase A (Compiler finalization — fix stub root causes) ✅ — 2,777→1,478, -46.8%, 95.2%
       ↓
Phase B (FileStream BCL IL chain validation) — Windows ✅, B.5/B.6 pending
       ↓
Phase C (Socket/HTTP BCL IL chain)  ←→  Phase D (NativeAOT metadata)  [parallelizable]
       ↓                                  ↓
            Phase E (Native library linking: TLS/zlib)  [convergence]
                 ↓
            Phase F (Performance: SIMD/Task refactoring/Reflection)
                 ↓
            Phase G (Productization: CI/CD/Validation)
```

---

## Milestones

| Milestone | Criteria | Phase |
|-----------|---------|-------|
| **M1: Compiler Maturity** | stubs < 2,000, translation rate > 92% | A ✅ (1,478 stubs, 95.2%) |
| **M2: File I/O** | FileStream/StreamReader compile from BCL IL and run | B (~90%, Windows ✅) |
| **M3: Networked Apps** | HttpClient HTTP GET compiles from BCL IL and runs | C |
| **M4: Library Ecosystem** | DI + JSON (SG) + Logging compilable | D |
| **M5: Production-Grade** | HTTPS + Compression | E |
| **M6: Release** | CI/CD + 10 real package validation | G |

## Metric Definitions

| Metric | Definition | Current | Phase A Target | Long-term Target |
|--------|-----------|---------|---------------|-----------------|
| IL translation rate | (total_methods - stubs) / total_methods | **~95.2%** (1,478 stubs / 30,727 methods) | >92% ✅ | >95% ✅ |
| RuntimeProvided count | RuntimeProvidedTypes entries | **32** (was 40, -8) | ~32 | ~25 (Phase F.2) |
| CoreRuntime count | Methods fully provided by C++ | 22 | ~22 | ~10 (Phase F.4) |
| ICall count | C++ internal calls | **~396** (321+30+45) | ~400 | Stabilize (features come from BCL IL, not ICall) |

---

## Key Decision Summary

| Decision | Result | Rationale |
|----------|--------|-----------|
| RuntimeType | Type alias | Matches IL2CPP `Il2CppReflectionType` |
| Reflection types | Keep CoreRuntime | .NET 8 BCL reflection IL deeply depends on QCall/MetadataImport, cannot IL-compile short-term |
| Task | Keep RuntimeProvided (short-term) | 4 custom runtime fields + std::mutex* + MSVC padding, long-term needs architectural refactoring |
| WaitHandle | Target IL + ICall (Phase IV) | Simple struct, BCL IL compilable, needs 8 OS primitive ICall registrations |
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
