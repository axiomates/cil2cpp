# CIL2CPP 能力清单

> 最后更新：2026-03-01
>
> 本文档描述 CIL2CPP 当前**能做什么**。开发计划与进度见 [roadmap.zh-CN.md](roadmap.zh-CN.md)。
>
> [English Version](capabilities.md)

## 概览

CIL2CPP 是 C# → C++ AOT 编译器（类似 Unity IL2CPP）。当前支持完整 C# 语法（100% IL 操作码覆盖），BCL 从 IL 编译（Unity IL2CPP 架构），~400 个 ICall 条目。1,240 C# + 592 C++ + 47 集成测试全部通过。

## 核心指标

| 指标 | 数量 |
|------|------|
| IL 操作码覆盖率 | **100%**（全部 ~230 种 ECMA-335 操作码） |
| ICallRegistry 条目 | **~400 个**（涵盖 30+ 类别） |
| C# 编译器测试 | **~1,240 个**（xUnit） |
| C++ 运行时测试 | **592 个**（Google Test，18 个测试文件） |
| 端到端集成测试 | **47 个**（11 个阶段） |
| 运行时头文件 | **32 个** |

---

## C# 功能支持表

> ✅ 已支持 ⚠️ 部分支持（BCL/运行时限制） ❌ 未支持（缺失 icall 或 AOT 限制）
>
> 所有 C# 语法经 Roslyn 编译为标准 IL 后，CIL 指令翻译层均已覆盖。
> 状态标记 ⚠️/❌ 反映的是 BCL 依赖链或运行时 icall 层面的限制。

### 基本类型

| 功能 | 状态 | 备注 |
|------|------|------|
| int, long, float, double | ✅ | 映射到 C++ int32_t, int64_t, float, double |
| bool, byte, sbyte, short, ushort, uint, ulong | ✅ | 完整基本类型映射 |
| char | ✅ | UTF-16 (char16_t) |
| string | ✅ | 不可变，UTF-16 编码，字面量驻留池 |
| IntPtr, UIntPtr | ✅ | intptr_t, uintptr_t |
| 类型转换 (全部 Conv_*) | ✅ | 13 种基本 + 20 种 checked |
| struct (值类型) | ✅ | initobj/ldobj/stobj + 装箱/拆箱 + 拷贝 + ldind/stind |
| enum | ✅ | typedef + constexpr 常量 + TypeInfo |
| 装箱/拆箱 | ✅ | box\<T\>() / unbox\<T\>()，Nullable box 拆包 |
| Nullable\<T\> | ✅ | BCL IL 编译 + box 拆包 + 泛型单态化 |
| ValueTuple | ✅ | BCL IL 编译，支持 >7 嵌套 |
| record / record struct | ✅ | 方法合成（ToString/Equals/GetHashCode/Clone），with 表达式 |

### 面向对象

| 功能 | 状态 | 备注 |
|------|------|------|
| 类定义 / 构造函数 | ✅ | 实例字段 + 静态字段 + 方法 + newobj |
| 静态构造函数 (.cctor) | ✅ | `_ensure_cctor()` once-guard |
| 继承（单继承） | ✅ | 基类字段拷贝 + VTable 继承 |
| 虚方法 / 多态 | ✅ | VTable 分派 |
| 属性 | ✅ | get_/set_ 方法调用 |
| 类型转换 (is/as) | ✅ | isinst → object_as()，castclass → object_cast() |
| 抽象类/方法 | ✅ | VTable 正确分配槽位 |
| 接口 | ✅ | InterfaceVTable 分派 |
| 泛型类/方法 | ✅ | 单态化（monomorphization） |
| 运算符重载 | ✅ | op_Addition 等静态方法调用 |
| 终结器 / 析构函数 | ✅ | TypeInfo.finalizer + BoehmGC 注册 |
| 默认接口方法 (DIM) | ✅ | 接口默认实现作 VTable 回退 |
| 泛型协变/逆变 | ✅ | ECMA-335 variance-aware 检查 |

### 控制流

