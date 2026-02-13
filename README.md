# CIL2CPP

将 .NET/C# 程序编译为原生 C++ 代码的 AOT 编译工具，类似于 Unity IL2CPP。

## 工作原理

```
.csproj → dotnet build → .NET DLL (IL) → CIL2CPP → C++ 代码 + CMakeLists.txt → C++ 编译器 → 原生可执行文件
```

1. **IL 解析** — Mono.Cecil 读取 .NET 程序集中的 IL 字节码
2. **IR 构建** — 将 IL 指令转换为中间表示（4 遍：类型外壳 → 字段/基类 → 方法体 → VTable）
3. **C++ 生成** — 将 IR 翻译为 C++ 头文件、源文件、入口点和 CMakeLists.txt
4. **原生编译** — 使用 C++ 编译器编译生成的代码，通过 `find_package` 链接 CIL2CPP 运行时

## 项目结构

```
cil2cpp/
├── compiler/                   # C# 编译器 (.NET 项目)
│   ├── CIL2CPP.CLI/            #   命令行入口
│   ├── CIL2CPP.Core/           #   核心编译逻辑
│   │   ├── IL/                 #     IL 解析 (Mono.Cecil)
│   │   ├── IR/                 #     中间表示 + 类型映射
│   │   └── CodeGen/            #     C++ 代码生成
│   ├── CIL2CPP.Tests/          #   编译器单元测试 (xUnit)
│   └── samples/                #   示例 C# 程序
├── runtime/                    # C++ 运行时库 (CMake 项目)
│   ├── CMakeLists.txt
│   ├── cmake/                  #   CMake 包配置模板
│   ├── include/cil2cpp/        #   头文件
│   ├── src/                    #   GC、类型系统、异常、BCL
│   └── tests/                  #   运行时单元测试 (Google Test)
└── tests/
    └── integration/            # 端到端集成测试 (PowerShell)
```

## 前置要求

- **.NET 8 SDK** — 用于构建编译器和编译输入的 C# 项目，`dotnet` 需在 PATH 中
- **CMake 3.20+** — 用于构建运行时和生成的 C++ 代码
- **C++ 20 编译器**：
  - Windows: MSVC 2022 (Visual Studio 17.0+)
  - Linux: GCC 12+ 或 Clang 15+
  - macOS: Apple Clang 14+ (Xcode 14+)

---

## 工作流程

完整流程分为 4 步。步骤 1 只需执行一次，步骤 2-4 每次生成时执行。

### 步骤 1：构建并安装运行时（一次性）

将 CIL2CPP 运行时编译为静态库并安装到指定路径，供后续生成的 C++ 项目通过 CMake `find_package` 引用。

| 项目 | 说明 |
|------|------|
| **前提条件** | CMake 3.20+、C++ 20 编译器。此步骤不需要 .NET SDK |
| **输入** | `runtime/` 目录（C++ 源码） |
| **输出** | 安装目录，包含头文件、静态库和 CMake 包配置文件 |
| **用途** | 生成的 C++ 项目通过 `find_package(cil2cpp REQUIRED)` 自动找到并链接此运行时 |

**可选项：**

| 选项 | 说明 | 示例 |
|------|------|------|
| `--config` | 构建配置 | `Debug` 或 `Release` |
| `--prefix` | 安装路径（必填） | `C:/cil2cpp` 或 `/usr/local` |
| `-G` | CMake 生成器 | `"Visual Studio 17 2022"`、`"Ninja"` |

**命令：**

```bash
# 1. 配置（生成构建系统）
cmake -B build -S runtime

# 2. 编译静态库（建议同时编译 Release 和 Debug）
cmake --build build --config Release
cmake --build build --config Debug

# 3. 安装到指定路径（两个配置安装到同一目录，自动共存）
cmake --install build --config Release --prefix <安装路径>
cmake --install build --config Debug --prefix <安装路径>
```

**安装后的目录结构：**

