# 开发路线图

> 最后更新：2026-02-28
>
> [English Version](roadmap.md)

## 设计原则

### 核心目标

**只有真正无法从 IL 编译的内容使用 C++ runtime 映射，其他一切从 BCL IL 转译为 C++。**

以 Unity IL2CPP 为参考架构，但不盲目照搬——IL2CPP 使用 Mono BCL（依赖链远比 .NET 8 简单），且其源码来自社区反编译（可能不完整）。我们基于 .NET 8 BCL 的实际依赖链做第一性原理分析。

### 四条准则

1. **IL 优先**：一切可以从 BCL IL 编译的内容都应该编译——包括 FileStream、Socket、HttpClient、JSON 等。这些 BCL 类型有完整 IL 实现，最终通过 P/Invoke 调用 OS API。**提升能力的方式是修复编译器 bug 让 BCL IL 编译通，而不是用 ICall 重新实现功能。**
2. **ICall 是桥梁**：C++ runtime 仅通过 ICall 暴露底层原语（Monitor、Thread、Interlocked、GC），BCL IL 调用这些原语。**不为高层 BCL 功能写 ICall 替代实现。**
3. **编译器质量驱动**：提升 IL 转译率的方式是修复编译器 bug，而不是添加 RuntimeProvided 类型
4. **第一性原理判断**：每个 RuntimeProvided 类型必须有明确技术理由（runtime 直接访问字段 / BCL IL 引用 CLR 内部类型 / 嵌入 C++ 类型）
5. **原生库集成**：.NET BCL 通过 P/Invoke 调用平台原生库（kernel32、ws2_32、System.Native、OpenSSL 等）。CIL2CPP 集成这些库（类似 BoehmGC/ICU 的 FetchContent 模式），让 BCL P/Invoke 自然链接，而非用 ICall 绕过。

---

## RuntimeProvided 类型分类（第一性原理）

### 判断标准

一个类型需要 RuntimeProvided 当且仅当满足以下任一条件：
1. C++ runtime 需要**直接访问该类型的字段**（GC、异常、委托调度等）
2. BCL IL 方法体引用了**无法 AOT 编译的 CLR 内部类型**（QCall、MetadataImport 等）
3. struct 中嵌入了 **C++ 特有数据类型**（如 std::mutex*）

### 必须 C++ runtime 的类型（32 个 = 代码中 RuntimeProvidedTypes 条目数）

| 类型 | 数量 | 技术原因 |
|------|------|----------|
| Object / ValueType / Enum | 3 | GC 头 + 类型系统基础，每个托管对象的根 |
| String | 1 | 内联 UTF-16 buffer + GC 特殊处理（string_create/concat/intern） |
| Array | 1 | 变长布局 + bounds + GC（array_create/get/set） |
| Exception | 1 | setjmp/longjmp 异常机制，runtime 直接访问 message/inner/trace |
| Delegate / MulticastDelegate | 2 | 函数指针 + 调度链（delegate_invoke/combine） |
| Type / RuntimeType | 2 | 类型元数据系统，typeof() → TypeInfo* → Type* 缓存 |
| Thread | 1 | TLS + OS 线程管理，runtime 直接访问线程状态 |
| 反射 struct + alias ×12 | 12 | MemberInfo/MethodBase/MethodInfo/FieldInfo/ParameterInfo（5 real）+ RuntimeXxx alias（7） |
| TypedReference / ArgIterator | 2 | varargs 机制，编译器特殊处理 |
| Task + 异步非泛型 ×6 | 6 | Task（4 自定义字段 + std::mutex*）+ TaskAwaiter/Builder/ValueTask/ValueTaskAwaiter/AsyncIteratorBuilder |
| CancellationTokenSource | 1 | 依赖 ITimer + ManualResetEvent + Registrations 链 |

### 已迁移为 IL 的类型（8 个，Phase IV ✅）

