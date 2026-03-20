# 开发路线图

> 最后更新：2026-03-21
>
> [English Version](roadmap.md)

## 设计原则

### 核心目标

**只有真正无法从 IL 编译的内容使用 C++ runtime 映射，其他一切从 BCL IL 转译为 C++。**

以 Unity IL2CPP 为参考架构，但不盲目照搬——IL2CPP 使用 Mono BCL（依赖链远比 .NET 8 简单），且其源码来自社区反编译（可能不完整）。我们基于 .NET 8 BCL 的实际依赖链做第一性原理分析。

### 六条准则

1. **IL 优先**：一切可以从 BCL IL 编译的内容都应该编译——包括 FileStream、Socket、HttpClient、JSON 等。这些 BCL 类型有完整 IL 实现，最终通过 P/Invoke 调用 OS API。**提升能力的方式是修复编译器 bug 让 BCL IL 编译通，而不是用 ICall 重新实现功能。**
2. **ICall 是桥梁**：C++ runtime 仅通过 ICall 暴露底层原语（Monitor、Thread、Interlocked、GC），BCL IL 调用这些原语。**不为高层 BCL 功能写 ICall 替代实现。**
3. **编译器质量驱动**：提升 IL 转译率的方式是修复编译器 bug，而不是添加 RuntimeProvided 类型
4. **第一性原理判断**：每个 RuntimeProvided 类型必须有明确技术理由（runtime 直接访问字段 / BCL IL 引用 CLR 内部类型 / 嵌入 C++ 类型）
5. **原生库集成**：.NET BCL 通过 P/Invoke 调用平台原生库（kernel32、ws2_32、System.Native、OpenSSL 等）。CIL2CPP 集成这些库（类似 BoehmGC/ICU 的 FetchContent 模式），让 BCL P/Invoke 自然链接，而非用 ICall 绕过。
6. **Windows 优先，跨平台就绪**：主要开发目标为 Windows (x64)。平台抽象宏和条件分支从一开始就编写以便未来跨平台支持，但 Linux/macOS/32 位推迟到核心目标完成后。

### 目标分类

#### 必须实现（核心 NativeAOT 兼容性，Windows x64）

CIL2CPP 能声称"可编译 .NET NativeAOT 项目"之前必须完成：

| 目标 | 阶段 | 说明 |
|------|------|------|
| 完整 HTTP GET | C.6 ✅ | `HttpClient.GetStringAsync("http://...")` 异步请求/响应链 — **已完成** |
| NativeAOT 元数据 | D | `[DynamicallyAccessedMembers]`、ILLink feature switch、NuGet 包验证 |
| JSON 序列化 (SG) | D.5 ✅ | System.Text.Json source generator + Newtonsoft.Json 13.0.3 (NuGet) — **两者均已端到端验证** |
| MarshalAs P/Invoke | C.7 ✅ | `[MarshalAs]` ✅、`[Out]`/`[In]` ✅、数组编组（ByValTStr/ByValArray/LPArray ✅、[Out] LPArray + SizeParamIndex ✅）— PInvokeTest 10 项全部通过 |
| SChannel TLS (Windows) | E.win ✅ | 通过 `secur32.dll`/`schannel.dll` P/Invoke 实现 HTTPS — **已完成**（HttpsGetTest 通过） |
| 压缩 | E.2 ✅ | 通过 System.IO.Compression.Native 的 zlib — GZipStream/DeflateStream 往返测试通过（CompressionTest 通过） |
| RenderedBodyError → 0 | H.2 | 调查完成：~114 个 stubs 是正确的 AOT stubs（CLR内部依赖、泛型体类型冲突），非 codegen bug |
| SIMD 标量完善 | F.1 | 消除剩余 SIMD stubs（完整标量回退路径） |
| 10 个 NuGet 包验证 | G.2 | 证明真实包可编译和运行（10/10 冒烟测试通过，但覆盖极浅 — 见成熟度评估） |

#### 待定（必须实现完成后）

仅在核心 Windows NativeAOT 兼容性达成后才开始。平台抽象代码（宏、`#ifdef`、分支脚手架）现在就写好以便后续启用。

| 目标 | 阶段 | 说明 |
|------|------|------|
| Linux 支持 (System.Native) | B.5 | 从 dotnet/runtime 提取 ~30 .c 文件，FetchContent 编译 |
| macOS 支持 | — | System.Native + Objective-C 桥接平台 API |
| OpenSSL TLS (Linux) | E.linux | FetchContent + 链接 OpenSSL 实现 Linux HTTPS |
| 32 位目标 (ARM/x86) | — | IRBuilder.Types.cs 中的指针大小假设、TypeInfo 布局 |
| Task struct 重构 | F.2 | 降低 RuntimeProvided 32→25（内部质量，无用户可感知影响） |
| 增量编译 | F.3 | IR/codegen 缓存（性能优化） |
| 完整反射模型 | F.4 | QCall 替代方案评估 |

#### 架构上不可能（AOT 不兼容）

这些 .NET 特性与 AOT 编译根本不兼容，**永远不会**支持：

| 特性 | 原因 |
|------|------|
| `Assembly.Load` / 动态程序集加载 | 需要 JIT 在运行时编译新 IL |
| `Reflection.Emit` / 动态代码生成 | 在运行时创建新类型/方法 — 无 C++ 等价物 |
| `DynamicMethod` / 表达式树编译 | 动态生成 IL，需要 JIT |
| 分层 JIT 编译 / ReadyToRun | JIT 专用的运行时优化 |
| `Type.MakeGenericType` + 运行时类型 | AOT 无法单态化编译时未知的类型 |
| 动态 COM 互操作 (`IDispatch`) | 需要运行时类型发现 |
| QCall / CLR 内部类型 | CLR JIT 专用桥接（QCallTypeHandle、MetadataImport、MethodTable）— 永久保留为 stub（~96 个） |

> **注意**：使用这些特性的库（gRPC 动态代理、部分 ORM 如 Dapper 的动态查询、基于 Reflection.Emit 的序列化器）无法支持。应使用 source generator 等价方案（gRPC code-first、System.Text.Json SG）替代。

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

### 已迁移为 IL 的类型（8 个，Phase 4 ✅）

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

