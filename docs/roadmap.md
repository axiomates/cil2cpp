# 未来开发计划

## 概述

CIL2CPP 定位为**通用 AOT 编译器**（对标 NativeAOT 覆盖范围）。当前最大瓶颈是 **BCL 方法被 stub 化**——HelloWorld 生成 ~6,639 个 stub 方法，主要因为 CLR 内部类型依赖和 P/Invoke 原生模块过滤。

后续开发的核心目标：**打通 BCL 依赖链，减少 stub 数量，扩展功能覆盖**。

---

## 短期计划：Phase H — .NET Native Libs 集成

### H.1: 集成 System.Native（Linux/macOS 底层）

**目标**：编译 dotnet/runtime 的 System.Native 为静态库，链接到生成项目。

- FetchContent 拉取 dotnet/runtime（只取 `src/native/libs/System.Native/`，~30 个 .c 文件）
- 锁定 .NET 8.0.x release tag
- 输出静态库 `libSystem.Native.a`
- 从 InternalPInvokeModules 移除 `"System.Native"` 和 `"libSystem.Native"`
- 生成项目 CMakeLists.txt 中添加链接
- Windows 不需要此库（BCL 直接 P/Invoke kernel32.dll）

### H.2: 消除关键 CLR 内部类型阻断

**目标**：为最高影响的 CLR 内部类型提供 AOT 兼容实现，减少 stub 级联。

**策略**：不需要完整实现所有 37 个类型，只需让 `HasClrInternalDependencies` 对关键类型不再触发 stub 化。

1. **RuntimeType → 映射到现有 Type 系统**
   - 从 ClrInternalTypeNames 移除
   - 添加为 RuntimeProvidedType
   - C++ runtime 中添加 RuntimeType struct（继承 Type，加 TypeInfo* 字段）
   - 最小方法实现：get_Name, get_FullName, get_IsValueType 等

2. **RuntimeTypeHandle → thin wrapper**
   - struct with `intptr_t m_type`
   - GetTypeFromHandle ICall 已有

3. **SafeHandle / CriticalFinalizerObject 完善**
   - 当前只有 .ctor，补全 8 个缺失方法
   - 解锁 FileStream → SafeFileHandle 链路

4. **WaitHandle 最小实现**
   - 基本的 WaitOne/WaitAny/WaitAll → OS primitive

### H.3: 集成其他原生库

| 库 | 优先级 | 理由 |
|----|--------|------|
| System.IO.Compression.Native (zlib) | 高 | 小巧，FetchContent 简单，解锁 GZip |
| System.Security.Cryptography.Native.OpenSsl | 中 | 解锁 HTTPS 前提 |
| System.Net.Security.Native | 中 | 解锁 HTTPS/TLS |
| System.Globalization.Native | 低 | 已有 ICU 集成 |

每个库的集成模式与 H.1 相同：FetchContent → 编译 → 移除过滤 → 链接。

### H.4: Stub 级联分析与消除

**目标**：将 HelloWorld 的 ~6,639 stubs 减少 50%+（目标 <3,000）。

1. 构建 stub 依赖图：分析每个 stub 的根因
2. 按影响排序：找出消除后解锁最多方法的根因
3. 逐个修复：每修复一个根因，重新 codegen 统计减少量
4. 回归测试：确保全部测试通过

---

## 中期计划

| 功能 | 说明 | 依赖 |
|------|------|------|
| Memory\<T\> / ReadOnlyMemory\<T\> | 拦截核心方法，支持 System.IO.Pipelines | — |
| FileStream / StreamReader / StreamWriter | 流式 I/O 支持 | SafeHandle 完善 (H.2) |
| P/Invoke 完善 | 调用约定发射 + MarshalAs + CharSet.Auto 平台分支 | — |
| SafeHandle 完善 | DangerousGetHandle/DangerousRelease/Dispose 等 8 个方法 | — |

## 长期计划