| 类型 | 数量 | 状态 | 说明 |
|------|------|------|------|
| IAsyncStateMachine | 1 | ✅ 完成 | 纯接口，移除 RuntimeProvided |
| CancellationToken | 1 | ✅ 完成 | 只有 f_source 指针，struct 从 Cecil 生成 |
| WaitHandle 层级 | 6 | ✅ 完成 | BCL IL 编译 + 8 个 OS 原语 ICall |

### 长期可迁移的类型（7 个，需 Task 架构重构）

| 类型 | 数量 | 问题 |
|------|------|------|
| Task | 1 | 4 自定义字段 + std::mutex* + MSVC padding |
| TaskAwaiter / AsyncTaskMethodBuilder | 2 | 只有 f_task 字段，但依赖 Task struct 布局 |
| ValueTask / ValueTaskAwaiter / AsyncIteratorMethodBuilder | 3 | 依赖 Task + BCL 依赖链（ThreadPool、ExecutionContext） |
| CancellationTokenSource | 1 | 依赖 ITimer + ManualResetEvent + Registrations 链 |

**长期愿景**：重写异步运行时架构，让 Task 从自定义 C++ 实现迁移到 BCL IL 实现。这需要整个 TPL 依赖链（ThreadPool、TaskScheduler、ExecutionContext、SynchronizationContext）都能从 IL 编译。

### RuntimeProvided 目标

- **当前**：32 条目（Phase IV 完成：移除 IAsyncStateMachine + CancellationToken + WaitHandle×6 = -8）
- **短期目标达成**：40 → 32（-8 个 RuntimeProvided 类型）
- **长期**：32 → 25（Task 架构重构后移除 Task+异步依赖+CTS 共 7 个）

### Unity IL2CPP 参考（仅供对比，非盲目照搬）

> **注意**：IL2CPP 使用 Mono BCL，.NET 8 BCL 的依赖链远比 Mono 复杂。以下对比仅供参考。
> IL2CPP 源码来自社区反编译的 libil2cpp headers，可能不完整。

IL2CPP Runtime struct: `Il2CppObject` / `Il2CppString` / `Il2CppArray` / `Il2CppException` / `Il2CppDelegate` / `Il2CppMulticastDelegate` / `Il2CppThread` / `Il2CppReflectionType` / `Il2CppReflectionMethod` / `Il2CppReflectionField` / `Il2CppReflectionProperty` (~12 个)

IL2CPP 从 IL 编译: Task/async 全家族、CancellationToken/Source、WaitHandle 层级——但这基于 Mono 的较简单 BCL，不能直接类比 .NET 8。

---

## 当前状态

### RuntimeProvided 类型：32 条目（Phase IV 完成 -8，长期目标 25）

详见上方"RuntimeProvided 类型分类"章节。

### Stub 分布（HelloWorld, 1,478 个 stubs，~95.2% 翻译率）

| 类别 | 数量 | 占比 | 性质 |
|------|------|------|------|
| MissingBody | 666 | 45.1% | 无 IL body（abstract/extern/JIT intrinsic）— 多数合理 |
| KnownBrokenPattern | 620 | 41.9% | SIMD 333 + line-level 体扫描 patterns + TypeHandle/MethodTable |
| UndeclaredFunction | 68 | 4.6% | 泛型特化缺失（IRBuilder 未创建特化类型） |
| ClrInternalType | 96 | 6.5% | QCall/MetadataImport CLR JIT 专用类型 |
| RenderedBodyError | 28 | 1.9% | Codegen bug（PInvoke 回调、反射别名类型、指针转换） |

**不可修复或暂缓**：SIMD (333+) 需要 intrinsics 支持或运行时回退。CLR 内部类型 (96) 永久保留。

**IL 转译率**：~95.2%（29,249 已编译 / 30,727 总方法）。Phase A: 2,777 → 1,537; Phase B: 1,537 → 1,478。
**测试**：1,240 C# + 592 C++ + 39 集成 — 全部通过。

