# 未来开发计划

> 最后更新：2026-02-23

## 概述

CIL2CPP 定位为**通用 AOT 编译器**（对标 NativeAOT 覆盖范围）。当前最大瓶颈是 **BCL 方法被 stub 化**——HelloWorld 生成 ~6,639 个 stub 方法，主要因为 CLR 内部类型依赖和 P/Invoke 原生模块过滤。

后续开发分为 5 个阶段，目标：**打通 BCL 依赖链，减少 stub 数量，扩展功能覆盖，最终编译任意 .NET AOT 兼容项目**。

---

## 三大阻断源

1. **27 个 CLR 内部类型**（`ClrInternalTypeNames`）→ 引用它们的方法自动 stub 化 → 级联
2. **11 个被过滤的 P/Invoke 模块**（`InternalPInvokeModules`）→ 底层调用断裂 → 级联
3. **代码生成安全网**（HasKnownBrokenPatterns + RenderedBodyHasErrors）→ 保守 stub 化

---

## Phase I: 基础打通（目标：stub < 3,000）

| # | 任务 | 说明 |
|---|------|------|
| I.1 | Stub 依赖分析工具 | `--analyze-stubs` 选项：根因分类 + 级联影响量 + 解锁排名 |
| I.2 | RuntimeType AOT | RuntimeType = Type 别名（复用现有 `reflection.h`），从 ClrInternal 移除 |
| I.3 | Handle 类型移除 | RuntimeTypeHandle/MethodHandle/FieldHandle 从 ClrInternal 移除，BCL IL 自然编译 |
| I.4 | AggregateException | 添加 `f_innerExceptions` 字段，从 ClrInternal 移除 |
| I.5 | SafeHandle 补全 | 等 I.2-I.3 完成后检查是否自然解锁，剩余方法加 ICall |
| I.6 | Thread.CurrentThread | TLS 存储当前 Thread 对象，修复 nullptr FIXME |
| I.7 | GCHandle 弱引用 | BoehmGC `GC_register_disappearing_link` |
| I.8 | FIXME 修复 | IO Encoding/BOM、ThrowHelper 字符串、Array boxing |
| I.9 | 验证 | 重新 codegen + stub 统计 |

**关键决策**：RuntimeType = Type 别名，与 Unity IL2CPP `Il2CppReflectionType` 架构一致。

---

## Phase II: 中间层解锁（目标：stub < 1,500）

| # | 任务 | 说明 |
|---|------|------|
| II.1 | CLR 反射类型映射 | RuntimeMethodInfo→ManagedMethodInfo、RuntimeFieldInfo→FieldInfo、TypeInfo/Assembly 最小 AOT 实现 |
| II.2 | WaitHandle 混合方案 | BCL IL 编译上层 + ICall 封装 OS 原语（WaitOneCore, WaitMultipleIgnoringSyncContext） |
| II.3 | P/Invoke 调用约定 | `__stdcall`/`__cdecl` 修饰符发射（x86 需要） |
| II.4 | CharSet.Auto 分支 | Windows=Unicode, Linux=Ansi |
| II.5 | System.Native 集成 | FetchContent ~30 个 .c 文件，Linux 用（Windows 不需要） |
| II.6 | zlib 集成 | FetchContent，解锁 GZipStream/DeflateStream |
| II.7 | 验证 | stub 统计 + FileStream 链路检查 |

**关键决策**：WaitHandle 混合方案——上层 BCL IL + 底层 OS ICall，与 Unity IL2CPP / NativeAOT 一致。

---

## Phase III: 功能扩展（目标：覆盖复杂控制台应用）

| # | 任务 | 说明 |
|---|------|------|
| III.1 | FileStream/StreamReader/Writer | Phase II 完成后 BCL IL 自然解锁，用 Stub 工具检查残余 |
| III.2 | Memory\<T\>/ReadOnlyMemory\<T\> | 拦截核心方法（Span\<T\> 模式） |
| III.3 | OpenSSL 集成 | ICU 同模式：Win 预编译二进制 + Linux find_package |
| III.4 | Net.Security.Native | GSSAPI/TLS 包装，解锁 HTTPS/SslStream |
| III.5 | SIMD 精细化 | 不再整体 stub Intrinsics，只 stub 自递归方法 + `IsSupported=false` 常量折叠 |
| III.6 | BrokenPatterns 精细化 | 量化 9 个 pattern 影响，逐个修复根因 |

**关键决策**：
- SIMD 精细化 stub + 渐进式标量回退（BCL 有非 SIMD 回退路径）
- OpenSSL 与 ICU 相同的预编译模式

---

## Phase IV: 网络 & 高级功能（目标：Web 应用基础）