```
<安装路径>/
├── include/cil2cpp/            # 运行时头文件
│   ├── cil2cpp.h               #   主入口（runtime_init / runtime_shutdown）
│   ├── types.h                 #   基本类型别名（Int32, Boolean, ...）
│   ├── object.h                #   Object 基类 + 分配/转型
│   ├── string.h                #   String 类型（UTF-16，不可变，驻留池）
│   ├── array.h                 #   Array 类型（类型化，越界检查）
│   ├── gc.h                    #   GC 接口（alloc / collect / roots）
│   ├── exception.h             #   异常处理（setjmp/longjmp）
│   ├── type_info.h             #   TypeInfo / VTable / MethodInfo / FieldInfo
│   └── bcl/                    #   BCL 实现头文件
│       ├── System.Object.h
│       ├── System.String.h
│       └── System.Console.h
└── lib/
    ├── cil2cpp_runtime.lib     # Release 静态库（Windows .lib / Linux .a）
    ├── cil2cpp_runtimed.lib    # Debug 静态库（DEBUG_POSTFIX "d"）
    └── cmake/cil2cpp/          # CMake 包配置（自动选择 Release/Debug 库）
        ├── cil2cppConfig.cmake
        ├── cil2cppConfigVersion.cmake
        ├── cil2cppTargets.cmake
        ├── cil2cppTargets-release.cmake
        └── cil2cppTargets-debug.cmake
```

**平台说明：**
- Windows Debug 模式自动链接 `dbghelp`（用于栈回溯符号解析）
- Linux/macOS 自动链接 pthreads
- 这些依赖通过 CMake target 传递，消费者无需手动添加

---

### 步骤 2：生成 C++ 代码

CIL2CPP 编译器读取 C# 项目，将 IL 字节码翻译为 C++ 源代码。

| 项目 | 说明 |
|------|------|
| **前提条件** | .NET 8 SDK（`dotnet` 在 PATH 中） |
| **输入** | `.csproj` 文件（C# 项目文件，不是 .dll） |
| **输出** | C++ 头文件、源文件、入口点（仅可执行程序）和 CMakeLists.txt |
| **用途** | 输出的文件在步骤 3 中被 C++ 编译器编译为原生二进制 |

**可选项：**

| 选项 | 说明 | 默认值 |
|------|------|--------|
| `-i, --input` | 输入 .csproj 文件（必填） | — |
| `-o, --output` | 输出目录（必填） | — |
| `-c, --configuration` | 构建配置 | `Release` |

**命令：**

```bash
# Release（默认）— 无调试信息，优化体积和性能
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i compiler/samples/HelloWorld/HelloWorld.csproj \
    -o output

# Debug — #line 指令 + IL 偏移注释 + 栈回溯支持
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i compiler/samples/HelloWorld/HelloWorld.csproj \
    -o output -c Debug
```

**内部执行步骤：**

```
                  CIL2CPP 编译器内部
┌─────────────────────────────────────────────────────────────┐
│ 1. dotnet build       自动编译 .csproj，定位输出 DLL        │
│ 2. Mono.Cecil 读取    解析 DLL 中的类型、方法、IL 指令      │
│    (Debug: 同时读取 PDB 符号文件获取源码行号映射)            │
│ 3. IR 构建            IL → 中间表示                         │
│    Pass 1: 创建类型外壳                                      │
│    Pass 2: 填充字段、基类、接口                              │
│    Pass 3: 转换方法体（栈模拟 → 变量赋值）                   │
│    Pass 4: 构建 VTable                                       │
│ 4. C++ 代码生成       IR → .h + .cpp + main.cpp + CMake     │
└─────────────────────────────────────────────────────────────┘
```

**生成的文件：**

| 文件 | 内容 | 条件 |
|------|------|------|
| `<Name>.h` | 结构体声明、方法签名、TypeInfo 外部声明、静态字段存储 | 始终生成 |
| `<Name>.cpp` | 方法实现、TypeInfo 定义、字符串字面量初始化、GC 根注册 | 始终生成 |
| `main.cpp` | `CIL2CPP_MAIN` 宏（运行时初始化 → 入口方法 → 运行时关闭） | 仅可执行程序 |
| `CMakeLists.txt` | CMake 构建配置（`find_package(cil2cpp)` + 编译选项） | 始终生成 |

**CLI 命令一览：**

| 命令 | 用途 | 说明 |
|------|------|------|
| `codegen` | 生成 C++ 代码（推荐） | 3 步：读取程序集 → 构建 IR → 生成 C++ |
| `compile` | 完整编译流程 | 4 步（第 4 步原生编译尚未集成） |
| `dump` | 调试用，输出 IL 信息 | 仅输出到控制台，不生成文件 |

---