- **当前**：32 条目（Phase 4 完成：移除 IAsyncStateMachine + CancellationToken + WaitHandle×6 = -8）
- **短期目标达成**：40 → 32（-8 个 RuntimeProvided 类型）
- **长期**：32 → 25（Task 架构重构后移除 Task+异步依赖+CTS 共 7 个）

### Unity IL2CPP 参考（仅供对比，非盲目照搬）

> **注意**：IL2CPP 使用 Mono BCL，.NET 8 BCL 的依赖链远比 Mono 复杂。以下对比仅供参考。
> IL2CPP 源码来自社区反编译的 libil2cpp headers，可能不完整。

IL2CPP Runtime struct: `Il2CppObject` / `Il2CppString` / `Il2CppArray` / `Il2CppException` / `Il2CppDelegate` / `Il2CppMulticastDelegate` / `Il2CppThread` / `Il2CppReflectionType` / `Il2CppReflectionMethod` / `Il2CppReflectionField` / `Il2CppReflectionProperty` (~12 个)

IL2CPP 从 IL 编译: Task/async 全家族、CancellationToken/Source、WaitHandle 层级——但这基于 Mono 的较简单 BCL，不能直接类比 .NET 8。

---

## 当前状态

### RuntimeProvided 类型：32 条目（Phase 4 完成 -8，长期目标 25）

详见上方"RuntimeProvided 类型分类"章节。

### Stub 分布（HelloWorld, 1,280 个 stubs，~95%+ 翻译率）

> 历史基线，来自 commit 3f93840 (2026-03-05)。`stub_budget.json` 已在 Phase X 清理中移除。
> 程序集: HelloWorld（~26k 方法，经需求驱动泛型 + 特化方法可达性优化）。
> 获取当前计数：运行 codegen 并检查生成的 `*_stubs.cpp` 文件。

| 类别 | 数量 | 占比 | 性质 |
|------|------|------|------|
| MissingBody | 604 | 47.2% | 无 IL body（abstract/extern/JIT intrinsic）— 多数合理 |
| KnownBrokenPattern | 458 | 35.8% | SIMD 死代码分支 + TypeHandle/MethodTable |
| ClrInternalType | 96 | 7.5% | QCall/MetadataImport CLR JIT 专用类型（永久保留） |
| UndeclaredFunction | 95 | 7.4% | MissingBody/KBP 级联 — 泛型特化缺口 |
| RenderedBodyError | 17 | 1.3% | Codegen bug — Phase H.2 目标降至 0 |
| UnknownParameterTypes | 10 | 0.8% | 方法参数引用未知类型 |
| UnknownBodyReferences | 0 | 0% | 已解决 |

**不可修复或暂缓**：SIMD 死代码分支由 FeatureSwitchResolver 处理（IsSupported=false 死分支消除）。CLR 内部类型（~96）永久保留。

**IL 转译率**：~95%+。历程：Phase A: 2,777 → 1,478; Phase B: 1,478 → 1,537; Phase C: → 1,666; Phase X + 需求驱动泛型: → 1,280（方法总数也从 ~31k 降至 ~26k，得益于特化方法可达性分析）。
**测试**：1,291 C# + 576 C++ + 123 集成（20 个测试项目）— 全部通过。

### 已实现的架构能力

- **多项目编译**：AssemblySet + deps.json + CIL2CPPAssemblyResolver 按需加载 ProjectReference 程序集（MultiAssemblyTest 已验证）
- **Source Generator 感知**：SG 输出是标准 IL，dotnet build 已编译，CIL2CPP 直接读取
- **P/Invoke 完整**：声明生成 + 调用约定 + CharSet 编组，kernel32/user32 等 OS API 可直接调用
- **100% IL 操作码覆盖**：所有 ~230 ECMA-335 操作码已实现

### 成熟度评估

#### 为什么"功能清单完成度"会高估真实成熟度

CIL2CPP 的 "IL 优先" 架构在**广度**上极其高效：修复一个编译器 bug 就能解锁成千上万个 BCL 方法（因为它们全部从 IL 编译）。这给人快速进步的错觉——功能很快"亮灯"。但是：

- **每个功能仅经过冒烟测试**（30-100 行的测试程序）
- **每引入一个新 NuGet 包都需要修复编译器问题** — 编译器还不够健壮，无法处理任意 .NET 代码
- **没有任何真实的大型复杂 .NET NativeAOT 项目被编译通过** — 全部 20 个测试项目都是玩具级别的示例
- **零编译器优化** — 没有内联、没有去虚拟化、没有逃逸分析

对比参考：一个真正成熟的 C 编译器（如 chibicc）起码要编译通过 Linux 内核才能证明自己的健壮性。CIL2CPP 尚无类似的真实世界验证。

#### 代码规模对比

| 项目 | 代码行数 | 说明 |
|------|---------|------|
| **CIL2CPP** | **~50K** | 编译器 ~30K + 运行时 ~12K + 测试/工具 ~8K |
| Unity IL2CPP（估计） | ~300K-700K | 闭源，含编译器 + libil2cpp + 多平台支持 + 优化器 |
| .NET NativeAOT | 数百万行 | Microsoft 数百名工程师 10+ 年开发 |
| 最小可用级 AOT 编译器（估计） | ~200K-300K | 含优化、跨平台、完善测试 |

CIL2CPP 的 50K 行约为最小可用级的 **17-25%**，约为 IL2CPP 的 **7-17%**。

#### 剩余工作量分布

| 领域 | 当前行数 | 估计需要 | 倍数 | 说明 |
|------|---------|---------|------|------|
| **编译器核心**（IR/CodeGen） | ~22K | ~80-120K | 4-5x | 优化器、高级 IL 模式、健壮性 |
| **运行时**（C++） | ~12K | ~30-50K | 3-4x | 压缩、加密、高级线程、反射、调试 |
| **测试** | ~12K | ~50-80K | 4-7x | 按库详尽测试、压力测试、模糊测试 |
| **平台支持** | 0 (Linux/macOS) | ~20-40K | ∞ | System.Native、OpenSSL、POSIX 层、ARM |
| **工具/开发者体验** | ~3K | ~15-25K | 5-8x | 错误信息、诊断、增量编译、IDE 集成 |
| **合计** | **~50K** | **~200-300K** | **4-6x** | 生产级 Windows+Linux 工具 |

#### 多维度成熟度评估