| 功能 | 状态 | 备注 |
|------|------|------|
| if/else, while/for, do-while | ✅ | 全部条件分支指令 |
| switch (IL switch 表) | ✅ | C++ switch/goto 跳转表 |
| 模式匹配 (switch 表达式) | ✅ | Roslyn 编译为标准 IL |
| Range / Index (..) | ✅ | Index/Range 结构体 |
| checked 算术 | ✅ | OverflowException 抛出 |

### 数组

| 功能 | 状态 | 备注 |
|------|------|------|
| 一维数组 | ✅ | newarr + ldelem/stelem 全类型 + 越界检查 |
| 数组初始化器 | ✅ | RuntimeHelpers.InitializeArray → memcpy |
| 多维数组 (T[,]) | ✅ | MdArray 运行时 |
| Span\<T\> / ReadOnlySpan\<T\> | ✅ | BCL IL 编译，ref struct |

### 异常处理

| 功能 | 状态 | 备注 |
|------|------|------|
| throw / try / catch / finally | ✅ | setjmp/longjmp 宏 |
| rethrow | ✅ | CIL2CPP_RETHROW |
| 异常过滤器 (catch when) | ✅ | ECMA-335 Filter handler |
| 嵌套 try/catch/finally | ✅ | 多层嵌套完整支持 |
| 自定义异常类型 | ✅ | 继承 Exception |
| 栈回溯 | ✅ | Windows: DbgHelp, POSIX: backtrace（仅 Debug） |
| using 语句 | ✅ | try/finally + IDisposable 接口分派 |

### 标准库 (BCL)

| 功能 | 状态 | 备注 |
|------|------|------|
| System.Object | ✅ | ToString/GetHashCode/Equals/GetType |
| System.String | ✅ | 布局 icall + BCL IL 编译（Concat/Format/Join/Split 等） |
| Console.WriteLine/Write/ReadLine | ✅ | BCL IL 全链路编译 |
| System.Math / MathF | ✅ | ~40 个 icall |
| List\<T\> / Dictionary\<K,V\> | ✅ | BCL IL 编译 |
| LINQ | ✅ | Where/Select/OrderBy 等，BCL IL 编译 |
| yield return / IEnumerable | ✅ | 迭代器状态机 |
| IAsyncEnumerable\<T\> | ✅ | await foreach |
| System.IO (File/Path/Directory) | ✅ | 22 个 ICall，C++17 filesystem |
| System.Net（Socket/DNS） | ⚠️ | Socket TCP 环回 ✅、DNS 解析 ✅、HttpClient 构造 ✅（Windows，Winsock P/Invoke）。完整 HTTP GET 待做 |

### 委托与事件

| 功能 | 状态 | 备注 |
|------|------|------|
| 委托 / 多播委托 | ✅ | delegate_create / Combine / Remove |
| 事件 | ✅ | add_/remove_ + Delegate.Combine |
| Lambda / 闭包 | ✅ | 编译器生成 DisplayClass |

### 高级功能

| 功能 | 状态 | 备注 |
|------|------|------|
| async / await | ✅ | 线程池 + continuation + Task 组合器 |
| CancellationToken | ✅ | BCL IL 编译 |
| 多线程 | ✅ | Thread/Monitor/Interlocked/lock/volatile |
| 反射 | ✅ | typeof/GetType/GetMethods/GetFields/MethodInfo.Invoke |
| 特性 (Attribute) | ✅ | 元数据存储 + 运行时查询 |
| unsafe (指针/fixed/stackalloc) | ✅ | 指针类型 + BoehmGC 保守扫描 |
| P/Invoke / DllImport | ✅ | extern "C" + 类型编组 + SetLastError |
| Span\<T\> | ✅ | ref struct + BCL IL 编译 |

---

## ICallRegistry 分类明细（~270 个条目）

