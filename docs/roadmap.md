# 开发路线图

> 最后更新：2026-02-23

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

### 必须 C++ runtime 的类型（26 个，当前正确）

| 类型 | 数量 | 技术原因 |
|------|------|----------|
| Object / ValueType / Enum | 3 | GC 头 + 类型系统基础，每个托管对象的根 |
| String | 1 | 内联 UTF-16 buffer + GC 特殊处理（string_create/concat/intern） |
| Array | 1 | 变长布局 + bounds + GC（array_create/get/set） |
| Exception | 1 | setjmp/longjmp 异常机制，runtime 直接访问 message/inner/trace |
| Delegate / MulticastDelegate | 2 | 函数指针 + 调度链（delegate_invoke/combine） |
| Type / RuntimeType | 2 | 类型元数据系统，typeof() → TypeInfo* → Type* 缓存 |
| Thread | 1 | TLS + OS 线程管理，runtime 直接访问线程状态 |
| 反射 struct ×12 | 12 | .NET 8 BCL 反射 IL 深度依赖 QCall/MetadataImport，无法 AOT 编译 |
| TypedReference / ArgIterator | 2 | varargs 机制，编译器特殊处理 |
| **Task** | **1** | **4 个自定义运行时字段（f_status/f_exception/f_continuations/f_lock）+ f_lock 是 std::mutex* + MSVC padding 问题** |

### 可迁移为 IL 的类型（8 个，短期目标）

| 类型 | 数量 | 可行性 | 说明 |
|------|------|--------|------|
| IAsyncStateMachine | 1 | **容易** | 纯接口，当前 alias to Object，无需 struct |
| CancellationToken | 1 | **容易** | 只有 f_source 指针，struct 可从 Cecil 生成 |
| WaitHandle 层级 | 6 | **可行** | BCL IL 可编译，需注册 8 个 OS 原语 ICall |

### 依赖 Task 的类型（6 个，长期目标，需重大架构变更）

| 类型 | 数量 | 问题 |
|------|------|------|
| TaskAwaiter / AsyncTaskMethodBuilder | 2 | 只有 f_task 字段，但依赖 Task struct 布局 |
| ValueTask / ValueTaskAwaiter / AsyncIteratorMethodBuilder | 3 | 依赖 Task + BCL 依赖链（ThreadPool、ExecutionContext） |
| CancellationTokenSource | 1 | 依赖 ITimer + ManualResetEvent + Registrations 链 |

**长期愿景**：重写异步运行时架构，让 Task 从自定义 C++ 实现迁移到 BCL IL 实现。这需要整个 TPL 依赖链（ThreadPool、TaskScheduler、ExecutionContext、SynchronizationContext）都能从 IL 编译。

### RuntimeProvided 目标

- **短期**：35 → 27（移除 IAsyncStateMachine + CancellationToken + WaitHandle×6）
- **长期**：27 → 21（Task 架构重构后移除 TaskAwaiter/Builder/ValueTask 等 6 个）

### Unity IL2CPP 参考（仅供对比，非盲目照搬）

> **注意**：IL2CPP 使用 Mono BCL，.NET 8 BCL 的依赖链远比 Mono 复杂。以下对比仅供参考。
> IL2CPP 源码来自社区反编译的 libil2cpp headers，可能不完整。

IL2CPP Runtime struct: `Il2CppObject` / `Il2CppString` / `Il2CppArray` / `Il2CppException` / `Il2CppDelegate` / `Il2CppMulticastDelegate` / `Il2CppThread` / `Il2CppReflectionType` / `Il2CppReflectionMethod` / `Il2CppReflectionField` / `Il2CppReflectionProperty` (~12 个)

IL2CPP 从 IL 编译: Task/async 全家族、CancellationToken/Source、WaitHandle 层级——但这基于 Mono 的较简单 BCL，不能直接类比 .NET 8。

---

## 当前状态

### RuntimeProvided 类型：35 个（短期目标 ~27，长期 ~21）

详见上方"RuntimeProvided 类型分类"章节。

### Stub 分布（HelloWorld, 4,402 个）

| 类别 | 数量 | 占比 | 性质 |
|------|------|------|------|
| MissingBody | 2,171 | 49.3% | 无 IL body（abstract/extern/JIT intrinsic）— 多数合理 |
| RenderedBodyError | 701 | 15.9% | **编译器 bug — IL 有 body 但 C++ 编译失败** |
| UnknownBodyReferences | 540 | 12.3% | 方法体引用未声明类型 |
| KnownBrokenPattern | 452 | 10.3% | 已知无法编译的模式 |
| UnknownParameterTypes | 293 | 6.7% | 参数类型未声明 |
| UndeclaredFunction | 149 | 3.4% | 调用未声明函数 |
| ClrInternalType | 96 | 2.2% | QCall 桥接类型 |

**可修复的编译器问题**：RenderedBodyError (701) + UnknownBodyReferences (540) + UndeclaredFunction (149) = **1,390 个方法**，占 31.6%。

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

