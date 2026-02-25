# 开发路线图

> 最后更新：2026-02-25

## 设计原则

### 核心目标

**只有真正无法从 IL 编译的内容使用 C++ runtime 映射，其他一切从 BCL IL 转译为 C++。**

以 Unity IL2CPP 为参考架构，但不盲目照搬——IL2CPP 使用 Mono BCL（依赖链远比 .NET 8 简单），且其源码来自社区反编译（可能不完整）。我们基于 .NET 8 BCL 的实际依赖链做第一性原理分析。

### 四条准则

1. **IL 优先**：一切可以从 BCL IL 编译的内容都应该编译
2. **ICall 是桥梁**：C++ runtime 仅通过 ICall 暴露底层原语（Monitor、Thread、Interlocked、GC、IO），BCL IL 调用这些原语
3. **编译器质量驱动**：提升 IL 转译率的方式是修复编译器 bug，而不是添加 RuntimeProvided 类型
4. **第一性原理判断**：每个 RuntimeProvided 类型必须有明确技术理由（runtime 直接访问字段 / BCL IL 引用 CLR 内部类型 / 嵌入 C++ 类型）

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

### Stub 分布（HelloWorld, 3,167 个 / 25,444 总方法，87.6% 翻译率）

> codegen stub 数。`--analyze-stubs` 额外报告 96 个 ClrInternalType（QCall/MetadataImport），总计 3,263。

| 类别 | 数量 | 占比 | 性质 |
|------|------|------|------|
| MissingBody | 1,904 | 60.1% | 无 IL body（abstract/extern/JIT intrinsic）— 多数合理 |
| KnownBrokenPattern | 808 | 25.5% | SIMD 333 + SIMD-heavy 83 + TypeHandle/MethodTable 19 + 其他 |
| UndeclaredFunction | 298 | 9.4% | 泛型特化缺失（IRBuilder 未创建特化类型）+ 级联 |
| UnknownBodyReferences | 39 | 1.2% | 方法体引用未声明类型（多为嵌套泛型） |
| UnknownParameterTypes | 22 | 0.7% | 参数类型未声明（INumberBase DIM + 少量 Span 特化） |
| RenderedBodyError | 0 | 0% | III.15 全部重分类至 KBP（根因待修复） |

**可修复的编译器问题**：UndeclaredFunction (298) + UnknownBodyRefs (39) = **~337 个方法**。修复后预计 stub < 2,800，翻译率 > 89%。

**不可修复或暂缓**：MissingBody 中大部分是 abstract/extern/CLR intrinsic；SIMD (333+83) 需要 intrinsics 支持或运行时回退。

**IL 转译率**：87.6%（22,277 compiled / 25,444 total）。
**测试**：1,240 C# + 592 C++ + 35 集成 — 全部通过。

### 距离最终目标

| 项目类型 | 预估完成度 | 说明 |
|---------|-----------|------|
| 简单控制台应用 | ~90% | Phase III 大幅提升，基本 BCL 链通畅 |
| 类库项目 | ~80% | 集合、泛型、异步、反射都可用 |
| 复杂控制台应用 | ~60% | 深层 BCL 依赖（ConcurrentQueue、Regex 等）仍有 stub |
| ASP.NET / Web 项目 | ~5% | 需要 System.Net、HTTP 栈 |
| 任意 .NET 项目 | ~45% | CLR 内部类型依赖 + 深层 BCL 链是最大瓶颈 |

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

## Phase III: 编译器管道质量（进行中）

**目标**：提升 IL 转译率——修复阻止 BCL IL 编译的根因

**进展**：4,402 → 3,167 stubs（-1,235，-28.1%），IL 转译率 87.6%

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

---

## Phase IV: 可行类型回归 IL（40 → 32）✅

**目标**：移除 8 个 RuntimeProvided 类型 — **已完成**

| # | 任务 | 移除数 | 可行性 | 说明 |
|---|------|--------|--------|------|
| IV.1 | IAsyncStateMachine → IL | 1 | ✅ 完成 | 纯接口，移除 RuntimeProvided + 删除 task.h alias |
| IV.2 | CancellationToken → IL | 1 | ✅ 完成 | 只有 f_source 指针，struct 从 Cecil 生成 |
| IV.3-7 | WaitHandle 层级×6 → IL | 6 | ✅ 完成 | struct 从 Cecil 生成，TypeInfo 从 IL 生成，WaitOneCore ICall 保留；POSIX Mutex/Semaphore 有 TODO（当前为 stub 实现） |