| 类别 | 条目数 | 说明 |
|------|--------|------|
| System.Math | 28 | Sqrt/Sin/Cos/Pow/Log/Floor/Ceiling 等 double 版本 |
| System.MathF | 20 | 对应 float 版本 |
| System.ThrowHelper | 17 | 各种异常抛出辅助 |
| System.Char | 16 | IsLetter/IsDigit/IsUpper/ToUpper/ToLower 等 |
| System.Threading.Interlocked | 14 | Increment/Decrement/Exchange/CompareExchange |
| System.Array | 13 | Copy/Clear/GetLength/GetLowerBound/Reverse/Sort |
| System.IO.File | 12 | Exists/ReadAllText/WriteAllText/ReadAllBytes/Delete/Copy/Move |
| System.String | 12 | FastAllocateString/get_Length/get_Chars/Comparison |
| System.Globalization.CompareInfo | 11 | 区域感知字符串比较 |
| System.GC | 11 | Collect/WaitForPendingFinalizers/GetTotalMemory |
| System.IO.Path | 8 | GetFullPath/GetDirectoryName/GetFileName/GetExtension/GetTempPath |
| System.Threading.Monitor | 8 | Enter/Exit/TryEnter/Wait/Pulse/PulseAll |
| System.Environment | 8 | Exit/GetEnvironmentVariable/GetCommandLineArgs/ProcessorCount |
| System.Object | 6 | GetType/ToString/GetHashCode/Equals/MemberwiseClone |
| System.Threading.Thread | 6 | Start/Join/Sleep/CurrentThread/ManagedThreadId |
| System.Runtime.InteropServices.Marshal | 6 | AllocHGlobal/FreeHGlobal/AllocCoTaskMem/FreeCoTaskMem/GetLastPInvokeError |
| System.Buffer | 5 | BlockCopy/MemoryCopy/ByteLength |
| System.Delegate/MulticastDelegate | 5 | Combine/Remove/GetInvocationList |
| System.RuntimeHelpers | 4 | InitializeArray/IsReferenceOrContainsReferences |
| System.Runtime.InteropServices.GCHandle | 4 | Alloc/Free/Target/IsAllocated |
| System.ArgIterator | 4 | 变长参数支持 |
| System.Globalization.OrdinalCasing | 3 | 序数大小写转换 |
| System.IO.Directory | 2 | Exists/CreateDirectory |
| System.Runtime.InteropServices.SafeHandle | 8 | .ctor/DangerousGetHandle/SetHandle/DangerousAddRef/DangerousRelease/IsClosed/SetHandleAsInvalid/Dispose |
| 其他 (Volatile, Enum, Type, HashCode, Marvin, NativeLibrary, ...) | ~18 | 各 1-3 个条目 |

---

## System.IO 实现明细

### 架构

System.IO 采用 ICall 拦截模式，在公共 API 层拦截 File/Path/Directory 调用，使用 C++17 `<filesystem>` 实现跨平台支持。

### 已实现的 ICall（22 个）

**File（12 个）**：Exists, ReadAllText(1/2 参数), WriteAllText(1/2 参数), ReadAllBytes, WriteAllBytes, Delete, Copy, Move, ReadAllLines, AppendAllText

**Path（8 个）**：GetFullPath, GetDirectoryName, GetFileName, GetFileNameWithoutExtension, GetExtension, GetTempPath, Combine(2 参数), Combine(3 参数)

**Directory（2 个）**：Exists, CreateDirectory

### 未实现

| 功能 | 说明 |
|------|------|
| FileStream / StreamReader / StreamWriter | 无流式 I/O |
| 目录枚举 | 无 GetFiles / EnumerateFiles / Delete |
| 文件信息 | 无 FileInfo / DirectoryInfo，无时间戳/属性 |
| Encoding 参数 | ReadAllText/WriteAllText 的 Encoding 参数被忽略 (FIXME) |

---

## P/Invoke 实现明细

### 已支持

- DllImport 声明（extern "C"，自动过滤 .NET 内部模块）
- 基本类型编组（int/long/float/double/IntPtr 直接传递）
- String 编组（Ansi: UTF-8，Unicode: 零拷贝 UTF-16）
- Boolean 编组（C# bool ↔ Win32 BOOL）
- Blittable Struct 编组（SequentialLayout 值类型直接传递）
- 回调委托（函数指针：提取 method_ptr → C 函数指针）
- SetLastError（TLS 存储，调用前清零 + 调用后捕获）
- Marshal.AllocHGlobal/FreeHGlobal/AllocCoTaskMem/FreeCoTaskMem

### FIXME / 未实现