| 功能 | 说明 | 前提 |
|------|------|------|
| System.Net | Socket / HttpClient | System.Native + SafeHandle + WaitHandle |
| System.Text.Json | JSON 序列化/反序列化 | Span\<T\> + Reflection 完善 |
| Regex 完善 | 修复 RegexCache 依赖 | CLR 内部类型消除 |
| RuntimeType AOT 完整实现 | 完整的 Type API | 渐进式完善 |
| Linux CI/CD | GitHub Actions 自动构建和测试 | — |

---

## 阻断分析

### 被过滤的 P/Invoke 模块（11 个）

| 模块 | 功能 | 解锁内容 |
|------|------|---------|
| `System.Native` | POSIX 文件/进程/网络 | FileStream, Process, Socket (Linux) |
| `libSystem.Native` | 同上（带前缀） | 同上 |
| `System.Globalization.Native` | ICU 封装 | 我们已有 ICU，可替代 |
| `libSystem.Globalization.Native` | 同上 | 同上 |
| `System.IO.Compression.Native` | zlib | GZipStream, DeflateStream |
| `System.Security.Cryptography.Native.OpenSsl` | OpenSSL | SHA256, AES, RSA, X509 |
| `System.Net.Security.Native` | GSSAPI/TLS | HTTPS, SslStream |
| `QCall` | CLR 内部桥接 | 反射、类型加载（AOT 受限） |
| `QCall.dll` | 同上 | 同上 |
| `ucrtbase` | CRT | 已链接，无需处理 |
| `ucrtbase.dll` | 同上 | 同上 |

### 阻断链路的 CLR 内部类型（37 个，按影响分组）

**高影响（阻断 IO/Net/Crypto 链路）：**
- `System.RuntimeType` / `System.RuntimeTypeHandle` — 几乎所有 BCL 中间层都引用
- `System.RuntimeMethodHandle` / `System.RuntimeFieldHandle`
- `System.Reflection.RuntimeMethodInfo` / `RuntimeFieldInfo` / `RuntimePropertyInfo` / `RuntimeConstructorInfo`

**中影响（阻断特定功能）：**
- `System.Runtime.CompilerServices.QCallTypeHandle` / `QCallAssembly` / `ObjectHandleOnStack` / `MethodTable`
- `System.DefaultBinder` / `System.Signature`
- `System.Threading.WaitHandle`

**低影响（可容忍）：**
- `System.Globalization.CalendarId` / `EraInfo`
- `System.Threading.ThreadInt64PersistentCounter` / `IAsyncLocal`
- `System.Reflection.MetadataImport` / `RuntimeCustomAttributeData`
- `System.Runtime.InteropServices.PosixSignalRegistration`

---

## 风险与缓解

| 风险 | 缓解策略 |
|------|---------|
| System.Native 编译依赖复杂 | 先在 Linux CI 验证，Windows 不需要此库 |
| RuntimeType 实现不完整导致运行时崩溃 | 渐进式：先让方法编译通过，再逐步完善方法体 |
| 移除 CLR 类型过滤后新增编译错误 | 保留试渲染安全网（RenderedBodyHasErrors）作兜底 |
| stub 减少量不如预期 | H.4 阶段通过依赖图分析精确定位瓶颈 |

## 关键文件（Phase H 涉及修改）

| 文件 | 修改内容 |
|------|---------|
| `compiler/CIL2CPP.Core/CodeGen/CppCodeGenerator.Source.cs` | InternalPInvokeModules 列表 |
| `compiler/CIL2CPP.Core/IR/IRBuilder.cs` | ClrInternalTypeNames 列表 |
| `compiler/CIL2CPP.Core/CodeGen/CppCodeGenerator.Header.cs` | HasKnownBrokenPatterns |
| `runtime/CMakeLists.txt` | FetchContent System.Native |
| `runtime/include/cil2cpp/safe_handle.h` | SafeHandle 完善 |
| `runtime/include/cil2cpp/runtime_type.h` | RuntimeType AOT 实现 (NEW) |
| `compiler/CIL2CPP.Core/CodeGen/CppCodeGenerator.CMake.cs` | 生成项目链接 System.Native |
