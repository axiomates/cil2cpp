# CIL2CPP

将 .NET/C# 程序编译为原生 C++ 代码的 AOT 编译工具，类似于 Unity IL2CPP。

## 技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| 编译器 | C# / .NET | 8.0 |
| IL 解析 | Mono.Cecil | NuGet 最新 |
| 运行时 | C++ | 20 |
| GC | BoehmGC (bdwgc) | v8.2.12 (FetchContent 自动下载) |
| 构建系统 | dotnet + CMake | CMake 3.20+ |
| 运行时分发 | CMake install + find_package | |
| 编译器测试 | xUnit + coverlet | xUnit 2.9, coverlet 6.0 |
| 运行时测试 | Google Test | v1.15.2 (FetchContent) |
| 集成测试 | Python3 (`tools/dev.py integration`) | 跨平台 |
| 开发者工具 | Python3 (`tools/dev.py`) | stdlib only |

## 前置要求

- **.NET 8 SDK** — 用于构建编译器和编译输入的 C# 项目，`dotnet` 需在 PATH 中
- **CMake 3.20+** — 用于构建运行时和生成的 C++ 代码
- **C++ 20 编译器**：
  - Windows: MSVC 2022 (Visual Studio 17.0+)
  - Linux: GCC 12+ 或 Clang 15+
  - macOS: Apple Clang 14+ (Xcode 14+)

**开发环境额外依赖（可选，用于覆盖率报告）：**

> **快速安装：** `python tools/dev.py setup` 会自动检测并安装以下可选依赖。