| 维度 | 评估 | 说明 |
|------|------|------|
| **功能广度**（什么能编译） | ~60-70% | 很多 BCL 领域在冒烟测试级别可用 |
| **功能深度**（什么能可靠工作） | ~15-25% | 大多数功能只用单个简单示例测试过 |
| **生产就绪度**（真实应用） | ~10-15% | 没有复杂项目编译通过，每个包都需要修复 |
| **编译器优化** | ~10% | 零优化 pass vs NativeAOT 12+ 种优化 |
| **平台覆盖** | ~25% | 仅 Windows x64 |
| **综合成熟度** | **~20-30%** | 加权平均 |

> **关键区别**：功能清单上的"XX% 完成"衡量的是"冒烟测试通过"，不是"生产环境可靠"。一个功能在 30 行测试中工作，不代表它在真实应用的边界情况下也能工作。

#### 冒烟测试覆盖度（Windows x64）

> ⚠️ 以下百分比仅代表"在最小冒烟测试中通过"的覆盖度，不代表生产可靠性。
> 每个测试项目通常只有 30-100 行代码，只测试了对应库 API 的极小子集。

| 项目类型 | 冒烟测试覆盖 | 验证深度 | 关键说明 |
|---------|------------|---------|---------|
| 简单控制台应用 | ~95% | 低 | 仅 HelloWorld/FeatureTest 验证 |
| 文件 I/O 应用 | ~95% | 低 | 基本 FileStream/StreamReader 通过；复杂场景未验证 |
| 网络应用 (HTTP/HTTPS) | ~85% | 低 | 单次 GET 请求通过；重定向/认证/流式传输未验证 |
| JSON 序列化 | ~85% | 低 | 简单对象序列化通过；嵌套/多态/自定义转换器未验证 |
| NuGet 包 | ~70% | 极低 | 9 个包的最小示例通过；各库仅调用 2-5 个 API |
| 生产级应用 | ~15% | 无 | 无任何真实应用编译通过 |
| 任意 NativeAOT .csproj | **~20-30%** | 无 | 综合考虑广度和深度 |

> **Linux/macOS**：待定。以上百分比仅限 Windows。Linux 需要 System.Native 集成 (Phase B.5, 待定) + OpenSSL (Phase E.linux, 待定)。当前 Linux 支持：~5%（仅控制台，无文件 I/O 或网络）。

#### 已验证的 NuGet 包（2026-03-21）

| # | 包名 | 版本 | 测试行数 | 测试内容 |
|---|------|------|---------|---------|
| 1 | Newtonsoft.Json | 13.0.3 | 35 行 | 简单对象序列化/反序列化 |
| 2 | Microsoft.Extensions.DependencyInjection | 9.0.x | 88 行 | AddSingleton/AddTransient，构造函数注入 |
| 3 | Microsoft.Extensions.Logging | 9.0.x | (同上) | ILogger 基本日志 |
| 4 | Microsoft.Extensions.Logging.Console | 9.0.x | (同上) | Console sink |
| 5 | Humanizer | 2.14.1 | 31 行 | Humanize/Dehumanize、数字转英文 |
| 6 | Polly | 8.5.2 | 33 行 | 空管道、基本 Execute |
| 7 | Serilog | 4.2.0 | 44 行 | 模板解析、日志级别 |
| 8 | Serilog.Sinks.Console | 7.0.x | (同上) | Console 输出 |
| 9 | Microsoft.Extensions.Configuration | 9.0.x | 44 行 | AddInMemoryCollection、GetValue |
| 10 | Microsoft.Extensions.Configuration.Binder | 9.0.x | (同上) | 配置绑定 |

> **注意**：每个包仅用最小示例验证，覆盖了各库 API 的极小子集（<5%）。每引入一个新包都需要修复编译器 bug。这不等同于"该库可在生产中使用"。

#### 实现缺口（2026-03-21 审计）

- `[DynamicallyAccessedMembers]` — **已完成并验证**：13 种 DamFlag，字段/方法/参数扫描，CLI `--rdxml` 已接入
- ILLink feature switches — **已上线**：FeatureSwitchResolver 编译期替换 10+ AOT 默认开关
- `[MarshalAs]` 属性 — **C.7.1 ✅ + C.7.2 ✅ + C.7.3 ✅**：Cecil 解析 + 21 种类型映射 + [Out]/[In] 支持 + LPArray 数组编组（含方向感知 + SizeParamIndex 断言）
- NuGet PackageReference — 10 个包冒烟测试通过，但覆盖极浅
- Codegen 性能 — NuGetSimpleTest 196s→89s（2026-03-21 两次优化后）。仍有优化空间
- **编译器优化** — ❌ 零优化 pass（无内联、去虚拟化、逃逸分析）。生成的 C++ 是朴素 IL 翻译
- **真实项目验证** — ❌ 无任何超过 100 行的真实应用编译通过

---

## Phase 1: 基础打通 ✅

- RuntimeType = Type 别名（对标 `Il2CppReflectionType`）
- Handle 类型移除（RuntimeTypeHandle/MethodHandle/FieldHandle → intptr_t）
- AggregateException / SafeHandle / Thread.CurrentThread TLS / GCHandle 弱引用

## Phase 2: 中间层解锁 ✅

- CalendarId/EraInfo/DefaultBinder/DBNull 等 CLR 内部类型移除（从 27 个降到 6 个）
- 反射类型别名（RuntimeMethodInfo → ManagedMethodInfo 等）
- WaitHandle OS 原语 / P/Invoke 调用约定 / SafeHandle ICall 补全

## Phase 3: 编译器管道质量 ✅

**目标**：提升 IL 转译率——修复阻止 BCL IL 编译的根因

**成果**：4,402 → 2,860 stubs（-1,542，-35.1%），IL 转译率 88.8%。后续优化移至 Phase A。