## Phase III: 编译器管道质量（当前阶段）

**目标**：提升 IL 转译率——修复阻止 BCL IL 编译的根因

| # | 任务 | 影响量 | 说明 |
|---|------|--------|------|
| III.1 | SIMD 标量回退 | ✅ 完成 | Vector64/128/256/512 struct 定义 |
| III.2 | RenderedBodyErrors 修复 | ~701 方法 | 分析 C++ 编译失败的具体模式，逐个修复根因 |
| III.3 | UnknownBodyReferences 修复 | ~540 方法 | 方法体引用类型在 knownTypeNames 中缺失 |
| III.4 | UndeclaredFunction 修复 | ~149 方法 | 函数签名未声明 |
| III.5 | FilteredGenericNamespaces 放开 | 级联解锁 | 逐步放开安全命名空间（System.Diagnostics 等） |
| III.6 | KnownBrokenPattern 精简 | ~452 方法 | 审查每个模式是否还有必要 |

---

## Phase IV: 可行类型回归 IL（35 → 27）

**目标**：移除 8 个 RuntimeProvided 类型

| # | 任务 | 移除数 | 可行性 | 说明 |
|---|------|--------|--------|------|
| IV.1 | IAsyncStateMachine → IL | 1 | 容易 | 纯接口，移除 RuntimeProvided 即可 |
| IV.2 | CancellationToken → IL | 1 | 容易 | 只有 f_source 指针，struct 从 Cecil 生成 |
| IV.3 | WaitHandle → IL + ICall | 1 | 可行 | 注册 WaitOneCore 等 OS 原语 ICall |
| IV.4 | EventWaitHandle → IL + ICall | 1 | 可行 | 注册 CreateEventCoreWin32/Set/Reset ICall |
| IV.5 | ManualResetEvent / AutoResetEvent → IL | 2 | 可行 | 继承 EventWaitHandle，无额外字段 |
| IV.6 | Mutex → IL + ICall | 1 | 可行 | 注册 CreateMutexCoreWin32/ReleaseMutex ICall |
| IV.7 | Semaphore → IL + ICall | 1 | 可行 | 注册 CreateSemaphoreCoreWin32/ReleaseSemaphore ICall |

**前提**：Phase III 编译器质量足够让 BCL WaitHandle/CancellationToken IL 正确编译。

## Phase V: 异步架构重构（长期，27 → 21）

**目标**：让 Task 及依赖它的类型从 IL 编译

**挑战**：
- Task 有 4 个自定义运行时字段（f_status/f_exception/f_continuations/f_lock），其中 f_lock 是 std::mutex*
- MSVC tail-padding 问题要求 Task 不继承 Object
- 需要 BCL Task 的完整依赖链工作：ThreadPool、TaskScheduler、ExecutionContext

| # | 任务 | 说明 |
|---|------|------|
| V.1 | 评估 BCL Task IL 依赖链 | 分析 .NET 8 Task 方法体引用的所有类型，确认可编译范围 |
| V.2 | Task runtime 改造 | 从自定义字段改为 BCL 字段布局（m_stateFlags 等），运行时操作改为 ICall |
| V.3 | TaskAwaiter / AsyncTaskMethodBuilder → IL | 依赖 Task struct 布局 |
| V.4 | ValueTask / ValueTaskAwaiter / AsyncIteratorMethodBuilder → IL | 依赖 Task 链 |
| V.5 | CancellationTokenSource → IL | 依赖 ITimer + ManualResetEvent + Registrations 链 |

**注意**：此 Phase 需要大量架构重构，可能与功能扩展 Phase 并行推进。

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
Phase III (编译器管道质量) ← 当前
  III.1 SIMD 标量回退 ✅
  III.2-III.6 修复 RenderedBodyErrors / UnknownBodyRef / UndeclaredFunction
       ↓
Phase IV (可行类型 → IL) 35 → 27
  IAsyncStateMachine + CancellationToken + WaitHandle×6
       ↓                    ↓（可并行）
Phase V (异步架构重构)    Phase VII (功能扩展)
  Task 及依赖类型 → IL      System.Native / zlib / OpenSSL
  27 → 21（长期）              ↓
       ↓                  Phase VIII (网络 & 高级)
Phase VI (反射评估)         Socket / HttpClient / JSON / Regex
  评估高层 API IL 编译可行性     ↓
                          Phase IX (产品化)
                            CI/CD / 性能 / 测试 / 文档
```

---

## 指标定义

| 指标 | 定义 | 当前值 | 短期目标 | 长期目标 |
|------|------|--------|----------|----------|
| IL 转译率 | (reachable 方法 - stub 方法) / reachable 方法 | ~55% | >70% | >90% |
| RuntimeProvided 数 | C++ runtime 定义 struct 的类型数 | 35 | ~27 | ~21 |
| CoreRuntime 数 | 方法完全由 C++ 提供的类型数 | 22 | ~22 | ~10（若反射可 IL） |
| ICall 数 | C++ 实现的内部调用 | 248 | ~260 | ~300-500 |

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
