# 项目目标与定位

> [English Version](goals.md)

## CIL2CPP 是什么

CIL2CPP 是一个 **C# → C++ AOT (Ahead-of-Time) 编译器**，将 .NET 程序编译为原生 C++ 代码，再由 C++ 编译器（MSVC/GCC/Clang）编译为原生可执行文件。

名称中的 CIL 指 Common Intermediate Language（通用中间语言），即 .NET 编译器 (Roslyn) 将 C# 源码编译后的字节码格式。CIL2CPP 读取这些字节码并逐条翻译为等价的 C++ 代码，因此不需要逐个"支持"C# 语法特性——只要 IL 指令全覆盖，所有编译为这些指令的 C# 代码自动可用。

## 对标项目

| 项目 | 定位 | 架构 | 目标平台 |
|------|------|------|---------|
| **Unity IL2CPP** | Unity 引擎专用 AOT | Mono BCL + 自定义 GC | iOS/Android/WebGL/Console |
| **.NET NativeAOT** | 官方通用 AOT | CoreCLR BCL + 自定义 GC | 全平台 |
| **CIL2CPP** | 通用 AOT 编译器 | .NET 8 CoreCLR BCL + BoehmGC | Windows/Linux/macOS |

### 与 Unity IL2CPP 的关系

CIL2CPP 借鉴了 Unity IL2CPP 的核心架构思路——**将 BCL (Base Class Library) 的 IL 字节码直接编译为 C++**，仅在最底层保留手写的 `[InternalCall]` C++ 实现 (icall)。但在以下方面不同：

- **输入**：Unity 使用 Mono BCL（较旧/较小），CIL2CPP 使用 .NET 8 CoreCLR BCL（更大/更现代）
- **范围**：Unity 只需支持游戏相关的 BCL 子集，CIL2CPP 目标是通用覆盖
- **GC**：Unity 使用自定义 GC，CIL2CPP 使用 BoehmGC（与 Mono 相同的保守式 GC）

### 与 .NET NativeAOT 的关系

.NET NativeAOT 是微软官方的 AOT 方案，但它内嵌了一个精简版的 CoreCLR 运行时。CIL2CPP 的目标是在功能覆盖范围上对标 NativeAOT，但使用完全独立的、轻量级的 C++ 运行时。

## 项目范围

**定位：通用 AOT 编译器**（对标 .NET NativeAOT 的覆盖范围）。

目标不仅限于 Unity 支持的 BCL 子集，而是尽可能覆盖完整的 .NET BCL：
- 控制台应用（Console I/O、环境变量、命令行参数）
- 文件系统操作（File/Path/Directory，未来 FileStream）
- 集合与 LINQ（List/Dictionary/Where/Select 等）
- 异步编程（async/await、Task、线程池）
- 反射（typeof/GetType/GetMethods/GetFields/Invoke）
- 网络（未来：Socket/HttpClient）
- 序列化（未来：System.Text.Json）

## 核心设计原则

### 1. Unity IL2CPP 架构：BCL IL 全编译

所有有 IL 方法体的 BCL 方法**直接从 IL 编译为 C++**，与用户代码走完全相同的编译路径。仅在最底层保留 `[InternalCall]` 方法的 C++ 手写实现（约 243 个 icall 映射）。

```
方法调用
  ↓
ICallRegistry 查找
  ├─ 命中 → [InternalCall]，无 IL 方法体
  │         → 调用 C++ 运行时实现
  └─ 未命中 → 正常 IL 编译（BCL = 用户代码相同路径）
              → 生成 C++ 函数
```

这意味着 `Console.WriteLine("Hello")` 的完整调用链全部从 IL 编译：
```
Console.WriteLine → TextWriter.WriteLine → StreamWriter.Write → Encoding.GetBytes → P/Invoke → WriteFile
```

### 2. 多程序集 + 树摇

编译器自动加载用户程序集 + 第三方依赖 + BCL 程序集，通过可达性分析 (ReachabilityAnalyzer) 实现树摇，只编译实际使用的类型和方法。

### 3. 跨平台

- **编译器**：C# .NET 8，任何 .NET 支持的平台都能运行
- **运行时**：C++20，CMake 构建，支持 Windows (MSVC)、Linux (GCC/Clang)、macOS (Apple Clang)
- **生成代码**：平台无关的 C++ 代码 + CMake 项目

### 4. 多层安全网

BCL 中部分方法引用 CLR 内部类型（RuntimeType、QCallTypeHandle 等），无法编译为 C++。编译器有 4 层安全网自动检测并 stub 化这些方法，确保编译永不失败：

1. HasClrInternalDependencies — IR 级检测
2. HasKnownBrokenPatterns — 预渲染检测
3. RenderedBodyHasErrors — 试渲染检测
4. GenerateMissingMethodStubImpls — 兜底 stub

## 非目标 / AOT 根本限制

以下功能由于 AOT 编译模型的固有约束**无法支持**，这与 Unity IL2CPP 和 .NET NativeAOT 的限制相同：

| 功能 | 原因 |
|------|------|
| `System.Reflection.Emit` | 运行时生成 IL 并执行——AOT 编译后无 IL 解释器/JIT |
| `DynamicMethod` | 运行时创建方法并执行 |
| `Expression<T>.Compile()` | 运行时编译表达式树为可执行代码 |
| `Assembly.Load()` / `Assembly.LoadFrom()` | 运行时动态加载程序集——AOT 要求所有代码在编译期可知 |
| `Activator.CreateInstance(string typeName)` | 按名称字符串动态实例化——编译期无法确定目标类型 |
| `Type.MakeGenericType()` | 运行时构造泛型类型——单态化必须在编译期完成 |
| `ExpandoObject` / `dynamic` | DLR 完全依赖运行时绑定 |
| 运行时代码热更新 | 无 JIT 编译器，编译后的机器码不可替换 |