- **[OpenCppCoverage](https://github.com/OpenCppCoverage/OpenCppCoverage)** — C++ 代码覆盖率收集（Windows）
  ```bash
  winget install OpenCppCoverage.OpenCppCoverage
  ```
- **[ReportGenerator](https://github.com/danielpalme/ReportGenerator)** — 合并 C#/C++ 覆盖率并生成 HTML 报告
  ```bash
  dotnet tool install -g dotnet-reportgenerator-globaltool
  ```
- Linux 用户：`lcov` + `lcov_cobertura`（替代 OpenCppCoverage）

## 项目结构

```
cil2cpp/
├── compiler/                   # C# 编译器 (.NET 项目)
│   ├── CIL2CPP.CLI/            #   命令行入口
│   ├── CIL2CPP.Core/           #   核心编译逻辑
│   │   ├── IL/                 #     IL 解析 (Mono.Cecil)
│   │   ├── IR/                 #     中间表示 + 类型映射
│   │   └── CodeGen/            #     C++ 代码生成
│   └── CIL2CPP.Tests/          #   编译器单元测试 (xUnit, 1240+ tests)
├── tests/                      # 测试用 C# 项目（编译器输入）
├── runtime/                    # C++ 运行时库 (CMake 项目)
│   ├── CMakeLists.txt
│   ├── cmake/                  #   CMake 包配置模板
│   ├── include/cil2cpp/        #   头文件
│   ├── src/                    #   GC、类型系统、异常、BCL
│   └── tests/                  #   运行时单元测试 (Google Test, 446+ tests)
└── tools/
    └── dev.py                  # 开发者 CLI (build/test/coverage/codegen/integration)
```

## 工作原理

### 编译流水线全景

```
 C# 源码                    CIL2CPP 编译器 (C#)                             原生编译
─────────    ┌─────────────────────────────────────────────────────┐    ──────────────
             │                                                     │
 .csproj ──→ │ dotnet build ──→ .NET DLL (IL)                      │
             │       ↓                                             │
             │ Mono.Cecil ──→ 读取 IL 字节码 + 类型元数据            │
             │       ↓         (Debug: 同时读取 PDB 源码行号映射)    │
             │ AssemblySet ──→ 加载用户 + 第三方 + BCL 程序集        │
             │       ↓         (deps.json 依赖发现 + 自动解析)       │
             │ ReachabilityAnalyzer ──→ 可达性分析（树摇）           │
             │       ↓                 (从入口点/公共类型出发)       │
             │ IRBuilder.Build() ──→ 中间表示（8 遍，见下文）        │
             │       ↓                                             │
             │ CppCodeGenerator ──→ C++ 头文件 + 源文件 + CMake     │
             │                      (试渲染 + 错误检测 + 自动 stub) │     .h / .cpp
             └─────────────────────────────────────────────────────┘ ──→ CMakeLists.txt
                                                                            ↓
                                                                         cmake + C++ 编译器
                                                                            ↓
    C++ 运行时 (cil2cpp::runtime) ──────────────────────── find_package ──→ 链接
    BoehmGC / 类型系统 / 异常 / 线程池                                       ↓
                                                                       原生可执行文件
```

### IR 构建：8 遍流水线

编译器的核心是 `IRBuilder.Build()`，将 Mono.Cecil 的 IL 数据转换为中间表示 (IR)，分 8 遍完成：

| 遍次 | 名称 | 做什么 | 为什么需要这个顺序 |
|------|------|--------|-------------------|
| 1 | 类型外壳 | 创建所有 `IRType`（名称、标志、命名空间） | 后续遍次需要通过名称查找类型 |
| 2 | 字段与基类 | 填充字段、基类引用、接口列表、静态构造函数 | VTable 构建需要知道继承链 |
| 3 | 方法壳 | 创建 `IRMethod`（签名、参数），不含方法体 | 方法体中的 call 指令需要能找到目标方法 |
| 4 | 泛型单态化 | 收集所有泛型实例化 → 生成具体类型/方法 | `List<int>` → `List_1_System_Int32` 独立类型 |
| 5 | VTable | 按继承链递归构建虚方法表 | 方法体中的 callvirt 需要 VTable 槽号 |
| 6 | 接口映射 | 构建 InterfaceVTable（接口→实现方法映射） | 接口分派需要知道实现方法地址 |
| 7 | 方法体 | IL 栈模拟 → 变量赋值，生成 `IRInstruction` | 依赖前几遍：call 解析、VTable 槽号、接口映射 |
| 8 | 方法合成 | record 的 ToString/Equals/GetHashCode/Clone | 替换编译器生成的引用 EqualityComparer 的方法体 |

### BCL 编译策略：Unity IL2CPP 模型

CIL2CPP 采用与 Unity IL2CPP 相同的策略：**所有有 IL 方法体的 BCL 方法直接从 IL 编译为 C++**，与用户代码走完全相同的编译路径。仅在最底层保留 `[InternalCall]` 方法的 C++ 手写实现（icall）。

```
  方法调用
    ↓
  ICallRegistry 查找 (~50 个映射)
    ├─ 命中 → [InternalCall] 方法，无 IL 方法体
    │         GC / Monitor / Interlocked / Buffer / Math 等
    │         → 调用 C++ 运行时实现
    │
    └─ 未命中 → 正常 IL 编译（BCL 方法与用户方法相同路径）
               → 生成 C++ 函数
```

这意味着 `Console.WriteLine("Hello")` 的完整调用链全部从 IL 编译：

```
Console.WriteLine → TextWriter.WriteLine → StreamWriter.Write → Encoding.GetBytes → P/Invoke → WriteFile
```

### C++ 代码生成策略

生成的 C++ 代码将每个 .NET 类型映射为 C++ struct，每个方法映射为独立的 C 函数：

- **引用类型** → `struct` + `__type_info` 指针 + `__sync_block` + 用户字段，通过 `gc::alloc()` 堆分配
- **值类型** → 普通 `struct`（无对象头），栈分配，按值传递
- **实例方法** → `RetType FuncName(ThisType* __this, ...)` — 显式 this 参数
- **静态字段** → `<Type>_statics` 全局结构体 + `_ensure_cctor()` 初始化守卫
- **虚方法调用** → `obj->__type_info->vtable->methods[slot]` 函数指针调用
- **接口分派** → `type_get_interface_vtable()` 查找接口实现表

### 多层安全网：自动 stub 机制

BCL 中部分方法的 IL 引用了 CLR 内部类型（RuntimeType、QCallTypeHandle 等），无法编译为 C++。编译器有 4 层安全网自动检测并 stub 化这些方法：

1. **HasClrInternalDependencies** — IR 级：方法引用 CLR 内部类型 → 替换为返回默认值的 stub
2. **HasKnownBrokenPatterns** — 预渲染：JIT intrinsics、自递归等已知问题模式 → 跳过
3. **RenderedBodyHasErrors** — 试渲染：将方法体渲染为 C++ 后检测 MSVC 编译错误模式 → stub 化
4. **GenerateMissingMethodStubImpls** — 兜底：所有声明但未定义的函数 → 生成默认 stub

每次 codegen 会生成 `stubbed_methods.txt` 报告，列出所有被 stub 化的方法及原因。

## 使用方法

完整流程分为 4 步。步骤 1 只需执行一次，步骤 2-4 每次生成时执行。

### 步骤 1：构建并安装运行时（一次性）

将 CIL2CPP 运行时编译为静态库并安装到指定路径，供后续生成的 C++ 项目通过 CMake `find_package` 引用。

```bash
# 1. 配置
cmake -B build -S runtime

# 2. 编译（建议同时编译 Release 和 Debug）
cmake --build build --config Release
cmake --build build --config Debug

# 3. 安装到指定路径（两个配置安装到同一目录，自动共存）
cmake --install build --config Release --prefix <安装路径>
cmake --install build --config Debug --prefix <安装路径>
```

<details>
<summary>安装后的目录结构</summary>

```
<安装路径>/
├── include/cil2cpp/            # 运行时头文件（27 个）
│   ├── cil2cpp.h               #   主入口（runtime_init / runtime_shutdown / runtime_set_args）
│   ├── types.h                 #   基本类型别名（Int32, Boolean, ...）
│   ├── object.h                #   Object 基类 + 分配/转型
│   ├── string.h                #   String 类型（UTF-16，不可变，驻留池）
│   ├── array.h                 #   Array 类型（类型化，越界检查）
│   ├── gc.h                    #   GC 接口（BoehmGC 封装：alloc / collect）
│   ├── exception.h             #   异常处理（setjmp/longjmp + 栈回溯）
│   ├── type_info.h             #   TypeInfo / VTable / MethodInfo / FieldInfo
│   ├── boxing.h                #   装箱/拆箱模板（box<T> / unbox<T>）
│   ├── checked.h               #   溢出检查算术（checked_add / checked_mul 等）
│   ├── reflection.h            #   System.Type 反射包装（typeof / GetType）
│   ├── memberinfo.h            #   MemberInfo / MethodInfo / FieldInfo 运行时
│   ├── threading.h             #   多线程原语（Monitor / Interlocked）
│   ├── task.h                  #   Task 运行时（状态机、continuation）
│   ├── threadpool.h            #   线程池（queue_work / init / shutdown）
│   ├── cancellation.h          #   CancellationToken / CancellationTokenSource
│   ├── async_enumerable.h      #   IAsyncEnumerable / ValueTask 运行时
│   ├── delegate.h              #   委托创建/调用/组合
│   ├── collections.h           #   List<T> / Dictionary<K,V> 运行时辅助
│   ├── mdarray.h               #   多维数组 T[,] 运行时
│   ├── stackalloc.h            #   stackalloc 平台抽象宏（alloca）
│   ├── typed_reference.h       #   TypedReference / ArgIterator（变长参数）
│   ├── icall.h                 #   [InternalCall] icall 声明
│   └── bcl/                    #   BCL 类型头文件
│       ├── System.Object.h
│       ├── System.String.h
│       ├── System.Console.h
│       └── System.IO.h
└── lib/
    ├── cil2cpp_runtime.lib     # Release 静态库（Windows .lib / Linux .a）
    ├── cil2cpp_runtimed.lib    # Debug 静态库（DEBUG_POSTFIX "d"）
    ├── gc.lib                  # BoehmGC Release 静态库（自动安装）
    ├── gcd.lib                 # BoehmGC Debug 静态库
    └── cmake/cil2cpp/          # CMake 包配置（自动选择 Release/Debug 库）
        ├── cil2cppConfig.cmake
        ├── cil2cppConfigVersion.cmake
        ├── cil2cppTargets.cmake
        ├── cil2cppTargets-release.cmake
        └── cil2cppTargets-debug.cmake
```

</details>

**依赖说明：**
- **BoehmGC (bdwgc v8.2.12)** — 保守式垃圾收集器，通过 FetchContent 自动下载（缓存在 `runtime/.deps/`）
- gc.lib / gcd.lib 随运行时一起安装，消费者通过 `find_package(cil2cpp)` 自动链接
- Windows Debug 自动链接 `dbghelp`（栈回溯），Linux/macOS 自动链接 pthreads
- 所有依赖通过 CMake target 传递，消费者无需手动配置

---

### 步骤 2：生成 C++ 代码

```bash
# Release（默认）
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output

# Debug — #line 指令 + IL 偏移注释 + 栈回溯支持
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output -c Debug
```

| 选项 | 说明 | 默认值 |
|------|------|--------|
| `-i, --input` | 输入 .csproj 文件（必填） | — |
| `-o, --output` | 输出目录（必填） | — |
| `-c, --configuration` | 构建配置 | `Release` |

生成的文件：

| 文件 | 内容 | 条件 |
|------|------|------|
| `<Name>.h` | 结构体声明、方法签名、TypeInfo、静态字段 | 始终 |
| `<Name>.cpp` | 方法实现、TypeInfo 定义、字符串字面量 | 始终 |
| `main.cpp` | 运行时初始化 → 入口方法 → 运行时关闭 | 仅可执行程序 |
| `CMakeLists.txt` | CMake 配置（`find_package(cil2cpp)` + 编译选项） | 始终 |
| `stubbed_methods.txt` | Stub 诊断报告（被 stub 化的方法及原因） | 有 stub 时 |

---

### 步骤 3：编译为原生可执行文件

```bash
# 配置（find_package 在此步解析运行时位置）
cmake -B build_output -S output -DCMAKE_PREFIX_PATH=<安装路径>

# 编译 + 链接
cmake --build build_output --config Release
```

生成的 CMakeLists.txt 内部使用：
```cmake
find_package(cil2cpp REQUIRED)
target_link_libraries(HelloWorld PRIVATE cil2cpp::runtime)
```

---

### 步骤 4：运行

```bash
# Windows
build_output\Release\HelloWorld.exe

# Linux / macOS
./build_output/HelloWorld
```

HelloWorld 示例输出：

```
Hello, CIL2CPP!
30
42
```

---

## 可执行程序与类库

CIL2CPP 根据程序集是否包含入口点自动选择输出类型：

| C# 项目 | 检测条件 | 生成结果 |
|---------|---------|---------|
| 有 `static void Main()` | 可执行程序 | `main.cpp` + `add_executable` |
| 无入口点（类库） | 静态库 | 无 `main.cpp`，`add_library(STATIC)` |

类库输出可通过 CMake 的 `add_subdirectory()` 或 `target_link_libraries()` 被其他 C++ 项目引用。

## Debug 与 Release 配置

通过 `-c` 参数选择配置，影响生成的 C++ 代码和运行时行为：

| 特性 | Release | Debug |
|------|---------|-------|
| `#line` 指令（映射回 C# 源码） | — | Yes |
| `/* IL_XXXX */` 偏移注释 | — | Yes |
| PDB 符号读取 | — | Yes |
| 运行时栈回溯 | 禁用 | 平台原生（Windows: DbgHelp, POSIX: backtrace） |
| `CIL2CPP_DEBUG` 编译定义 | — | Yes |
| C++ 编译器优化 | MSVC: `/O2`, GCC/Clang: `-O2` | MSVC: `/Zi /Od /RTC1`, GCC/Clang: `-g -O0` |

Debug 模式下用 Visual Studio 调试生成的 C++ 程序时，`#line` 指令会让断点和单步执行定位到原始 C# 源文件。

## 代码转换示例

**输入 (C#)**:

```csharp
public class Calculator
{
    private int _result;
    public int Add(int a, int b) => a + b;
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello, CIL2CPP!");
        var calc = new Calculator();
        Console.WriteLine(calc.Add(10, 20));
    }
}
```

**输出 (C++)**:

```cpp
struct Calculator {
    cil2cpp::TypeInfo* __type_info;
    cil2cpp::UInt32 __sync_block;
    int32_t f_result;
};

int32_t Calculator_Add(Calculator* __this, int32_t a, int32_t b) {
    return a + b;
}

void Program_Main() {
    cil2cpp::System::Console_WriteLine(__str_0);
    auto __t0 = (Calculator*)cil2cpp::gc::alloc(sizeof(Calculator), &Calculator_TypeInfo);
    Calculator__ctor(__t0);
    cil2cpp::System::Console_WriteLine(Calculator_Add(__t0, 10, 20));
}
```

---

## 编译能力概览

CIL2CPP 是一个 **CIL (Common Intermediate Language) → C++ 翻译器**，不是 C# → C++ 源码转换器。C# 编译器 (Roslyn) 将所有 C# 语法编译为标准 IL 字节码，CIL2CPP 读取这些字节码并逐条翻译为等价的 C++ 代码。

> 因此，CIL2CPP 不需要逐个"支持"C# 特性——只要 IL 指令全覆盖，所有编译为这些指令的 C# 代码自动可用。
> 项目能力由三层决定：**CIL 指令翻译层**、**BCL 方法编译层**、**运行时 icall 层**。

### 架构分层

```
┌─────────────────────────────────────────────────────────┐
│  Layer 1: CIL 指令翻译                                   │
│  ConvertInstruction() switch — ~220 opcodes             │
│  覆盖率: 100% (全部 ~230 种操作码已实现)                  │
│  这一层决定: 能否将 IL 方法体翻译为 C++                   │
├─────────────────────────────────────────────────────────┤
│  Layer 2: BCL 方法编译                                   │
│  所有有 IL 方法体的 BCL 方法从 IL 编译为 C++              │
│  限制: CLR 内部类型依赖 → 自动 stub 化                    │
│  这一层决定: 哪些标准库方法可用                           │
├─────────────────────────────────────────────────────────┤
│  Layer 3: 运行时 icall                                  │
│  ~50 个 [InternalCall] 方法 → C++ 运行时实现             │
│  限制: 未实现的 icall → 功能不可用                        │
│  这一层决定: GC、线程、字符串布局等底层能力                │
└─────────────────────────────────────────────────────────┘
```

### CIL 指令覆盖率 (Layer 1)

ECMA-335 标准定义了约 230 种 IL 操作码变体。CIL2CPP 的 `ConvertInstruction()` switch 已实现全部操作码，覆盖率 100%。覆盖率通过 `ILOpcodeCoverageTests` 自动验证。

#### 已处理指令分类

| 类别 | 指令数 | 代表性指令 |
|------|--------|-----------|
| 常量加载 | 17 | ldc.i4.0 ~ ldc.r8, ldstr, ldnull, ldtoken |
| 参数操作 | 10 | ldarg.0~3, ldarg.s, starg.s, ldarga.s |
| 局部变量 | 14 | ldloc.0~3, ldloc.s, stloc.s, ldloca.s |
| 算术运算 | 15 | add/sub/mul/div/rem + checked 变体, neg |
| 位运算 | 7 | and, or, xor, not, shl, shr, shr.un |
| 比较 | 5 | ceq, cgt, cgt.un, clt, clt.un |
| 类型转换 | 33 | conv.i1~r8 (13) + conv.ovf.\* (20) |
| 分支 | 35 | br/brtrue/brfalse + beq/bne/bge/bgt/ble/blt + .un + .s 变体, switch |
| 字段访问 | 6 | ldfld, stfld, ldsfld, stsfld, ldflda, ldsflda |
| 间接访问 | 19 | ldind.\*, stind.\*, ldobj, stobj |
| 方法调用 | 4 | call, callvirt, newobj, ret |
| 数组 | 21 | newarr, ldlen, ldelem.\*, stelem.\*, ldelema |
| 对象模型 | 12 | castclass, isinst, box, unbox, initobj, throw, rethrow, leave, ... |
| 函数指针 | 3 | ldftn, ldvirtftn, calli |
| 栈操作 | 2 | dup, pop |
| 前缀 | 7 | nop, tail, readonly, constrained, volatile, unaligned, no. |
| 内存操作 | 5 | sizeof, localloc, cpblk, initblk, cpobj |
| TypedReference | 3 | mkrefany, refanyval, refanytype |
| 变长参数 | 2 | arglist, jmp |
| 其他 | 2 | ckfinite, break |
| **合计** | **~230** | |

> 全部 ECMA-335 IL 操作码均已实现，`KnownUnimplementedOpcodes` 为空集。

### C# 功能参考表

> 以下表格从 C# 用户视角列出功能支持状态，方便查阅。
> 所有 C# 语法经 Roslyn 编译为标准 IL 后，CIL 指令翻译层 (Layer 1) 均已覆盖。
> 状态标记 ⚠️/❌ 反映的是 **BCL 依赖链或运行时 icall 层面的限制**，而非 IL 指令翻译问题。
>
> ✅ 已支持 ⚠️ 部分支持（BCL/运行时限制） ❌ 未支持（缺失 icall 或 AOT 限制）

### 基本类型

| 功能 | 状态 | 备注 |
|------|------|------|
| int, long, float, double | ✅ | 映射到 C++ int32_t, int64_t, float, double |
| bool, byte, sbyte, short, ushort | ✅ | 完整的基本类型映射 |
| uint, ulong | ✅ | |
| char | ✅ | UTF-16 (char16_t) |
| string | ✅ | 不可变，UTF-16 编码，字面量驻留池 |
| IntPtr, UIntPtr | ✅ | 映射到 intptr_t, uintptr_t |
| 类型转换 (全部) | ✅ | Conv_I1/I2/I4/I8/U1/U2/U4/U8/I/U/R4/R8/R_Un（共 13 种） |
| struct (值类型) | ✅ | 结构体定义 + initobj/ldobj/stobj + 装箱/拆箱 + 拷贝语义 + ldind/stind |
| enum | ✅ | typedef 到底层整数类型 + constexpr 命名常量 + TypeInfo (Enum\|ValueType 标志) |
| 装箱 / 拆箱 | ✅ | box / unbox / unbox.any，值类型→`box<T>()`/`unbox<T>()`，引用类型 unbox.any→castclass，Nullable\<T\> box 拆包 |
| Nullable\<T\> | ✅ | BCL IL 编译（get_HasValue/get_Value/GetValueOrDefault/.ctor），box 拆包（HasValue→box\<T\>/null），泛型单态化 |
| Tuple (ValueTuple) | ✅ | BCL IL 编译（.ctor/Equals/GetHashCode/ToString/字段访问），支持任意元素数（>7 通过嵌套 TRest），解构赋值 |
| record / record struct | ✅ | 编译器生成方法合成（ToString/Equals/GetHashCode/Clone），`with` 表达式，`==`/`!=`，值类型 record struct |

### 面向对象

| 功能 | 状态 | 备注 |
|------|------|------|
| object (System.Object) | ✅ | 所有引用类型基类，运行时提供 ToString/GetHashCode/Equals/GetType |
| 类定义 | ✅ | 实例字段 + 静态字段 + 方法 |
| 构造函数 | ✅ | 默认构造和参数化构造（newobj IL 指令） |
| 静态构造函数 (.cctor) | ✅ | 自动检测 + `_ensure_cctor()` once-guard，访问静态字段/创建实例前自动调用 |
| 实例方法 | ✅ | 编译为 C 函数，`this` 作为第一个参数 |
| 静态方法 | ✅ | |
| 实例字段 | ✅ | ldfld / stfld |
| 静态字段 | ✅ | 存储在 `<Type>_statics` 全局结构体中 |
| 继承（单继承） | ✅ | 基类字段拷贝到派生结构体，base 类型追踪，VTable 继承 |
| 虚方法 / 多态 | ✅ | 完整 VTable 分派：`obj->__type_info->vtable->methods[slot]` 函数指针调用 |
| 属性 | ✅ | C# 编译器生成的 get_/set_ 方法调用可工作（auto-property + 手动 property） |
| 类型转换 (is / as) | ✅ | isinst → object_as()，castclass → object_cast() |
| 抽象类/方法 | ✅ | 识别 IsAbstract，抽象方法跳过代码生成，VTable 正确分配槽位由子类覆盖 |
| 接口 | ✅ | InterfaceVTable 分派：编译器生成接口方法表，运行时 `type_get_interface_vtable()` 查找 |
| 泛型类 | ✅ | 单态化（monomorphization）：`Wrapper<int>` → `Wrapper_1_System_Int32` 独立 C++ 类型 |
| 泛型方法 | ✅ | 单态化：`Identity<int>()` → `GenericUtils_Identity_System_Int32()` 独立函数 |
| 运算符重载 | ✅ | C# 编译为 `op_Addition` 等静态方法调用，编译器自动识别并标记 |
| 索引器 | ✅ | C# 编译为 `get_Item`/`set_Item` 普通方法调用，无需特殊处理 |
| 终结器 / 析构函数 | ✅ | 编译器检测 `Finalize()` 方法，生成 finalizer wrapper → TypeInfo.finalizer，BoehmGC 自动注册 |
| 显式接口实现 | ✅ | Cecil `.override` 指令解析，`void IFoo.Method()` 映射到正确的接口 VTable 槽位 |
| 方法隐藏 (`new`) | ✅ | `newslot` 标志检测，`new virtual` 创建新 VTable 槽位而非覆盖父类 |
| 默认接口方法 (DIM) | ✅ | C# 8+ 接口默认实现，未覆盖时使用接口方法体作为 VTable 回退 |
| 泛型协变/逆变 (`out T`/`in T`) | ✅ | ECMA-335 II.9.11 variance-aware 可赋值检查：`IEnumerable<Dog>` → `IEnumerable<Animal>` |

### 控制流

| 功能 | 状态 | 备注 |
|------|------|------|
| if / else | ✅ | 全部条件分支指令：beq, bne, bge, bgt, ble, blt + 无符号变体 bge.un, bgt.un, ble.un, blt.un + 全部短形式 |
| while / for / do-while | ✅ | C# 编译器编译为条件分支，CIL2CPP 正常处理（含嵌套循环 + break/continue） |
| goto (无条件分支) | ✅ | br / br.s（前向 + 后向跳转） |
| 比较运算 (==, !=, <, >, <=, >=) | ✅ | ceq, cgt, cgt.un, clt, clt.un + 有符号/无符号条件分支 |
| switch (IL switch 表) | ✅ | 编译为 C++ switch/goto 跳转表 |
| 模式匹配 (switch 表达式) | ✅ | Roslyn 将所有模式编译为标准 IL（isinst/ceq/switch/分支链），CIL2CPP 全部支持；字符串模式需 `String.op_Equality` BCL 映射 |
| Range / Index (..) | ✅ | `Index`（构造/GetOffset/Value/IsFromEnd）、`Range`（GetOffsetAndLength）、`arr[^1]`、`arr[1..3]`、`string[1..4]` |

### 算术与位运算

| 功能 | 状态 | 备注 |
|------|------|------|
| +, -, *, /, % | ✅ | add, sub, mul, div, rem |
| &, \|, ^, <<, >> | ✅ | and, or, xor, shl, shr, shr.un |
| 一元 - (取负) | ✅ | neg |
| 一元 ~ (按位取反) | ✅ | not |
| 溢出检查 (checked) | ✅ | 算术运算（add/sub/mul）+ 类型转换（全 20 种 `Conv_Ovf_*`）均有溢出检查，抛 OverflowException |

### 数组

| 功能 | 状态 | 备注 |
|------|------|------|
| 创建 (`new T[n]`) | ✅ | newarr → `array_create()`，正确设置 `__type_info` + `element_type`；基本类型自动生成 TypeInfo |
| Length 属性 | ✅ | ldlen → `array_length()` |
| 元素读写 (`arr[i]`) | ✅ | ldelem/stelem 全类型：I1/I2/I4/I8/U1/U2/U4/R4/R8/Ref/I/Any → `array_get<T>()` / `array_set<T>()` |
| 元素地址 (`ref arr[i]`) | ✅ | ldelema → `array_get_element_ptr()` + 类型转换（带越界检查） |
| 数组初始化器 (`new int[] {1,2,3}`) | ✅ | ldtoken + `RuntimeHelpers.InitializeArray` → 静态字节数组 + `memcpy`；`<PrivateImplementationDetails>` 类型自动过滤 |
| 越界检查 | ✅ | `array_bounds_check()` → 抛出 IndexOutOfRangeException |
| 多维数组 (`T[,]`) | ✅ | MdArray 运行时：`mdarray_create` / `Get` / `Set` / `Address` / `GetLength(dim)`，bounds check，行主序连续存储 |
| Span\<T\> / ReadOnlySpan\<T\> | ✅ | BCL IL 编译（.ctor/get_Item/get_Length/Slice/ToArray/GetPinnableReference），ref struct 检测（`IsByRefLikeAttribute`），stackalloc 集成 |

### 异常处理

| 功能 | 状态 | 备注 |
|------|------|------|
| 异常类型 | ✅ | 24 种：完整 .NET 异常层次（Exception, ArithmeticException, OverflowException, DivideByZeroException, Argument*, InvalidOperation*, NotSupported*, NotImplemented*, Format*, KeyNotFound*, AggregateException, OperationCanceled* 等） |
| throw | ✅ | throw → `cil2cpp::throw_exception()`；运行时 `throw_null_reference()` 等便捷函数 |
| try / catch / finally | ✅ | 编译器读取 IL ExceptionHandler 元数据 → 生成 `CIL2CPP_TRY` / `CIL2CPP_CATCH` / `CIL2CPP_FINALLY` 宏调用 |
| rethrow | ✅ | `CIL2CPP_RETHROW` |
| 异常过滤器 (`catch when`) | ✅ | ECMA-335 Filter handler，catch-all + 条件判断 + 条件 rethrow，`CIL2CPP_FILTER` / `CIL2CPP_ENDFILTER` 宏 |
| 自动 null 检查 | ✅ | `null_check()` 内联函数 |
| 栈回溯 | ✅ | `capture_stack_trace()` — Windows: DbgHelp, POSIX: backtrace；运行时 throw 和用户 throw 均捕获；仅 Debug |
| using 语句 | ✅ | try/finally + BCL 接口代理（IDisposable）→ 接口分派 Dispose()，单程序集/多程序集均可工作 |
| 嵌套 try/catch/finally | ✅ | 宏基于 setjmp/longjmp，完整支持多层嵌套（try-catch 嵌套 try-finally、三层嵌套、catch 重抛、finally 替换异常等） |

### 标准库 (BCL)

> **Unity IL2CPP 架构**：BCL 方法体从 IL 编译为 C++，与用户代码走相同的编译路径。仅在最底层保留 `[InternalCall]` icall（GC、线程原语、OS API）。不再使用手动映射——所有有 IL 方法体的 BCL 方法都编译为 C++。

| 功能 | 状态 | 备注 |
|------|------|------|
| System.Object (ToString, GetHashCode, Equals, GetType) | ✅ | 运行时提供默认实现（vtable 基类）；`GetType()` 返回缓存的 `Type` 对象 |
| System.String | ✅ | `FastAllocateString` / `get_Length` / `get_Chars` 为 icall（运行时布局），其余方法（Concat/Format/Join/Split 等）从 BCL IL 编译 |
| Console.WriteLine / Write / ReadLine | ✅ | BCL IL 全链路编译：Console → TextWriter → StreamWriter → Encoding → P/Invoke（Windows: kernel32!WriteFile, Linux: libSystem.Native!write） |
| System.Math / MathF | ✅ | Math.Sqrt/Sin/Cos/Pow 等 ~40 个 `[InternalCall]` 方法已映射到 `<cmath>`（double + float 版本） |
| 多程序集 + 树摇 | ✅ | 默认启用：加载用户 + 第三方 + BCL 程序集，可达性分析树摇，BCL IL 全面编译 |
| List\<T\> / Dictionary\<K,V\> | ✅ | 从 BCL IL 编译（不再使用 C++ 手动实现） |
| LINQ | ✅ | Where/Select/OrderBy/Count/Any/All/First/Last 等，从 BCL IL 编译 |
| yield return / IEnumerable | ✅ | C# 编译器生成迭代器状态机类，BCL 接口代理启用接口分派 |
| IAsyncEnumerable\<T\> | ✅ | `await foreach` 支持，ValueTask/AsyncIteratorMethodBuilder 从 BCL IL 编译 |
| System.IO (File, Directory, Path) | ⚠️ | BCL IL 编译，但深层依赖链可能被 stub 化 |
| System.Net | ❌ | BCL IL 存在但依赖未实现的底层 icall |

### 委托与事件

| 功能 | 状态 | 备注 |
|------|------|------|
| 委托 (Delegate) | ✅ | ldftn/ldvirtftn → 函数指针，newobj → `delegate_create()`，Invoke → `IRDelegateInvoke` |
| 事件 (event) | ✅ | C# 生成 add_/remove_ 方法 + 委托字段，Subscribe/Unsubscribe 通过 `Delegate.Combine/Remove` |
| 多播委托 | ✅ | `Delegate.Combine` / `Delegate.Remove` 映射到运行时 `delegate_combine` / `delegate_remove` |
| Lambda / 匿名方法 | ✅ | C# 编译器生成 `<>c` 静态类（无捕获）/ `<>c__DisplayClass`（闭包），编译器自动处理 |

### 高级功能

| 功能 | 状态 | 备注 |
|------|------|------|
| async / await | ✅ | 真正并发：线程池 + continuation + Task.Delay/WhenAll/WhenAny/Run；Task/TaskAwaiter/AsyncTaskMethodBuilder 从 BCL IL 编译 |
| await foreach (IAsyncEnumerable) | ✅ | 异步迭代器状态机，ValueTask\<T\>/AsyncIteratorMethodBuilder/ManualResetValueTaskSourceCore 从 BCL IL 编译 |
| CancellationToken | ✅ | `CancellationTokenSource`/`CancellationToken` 从 BCL IL 编译 |
| 多线程 | ✅ | `Thread`（创建/Start/Join）、`Monitor`（Enter/Exit/Wait/Pulse）、`lock` 语句、`Interlocked`（Increment/Decrement/Exchange/CompareExchange）、`Thread.Sleep`、`volatile` 字段 |
| 反射 (typeof / GetType / GetMethods / GetFields) | ✅ | `typeof(T)` / `obj.GetType()` → 缓存 `Type` 对象；13 项属性；GetMethods/GetFields/GetMethod/GetField → ManagedMethodInfo/ManagedFieldInfo；MethodInfo.Invoke/GetParameters；FieldInfo.GetValue/SetValue；MemberInfo 通用分派 |
| 特性 (Attribute) | ✅ | 元数据存储 + 运行时查询（`type_has_attribute` / `type_get_attribute`）；支持基本类型 + 字符串 + Type + 枚举 + 数组构造参数 |
| unsafe 代码 (指针, fixed, stackalloc) | ✅ | `PointerType` 解析，`fixed`（pinned local → BoehmGC 保守扫描无需实际 pin），`stackalloc` → `localloc` → 平台 `alloca` 宏 |
| P/Invoke / DllImport | ✅ | extern "C" 声明 + 基本类型/String marshaling（Ansi/Unicode/Auto）+ blittable struct marshaling + callback delegate（函数指针） |
| 默认参数 / 命名参数 | ✅ | C# 编译器在调用点填充默认值，IL 中无可选参数语义 |
| ref struct | ✅ | `IsByRefLikeAttribute` 检测 → `IsRefStruct` 标志，Span\<T\> / ReadOnlySpan\<T\> 均为 ref struct |
| init-only setter | ✅ | 编译为普通 setter + `modreq(IsExternalInit)`，CIL2CPP 忽略 modreq |

### 运行时

| 功能 | 状态 | 备注 |
|------|------|------|
| BoehmGC | ✅ | 保守扫描 GC（bdwgc），自动管理栈根、全局变量、堆引用 |
| TypeInfo / VTable / InterfaceVTable | ✅ | 完整类型元数据 + VTable 多态分派 + 接口分派 + Finalizer |
| 对象模型 | ✅ | Object 基类 + __type_info + __sync_block |
| 字符串 (UTF-16) | ✅ | 不可变，驻留池，FNV-1a 哈希 |
| 数组（类型化 + 越界检查） | ✅ | `array_get<T>` / `array_set<T>` / `array_get_element_ptr` + 编译器完整 ldelem/stelem/ldelema + 数组初始化器 |
| 装箱/拆箱 | ✅ | `boxing.h` 模板：`box<T>()`, `unbox<T>()`, `unbox_ptr<T>()` |
| 异常处理 (setjmp/longjmp) | ✅ | CIL2CPP_TRY/CATCH/FINALLY 宏 + 编译器完整生成 |
| 增量 GC | ✅ | `GC_enable_incremental()` 已启用，`gc::collect_a_little()` 增量回收 API |

---

## 已知限制

> 以下限制按架构层分类。**IL 指令翻译层 (Layer 1)** 已 100% 覆盖（全部 ~230 种操作码已实现），
> 所有"不支持"的功能属于 **BCL 依赖链 (Layer 2)** 或 **运行时 icall (Layer 3)** 层面的问题。

### BCL 依赖链限制 (Layer 2)

| 限制 | 说明 |
|------|------|
| CLR 内部类型依赖 | BCL IL 引用 RuntimeType / QCallTypeHandle 等 CLR 内部类型 → 方法体自动 stub 化 |
| BCL 深层依赖链 | 中间层被 stub 化 → 上层方法不可用（如 System.IO 部分方法） |
| System.Net | 网络层底层 icall 未实现 → 整个命名空间不可用 |
| Regex 内部 | 依赖 CLR 内部 RegexCache 等 → 部分重载被 stub 化 |
| SIMD / `System.Numerics.Vector` | 需要平台特定 intrinsics (SSE/AVX/NEON)，BCL IL 引用 JIT intrinsics |
| Parallel LINQ (PLINQ) | 需要高级线程池调度，依赖链极深 |

### 运行时 icall 限制 (Layer 3)

| 限制 | 说明 |
|------|------|
| P/Invoke | 基本类型 + String + blittable struct + 回调委托（函数指针）均已支持 |
| Attribute 复杂参数 | 基本类型 + 字符串 + Type + 枚举 + 数组均已支持 |
| System.Math / MathF | ✅ ~40 个 icall 已注册（Sqrt/Sin/Cos/Pow/Log/Floor/Ceiling 等 double + float） |
| Array.Copy | ✅ 已注册 ICallRegistry（3 参数和 5 参数重载） |
| 异常类型 | 支持 24 种运行时异常 + 任意用户自定义异常类型（继承 Exception） |
| 泛型约束 | 编译期验证泛型约束（struct/class/new()/接口/基类），违反时发出警告 |

### AOT 架构根本限制

以下功能由于 AOT 编译模型的固有约束**无法支持**，与 Unity IL2CPP 和 .NET NativeAOT 的限制相同。

| 限制 | 原因 |
|------|------|
| `System.Reflection.Emit` | 运行时生成 IL 并执行——AOT 编译后无 IL 解释器/JIT |
| `DynamicMethod` | 运行时创建方法并执行——同上 |
| `Expression<T>.Compile()` | 运行时编译表达式树为可执行代码 |
| `Assembly.Load()` / `Assembly.LoadFrom()` | 运行时动态加载程序集——AOT 要求所有代码在编译期可知 |
| `Activator.CreateInstance(string typeName)` | 按名称字符串动态实例化——编译期无法确定目标类型 |
| `MethodInfo.Invoke()` (任意参数) | 完整反射调用需要运行时解释器；当前支持 0-2 参数的 Invoke |
| `Type.MakeGenericType()` | 运行时构造泛型类型——单态化必须在编译期完成 |
| `ExpandoObject` / `dynamic` | DLR (Dynamic Language Runtime) 完全依赖运行时绑定 |
| 运行时代码热更新 | 无 JIT 编译器，编译后的机器码不可替换 |

---

## BCL 策略

### Unity IL2CPP 架构

CIL2CPP 采用与 **Unity IL2CPP 相同的架构**：编译所有 BCL IL 方法体为 C++，仅在最底层使用 `[InternalCall]` icall。

**核心原则：** 如果方法有 IL 方法体，就从 IL 编译。如果是 `[InternalCall]`（无 IL 方法体），就用 icall 映射到 C++ 运行时实现。

### BCL 方法解析流水线

```
C# 用户代码 / BCL 代码中的方法调用
    ↓
ICallRegistry 查找（~50 个 icall 映射）
  ├─ [InternalCall] 方法: GC, Monitor, Interlocked, Buffer, Thread.Sleep, ...
  │  → 无 IL 方法体，必须由 C++ 运行时实现
  └─ 运行时布局依赖: String.get_Length, Array.get_Length, Delegate.Combine, ...
     → 访问运行时内部布局，需要 C++ 实现
    ↓ 未命中 icall 的方法
正常 IL 编译（与用户代码相同路径）
    ↓
生成 C++ 代码
```

### 运行时提供的类型（RuntimeProvidedTypes）

以下类型的**结构体定义**由 C++ 运行时提供（不从 BCL IL 编译结构体），但方法体从 IL 编译：
- **核心类型**: Object, String, Array, Exception, Delegate, MulticastDelegate, Type, Enum
- **异常类型（24 种）**: Exception, InvalidOperationException, ArgumentException 等 — 运行时定义结构体，`newobj` 拦截为运行时构造
- **异步类型**: Task, TaskAwaiter, AsyncTaskMethodBuilder（非泛型基类）
- **反射类型**: MemberInfo, MethodInfo, FieldInfo, ParameterInfo
- **线程类型**: Thread, CancellationToken, CancellationTokenSource

### 编译器内置 intrinsics

少数 JIT intrinsic 方法由编译器内联处理（不经过 ICallRegistry）：
- `Unsafe.SizeOf<T>` / `Unsafe.As<T>` / `Unsafe.Add<T>` → C++ sizeof / reinterpret_cast / 指针运算
- `INumber<T>.CreateTruncating` → C++ static_cast
- `Array.Empty<T>()` → 静态空数组实例
- `RuntimeHelpers.InitializeArray` → memcpy 静态数据
- `TryGetIntrinsicOperator` → 泛型数值接口运算符（`IBitwiseOperators` 等）→ C++ 运算符

### HasClrInternalDependencies 过滤

部分 BCL 方法的 IL 引用了 CLR 内部类型（`RuntimeType`、`QCallTypeHandle`、`MethodTable` 等），这些类型在 AOT 环境中不存在。编译器自动检测这种情况，将方法体替换为 TODO stub，避免编译错误。

### 为什么某些 BCL 方法仍不可用？

不再是"未映射"的问题，而是：
- 方法的 IL 引用了 CLR 内部类型 → 被自动 stub 化
- 方法依赖的底层 `[InternalCall]` 尚未实现 icall
- 方法依赖的 BCL 依赖链过深，中间某层被 stub 化

要支持新的 BCL 功能，通常需要：为底层 `[InternalCall]` 方法添加 C++ icall 实现。

---

## 垃圾收集器 (GC)

运行时使用 [BoehmGC (bdwgc)](https://github.com/ivmai/bdwgc) 作为垃圾收集器——与 Mono 相同的保守式 GC。

### 架构

```
C# 用户代码          编译器 codegen              运行时
───────────          ──────────────              ──────
new MyClass()   →    gc::alloc(sizeof, &TypeInfo)  →  GC_MALLOC() + 设置对象头
                     (IRNewObj)                        注册 finalizer（如有）

new int[10]     →    array_create(&TypeInfo, 10)   →  GC_MALLOC(header + data)
                     (IRRawCpp)                        设置 element_type + length

                     runtime_init()               →  GC_INIT()
                     runtime_shutdown()            →  GC_gcollect() + 退出
```

### 为什么选择 BoehmGC

BoehmGC 的**保守扫描**自动解决所有根追踪问题：

| 场景 | 自定义 GC（已废弃） | BoehmGC |
|------|---------------------|---------|
| 栈上局部变量 | 需要 shadow stack | 自动扫描栈 |
| 引用类型字段 | 需要引用位图 | 自动扫描堆 |
| 数组中的引用元素 | 需要手动标记 | 自动扫描 |
| 静态字段（全局变量） | 需要 add_root | 自动扫描全局区 |
| 值类型中的引用字段 | 需要精确布局 | 自动扫描 |

### 依赖管理

```
runtime/CMakeLists.txt
├── FetchContent: bdwgc v8.2.12        ← 自动下载
├── FetchContent: libatomic_ops v7.10.0 ← MSVC 需要
└── 缓存: runtime/.deps/               ← gitignored，删 build/ 不重新下载
```

- bdwgc 编译为静态库，PRIVATE 链接到 cil2cpp_runtime
- 安装时 gc.lib / gcd.lib 单独拷贝到 `lib/`
- 消费者通过 `find_package(cil2cpp)` 自动链接（cil2cppConfig.cmake 创建 `BDWgc::gc` imported target）

### 当前状态

| 功能 | 状态 | 说明 |
|------|------|------|
| 对象分配 + TypeInfo | ✅ | `gc::alloc()` → `GC_MALLOC()` + 设置 `__type_info` |
| 数组分配 | ✅ | `alloc_array()` → `GC_MALLOC()` + 正确设置 `__type_info` 和 `element_type` |
| 自动根扫描 | ✅ | BoehmGC 保守扫描，无需 shadow stack / add_root |
| Finalizer 注册 | ✅ | `GC_register_finalizer_no_order()`，运行时已就绪 |
| Finalizer 检测 | ✅ | 编译器检测 `Finalize()` 方法，生成 wrapper → TypeInfo.finalizer，`GC_register_finalizer_no_order()` 自动注册 |
| GC 统计 | ✅ | `GC_get_heap_size()` / `GC_get_total_bytes()` / `GC_get_gc_no()` |
| Write barrier | ✅ | 空操作（BoehmGC 不需要） |
| 增量回收 | ✅ | `GC_enable_incremental()` 已启用，`gc::collect_a_little()` 增量回收 API |

---

## 测试

项目包含三层测试：编译器单元测试、运行时单元测试、端到端集成测试。

### 编译器单元测试 (C# / xUnit)

测试覆盖：类型映射 (CppNameMapper)、构建配置 (BuildConfiguration)、IR 模块/方法/指令、C++ 代码生成器、程序集解析 (AssemblyResolver/AssemblySet)、可达性分析 (ReachabilityAnalyzer)。

```bash
# 运行测试
dotnet test compiler/CIL2CPP.Tests

# 运行测试 + 覆盖率报告
dotnet test compiler/CIL2CPP.Tests --collect:"XPlat Code Coverage"
```

| 模块 | 测试数 |
|------|--------|
| IRBuilder | 340 |
| ILInstructionCategory | 173 |
| CppNameMapper | 104 |
| CppCodeGenerator | 70 |
| TypeDefinitionInfo | 65 |
| IR Instructions (全部) | 54 |
| IRModule | 44 |
| ICallRegistry | 42 |
| IRMethod | 30 |
| AssemblySet | 28 |
| RuntimeLocator | 27 + 5 (集成) |
| IRType | 23 |
| ReachabilityAnalyzer | 22 |
| DepsJsonParser | 18 |
| AssemblyResolver | 18 |
| BuildConfiguration | 15 |
| AssemblyReader | 12 |
| IRField / IRVTableEntry / IRInterfaceImpl | 7 |
| SequencePointInfo | 5 |
| BclProxy | 20 |
| ILOpcodeCoverage | 113 |
| **合计** | **1240+** |

### 运行时单元测试 (C++ / Google Test)

测试覆盖：GC（分配/回收/根/终结器/增量）、字符串（创建/连接/比较/哈希/驻留）、数组（创建/越界检查/多维）、类型系统（继承/接口/注册/泛型协变）、对象模型（分配/转型/相等性）、异常处理（抛出/捕获/过滤/栈回溯）、多线程（Thread/Monitor/Interlocked）、反射（Type 缓存/属性/方法）、集合（List/Dictionary）、异步（线程池/Task/continuation/combinator）。

```bash
# 配置 + 编译
cmake -B runtime/tests/build -S runtime/tests
cmake --build runtime/tests/build --config Debug

# 运行测试
ctest --test-dir runtime/tests/build -C Debug --output-on-failure
```

| 模块 | 测试数 |
|------|--------|
| String | 107 |
| Exception | 66 (1 disabled) |
| Reflection | 46 |
| Collections | 42 |
| Type System | 39 |
| Array | 34 |
| Object | 28 |
| Console | 27 |
| Boxing | 26 |
| GC | 23 |
| MemberInfo (Reflection) | 28 |
| Async (Task/ThreadPool) | 19 |
| Delegate | 18 |
| Threading | 17 |
| **合计** | **446+ (1 disabled)** |

### 端到端集成测试

测试完整编译流水线：C# `.csproj` → codegen → CMake configure → C++ build → run → 验证输出。

```bash
python tools/dev.py integration
```

| 阶段 | 测试内容 | 测试数 |
|------|---------|--------|
| 前置检查 | dotnet、CMake、runtime 安装 | 3 |
| HelloWorld | 可执行程序（codegen → build → run → 验证输出） | 5 |
| 类库项目 | 无入口点 → add_library → build | 4 |
| Debug 配置 | #line 指令、IL 注释、Debug build + run | 4 |
| 字符串字面量 | string_literal、__init_string_literals | 2 |
| 多程序集 | 跨程序集类型/方法、MathLib 引用、BCL IL 编译 | 5 |
| ArglistTest | 变长参数（codegen → build → run → 验证输出） | 5 |
| FeatureTest | 综合语言特性（codegen-only，100+ 特性验证） | 3 |
| **合计** | | **31** |

### 全部运行

```bash
# 推荐：使用 dev.py 一键运行全部测试
python tools/dev.py test --all

# 或手动执行：
dotnet test compiler/CIL2CPP.Tests
cmake --build runtime/tests/build --config Debug && ctest --test-dir runtime/tests/build -C Debug --output-on-failure
python tools/dev.py integration
```

---

## 开发者工具 (`tools/dev.py`)

Python3 交互式 CLI（仅标准库），统一所有构建/测试/覆盖率/代码生成操作，避免记忆多个命令和参数。

### 子命令模式

```bash
python tools/dev.py build                  # 编译 compiler + runtime
python tools/dev.py build --compiler       # 仅编译 compiler
python tools/dev.py build --runtime        # 仅编译 runtime
python tools/dev.py test --all             # 运行全部测试（编译器 + 运行时 + 集成）
python tools/dev.py test --compiler        # 仅编译器测试 (1236+ xUnit)
python tools/dev.py test --runtime         # 仅运行时测试 (461+ GTest)
python tools/dev.py test --coverage        # 测试 + 覆盖率 HTML 报告
python tools/dev.py test --compiler --filter ILOpcode  # 仅 IL opcode 覆盖测试
python tools/dev.py install                # 安装 runtime (Debug + Release)
python tools/dev.py codegen HelloWorld     # 快速代码生成测试
python tools/dev.py integration            # 集成测试（完整编译流水线）
python tools/dev.py setup                  # 检查前置 + 安装可选依赖
```

### 交互式菜单

无参数运行时进入交互式菜单：

```bash
python tools/dev.py
```

### 覆盖率报告（C# + C++ 统一）

```bash
python tools/dev.py test --coverage
# 1. C# 覆盖率 (coverlet) — dotnet test --collect:"XPlat Code Coverage"
# 2. C++ 覆盖率 (OpenCppCoverage) — 收集 runtime 测试覆盖
# 3. 合并两份 Cobertura XML → ReportGenerator → 统一 HTML 报告
# → 自动打开浏览器查看报告（含图表）
# → 报告路径: CoverageResults/CoverageReport/index.html
```

---

## 待改进项

| 项目 | 影响 |
|------|------|
| Memory\<T\> / ReadOnlyMemory\<T\> 未拦截 | 与 Span\<T\> 类似但无拦截，影响 System.IO.Pipelines 等现代 API |
| ICU 集成缺失 | Unicode 字符分类（IsLetter/IsUpper 等）依赖 ICU，目前使用简化实现 |
| System.Net | 网络层底层 icall 未实现 → 整个命名空间不可用 |
| FeatureTest C++ 编译 | codegen 成功但异步相关代码存在类型不匹配，仅作 codegen-only 测试 |

---

## 开发路线图

| 阶段 | 内容 | 状态 |
|------|------|------|
| **Phase A** | 关键正确性修复：unsigned 算术、surrogate pair、volatile fence | ✅ 已完成 |
| **Phase B** | Math icall + KnownStubs 数字格式化 + FeatureTest 集成测试 | ✅ 已完成 |
| **Phase C** | 死代码清理 + Enum CorElementType 修复 | ✅ 已完成 |
| **Phase D** | Stub 诊断报告 + FilteredGenericNamespaces + BCL 接口代理 | ✅ 已完成 |
| **Phase E** | argc/argv + Environment icalls (Exit/GetEnv/GetCommandLineArgs) | ✅ 已完成 |
| **Phase F** | ICU 集成（FetchContent，Unicode 字符分类） | 待实施 |
| **Phase G** | Memory\<T\> 拦截 + System.IO 改进 + System.Net 评估 | 待实施 |

## 参考

- [Unity IL2CPP](https://docs.unity3d.com/Manual/IL2CPP.html)
- [Mono.Cecil](https://github.com/jbevain/cecil)
- [BoehmGC (bdwgc)](https://github.com/ivmai/bdwgc) — 保守式垃圾收集器
- [NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [ECMA-335 CLI Specification](https://www.ecma-international.org/publications-and-standards/standards/ecma-335/)