### 步骤 3：编译为原生可执行文件

使用 CMake 和 C++ 编译器将步骤 2 生成的代码编译为原生二进制。

| 项目 | 说明 |
|------|------|
| **前提条件** | 步骤 1 已完成（运行时已安装）、步骤 2 已完成（C++ 已生成）、CMake + C++ 编译器 |
| **输入** | 步骤 2 的输出目录（包含 CMakeLists.txt 和 .h/.cpp 文件） |
| **输出** | 原生可执行文件（.exe / ELF）或静态库（.lib / .a） |
| **用途** | 直接运行；库项目可通过 CMake 被其他 C++ 项目引用 |

**可选项：**

| 选项 | 说明 | 备注 |
|------|------|------|
| `-DCMAKE_PREFIX_PATH` | 运行时安装路径（必填） | 即步骤 1 的 `--prefix` 值 |
| `--config` | 构建配置 | `Debug` 或 `Release` |
| `-G` | CMake 生成器 | 同步骤 1 |

**命令：**

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
所有运行时头文件和库路径由 `find_package` 自动解析，无需手动配置。

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
    cil2cpp::UInt32 __gc_mark;
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

## C# 功能支持状态

> ✅ 已支持 &nbsp;&nbsp; ⚠️ 部分支持 &nbsp;&nbsp; ❌ 未支持

### 基本类型

| 功能 | 状态 | 备注 |
|------|------|------|
| int, long, float, double | ✅ | 映射到 C++ int32_t, int64_t, float, double |
| bool, byte, sbyte, short, ushort | ✅ | 完整的基本类型映射 |
| uint, ulong | ✅ | |
| char | ✅ | UTF-16 (char16_t) |
| string | ✅ | 不可变，UTF-16 编码，字面量驻留池 |
| IntPtr, UIntPtr | ✅ | 映射到 intptr_t, uintptr_t |
| 类型转换 (int→long 等) | ✅ | Conv_I4, Conv_I8, Conv_R4, Conv_R8 |
| struct (值类型) | ⚠️ | 识别 IsValueType 标志，但当作引用类型处理（无栈分配、无拷贝语义） |
| enum | ⚠️ | 识别 IsEnum 标志，但无 ToString/Parse 等特殊处理 |
| Nullable\<T\> | ❌ | 需要泛型支持 |
| Tuple (ValueTuple) | ❌ | 需要泛型支持 |
| record | ❌ | |

### 面向对象

| 功能 | 状态 | 备注 |
|------|------|------|
| 类定义 | ✅ | 实例字段 + 静态字段 + 方法 |
| 构造函数 | ✅ | 默认构造和参数化构造（newobj IL 指令） |
| 实例方法 | ✅ | 编译为 C 函数，`this` 作为第一个参数 |
| 静态方法 | ✅ | |
| 实例字段 | ✅ | ldfld / stfld |
| 静态字段 | ✅ | 存储在 `<Type>_statics` 全局结构体中 |
| 继承（单继承） | ⚠️ | 基类字段拷贝到派生结构体，base 类型追踪 |
| 虚方法 | ⚠️ | VTable 基础设施已构建，callvirt 已处理 |
| 属性 | ⚠️ | C# 编译器生成的 get_/set_ 方法调用可工作，但无属性元数据 |
| 类型转换 (is / as) | ✅ | isinst → object_as()，castclass → object_cast() |
| 抽象类/方法 | ⚠️ | 识别 IsAbstract 标志，抽象方法跳过代码生成 |
| 接口 | ❌ | 识别并记录接口列表，但未生成接口分派代码 |
| 泛型 | ❌ | |
| 运算符重载 | ❌ | |
| 索引器 | ❌ | |
| 静态构造函数 (.cctor) | ❌ | |
| 终结器 / 析构函数 | ❌ | GC TypeInfo 中有 finalizer 字段，但未接入 |

### 控制流

| 功能 | 状态 | 备注 |
|------|------|------|
| if / else | ✅ | 全部条件分支指令：beq, bne, bge, bgt, ble, blt 及短形式 |
| while / for / do-while | ✅ | C# 编译器编译为条件分支，CIL2CPP 正常处理 |
| goto (无条件分支) | ✅ | br / br.s |
| 比较运算 (==, !=, <, >, <=, >=) | ✅ | ceq, cgt, clt + 条件分支 |
| switch (IL switch 表) | ❌ | |
| 模式匹配 (switch 表达式) | ❌ | |
| Range / Index (..) | ❌ | |