| 功能 | 状态 | 说明 |
|------|------|------|
| 调用约定 | ✅ | StdCall/FastCall/ThisCall 已发射到 extern 声明 |
| CharSet.Auto | ⚠️ | 硬编码为 Unicode |
| SafeHandle 方法 | ⚠️ | 8 个 ICall（.ctor/DangerousGetHandle/SetHandle/DangerousAddRef/DangerousRelease/IsClosed/SetHandleAsInvalid/Dispose），缺 ReleaseHandle 虚方法分派 |
| MarshalAs 特性 | ❌ | 未解析 |
| Out/In 特性 | ❌ | 未区分参数方向 |
| 数组编组 / Ref String | ❌ | 不支持 |

---

## 已知限制

| 限制 | 说明 |
|------|------|
| CLR 内部类型依赖 | BCL IL 引用 QCallTypeHandle / MetadataImport 等 CLR 内部类型 → 方法体自动 stub 化 |
| BCL 深层依赖链 | 中间层被 stub 化 → 上层方法不可用 |
| System.Net（完整 HTTP） | HttpClient 已可构造，完整 HTTP GET 请求/响应链待做 |
| Regex 内部 | 依赖 CLR 内部 RegexCache 等 |
| SIMD | 需要平台特定 intrinsics，当前使用标量回退 struct |
| 32 位目标 | 指针大小硬编码为 8 字节（仅 64 位）。ARM/x86 推迟到 Phase F |

---

## 平台兼容性矩阵

> 兼容性等级：**完整** = 与 .NET 行为一致 | **功能性** = 可用但元数据简化 | **占位** = 返回占位值 | **未实现** = 尚不可用

| 功能区域 | Windows x64 | Linux x64 | macOS | 说明 |
|---------|:-----------:|:---------:|:-----:|------|
| Console I/O | 完整 | 完整 | 完整 | BCL IL 编译 |
| Math/MathF | 完整 | 完整 | 完整 | ~45 icall，直接 C++ stdlib |
| String 操作 | 完整 | 完整 | 完整 | BCL IL 编译，ICU 全球化 |
| 集合 (List/Dict/LINQ) | 完整 | 完整 | 完整 | BCL IL 编译 |
| async/await | 完整 | 完整 | 完整 | 自定义线程池 + 续体 |
| 文件 I/O (FileStream) | 完整 | 未实现 | 未实现 | Windows: kernel32 P/Invoke。Linux: 需 System.Native (Phase B.5) |
| 文件 I/O (File.ReadAllText) | 功能性 | 未实现 | 未实现 | HACK: 绕过 BCL IL，仅 UTF-8。将被移除 (Phase H.3) |
| Socket (TCP) | 完整 | 未实现 | 未实现 | Windows: ws2_32 P/Invoke |
| DNS 解析 | 完整 | 未实现 | 未实现 | Windows: GetAddrInfoW P/Invoke |
| HttpClient 构造 | 完整 | 未实现 | 未实现 | BCL IL 的 SocketsHttpHandler 链 |
| 反射 (GetType/typeof) | 完整 | 完整 | 完整 | TypeInfo 基础 |
| 反射 (GetMethods/GetFields) | 功能性 | 功能性 | 功能性 | 名称正确，属性简化 |
| 反射 (BindingFlags) | 占位 | 占位 | 占位 | 返回硬编码 Public\|Instance (0x14) |
| 反射 (TypeCode) | 占位 | 占位 | 占位 | 所有类型返回 Object。修复计划 (Phase H.1) |
| 反射 (IsPublic/可见性) | 占位 | 占位 | 占位 | 所有类型返回 true |
| 反射 (StackFrame) | 占位 | 占位 | 占位 | 方法信息返回 nullptr |
| 反射 (Assembly 元数据) | 占位 | 占位 | 占位 | 无 Assembly 对象。需 Phase D |
| 线程优先级 | 占位 | 占位 | 占位 | No-op，返回 Normal |
| 线程 ManagedId | 功能性 | 功能性 | 占位 | 使用 OS 线程 ID（macOS: 返回 0） |
| P/Invoke (kernel32/ws2_32) | 完整 | 未实现 | 未实现 | Windows 专用 |
| DLL 符号加载 | 完整 | 未实现 | 未实现 | Windows: GetProcAddress。Linux: dlsym 待做 |
| SIMD 执行 | 未实现 | 未实现 | 未实现 | 仅标量回退 struct |
| 编码 (非 UTF-8) | 占位 | 未实现 | 未实现 | File ICall 忽略 Encoding 参数。修复: 移除 ICall (Phase H.3) |