| # | 任务 | 影响量 | 状态 | 说明 |
|---|------|--------|------|------|
| 3.1 | SIMD 标量回退 | ✅ 完成 | ✅ | Vector64/128/256/512 struct 定义 |
| 3.2a | ExternalEnumTypes 修复 | -386 | ✅ | 外部程序集枚举类型注册到 knownTypeNames |
| 3.2b | Ldelem_Any/Stelem_Any 修复 | -114 | ✅ | 泛型数组元素访问指令支持 |
| 3.2c | IsResolvedValueType 修正 | 正确性 | ✅ | IsPrimitive 包含 String/Object → 改用 IsValueType |
| 3.2d | IsValidMergeVariable 修正 | -45 | ✅ | 禁止 &expr 作为分支合并赋值目标 |
| 3.2e | DetermineTempVarTypes 改进 | -17 | ✅ | IRBinaryOp 类型推断 + IRRawCpp 模式推断 |
| 3.2f | Stub 分类完善 | 诊断 | ✅ | GetBrokenPatternDetail 覆盖所有 HasKnownBrokenPatterns 模式 |
| 3.2g | 集成测试修复 | 69/69 | ✅ | 修复 7 个 C++ 编译错误模式（void ICall/TypeInfo/ctor/Span/指针类型） |
| 3.2h | StackEntry 类型化栈 + IRRawCpp 类型标注 | -98 | ✅ | Stack\<StackEntry\> 类型跟踪 + IRRawCpp ResultVar/ResultTypeCpp 补全 |
| 3.3 | UnknownBodyReferences 修复 | 506→285 | ✅ | gate 重排序 + knownTypeNames 同步 + opaque stubs + SIMD/数组类型检测 |
| 3.4 | UndeclaredFunction 修复 | 222→151 | ✅ | 拓宽 calledFunctions 扫描 + 多趟发现 + 诊断 filter 修复（剩余 151 为泛型特化缺失） |
| 3.5 | FilteredGenericNamespaces 放开 | 级联解锁 | 待定 | 逐步放开安全命名空间（System.Diagnostics 等） |
| 3.6 | KnownBrokenPattern 精简 + unbox 修复 | 637→604 | ✅ | 分类完善 + 数组类型修复 + 自递归误判移除 + unbox 泛型尾部下划线修复 |
| 3.7 | 嵌套泛型类型特化 | -26 | ✅ | CreateNestedGenericSpecializations: Dictionary.Entry, List.Enumerator 等 |
| 3.8 | 指针 local 修复 + opaque stubs | -46 | ✅ | HasUnknownBodyReferences 死代码修复 + 值类型 local 的 opaque struct 生成 + 指针 local 前向声明 |
| 3.9 | 嵌套嵌套类型定点迭代 + 参数/返回类型 stubs | -46 | ✅ | CreateNestedGenericSpecializations fixpoint loop + 方法参数和返回类型的 opaque struct 扫描 |
| 3.10 | 泛型特化泛型参数解析 + stub gate 修正 | -84 | ✅ | ResolveRemainingGenericParams 扩展到全指令类型 + func ptr 误判修正 + delegate arg 类型转换 |
| 3.11 | Scalar alias + Numerics DIM + TimeZoneInfo | -80 | ✅ | m_value 标量拦截 + 原始 Numerics DIM 放行 + TimeZoneInfo 误判移除 |
| 3.12 | 泛型特化 mangled 名解析 | -49 | ✅ | arity-prefixed mangled name resolution（_N_TKey → _N_System_String），29 unresolved generic → 0 |
| 3.13 | transitive generic discovery | +1319 compiled | ✅ | fixpoint loop 发现 207 新类型 1393 方法，gate hardening 5 pattern（Object*→f_、MdArray**、MdArray*→typed、FINALLY without END_TRY、delegate invoke Object*→typed） |
| 3.14 | delegate invoke typed pointer cast | -28 | ✅ | IRDelegateInvoke 对所有 typed 指针参数添加 (void*) 中间转换，修复 Object*→String* 等 C2664 |
| 3.15 | RenderedBodyError false positives + ldind.ref type tracking | RE -41 | ✅ | 5 fixes: non-pointer void* cast RHS check, static_cast skip, TypeHandle→KnownBroken, ldind.ref StackEntry typed deref, Span byref detection |
| 3.15b | IntPtr/UIntPtr ICall + intptr_t casting + RE reclassification | RE 113→0 | ✅ | IntPtr/UIntPtr ctor ICall + intptr_t arg/return casting + 113 RE→KBP 方法级重分类（权宜之计，根因待修复） |
| 3.16 | 修复 reclassified RE 根因 | -10 | ✅ | GuidResult/RuntimeType.SplitName RE 根因修复 + KBP 误判移除（TimeSpanFormat/Number/GuidResult/SplitName） |
| 3.17 | IRBuilder 泛型特化补全 | -57 | ✅ | 嵌套类型方法体编译 + DiscoverTransitiveGenericTypesFromMethods + GIM 参数扫描 |
| 3.18 | ICall-mapped 方法体跳过 + ThreadPool ICall | -143 | ✅ | HasICallMapping 方法跳过体转换（-124 MissingBody）+ ThreadPool/Interlocked ICall（-19） |
| 3.19 | stub budget ratchet 更新 | 诊断 | ✅ | stub_budget.json 基线从 3,310 → 3,147 |
| 3.20 | KBP false positive audit | -287 | ✅ | 移除 30+ 过度宽泛的 method-level KBP 检查（Numerics DIM -60、DISH -35、Span/IAsyncLocal -58、CWT -23、P/Invoke/Buffers/Reflection -68 等）。RenderedBodyError 0→90（真实 codegen bug 正确暴露） |

---

### Phase X：妥协代码移除 ✅（2026-03）

> **所有 stub/gate/workaround 基础设施已移除。**删除约 5,500 行妥协代码。