### 算术与位运算

| 功能 | 状态 | 备注 |
|------|------|------|
| +, -, *, /, % | ✅ | add, sub, mul, div, rem |
| &, \|, ^, <<, >> | ✅ | and, or, xor, shl, shr |
| 一元 - (取负) | ✅ | neg |
| 一元 ~ (按位取反) | ✅ | not |
| 溢出检查 (checked) | ❌ | Add_Ovf, Mul_Ovf 等未处理 |

### 数组

| 功能 | 状态 | 备注 |
|------|------|------|
| 创建 (`new T[n]`) | ✅ | newarr 指令 |
| Length 属性 | ✅ | ldlen 指令 |
| 元素读写 (`arr[i]`) | ❌ | ldelem / stelem 未处理；运行时的 `array_get<T>` / `array_set<T>` 已就绪 |
| 多维数组 | ❌ | |
| 数组初始化器 | ❌ | |
| Span\<T\> / Memory\<T\> | ❌ | |

### 异常处理

| 功能 | 状态 | 备注 |
|------|------|------|
| 异常类型 | ✅ | NullReferenceException, IndexOutOfRangeException, InvalidCastException, ArgumentException 等 |
| throw | ✅ | throw_exception(), throw_null_reference() 等 |
| 自动 null 检查 | ✅ | null_check() 内联函数 |
| 栈回溯 | ⚠️ | capture_stack_trace() — Windows: DbgHelp, POSIX: backtrace；仅 Debug |
| try / catch / finally | ❌ | 运行时有 CIL2CPP_TRY / CIL2CPP_CATCH / CIL2CPP_FINALLY 宏（setjmp/longjmp），但编译器未从 IL 异常处理器生成对应代码 |
| using 语句 | ❌ | 需要 try/finally + IDisposable |
| throw 表达式 | ❌ | |

### 标准库 (BCL)

> 当前：`MapBclMethod()` 硬编码映射（每个 BCL 方法手动添加）
> 目标：Unity IL2CPP 风格，BCL IL 自动翻译 + icall 层（详见路线图 Phase 4）

| 功能 | 状态 | 备注 |
|------|------|------|
| System.Object (ToString, GetHashCode, Equals, GetType) | ✅ | 手写映射 |
| System.String (Concat, IsNullOrEmpty, Length, Substring) | ✅ | 手写映射 |
| Console.WriteLine (全部重载) | ✅ | 手写映射，String, Int32, Int64, Single, Double, Boolean, Object |
| Console.Write (全部重载) | ✅ | 手写映射 |
| Console.ReadLine / Read | ✅ | 手写映射 |
| System.Math | ❌ | Phase 1: 手写映射 → Phase 4: BCL 自动翻译 + `<cmath>` icall |
| 集合类 (List, Dictionary, HashSet 等) | ❌ | Phase 4: BCL IL 自动翻译（需要 Phase 3 泛型） |
| System.IO (File, Stream) | ❌ | Phase 4: BCL IL 自动翻译 + 文件系统 icall |
| System.Net | ❌ | Phase 5 |

### 委托与事件

| 功能 | 状态 | 备注 |
|------|------|------|
| 委托 (Delegate) | ❌ | |
| 事件 (event) | ❌ | |
| 多播委托 | ❌ | |
| Lambda / 匿名方法 | ❌ | |
| LINQ | ❌ | 需要泛型 + 委托 + IEnumerable\<T\> |

### 高级功能

| 功能 | 状态 | 备注 |
|------|------|------|
| async / await | ❌ | 需要委托 + 泛型 + 异常处理 + 状态机 |
| 多线程 (Thread, Task, lock) | ❌ | |
| 反射 (Type.GetMethods 等) | ❌ | TypeInfo 有 MethodInfo/FieldInfo 数组但未填充 |
| 特性 (Attribute) | ❌ | |
| unsafe 代码 (指针, fixed, stackalloc) | ❌ | |
| P/Invoke / DllImport | ❌ | |
| 默认参数 / 命名参数 | ❌ | |
| ref struct / ref return | ❌ | |
| init-only setter | ❌ | |

### 运行时