### 已实现的架构能力

- **多项目编译**：AssemblySet + deps.json + CIL2CPPAssemblyResolver 按需加载 ProjectReference 程序集（MultiAssemblyTest 已验证）
- **Source Generator 感知**：SG 输出是标准 IL，dotnet build 已编译，CIL2CPP 直接读取
- **P/Invoke 完整**：声明生成 + 调用约定 + CharSet 编组，kernel32/user32 等 OS API 可直接调用
- **100% IL 操作码覆盖**：所有 ~230 ECMA-335 操作码已实现

### 距离最终目标

| 项目类型 | 预估完成度 | 阻塞项 |
|---------|-----------|--------|
| 简单控制台应用 | ~95% | 基本 BCL 链通畅，95.2% 翻译率 |
| 类库项目 | ~85% | 集合、泛型、异步、反射都可用 |
| 文件 I/O 应用 | ~85% | FileStream/StreamReader BCL IL 链端到端可用（Windows ✅），Linux 待 System.Native |
| 网络应用 | ~10% | Socket/HttpClient BCL IL 链未验证，System.Native 未集成 |
| 生产级应用 | ~5% | 需要 TLS + JSON + DI 等完整 BCL 链 |
| 任意 NativeAOT .csproj | ~35% | 编译器成熟（1,478 stubs）但原生库集成 + NativeAOT 元数据待做 |

---

## Phase I: 基础打通 ✅

- Stub 依赖分析工具 (`--analyze-stubs`)
- RuntimeType = Type 别名（对标 `Il2CppReflectionType`）
- Handle 类型移除（RuntimeTypeHandle/MethodHandle/FieldHandle → intptr_t）
- AggregateException / SafeHandle / Thread.CurrentThread TLS / GCHandle 弱引用

## Phase II: 中间层解锁 ✅

- CalendarId/EraInfo/DefaultBinder/DBNull 等 CLR 内部类型移除（从 27 个降到 6 个）
- 反射类型别名（RuntimeMethodInfo → ManagedMethodInfo 等）
- WaitHandle OS 原语 / P/Invoke 调用约定 / SafeHandle ICall 补全

## Phase III: 编译器管道质量 ✅

**目标**：提升 IL 转译率——修复阻止 BCL IL 编译的根因

**成果**：4,402 → 2,860 stubs（-1,542，-35.1%），IL 转译率 88.8%。后续优化移至 Phase A。