| # | 任务 | 说明 |
|---|------|------|
| IV.1 | Socket 基础 | 路线 A: BCL IL 自然编译（Winsock + System.Native 跨平台，不引入 C++ 网络库） |
| IV.2 | HttpClient | BCL SocketsHttpHandler 纯 C# 编译 |
| IV.3 | System.Text.Json | Utf8JsonReader/Writer + JsonSerializer |
| IV.4 | Regex 完善 | 解释器模式 + source generator 支持（Compiled 模式 AOT 不兼容） |
| IV.5 | 32-bit 支持 | 指针大小通过 BuildConfiguration 传入 |

**关键决策**：
- 网络层路线 A——BCL 内置跨平台分支，不需要 C++ 网络库
- Regex 走解释器模式（Compiled 用 Reflection.Emit → AOT 不支持）

---

## Phase V: 产品化（目标：对标 NativeAOT 覆盖）

| # | 任务 | 说明 |
|---|------|------|
| V.1 | CI/CD | GitHub Actions: Windows (MSVC) + Linux (GCC/Clang) + stub 回归检测 |
| V.2 | 性能基准 | 编译时间 + 运行时性能 + 代码大小 |
| V.3 | 真实项目测试 | 5-10 个 NuGet 包编译验证 |
| V.4 | 文档完善 | 英文文档 + API 参考 + 迁移指南 |
| V.5 | Stub 优化 | 目标 < 500（当前 ~6,639） |

---

## 依赖关系图

```
Phase I (基础打通, stub < 3000)
  I.1 Stub 分析工具
  I.2 RuntimeType = Type alias
  I.3 Handle types 移除
  I.4 AggregateException 修复
  I.5 SafeHandle (依赖 I.2-I.3)
  I.6-I.8 Thread/GCHandle/FIXME (独立)
       ↓
Phase II (中间层解锁, stub < 1500)
  II.1 反射类型映射
  II.2 WaitHandle (BCL IL + OS ICall)
  II.3-II.4 P/Invoke 修复 (独立)
  II.5 System.Native (Linux)
  II.6 zlib
       ↓
Phase III (功能扩展)
  III.1 FileStream (依赖 II: SafeHandle+WaitHandle+System.Native)
  III.2 Memory<T> (独立)
  III.3 OpenSSL (ICU 同模式)
  III.4 Net.Security.Native (依赖 III.3)
  III.5-III.6 SIMD+Patterns 精细化 (独立)
       ↓
Phase IV (网络 & 高级)
  IV.1 Socket (路线A: BCL IL, 依赖 II+III)
  IV.2 HttpClient (依赖 IV.1+III.3)
  IV.3 JSON (依赖 Reflection)
  IV.4 Regex (解释器模式)
  IV.5 32-bit (独立)
       ↓
Phase V (产品化)
  CI/CD, 性能, 测试, 文档, 持续优化
```

---

## 关键决策总结

| 决策 | 结果 | 理由 |
|------|------|------|
| RuntimeType | Type 别名 | 与 Unity IL2CPP `Il2CppReflectionType` 一致 |
| WaitHandle | 混合方案（BCL IL + OS ICall） | Unity IL2CPP / NativeAOT 共同采用 |
| SIMD | 精细化 stub + 渐进式标量回退 | BCL 有非 SIMD 回退路径 |
| 网络层 | 路线 A: BCL IL 自然编译 | BCL 内置跨平台分支，不需要 C++ 网络库 |
| OpenSSL | ICU 同模式（Win 预编译 + Linux 系统包） | 已验证可行 |
| Regex | 解释器模式 + source generator 支持 | Compiled 模式用 Reflection.Emit → AOT 不兼容 |

---

## 被过滤的 P/Invoke 模块（11 个）

| 模块 | 功能 | 解锁阶段 |
|------|------|---------|
| `System.Native` / `libSystem.Native` | POSIX 文件/进程/网络 | Phase II |
| `System.IO.Compression.Native` | zlib | Phase II |
| `System.Globalization.Native` / `libSystem.Globalization.Native` | ICU 封装 | 已有 ICU 集成 |
| `System.Security.Cryptography.Native.OpenSsl` | OpenSSL | Phase III |
| `System.Net.Security.Native` | GSSAPI/TLS | Phase III |
| `QCall` / `QCall.dll` | CLR 内部桥接 | 保留（CLR JIT 专用） |
| `ucrtbase` / `ucrtbase.dll` | CRT | 已链接 |

## CLR 内部类型消解计划（27 个）

| 批次 | 类型 | 解锁阶段 |
|------|------|---------|
| 第一批（高影响） | RuntimeType, RuntimeTypeHandle, RuntimeMethodHandle, RuntimeFieldHandle, AggregateException | Phase I |
| 第二批（中影响） | RuntimeMethodInfo, RuntimeFieldInfo, RuntimePropertyInfo, RuntimeConstructorInfo, TypeInfo, Assembly, RuntimeAssembly, WaitHandle | Phase II |
| 第三批（低影响/保留） | QCall 桥接(4), DefaultBinder, DBNull, Signature, MetadataImport, RuntimeCustomAttributeData, ThreadInt64PersistentCounter, IAsyncLocal, CalendarId, EraInfo, PosixSignalRegistration | 保留 stub 或按需处理 |