| 功能 | 状态 | 备注 |
|------|------|------|
| Mark-sweep GC | ⚠️ | malloc/free，基本标记-清扫。已知问题：栈根未扫描、引用字段未追踪、阈值未执行（详见 `.claude/gc-refactoring-plan.md`） |
| TypeInfo / VTable | ✅ | 完整类型元数据 |
| 对象模型 | ✅ | Object 基类 + __type_info + __gc_mark + __sync_block |
| 字符串 (UTF-16) | ✅ | 不可变，驻留池，FNV-1a 哈希 |
| 数组（类型化 + 越界检查） | ⚠️ | 运行时 array_get/set 模板就绪，编译器未生成元素访问 |
| 异常处理 (setjmp/longjmp) | ⚠️ | CIL2CPP_TRY/CATCH/FINALLY 宏已定义，编译器未生成 |
| 分代 GC | ❌ | 写屏障 stub 存在（gc.h TODO） |
| 多线程 GC | ❌ | 当前单线程 stop-the-world |

---

## 开发路线图

基于功能依赖关系的分阶段实现计划。每个阶段产出可用的增量：

```
Phase 1 (基础)        Phase 2 (对象模型)       Phase 3 (泛型/委托)
  数组元素访问          接口分派                 泛型
  try/catch/finally     装箱/拆箱               委托 → 事件
  switch                枚举完整支持              Lambda/闭包
  值类型语义            运算符重载               集合类 (List<T>)
  静态构造函数          终结器                   IEnumerable<T> / foreach
  System.Math (icall)
       │                    │                        │
       └────────────────────┘────────────────────────┘
                                                     │
                  Phase 4 (BCL 自动翻译)         Phase 5 (高级运行时)
                    mscorlib IL → C++              async/await
                    System.dll IL → C++            多线程
                    icall 层                       反射 / 特性
                    淘汰 MapBclMethod()            分代 GC
                    语言特性 (LINQ, ...)           P/Invoke / unsafe
```

### BCL 策略：从手写映射到自动翻译

当前 BCL 支持通过 `MapBclMethod()` 硬编码映射（如 `Console.WriteLine` → `cil2cpp::System::Console_WriteLine`）。长期目标是采用 **Unity IL2CPP 风格**：将 BCL 程序集的 IL 和用户代码一起翻译为 C++，仅在最底层提供 icall（内部调用）实现。

```
当前 (Phase 1-2):                      目标 (Phase 4+):
  C# 用户代码                            C# 用户代码
      ↓ IL → C++                             ↓ IL → C++
  MapBclMethod() 硬编码映射               mscorlib.dll / System.dll
      ↓                                      ↓ IL → C++ (同一流水线)
  手写 C++ 运行时实现                     icall 层 (C++ 薄封装)
      ↓                                      ↓
  printf / <cmath> / ...                 printf / <cmath> / OS API / ...
```

**icall（内部调用）** 是 .NET 中标记为 `[MethodImpl(MethodImplOptions.InternalCall)]` 的方法——BCL 的 C# 代码调用到原生实现的边界。例如 `Math.Sin()` 的 C# 代码最终调用一个 icall，该 icall 的 C++ 实现调用 `<cmath>` 的 `sin()`。

**过渡路径：**

| 阶段 | BCL 方式 | 覆盖面 |
|------|---------|--------|
| Phase 1-2 | `MapBclMethod()` 手写映射 + 手写 C++ 实现 | Object, String, Console, Math |
| Phase 3 | 手写映射 + 开始识别 `[InternalCall]` 特性 | 同上 + 泛型集合类（手写） |
| Phase 4 | **BCL IL 自动翻译** + icall 层 | mscorlib.dll 中大部分类型 |
| Phase 5 | 完整 BCL 翻译 | System.dll, System.IO, System.Net 等 |

### Phase 1：基础完善

完成核心缺失功能，解锁后续所有阶段的前置条件。