**前提**：Phase III 编译器质量足够让 BCL WaitHandle/CancellationToken IL 正确编译。

## Phase V: 异步架构重构（长期，32 → 25）

**目标**：让 Task 及依赖它的类型从 IL 编译

**V.1 分析结论** ✅（详见 [phase_v1_analysis.md](phase_v1_analysis.md)）：
- **66 个 Task 方法已从 BCL IL 成功编译**（状态查询、原子操作、生命周期、异常处理等）
- **65 个 Task 方法 stubbed**，三条主要依赖链：
  - **TplEventSource/EventSource**（22 个）：纯诊断，no-op ICall 零风险解锁
  - **ThreadPool/PortableThreadPool**（15 个）：泛型特化缺失 + RenderedBodyError
  - **RenderedBodyError**（18 个）：编译器 bug，可修复
- **字段布局不兼容**：运行时 Task 有 4 个自定义字段（offset 12-39），Generated Task\<T\> 在 offset 12 直接放 BCL 字段
- **推荐路径**：渐进式迁移（C 方案），先 TplEventSource no-op → ThreadPool ICall → 视结果决定 Task struct 重构

| # | 任务 | 复杂度 | 状态 | 说明 |
|---|------|--------|------|------|
| V.1 | 评估 BCL Task IL 依赖链 | — | ✅ | 详见 phase_v1_analysis.md |
| V.1.1 | TplEventSource → no-op ICall | 低 | ✅ | 5 个 EventSource ICall（ctor/IsEnabled×2/IsSupported/WriteEvent），MissingBody -30, UndeclaredFunction -30 |
| V.1.2 | ExecutionContext fix + ThreadPool ICall | 中 | ✅ | ThreadPool 9 ICall + Interlocked.ExchangeAdd 2 ICall（III.18 合并完成） |
| V.2 | Task struct 重构 | **高** | 待定 | 删除 4 自定义字段→BCL 原生布局，重写 continuation 系统 |
| V.3 | TaskAwaiter / AsyncTaskMethodBuilder → IL | 中 | 待定 | 依赖 V.2 Task struct 布局 |
| V.4 | ValueTask / ValueTaskAwaiter / AsyncIteratorBuilder → IL | 中 | 待定 | 依赖 Task 链 |
| V.5 | CancellationTokenSource → IL | 中 | 待定 | 依赖 ITimer + ManualResetEvent + Registrations 链 |

**V.1.1 前置条件**：无——纯 ICall 注册，可立即开始。

**V.1.2 前置条件**：
- RenderedBodyError < 150（当前 249）
- IRBuilder 泛型特化修复（PortableThreadPool 方法）

**V.2-V.5 前置条件**：
- V.1.1 + V.1.2 完成并验证
- delegate invoke 修复（Task continuation 使用 delegate）
- ConcurrentQueue try-finally 修复（Task 依赖 ConcurrentQueue）

## Phase VI: 反射模型优化（评估）

**目标**：评估反射高层 API 是否可从 IL 编译

| # | 任务 | 说明 |
|---|------|------|
| VI.1 | 分析 BCL 反射 IL 依赖 | .NET 8 的 RuntimeType.GetMethodCandidates() 等方法的 QCall 依赖深度 |
| VI.2 | 评估 ICall 拦截可行性 | 是否可以在 ICall 层拦截 QCall 调用，让高层 IL 编译 |
| VI.3 | 如可行：添加反射 ICall + 移出 CoreRuntime | Type.GetType_internal、MethodBase.Invoke_internal 等 |
| VI.4 | 如不可行：保持现状 | .NET 8 反射 IL 与 Mono 差异太大，保持 CoreRuntime 是务实选择 |

**风险**：.NET 8 BCL 反射 IL 比 Mono 深度依赖 QCall/MetadataImport。IL2CPP 的三层反射模型基于 Mono，不能直接套用。

---

## Phase VII: 功能扩展

| # | 任务 | 说明 |
|---|------|------|
| VII.1 | System.Native 集成 | FetchContent ~30 个 .c 文件（Linux POSIX 层） |
| VII.2 | Memory\<T\>/ReadOnlyMemory\<T\> | BCL IL 自然编译 |
| VII.3 | zlib 集成 | FetchContent，解锁 GZipStream/DeflateStream |
| VII.4 | OpenSSL 集成 | ICU 同模式（Win 预编译 + Linux find_package） |

## Phase VIII: 网络 & 高级功能