| # | 任务 | 影响量 | 状态 | 说明 |
|---|------|--------|------|------|
| III.1 | SIMD 标量回退 | ✅ 完成 | ✅ | Vector64/128/256/512 struct 定义 |
| III.2a | ExternalEnumTypes 修复 | -386 | ✅ | 外部程序集枚举类型注册到 knownTypeNames |
| III.2b | Ldelem_Any/Stelem_Any 修复 | -114 | ✅ | 泛型数组元素访问指令支持 |
| III.2c | IsResolvedValueType 修正 | 正确性 | ✅ | IsPrimitive 包含 String/Object → 改用 IsValueType |
| III.2d | IsValidMergeVariable 修正 | -45 | ✅ | 禁止 &expr 作为分支合并赋值目标 |
| III.2e | DetermineTempVarTypes 改进 | -17 | ✅ | IRBinaryOp 类型推断 + IRRawCpp 模式推断 |
| III.2f | Stub 分类完善 | 诊断 | ✅ | GetBrokenPatternDetail 覆盖所有 HasKnownBrokenPatterns 模式 |
| III.2g | 集成测试修复 | 35/35 | ✅ | 修复 7 个 C++ 编译错误模式（void ICall/TypeInfo/ctor/Span/指针类型） |
| III.2h | StackEntry 类型化栈 + IRRawCpp 类型标注 | -98 | ✅ | Stack\<StackEntry\> 类型跟踪 + IRRawCpp ResultVar/ResultTypeCpp 补全 |
| III.3 | UnknownBodyReferences 修复 | 506→285 | ✅ | gate 重排序 + knownTypeNames 同步 + opaque stubs + SIMD/数组类型检测 |
| III.4 | UndeclaredFunction 修复 | 222→151 | ✅ | 拓宽 calledFunctions 扫描 + 多趟发现 + 诊断 filter 修复（剩余 151 为泛型特化缺失） |
| III.5 | FilteredGenericNamespaces 放开 | 级联解锁 | 待定 | 逐步放开安全命名空间（System.Diagnostics 等） |
| III.6 | KnownBrokenPattern 精简 + unbox 修复 | 637→604 | ✅ | 分类完善 + 数组类型修复 + 自递归误判移除 + unbox 泛型尾部下划线修复 |
| III.7 | 嵌套泛型类型特化 | -26 | ✅ | CreateNestedGenericSpecializations: Dictionary.Entry, List.Enumerator 等 |
| III.8 | 指针 local 修复 + opaque stubs | -46 | ✅ | HasUnknownBodyReferences 死代码修复 + 值类型 local 的 opaque struct 生成 + 指针 local 前向声明 |
| III.9 | 嵌套嵌套类型定点迭代 + 参数/返回类型 stubs | -46 | ✅ | CreateNestedGenericSpecializations fixpoint loop + 方法参数和返回类型的 opaque struct 扫描 |
| III.10 | 泛型特化泛型参数解析 + stub gate 修正 | -84 | ✅ | ResolveRemainingGenericParams 扩展到全指令类型 + func ptr 误判修正 + delegate arg 类型转换 |
| III.11 | Scalar alias + Numerics DIM + TimeZoneInfo | -80 | ✅ | m_value 标量拦截 + 原始 Numerics DIM 放行 + TimeZoneInfo 误判移除 |
| III.12 | 泛型特化 mangled 名解析 | -49 | ✅ | arity-prefixed mangled name resolution（_N_TKey → _N_System_String），29 unresolved generic → 0 |
| III.13 | transitive generic discovery | +1319 compiled | ✅ | fixpoint loop 发现 207 新类型 1393 方法，gate hardening 5 pattern（Object*→f_、MdArray**、MdArray*→typed、FINALLY without END_TRY、delegate invoke Object*→typed） |
| III.14 | delegate invoke typed pointer cast | -28 | ✅ | IRDelegateInvoke 对所有 typed 指针参数添加 (void*) 中间转换，修复 Object*→String* 等 C2664 |
| III.15 | RenderedBodyError false positives + ldind.ref type tracking | RE -41 | ✅ | 5 fixes: non-pointer void* cast RHS check, static_cast skip, TypeHandle→KnownBroken, ldind.ref StackEntry typed deref, Span byref detection |
| III.15b | IntPtr/UIntPtr ICall + intptr_t casting + RE reclassification | RE 113→0 | ✅ | IntPtr/UIntPtr ctor ICall + intptr_t arg/return casting + 113 RE→KBP 方法级重分类（权宜之计，根因待修复） |
| III.16 | 修复 reclassified RE 根因 | -10 | ✅ | GuidResult/RuntimeType.SplitName RE 根因修复 + KBP 误判移除（TimeSpanFormat/Number/GuidResult/SplitName） |
| III.17 | IRBuilder 泛型特化补全 | -57 | ✅ | 嵌套类型方法体编译 + DiscoverTransitiveGenericTypesFromMethods + GIM 参数扫描 |
| III.18 | ICall-mapped 方法体跳过 + ThreadPool ICall | -143 | ✅ | HasICallMapping 方法跳过体转换（-124 MissingBody）+ ThreadPool/Interlocked ICall（-19） |
| III.19 | stub budget ratchet 更新 | 诊断 | ✅ | stub_budget.json 基线从 3,310 → 3,147 |
| III.20 | KBP false positive audit | -287 | ✅ | 移除 30+ 过度宽泛的 method-level KBP 检查（Numerics DIM -60、DISH -35、Span/IAsyncLocal -58、CWT -23、P/Invoke/Buffers/Reflection -68 等）。RenderedBodyError 0→90（真实 codegen bug 正确暴露） |