| 功能 | 说明 |
|------|------|
| **数组元素读写** | 运行时 `array_get<T>` / `array_set<T>` 已就绪，需在 IRBuilder 中处理 ldelem / stelem 系列指令 |
| **try / catch / finally** | **最关键**。运行时已有 `CIL2CPP_TRY` / `CIL2CPP_CATCH` / `CIL2CPP_FINALLY` 宏（基于 setjmp/longjmp），需从 Mono.Cecil 的 ExceptionHandler 生成对应代码。using、foreach、LINQ、async 全部依赖此功能 |
| **switch 语句** | 处理 IL switch 指令，生成 C++ switch/case |
| **值类型 (struct) 语义** | 栈分配、拷贝语义、无 GC 头部 |
| **静态构造函数 (.cctor)** | 在类型首次使用前自动调用 |
| **System.Math** | 临时方案：`MapBclMethod()` 映射 + icall 风格的 C++ 封装（`Math.Abs` → `std::abs` 等）。Phase 4 自动翻译后此映射将被淘汰 |

### Phase 2：对象模型完善

完成面向对象体系，支持多态和接口。

| 功能 | 说明 |
|------|------|
| **接口分派** | 生成接口方法表，实现接口方法调用。IDisposable、IEnumerable、IComparable 等关键接口需要此功能 |
| **装箱 / 拆箱** | 值类型作为 object 引用使用时的包装/解包。许多 BCL 模式和隐式转换依赖此功能 |
| **枚举完整支持** | 底层整数类型、ToString、Parse、Flags 特性 |
| **运算符重载** | 映射到 op_Addition / op_Equality 等方法 |
| **终结器** | 将 TypeInfo 中的 finalizer 函数指针接入 GC sweep 阶段 |

### Phase 3：泛型与委托

解锁集合类库和函数式编程范式。这是支持现代 C# 代码的关键阶段。

| 功能 | 说明 |
|------|------|
| **泛型（类、方法、约束）** | C++ 模板或单态化实现。解锁 List\<T\>、Dictionary\<K,V\> 等集合类 |
| **委托** | 函数指针包装。解锁事件、回调、LINQ |
| **事件** | 基于委托的 add/remove 方法 |
| **Lambda / 闭包** | 编译器生成的显示类 + 委托实例化 |
| **`[InternalCall]` 识别** | 编译器识别 `MethodImplOptions.InternalCall` 特性，生成 icall 桩函数而非翻译方法体。为 Phase 4 做准备 |

### Phase 4：BCL 自动翻译 + 语言特性

**架构升级**：将 BCL 程序集（mscorlib.dll、System.dll）作为输入，和用户代码一起通过 IL → C++ 流水线翻译。淘汰 `MapBclMethod()` 硬编码映射。

| 功能 | 说明 |
|------|------|
| **多程序集输入** | CLI 接受多个 .dll 输入（或自动解析引用的 BCL 程序集），全部喂入 IRBuilder |
| **icall 层** | 为 `[InternalCall]` 方法提供 C++ 原生实现。初期覆盖：数学函数（`<cmath>`）、字符串操作（UTF-16）、Console I/O（`<cstdio>`）、文件操作（`<filesystem>`） |
| **BCL 类型裁剪** | 只翻译用户代码实际引用到的 BCL 类型和方法（tree shaking），避免翻译整个 mscorlib |
| **LINQ** | BCL 自动翻译后自然可用（需要泛型 + 委托 + IEnumerable\<T\>） |
| **Nullable\<T\>** | BCL 自动翻译后自然可用 |
| **using 语句** | try/finally（Phase 1）+ IDisposable（Phase 2）→ 自动生效 |
| **System.IO / System.Math / System.Net** | BCL IL 自动翻译 + 对应 icall 实现 |

**icall 实现清单（按优先级）：**

| icall 类别 | C++ 实现 | 涉及的 BCL 类型 |
|-----------|---------|----------------|
| 数学运算 | `<cmath>` | System.Math, System.MathF |
| 字符串内部 | UTF-16 操作 | System.String (内部方法) |
| Console I/O | `<cstdio>` | System.Console |
| 文件系统 | `<filesystem>` / OS API | System.IO.File, Path, Directory |
| 环境 | OS API | System.Environment |
| 时间 | `<chrono>` | System.DateTime, Stopwatch |
| 线程 | `<thread>` / OS API | System.Threading (Phase 5) |

### Phase 5：高级运行时

复杂子系统，大多数基础程序不需要。

| 功能 | 说明 |
|------|------|
| **async / await** | 状态机生成，Task\<T\>，需要委托 + 泛型 + 异常处理 |
| **多线程** | Thread、Task、Monitor、lock 语句；需要多线程安全的 GC |
| **反射** | 填充 MethodInfo / FieldInfo 数组，Type.GetMethods、Invoke |
| **特性 (Attribute)** | 元数据存储和运行时查询 |
| **分代 GC** | 写屏障、新生代/老年代，减少 GC 暂停时间 |
| **P/Invoke / DllImport** | 原生互操作 |
| **unsafe 代码** | 指针运算、fixed、stackalloc |