**已删除系统**：
- **Gate 系统**（5 个预渲染门控、试渲染、后渲染验证）从 Header.cs/Source.cs 移除
- **StubAnalyzer.cs**（634 行）— 根因分析、调用图、预算检查
- **KnownStubs.cs**（521 行）— 手写 AOT 替代方法体
- **`__SIMD_STUB__` 哨兵**（IRBuilder.Emit/Methods 中约 20 处检查）— 死代码传播的权宜之计
- **命名空间黑名单**（非 AOT 不兼容的排除项如 System.Data、Interop/*）— 移除
- **泛型过滤黑名单**（FilteredGenericNamespaces、VectorScalarFallbackTypes）— 删除
- **`--analyze-stubs` / `--stub-budget` CLI 选项** — 删除

**替代方案**：
- **需求驱动泛型发现**：类型在方法体编译时按需发现（无批量预扫描）
- **特化方法可达性**：`_calledSpecializedMethods` HashSet 追踪哪些泛型特化的哪些方法被实际调用 — 跳过 77% 泛型方法
- **接口分派感知裁剪**：未分派的泛型接口不会被实例化 — 减少 311 个类型（-7.5%）
- **FeatureSwitchResolver**：SIMD IsSupported=false 死分支消除（brfalse 模式）
- **自引用泛型检测**：替代任意的 MaxGenericNestingDepth=5

**影响**：HelloWorld 代码生成减少 21%，27799 → 26248 方法，315K → 260K 行。

---

## Phase 4: 可行类型回归 IL（40 → 32）✅

**目标**：移除 8 个 RuntimeProvided 类型 — **已完成**

| # | 任务 | 移除数 | 可行性 | 说明 |
|---|------|--------|--------|------|
| 4.1 | IAsyncStateMachine → IL | 1 | ✅ 完成 | 纯接口，移除 RuntimeProvided + 删除 task.h alias |
| 4.2 | CancellationToken → IL | 1 | ✅ 完成 | 只有 f_source 指针，struct 从 Cecil 生成 |
| 4.3-7 | WaitHandle 层级×6 → IL | 6 | ✅ 完成 | struct 从 Cecil 生成，TypeInfo 从 IL 生成，WaitOneCore ICall 保留；POSIX Mutex/Semaphore 有 TODO（当前为 stub 实现） |

**前提**：Phase 3 编译器质量足够让 BCL WaitHandle/CancellationToken IL 正确编译。

## Phase 5: 异步架构重构（长期，32 → 25）— 降级为 Phase F.2

> **降级原因**：async/await 已能工作（true concurrency + thread pool + combinators）。Task struct 重构是内部质量优化，对"编译任意项目"几乎无贡献。

**5.1 已完成** ✅：TplEventSource no-op ICall + ThreadPool ICall + Interlocked.ExchangeAdd — 66 方法从 IL 编译，65 stubbed
**5.2-5.5 待定**：Task struct 重构 → 降级到 Phase F.2（性能优化阶段）

---

## 未来阶段（Phase A-G）

> **核心思路**：不是"给每个功能写 ICall"，而是"修编译器让 BCL IL 编译通"。
> FileStream / Socket / HttpClient / JSON 在 .NET BCL 中有完整 IL 实现，最终通过 P/Invoke 调用 OS API。P/Invoke 已可用。问题在于编译器 bug 导致中间 BCL 方法无法从 IL 翻译。

### Phase A: 编译器收尾 — 修复 stubs 根因 ✅

**目标**：翻译率 > 92%，stubs < 2,000 — **已达成**（Phase A 结束时 1,478 stubs；Phase X 清理 + 需求驱动泛型后当前 1,280）

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

### Phase B: BCL 链验证 — Streaming I/O ✅（Windows）

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
| B.5 | System.Native 原生库集成（Linux） | 中 | **待定** | 类似 BoehmGC/ICU：从 dotnet/runtime 提取 ~30 .c 文件，FetchContent 编译。*推迟到必须实现目标完成后。* |
| B.6 | 移除 File.ReadAllText/WriteAllText ICall 绕过 | 低 | ✅ | File ICall 已全部移除。File/Path/Directory 全部从 BCL IL 编译。SystemIOTest + FileStreamTest 端到端通过（105/105 集成测试）。 |
| B.7 | 集成测试套件 + baselines | 低 | ✅ | FileStreamTest 加入集成测试（Phase 9，39/39 通过）+ UF/RE stub 减少（-94） |

**前置**：Phase A ✅
**产出**：FileStream 端到端从 BCL IL 编译 — **Windows 已通过** ✅，Linux 待 System.Native

### Phase C: BCL 链扩展 — 网络 ✅

**目标**：`HttpClient.GetStringAsync("http://...")` 从 BCL IL 编译

**策略**：同 Phase B — 追踪 BCL IL 链，修中断点。

**平台**：Windows → P/Invoke to **ws2_32.dll** (Winsock2)。Linux 待定（需 System.Native，Phase B.5）。

| # | 任务 | 预估 | 状态 | 说明 |
|---|------|------|------|------|
| C.1 | 追踪 Socket BCL IL 链 | 中 | ✅ | Socket → SafeSocketHandle → Interop.Winsock P/Invoke (Windows)。RuntimeType 修复 + ObjectHasComponentSize ICall |
| C.2 | TCP socket 生命周期 | 高 | ✅ | TCP 完整环回：bind/listen/connect/accept/send/recv（Winsock P/Invoke）。gate pattern 修复（指针 local、Array ref/out、delegate 跨作用域） |
| C.3 | HttpClient 构造 | 高 | ✅ | HttpClient → SocketsHttpHandler → HttpConnectionSettings → TimeSpan/Int128。5 个编译器/运行时修复：有符号比较（`clt`/`cgt` → `signed_lt/gt`）、Exception.GetType ICall、SR 资源字符串 ICall、RunClassConstructor stub、泛型嵌套类型名 mangling（边界感知正则） |
| C.4 | DNS 解析 | 低 | ✅ | `Dns.GetHostAddresses` 通过 Winsock GetAddrInfoW P/Invoke。`dup` 操作码解耦修复（`a[i++]` 模式） |
| C.5 | 集成测试 | 低 | ✅ | SocketTest（TCP 环回 + DNS）+ HttpTest（HttpClient 构造）— 全部集成测试通过 |
| C.6 | 完整 HTTP GET（明文） | 高 | ✅ | HttpGetTest：完整 `HttpClient.GetStringAsync("http://...")` 异步请求/响应链已通过。HttpsGetTest：HTTPS 通过 SChannel (secur32/sspicli) 也已通过。两者均作为集成测试通过。 |

**前置**：Phase B ✅
**产出**：Socket + DNS + HttpClient HTTP GET + HTTPS GET 全部已通过（Windows）。93/93 集成测试通过。

### ThreadPool 架构评估（2026-03-02）

> **决策**：短期无需重构。当前实现**在当前范围内是正确的**；限制在于性能而非正确性。

**当前架构**（`runtime/src/async/threadpool.cpp`，~125 行）：
- 固定大小工作线程池（`std::thread` × `hardware_concurrency`）
- 单一全局 FIFO 队列（`std::queue` + `std::mutex` + `std::condition_variable`）
- 所有 async/await、Task 组合器、continuations 均以真正并发工作
- BCL ThreadPool ICalls（9 个条目）均为有意的 no-op — CIL2CPP 通过自己的 C++ 线程池路由工作

**已验证工作**（576 运行时测试 + 93 集成测试）：
- `queue_work()` 在工作线程上执行（100 个并发项 ✅）
- Task.Run / task_delay / task_when_all / task_when_any ✅
- Continuations: 线程安全链表，400 个并发注册 ✅

**缺少的 .NET 特性**（性能，非正确性）：
- Hill climbing（动态线程数）— 固定数量对 <100 并发任务足够
- Work stealing（每线程 LIFO 队列）— 单队列竞争更高但可工作
- 线程注入（`RequestWorkerThread` 是 no-op）— 极端负载下嵌套 `Task.Run` 死锁风险，BCL 同步回退缓解

**何时重构**（Phase F.2）：
- 当 Task 结构体迁移到 BCL IL 时（需要完整 TPL 依赖链）
- 或性能基准测试显示 4+ 核心上竞争问题
- 中间步骤：实现 work-stealing 队列（~200 行）在完整 Task 迁移之前

### Phase H: 质量收敛（与 C.6/D 并行）

**目标**：让已有能力可预测、可解释、可回归测试。源于外部代码评审发现"可编译但行为偏差"的 icall 简化实现和未文档化的限制。

**策略**：不是"停下来清理"，而是与功能开发（C.6/D）并行，按用户可感知影响排序。

| # | 任务 | 优先级 | 状态 | 说明 |
|---|------|--------|------|------|
| H.1 | TypeCode ICall 修复 | 高 | ✅ | TypeInfo 名称映射到 TypeCode 枚举（17 种原始类型）。修复 `Convert.*`、`String.Format`、序列化器类型判断 |
| H.2 | RenderedBodyError 消减 | 高 | ~85% 完成 | 116 → 17 RE stubs。剩余 17 个为真实 codegen 边界情况。目标：0。 |
| H.3 | 移除 File ICall 绕过（B.6） | 高 | ✅ | File ICall 绕过已移除。FileStream.Read 挂起已修复。BCL IL 通过 StreamReader 正确处理编码。105/105 集成测试通过。 |
| H.4 | 平台兼容性文档 | 中 | ✅ | 支持矩阵 + 承诺等级：完整 / 功能性 / 占位 / 未实现 |
| H.5 | 反射状态文档 | 中 | ✅ | 全部 23 个反射 ICall 的"期望行为 vs 实际行为"表 + 完整修复的前置阶段 |
| H.6 | Codegen bug 最小复现测试 | 低 | 待定 | 每个 FIXME gate pattern 对应一个最小 C# 测试用例（回归锚点） |

**前置**：无（并行运行）
**产出**：行为边界文档化、减少静默降级风险、RenderedBodyError 消减

### Phase D: NativeAOT 元数据 & 生态验证（立即启动 — 与 C.6 并行）

**目标**：支持修剪注解 + 验证 NuGet 生态 — **这是迈向"编译任意 NativeAOT 项目"的最大单次跳跃**

**为什么 D 现在就是关键**：没有 `[DynamicallyAccessedMembers]` 支持，ReachabilityAnalyzer 会静默 tree-shake 掉 NuGet 包运行时需要的类型。没有 ILLink feature switch，System.Text.Json source generator 路径不会激活。每个有 NuGet 依赖的项目都会遇到这些问题。D 解锁整个 NuGet 生态；C.6 仅解锁 HTTP。

| # | 任务 | 预估 | 状态 | 说明 |
|---|------|------|------|------|
| D.0 | NuGet 包集成测试 | 中 | ✅ 已完成 | 10 个 NuGet 包冒烟测试通过（Newtonsoft.Json、DI+Logging+Console、Humanizer、Polly、Serilog+Console、Configuration+Binder）。每个包使用 30-88 行最小示例验证，覆盖极浅。123/123 集成测试。 |
| D.1 | `[DynamicallyAccessedMembers]` 解析 | 中 | ✅ 已完成并验证 | ReachabilityAnalyzer.cs — 完整 13 种 DamFlag 解析 + SeedDynamicallyAccessedMembers()。包含泛型方法/类型参数上的 DAM 支持（DI 关键：`AddSingleton<TService, [DAM] TImpl>()` 保留 TImpl 构造函数）。CLI `--rdxml`。7 个 DAM 测试 + 14 个 rd.xml 测试。 |
| D.2 | rd.xml 解析器 | 低 | ✅ 已完成并验证 | RdXmlParser.cs — 完整 XML 解析 + PreservationRule 映射。CLI `--rdxml` 选项已在 Program.cs 中接入。 |
| D.3 | ILLink feature switch 替换 | 中 | ✅ 已上线 | FeatureSwitchResolver.cs（10 个 AOT 默认开关）+ IRBuilder.Methods.cs:1372-1386（Ldsfld 编译期替换）。所有构建自动生效。 |
| D.4 | AOT 兼容性警告 | 低 | 待定 | 报告 `[RequiresUnreferencedCode]` 调用链 |
| D.5 | Source generator 验证 | 中 | ✅ 已完成（可运行） | JsonSGTest（Phase 13）：`[JsonSerializable]` + AppJsonContext SG 输出通过 CIL2CPP 端到端编译并运行。NuGetSimpleTest（Phase 14）：Newtonsoft.Json 13.0.3 也已完整验证。 |

**前置**：无（可与 C.6 并行）
**产出**：DI + JSON (SG) + Logging 可编译；NuGet 包在 tree-shaking 下正确工作

### Phase C.7: P/Invoke 编组完善（C.6 之后）

**目标**：`[MarshalAs]` + `[Out]` + 数组编组，支持 NuGet 生态和 System.Native

**为什么需要**：P/Invoke 已有 CharSet/CallingConvention/SetLastError。`[MarshalAs]` 属性解析和类型映射已完成（C.7.1）。剩余：`[Out]`/`[In]` 回写语义和数组编组，供 System.Native 和 NuGet 原生互操作包使用。

| # | 任务 | 预估 | 状态 | 说明 |
|---|------|------|------|------|
| C.7.1 | `[MarshalAs]` 属性解析 | 中 | ✅ 完成 | Cecil MarshalInfo 解析（IRBuilder.Methods.cs:123-143），21 种 MarshalAsType 枚举（PInvokeEnums.cs），完整类型映射 GetPInvokeNativeType()（Source.cs:3024-3094）。LPStr/LPWStr/Bool/整数类型均可用。 |
| C.7.2 | `[Out]`/`[In]` 参数方向 | 低 | ✅ 完成 | IR 层完整解析 PInvokeDirection（In/Out/InOut）。bool* copy-back 已实现（bool=1B, BOOL=4B 不可 reinterpret_cast）。blittable 指针类型 native 直接写入无需 copy-back（IL2CPP 架构：managed 即 C++ 类型，无 managed/native 内存边界）。PInvokeTest 集成测试验证：GetSystemInfo [Out] struct、QueryPerformanceCounter [Out] int64、GetDiskFreeSpaceExW 多 [Out] + SetLastError。 |
| C.7.3 | 数组编组 | 中 | ✅ 完成 | ByValTStr ✅（Header.cs 内联 char16_t 数组）、ByValArray ✅（Header.cs 内联固定大小数组）、LPArray ✅（Source.cs 管理数组→原生指针，支持 [In]/[Out]/[InOut] 方向）、ExplicitLayout ✅。SizeParamIndex codegen 调试断言已实现。PInvokeTest 验证：Test 6 GetTempPathW、Test 9 GetModuleFileNameW [Out]、Test 10 GetComputerNameW [Out]+ref size。IL2CPP 架构下管理内存即原生内存，[Out] 数组无需 copy-back。 |

**前置**：Phase C ✅
**产出**：P/Invoke 兼容 System.Native 声明和 NuGet 原生互操作包

### Phase E: 原生库集成 — TLS/zlib（仅链接，不重写）

**目标**：HTTPS + Compression

**注意**：.NET BCL 的 TLS/zlib 有完整 IL，通过 P/Invoke 调用 .NET 专属原生库（与 System.Native 同模式，均从 [dotnet/runtime](https://github.com/dotnet/runtime) 提取）：
- TLS → SChannel（Windows，`secur32.dll`/`schannel.dll` P/Invoke — **无需 FetchContent**）/ `System.Security.Cryptography.Native.OpenSsl`（Linux）
- zlib → `System.IO.Compression.Native`（.NET 的 zlib 封装）

**平台策略**：Windows TLS 使用 SChannel（OS 自带，与 kernel32/ws2_32 同模式）。Linux TLS 使用 OpenSSL（需 FetchContent）。拆分 E.win/E.linux 以更早交付 Windows HTTPS。

| # | 任务 | 预估 | 说明 |
|---|------|------|------|
| E.win | SChannel TLS（Windows） | 中 | ✅ SslStream → P/Invoke to `secur32.dll`/`schannel.dll`。HttpsGetTest 作为集成测试通过。 |
| E.linux | OpenSSL TLS（Linux） | 高 | **待定。**从 dotnet/runtime 提取 `System.Security.Cryptography.Native.OpenSsl`，FetchContent + 链接 OpenSSL |
| E.2 | System.IO.Compression.Native 集成 | 低 | 从 dotnet/runtime 提取，内嵌 zlib |
| E.3 | 移出 InternalPInvokeModules 对应项 | 低 | 让 BCL P/Invoke 声明正常生成 |
| E.4 | Regex 解释器 BCL IL 验证 | 中 | 非 Compiled 模式不依赖 Reflection.Emit |
| E.5 | 端到端测试 | 低 | HTTPS GET + JSON 反序列化 |

**前置**：Phase C (TLS 需要 Socket) + Phase D (JSON 需要元数据感知) + Phase C.7 (MarshalAs 原生库 P/Invoke 需要)
**产出**：`HttpClient.GetStringAsync("https://...")` + `JsonSerializer.Deserialize<T>()` 可用

### Phase F: 性能 & 高级

**目标**：翻译率 > 95%

| # | 任务 | 预估 | 说明 |
|---|------|------|------|
| F.1 | SIMD 标量回退路径完善 | 高 | ✅ 大幅完成。4 层死代码消除（FeatureSwitchResolver + IR 常量传播 + 容器类型泄漏修复 + 渲染时替换）。HttpsGetTest SIMD 错误 303→0。剩余 SIMD stubs 为 KBP 死分支残余，不阻塞。 |
| F.2 | Task struct 重构（原 Phase 5.2-5.5） | 高 | **技术债务。**降低 RuntimeProvided 32→25。异步功能正确工作，但 7 个类型（Task + 6 异步依赖）仍为 C++ runtime struct。当前测试覆盖可能未涉及所有边界用例——扩展 NuGet 验证可能暴露问题。待测试覆盖足够广泛后处理。 |
| F.3 | 增量编译 | 中 | **待定。**IR/codegen 缓存（性能优化） |
| F.4 | 反射模型评估（原 Phase 6） | 中 | **待定。**评估 QCall 替代方案 |

**前置**：Phase A-E 核心功能完成
**产出**：翻译率 > 95%

### Phase G: 产品化

**目标**：从冒烟测试级别提升到真实项目可用

| # | 任务 | 预估 | 状态 | 说明 |
|---|------|------|------|------|
| G.1 | CI/CD (GitHub Actions: Win + Linux) | 中 | 未开始 | |
| G.2 | 10 NuGet 包冒烟测试 | 高 | ✅ 完成 | 10 个包通过最小示例测试（见成熟度评估中的详细列表）。注意：每个包只验证了极小 API 子集（<5%） |
| G.3 | 自包含部署模式 + RID 检测 | 中 | 未开始 | |
| G.4 | 文档完善 | 中 | 未开始 | |
| G.5 | 50+ NuGet 包无需手动修复编译通过 | 极高 | 未开始 | 真正的编译器健壮性验证 — 当前每个新包都需要修复编译器 bug |
| G.6 | 真实应用验证 | 极高 | 未开始 | 编译 3+ 个超过 1000 行的真实 .NET NativeAOT 项目（如 CLI 工具、Web API） |

---

## 依赖关系图

### 当前阶段 → M5（生态广度）

```
Phase 1-4 ✅  →  Phase A ✅  →  Phase B ✅ (Windows)
       ↓
   ┌── Phase C.6 ✅ (完整 HTTP GET) ────────────┐
   │   Phase E.win ✅ (SChannel TLS)             │ ← 并行
   │      ↓                                     │
   │   Phase C.7.2 ✅ ([Out]/[In])              Phase D ✅ (NativeAOT 元数据 + NuGet)
   │   Phase C.7.3 ✅ (数组编组)                 │
   │      ↓                                     │
   │   Phase E.2 (zlib 压缩)                    │
   │      ↓                                     │
   └──────┴─────── 汇合 ───────────────────────┘
                        ↓
              Phase H.2 (RenderedBodyError → 0) — 持续进行
              Phase F.1 ✅ (SIMD: 4 层消除完成)
              Phase G.2 ✅ (10 NuGet 包冒烟测试)
                        ↓
                   ═══ M5 完成 ═══
```

### M5 之后 → M6-M9（深度、优化、跨平台、生产级）

```
M5 ✅ (生态广度)
  ↓
Phase G.5  (50+ NuGet 包无需手动修复) ─── M6 (生态深度)
Phase G.6  (真实应用验证: 3+ 项目)   ──┘
  ↓
Phase F.5  (内联、去虚拟化)        ─── M7 (编译器优化)
Phase F.6  (逃逸分析)              ──┘
  ↓
Phase B.5  (System.Native — Linux I/O + 网络) ─── M8 (跨平台)
Phase E.linux (OpenSSL TLS — Linux HTTPS)     ──┘
  ↓
Phase G.1  (CI/CD)                          ─── M9 (生产候选)
Phase G.4  (文档)                           ──┘
Phase F.3  (增量编译 — 性能)
```

### 远期

```
Phase F.2  (Task struct 重构 — 内部质量)
Phase F.4  (完整反射模型 — QCall 替代)
32 位目标 (ARM/x86 指针大小)
macOS 支持 (Objective-C 桥接)
```

---

## 里程碑

> **重要说明**：M1-M4 的达成标准是"功能在冒烟测试中通过"，不代表生产环境可靠性。
> 见上方"成熟度评估"章节了解真实成熟度。

| 里程碑 | 达成条件 | 对应阶段 | 状态 |
|--------|---------|---------|------|
| **M1: 编译器成熟** | stubs < 2,000，翻译率 > 92% | A | ✅（1,280 stubs, ~95%+） |
| **M2: 文件 I/O** | FileStream/StreamReader 从 BCL IL 编译并运行 | B | ✅ Windows |
| **M3: 联网应用** | HttpClient HTTP GET 从 BCL IL 编译并运行 | C.6 | ✅ HTTP + HTTPS GET 已通过 |
| **M3.5: REST 客户端** | HTTP GET + JSON 序列化端到端 | C.6+D | ✅ JsonSGTest + NuGetSimpleTest（Newtonsoft.Json） |
| **M4: 库生态** | 3+ NuGet PackageReference 项目编译并运行 | D+G.2 | ✅ DITest（DI+Logging+Console = 3 个 NuGet 包） |
| **M5: 生态广度** | HTTPS + 压缩 + DI/日志/配置 + 10 NuGet 包冒烟测试 | E+G.2 | ✅ 完成（HTTPS ✅、压缩 ✅、DI/日志/配置 ✅、SIMD ✅、10 NuGet 包 ✅、ValidationApp ✅） |
| **M6: 生态深度** | 50+ NuGet 包无需手动修复即可编译 + 3 个真实应用验证 | G | 未开始 |
| **M7: 编译器优化** | 内联、去虚拟化、基本逃逸分析 | F+ | 未开始 |
| **M8: 跨平台** | Linux x64 支持（System.Native + OpenSSL） | B.5+E.linux | 未开始 |
| **M9: 生产候选** | 综合测试 + CI/CD + 文档 + 调试支持 | G+ | 未开始 |

## 指标定义

| 指标 | 定义 | 当前值 | Phase A 目标 | 长期目标 |
|------|------|--------|-------------|----------|
| IL 转译率 | (total_methods - stubs) / total_methods | **~95%+**（1,280 stubs / ~26k 方法） | >92% ✅ | >95% |
| RuntimeProvided 数 | RuntimeProvidedTypes 条目 | **32**（was 40, -8） | ~32 | ~25（Phase F.2） |
| CoreRuntime 数 | 方法完全由 C++ 提供 | 22 | ~22 | ~10（Phase F.4） |
| ICall 数 | C++ 内部调用 | **~490** | ~400 | 趋稳（功能来自 BCL IL，非 ICall） |

### 指标口径规范（确保一致性报告）

| 维度 | 值 | 说明 |
|------|-----|------|
| **程序集** | HelloWorld（主要），SocketTest（次要） | 所有标题指标使用 HelloWorld，除非另有说明 |
| **数据来源** | 生成的 `*_stubs.cpp` 文件 | `stub_budget.json` 已在 Phase X 中移除 |
| **分类** | 7 个 stub 根因分类 | MissingBody, KnownBrokenPattern, RenderedBodyError, ClrInternalType, UndeclaredFunction, UnknownParameterTypes, UnknownBodyReferences |
| **转译率** | `1 - (stub_total / total_methods)` | `total_methods` 来自所有 pass 完成后的 IRModule |
| **版本绑定** | 引用指标时包含 commit hash + 日期 | 防止过时数据跨阶段持续 |

> **注意**：当编译器改进扩展编译范围时，stub 数量可能暂时增加（例如修复 C2362 switch 门控使更多方法可编译，暴露其被调用方为新 stubs）。这是正面进展——更多方法编译——即使标题数字上升。

---

## 关键决策总结

| 决策 | 结果 | 理由 |
|------|------|------|
| RuntimeType | Type 别名 | 对标 IL2CPP `Il2CppReflectionType` |
| 反射类型 | 保持 CoreRuntime | .NET 8 BCL 反射 IL 深度依赖 QCall/MetadataImport，短期无法 IL 编译 |
| Task | 保持 RuntimeProvided（短期） | 4 个自定义运行时字段 + std::mutex* + MSVC padding，长期需架构重构 |
| ThreadPool | 保持自定义 C++ 实现（短期） | 固定大小线程池 + 全局 FIFO 队列在当前范围内正确。BCL ThreadPool ICalls 为有意 no-op。缺少 hill climbing、work stealing、每线程队列 — 仅性能优化。重构推迟到 Phase F.2。 |
| WaitHandle | 目标 IL + ICall（Phase 4） | struct 简单，BCL IL 可编译，需注册 8 个 OS 原语 ICall |
| SIMD | 标量回退 struct + IsSupported=false | BCL 有非 SIMD 回退路径 |
| File I/O ICall | ✅ 已移除 | File ICall 已移除。完整 BCL IL 链：File.* → StreamReader → FileStream → SafeFileHandle → P/Invoke kernel32 |
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