---

## Phase IV: 可行类型回归 IL（40 → 32）✅

**目标**：移除 8 个 RuntimeProvided 类型 — **已完成**

| # | 任务 | 移除数 | 可行性 | 说明 |
|---|------|--------|--------|------|
| IV.1 | IAsyncStateMachine → IL | 1 | ✅ 完成 | 纯接口，移除 RuntimeProvided + 删除 task.h alias |
| IV.2 | CancellationToken → IL | 1 | ✅ 完成 | 只有 f_source 指针，struct 从 Cecil 生成 |
| IV.3-7 | WaitHandle 层级×6 → IL | 6 | ✅ 完成 | struct 从 Cecil 生成，TypeInfo 从 IL 生成，WaitOneCore ICall 保留；POSIX Mutex/Semaphore 有 TODO（当前为 stub 实现） |

**前提**：Phase III 编译器质量足够让 BCL WaitHandle/CancellationToken IL 正确编译。

## Phase V: 异步架构重构（长期，32 → 25）— 降级为 Phase F.2

> **降级原因**：async/await 已能工作（true concurrency + thread pool + combinators）。Task struct 重构是内部质量优化，对"编译任意项目"几乎无贡献。

**V.1 已完成** ✅：TplEventSource no-op ICall + ThreadPool ICall + Interlocked.ExchangeAdd — 66 方法从 IL 编译，65 stubbed
**V.2-V.5 待定**：Task struct 重构 → 降级到 Phase F.2（性能优化阶段）

---

## 未来阶段（Phase A-G）

> **核心思路**：不是"给每个功能写 ICall"，而是"修编译器让 BCL IL 编译通"。
> FileStream / Socket / HttpClient / JSON 在 .NET BCL 中有完整 IL 实现，最终通过 P/Invoke 调用 OS API。P/Invoke 已可用。问题在于编译器 bug 导致中间 BCL 方法无法从 IL 翻译。

### Phase A: 编译器收尾 — 修复 stubs 根因 ✅

**目标**：翻译率 > 92%，stubs < 2,000 — **已达成：1,478 stubs，~95.2% 翻译率**

**成果**：2,777 → 1,478 stubs（-1,299，-46.8%）

| # | 任务 | 影响 | 状态 |
|---|------|------|------|
| A.1 | 修复 RenderedBodyError | RE 90→28 | ✅ |
| A.2 | EventSource 更广泛 no-op ICall + modreq/modopt | -137 | ✅ |
| A.3 | 修复 UndeclaredFunction（泛型发现、ICalls） | UF 287→68 | ✅ |
| A.4 | KBP 审计 + 误判移除 | KBP 611→620（正确暴露） | ✅ |
| A.5 | 修复 UBR + UP | UBR 41→0, UP 22→0 | ✅ |
| A.6 | 修复 stub 重复计数（gate-failed MissingBody） | -698 MissingBody | ✅ |
| A.7 | Callee 扫描优化（gated method skip + UF prediction） | -40 cascade | ✅ |

**产出**：M1 里程碑达成。BCL 中间方法大面积解锁。

### Phase B: BCL 链验证 — Streaming I/O（进行中）

**目标**：`File.OpenRead(path)` / `new StreamReader(path)` 从 BCL IL 编译并运行

**策略**：**不写新 ICall**。从 FileStream BCL IL 链开始，自顶向下找中断点，修编译器。