---

## 测试

项目包含三层测试：编译器单元测试、运行时单元测试、端到端集成测试。

### 编译器单元测试 (C# / xUnit)

测试覆盖：类型映射 (CppNameMapper)、构建配置 (BuildConfiguration)、IR 模块/方法/指令、C++ 代码生成器。

```bash
# 运行测试
dotnet test compiler/CIL2CPP.Tests

# 运行测试 + 覆盖率报告
dotnet test compiler/CIL2CPP.Tests --collect:"XPlat Code Coverage"
```

| 模块 | 测试数 | 覆盖率 |
|------|--------|--------|
| CppNameMapper | 17 | 100% |
| BuildConfiguration | 10 | 100% |
| IRModule | 8 | 100% |
| IRMethod / IRType | 7 | 100% |
| IR Instructions (全部) | 27 | 100% |
| CppCodeGenerator | 20 | 93.5% |
| **合计** | **89** | |

### 运行时单元测试 (C++ / Google Test)

测试覆盖：GC（分配/回收/根/终结器）、字符串（创建/连接/比较/哈希/驻留）、数组（创建/越界检查）、类型系统（继承/接口/注册）、对象模型（分配/转型/相等性）、异常处理（抛出/捕获/栈回溯）。

```bash
# 配置 + 编译
cmake -B runtime/tests/build -S runtime/tests
cmake --build runtime/tests/build --config Debug

# 运行测试
ctest --test-dir runtime/tests/build -C Debug --output-on-failure
```

| 模块 | 测试数 |
|------|--------|
| GC | 17 |
| String | 34 |
| Array | 14 |
| Type System | 18 |
| Object | 18 |
| Exception | 11 (1 disabled) |
| **合计** | **112** |

### 端到端集成测试 (PowerShell)

测试完整编译流水线：C# `.csproj` → codegen → CMake configure → C++ build → run → 验证输出。

```powershell
# 运行集成测试（需要先安装运行时到 C:/cil2cpp_test）
powershell -ExecutionPolicy Bypass -File tests/integration/run_pipeline.ps1

# 指定运行时路径
powershell -ExecutionPolicy Bypass -File tests/integration/run_pipeline.ps1 -RuntimePrefix C:/cil2cpp_test
```

| Phase | 测试内容 | 测试数 |
|-------|---------|--------|
| 0 | 前置检查（dotnet、CMake、runtime 安装） | 3 |
| 1 | HelloWorld 可执行程序（codegen → build → run → 验证输出） | 5 |
| 2 | 类库项目（无入口点 → add_library → build） | 4 |
| 3 | Debug 配置（#line 指令、IL 注释、Debug build + run） | 4 |
| 4 | 字符串字面量（string_literal、__init_string_literals） | 2 |
| **合计** | | **18** |

### 全部运行

```bash
# 1. 编译器单元测试
dotnet test compiler/CIL2CPP.Tests

# 2. 运行时单元测试
cmake --build runtime/tests/build --config Debug
ctest --test-dir runtime/tests/build -C Debug --output-on-failure

# 3. 集成测试
powershell -ExecutionPolicy Bypass -File tests/integration/run_pipeline.ps1
```

---

## 技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| 编译器 | C# / .NET | 8.0 |
| IL 解析 | Mono.Cecil | NuGet 最新 |
| 运行时 | C++ | 20 |
| GC | 自研 mark-sweep | 精确回收，malloc/free |
| 构建系统 | dotnet + CMake | CMake 3.20+ |
| 运行时分发 | CMake install + find_package | |
| 编译器测试 | xUnit + coverlet | xUnit 2.9, coverlet 6.0 |
| 运行时测试 | Google Test | v1.15.2 (FetchContent) |
| 集成测试 | PowerShell | 跨平台 |

## 参考

- [Unity IL2CPP](https://docs.unity3d.com/Manual/IL2CPP.html)
- [Mono.Cecil](https://github.com/jbevain/cecil)
- [NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [ECMA-335 CLI Specification](https://www.ecma-international.org/publications-and-standards/standards/ecma-335/)