---

## 反射 ICall 状态

> 23 个反射 ICall 中有 14 个返回简化/占位值。完整反射保真度需要 Phase D（NativeAOT 元数据）。

| ICall | 期望行为 (.NET) | 当前行为 (CIL2CPP) | 修复阶段 |
|-------|---------------|-------------------|---------|
| Type.GetTypeCode | 按类型返回枚举 (Int32→9, String→18 等) | 始终返回 Object (1) | H.1 |
| Type.IsPublic | 实际可见性 | 始终返回 true | H.1 (TypeFlags) |
| Type.IsAbstract | 实际标志位 | 正确 ✅ | — |
| Type.IsValueType | 实际标志位 | 正确 ✅ | — |
| Type.IsArray | 实际标志位 | 正确 ✅ | — |
| Type.IsNestedPublic | 嵌套可见性 | 始终返回 false | D |
| Type.IsEnumDefined | 枚举值查找 | 始终返回 false | D |
| Type.IsEquivalentTo | 结构等价 | 仅指针比较 | D |
| RuntimeTypeHandle.GetElementType | 数组/指针元素类型 | 返回 nullptr | D |
| RuntimeTypeHandle.GetToken | 元数据 token | 返回 0 | D |
| RuntimeTypeHandle.GetAssembly | Assembly 对象 | 返回 nullptr | D |
| RuntimeTypeHandle.IsByRefLike | ref struct 检测 | 始终返回 false | D |
| MethodBase.IsVirtual | 实际标志位 | 始终返回 false | D |
| MethodInfo.BindingFlags | 实际标志位 | 硬编码 0x14 | D |
| MethodInfo.GetGenericArguments | 泛型类型参数 | 返回 nullptr | D |
| MethodInfo.GetDeclaringType | 声明类型 | 返回 nullptr | D |
| Delegate.get_Method | 目标 MethodInfo | 返回 nullptr | D |
| StackFrame.GetMethod | 栈方法信息 | 返回 nullptr | F.4 |
| GCHandle.CompareExchange | 原子表操作 | 返回 handle 不变 | F.4 |

---

## 测试覆盖

### C++ 运行时测试（591 个，18 个文件）

| 模块 | 测试数 |
|------|--------|
| Exception | 71 |
| String | 52 |
| Type System | 48 |
| Checked | 47 |
| Reflection | 46 |
| Unicode | 40 |
| IO | 34 |
| Array | 31 |
| Collections | 31 |
| Object | 28 |
| MemberInfo | 28 |
| Boxing | 26 |
| Globalization | 24 |
| Async | 23 |
| Delegate | 18 |
| Threading | 17 |
| GC | 16 |
| TypedReference | 11 |
| **合计** | **592** |

### 端到端集成测试（47 个，11 个阶段）

| 阶段 | 测试内容 | 测试数 |
|------|---------|--------|
| 前置检查 | dotnet、CMake、runtime 安装 | 3 |
| HelloWorld | codegen → build → run → 验证输出 | 5 |
| 类库项目 | 无入口点 → add_library | 4 |
| Debug 配置 | #line 指令、IL 注释 | 4 |
| 字符串字面量 | string_literal、__init_string_literals | 2 |
| 多程序集 | 跨程序集类型/方法 | 5 |
| ArglistTest | 变长参数 | 5 |
| FeatureTest | 综合语言特性 codegen-only | 3 |
| SystemIOTest | System.IO 端到端 | 4 |
| FileStreamTest | FileStream BCL IL 链（write/read/streamwriter/streamreader） | 4 |
| SocketTest | TCP 环回 + DNS 解析（Winsock P/Invoke） | 4 |
| HttpTest | HttpClient 从 BCL IL 构造 | 4 |
| **合计** | | **47** |