**平台差异**：
- **Windows**：FileStream → P/Invoke to **kernel32.dll** (CreateFile/ReadFile/WriteFile) — P/Invoke 已可用
- **Linux**：FileStream → P/Invoke to **System.Native** (.NET 官方 POSIX 封装层，~30 .c 文件，[dotnet/runtime 开源](https://github.com/dotnet/runtime)) — 需作为原生库集成

| # | 任务 | 预估 | 状态 | 说明 |
|---|------|------|------|------|
| B.1 | 追踪 FileStream IL 依赖链 | 中 | ✅ | FileStream → FileStreamStrategy → SafeFileHandle → Interop.Kernel32 — 完整链已从 IL 编译 |
| B.2 | 修复链中遇到的 stubs | 高 | ✅ | KBP Pattern 8 修复（-376 stubs）、Buffer.Memmove sizeof 修复、SafeFileHandle/ThreadPool/ASCII ICalls |
| B.3 | SpanHelpers 标量搜索拦截 | 中 | ✅ | BCL SIMD 分支 → AOT 标量模板（IndexOfAny/IndexOf/LastIndexOf/IndexOfAnyExcept） |
| B.4 | 端到端 FileStreamTest | 低 | ✅ | FileStream Write/Read、StreamWriter、StreamReader.ReadLine — 全部通过（Windows） |
| B.5 | System.Native 原生库集成（Linux） | 中 | 待定 | 类似 BoehmGC/ICU：从 dotnet/runtime 提取 ~30 .c 文件，FetchContent 编译 |
| B.6 | 移除 File.ReadAllText/WriteAllText ICall 绕过 | 低 | 阻塞 | HACK 清理：File.ReadAllText 可通过 BCL IL 工作，但 File.ReadAllBytes 挂起（FileStream.Read(byte[]) 代码路径有 bug）。需修复后再移除。 |
| B.7 | 集成测试套件 + baselines | 低 | ✅ | FileStreamTest 加入集成测试（Phase 9，39/39 通过）+ UF/RE stub 减少（-94） |

**前置**：Phase A ✅
**产出**：FileStream 端到端从 BCL IL 编译 — **Windows 已通过** ✅，Linux 待 System.Native

### Phase C: BCL 链扩展 — 网络

**目标**：`HttpClient.GetStringAsync("http://...")` 从 BCL IL 编译

**策略**：同 Phase B — 追踪 BCL IL 链，修中断点。

**平台差异**：
- **Windows**：Socket → P/Invoke to **ws2_32.dll** (Winsock2)
- **Linux**：Socket → P/Invoke to **System.Native**（Phase B.3 已集成）

| # | 任务 | 预估 | 说明 |
|---|------|------|------|
| C.1 | 追踪 Socket BCL IL 链 | 中 | Socket → SafeSocketHandle → Interop.Winsock (Win) / System.Native (Linux) |
| C.2 | 修复链中遇到的 stubs | 高 | 同 B.2 |
| C.3 | 追踪 HttpClient BCL IL 链 | 高 | SocketsHttpHandler → Socket → MemoryPool → HPack |
| C.4 | DNS P/Invoke 验证 | 低 | getaddrinfo 是 P/Invoke，应直接可用 |
| C.5 | 端到端集成测试 | 低 | HTTP GET (明文) |

**前置**：Phase B
**产出**：HttpClient 明文 HTTP 可用

### Phase D: NativeAOT 元数据（可与 Phase C 并行）

**目标**：支持修剪注解，让反射型库正常工作

| # | 任务 | 预估 | 说明 |
|---|------|------|------|
| D.1 | `[DynamicallyAccessedMembers]` 解析 | 中 | ReachabilityAnalyzer 读取自定义属性，保留标注成员 |
| D.2 | rd.xml 解析器 | 低 | XML 格式保留规则 |
| D.3 | ILLink feature switch 替换 | 中 | 编译期常量 (`IsDynamicCodeSupported = false`) |
| D.4 | AOT 兼容性警告 | 低 | 报告 `[RequiresUnreferencedCode]` 调用链 |

**前置**：无
**产出**：DI + JSON (SG) + Logging 可编译

### Phase E: 原生库集成 — TLS/zlib（仅链接，不重写）

**目标**：HTTPS + Compression

**注意**：.NET BCL 的 TLS/zlib 有完整 IL，通过 P/Invoke 调用 .NET 专属原生库（与 System.Native 同模式，均从 [dotnet/runtime](https://github.com/dotnet/runtime) 提取）：
- TLS → `System.Security.Cryptography.Native.OpenSsl`（Linux）/ SChannel（Windows，kernel32 P/Invoke）
- zlib → `System.IO.Compression.Native`（.NET 的 zlib 封装）

| # | 任务 | 预估 | 说明 |
|---|------|------|------|
| E.1 | System.Security.Cryptography.Native 集成 | 高 | 从 dotnet/runtime 提取，链接 OpenSSL (Linux) / SChannel (Win) |
| E.2 | System.IO.Compression.Native 集成 | 低 | 从 dotnet/runtime 提取，内嵌 zlib |
| E.3 | 移出 InternalPInvokeModules 对应项 | 低 | 让 BCL P/Invoke 声明正常生成 |
| E.4 | Regex 解释器 BCL IL 验证 | 中 | 非 Compiled 模式不依赖 Reflection.Emit |
| E.5 | 端到端测试 | 低 | HTTPS GET + JSON 反序列化 |

**前置**：Phase C (TLS 需要 Socket) + Phase D (JSON 需要元数据感知)
**产出**：`HttpClient.GetStringAsync("https://...")` + `JsonSerializer.Deserialize<T>()` 可用

### Phase F: 性能 & 高级

**目标**：翻译率 > 95%

| # | 任务 | 预估 | 说明 |
|---|------|------|------|
| F.1 | SIMD 标量回退路径完善 | 高 | 消除 333 SIMD stubs |
| F.2 | Task struct 重构（原 Phase V.2-V.5） | 高 | 降低 RuntimeProvided 32→25 |
| F.3 | 增量编译 | 中 | IR/codegen 缓存 |
| F.4 | 反射模型评估（原 Phase VI） | 中 | 评估 QCall 替代方案 |

**前置**：Phase A-E 核心功能完成
**产出**：翻译率 > 95%

### Phase G: 产品化

**目标**：可发布的工具

| # | 任务 | 预估 |
|---|------|------|
| G.1 | CI/CD (GitHub Actions: Win + Linux) | 中 |
| G.2 | 10+ 真实 NuGet 包编译验证 | 高 |
| G.3 | 自包含部署模式 + RID 检测 | 中 |
| G.4 | 文档完善 | 中 |

---

## 依赖关系图

```
Phase I   (基础打通) ✅
Phase II  (中间层解锁) ✅
Phase III (编译器管道质量) ✅ — 4,402→2,860, -35.1%, 88.8%
Phase IV  (可行类型→IL) 40→32 ✅
Phase V.1 (Task 依赖分析) ✅
       ↓
Phase A (编译器收尾 — 修 stubs 根因) ✅ — 2,777→1,478, -46.8%, 95.2%
       ↓
Phase B (FileStream BCL IL 链验证) — Windows ✅, B.5/B.6 待定
       ↓
Phase C (Socket/HTTP BCL IL 链)  ←→  Phase D (NativeAOT 元数据)  [可并行]
       ↓                                  ↓
            Phase E (原生库链接: TLS/zlib)  [汇合]
                 ↓
            Phase F (性能: SIMD/Task重构/反射)
                 ↓
            Phase G (产品化: CI/CD/验证)
```

---

## 里程碑

| 里程碑 | 达成条件 | 对应阶段 |
|--------|---------|---------|
| **M1: 编译器成熟** | stubs < 2,000，翻译率 > 92% | A ✅（1,478 stubs, 95.2%） |
| **M2: 文件 I/O** | FileStream/StreamReader 从 BCL IL 编译并运行 | B（~90%，Windows ✅） |
| **M3: 联网应用** | HttpClient HTTP GET 从 BCL IL 编译并运行 | C |
| **M4: 库生态** | DI + JSON (SG) + Logging 可编译 | D |
| **M5: 生产级** | HTTPS + Compression | E |
| **M6: 发布** | CI/CD + 10 真实包验证 | G |

## 指标定义

| 指标 | 定义 | 当前值 | Phase A 目标 | 长期目标 |
|------|------|--------|-------------|----------|
| IL 转译率 | (total_methods - stubs) / total_methods | **~95.2%**（1,478 stubs / 30,727 方法） | >92% ✅ | >95% ✅ |
| RuntimeProvided 数 | RuntimeProvidedTypes 条目 | **32**（was 40, -8） | ~32 | ~25（Phase F.2） |
| CoreRuntime 数 | 方法完全由 C++ 提供 | 22 | ~22 | ~10（Phase F.4） |
| ICall 数 | C++ 内部调用 | **~396**（321+30+45） | ~400 | 趋稳（功能来自 BCL IL，非 ICall） |

---

## 关键决策总结

| 决策 | 结果 | 理由 |
|------|------|------|
| RuntimeType | Type 别名 | 对标 IL2CPP `Il2CppReflectionType` |
| 反射类型 | 保持 CoreRuntime | .NET 8 BCL 反射 IL 深度依赖 QCall/MetadataImport，短期无法 IL 编译 |
| Task | 保持 RuntimeProvided（短期） | 4 个自定义运行时字段 + std::mutex* + MSVC padding，长期需架构重构 |
| WaitHandle | 目标 IL + ICall（Phase IV） | struct 简单，BCL IL 可编译，需注册 8 个 OS 原语 ICall |
| SIMD | 标量回退 struct + IsSupported=false | BCL 有非 SIMD 回退路径 |
| File I/O ICall | HACK — Phase B 完成后移除 | File.ReadAllText 等 12 个 ICall 绕过了 FileStream IL 链，违反 IL-first |
| 网络层 | BCL IL 自然编译 | BCL 内置跨平台分支 |
| Regex | 解释器模式 + source generator | Compiled 模式用 Reflection.Emit → AOT 不兼容 |
| IL2CPP 对标 | 参考但不盲目照搬 | IL2CPP 基于 Mono BCL，.NET 8 依赖链差异大 |

## CLR 内部类型（永久保留为 stub）

| 类型 | 说明 |
|------|------|
| QCallTypeHandle / QCallAssembly / ObjectHandleOnStack / MethodTable | CLR JIT 专用桥接 |
| MetadataImport / RuntimeCustomAttributeData | CLR 内部元数据访问 |

## 被过滤的 P/Invoke 模块（InternalPInvokeModules）

> 这些模块在 `CppCodeGenerator.Source.cs` 的 `InternalPInvokeModules` 黑名单中，P/Invoke 声明不会生成。
> 集成方式：从 [dotnet/runtime](https://github.com/dotnet/runtime) 提取 .c 源码，FetchContent 编译，然后移出黑名单。

| 模块 | 功能 | 解锁阶段 | 集成方式 |
|------|------|---------|---------|
| `System.Native` | POSIX 文件/进程/网络 (~30 .c) | Phase B | FetchContent from dotnet/runtime |
| `System.IO.Compression.Native` | zlib 封装 | Phase E | FetchContent + 内嵌 zlib |
| `System.Globalization.Native` | ICU 封装 | ✅ 已有 ICU 集成 | — |
| `System.Security.Cryptography.Native.OpenSsl` | OpenSSL/TLS | Phase E | FetchContent + 链接 OpenSSL |
| `System.Net.Security.Native` | GSSAPI/TLS | Phase E | FetchContent |
| `QCall` / `QCall.dll` | CLR 内部桥接 | 永久保留 | 不可集成（CLR JIT 专用） |
| `ucrtbase` / `ucrtbase.dll` | CRT | ✅ 已链接 | — |