| # | 任务 | 说明 |
|---|------|------|
| VIII.1 | Socket 基础 | BCL IL + Winsock/System.Native |
| VIII.2 | HttpClient | BCL SocketsHttpHandler |
| VIII.3 | System.Text.Json | Utf8JsonReader/Writer + JsonSerializer |
| VIII.4 | Regex | 解释器模式 + source generator 支持 |

## Phase IX: 产品化

| # | 任务 | 说明 |
|---|------|------|
| IX.1 | CI/CD | GitHub Actions: Windows (MSVC) + Linux (GCC/Clang) |
| IX.2 | 性能基准 | 编译时间 + 运行时性能 + 代码大小 |
| IX.3 | 真实项目测试 | 5-10 个 NuGet 包编译验证 |
| IX.4 | 文档完善 | 英文文档 + API 参考 + 迁移指南 |

---

## 依赖关系图

```
Phase I  (基础打通) ✅
Phase II (中间层解锁) ✅
       ↓
Phase III (编译器管道质量) ← 当前（4,402→3,167, -28.1%, 87.6%）
  III.1-18 ✅ (泛型特化补全 + ICall-mapped 跳过 + ThreadPool ICall)
       ↓
Phase IV (可行类型 → IL) 40 → 32 ✅
       ↓                    ↓（可并行）
Phase V (异步架构重构)    Phase VII (功能扩展)
  V.1 分析 ✅                System.Native / zlib / OpenSSL
  V.1.1 TplEventSource ✅
  V.1.2 ThreadPool ICall ✅
  V.2-V.5 ← 需 V.1.x 验证     ↓
  32 → 25（长期）          Phase VIII (网络 & 高级)
       ↓                    Socket / HttpClient / JSON / Regex
Phase VI (反射评估)            ↓
  评估高层 API IL 编译     Phase IX (产品化)
                            CI/CD / 性能 / 测试 / 文档
```

---

## 指标定义

| 指标 | 定义 | 当前值 | 短期目标 | 长期目标 |
|------|------|--------|----------|----------|
| IL 转译率 | (reachable 方法 - stub 方法) / reachable 方法 | **87.6%** (3167/25444) | >70% ✅ | >90% |
| RuntimeProvided 数 | RuntimeProvidedTypes 集合条目数 | **32** (was 40, -8) | ~32 | ~25（Task 重构后） |
| CoreRuntime 数 | 方法完全由 C++ 提供的类型数 | 22 | ~22 | ~10（若反射可 IL） |
| ICall 数 | C++ 实现的内部调用 | ~282 | ~300 | ~500 |

---

## 关键决策总结

| 决策 | 结果 | 理由 |
|------|------|------|
| RuntimeType | Type 别名 | 对标 IL2CPP `Il2CppReflectionType` |
| 反射类型 | 保持 CoreRuntime | .NET 8 BCL 反射 IL 深度依赖 QCall/MetadataImport，短期无法 IL 编译 |
| Task | 保持 RuntimeProvided（短期） | 4 个自定义运行时字段 + std::mutex* + MSVC padding，长期需架构重构 |
| WaitHandle | 目标 IL + ICall（Phase IV） | struct 简单，BCL IL 可编译，需注册 8 个 OS 原语 ICall |
| SIMD | 标量回退 struct + IsSupported=false | BCL 有非 SIMD 回退路径 |
| 网络层 | BCL IL 自然编译 | BCL 内置跨平台分支 |
| Regex | 解释器模式 + source generator | Compiled 模式用 Reflection.Emit → AOT 不兼容 |
| IL2CPP 对标 | 参考但不盲目照搬 | IL2CPP 基于 Mono BCL，.NET 8 依赖链差异大 |

## CLR 内部类型（永久保留为 stub）

| 类型 | 说明 |
|------|------|
| QCallTypeHandle / QCallAssembly / ObjectHandleOnStack / MethodTable | CLR JIT 专用桥接 |
| MetadataImport / RuntimeCustomAttributeData | CLR 内部元数据访问 |

## 被过滤的 P/Invoke 模块

| 模块 | 功能 | 解锁阶段 |
|------|------|---------|
| `System.Native` | POSIX 文件/进程/网络 | Phase VII |
| `System.IO.Compression.Native` | zlib | Phase VII |
| `System.Globalization.Native` | ICU 封装 | ✅ 已有 ICU 集成 |
| `System.Security.Cryptography.Native.OpenSsl` | OpenSSL | Phase VII |
| `System.Net.Security.Native` | GSSAPI/TLS | Phase VIII |
| `QCall` / `QCall.dll` | CLR 内部桥接 | 永久保留 |
| `ucrtbase` / `ucrtbase.dll` | CRT | ✅ 已链接 |
