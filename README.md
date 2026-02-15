# CIL2CPP

将 .NET/C# 程序编译为原生 C++ 代码的 AOT 编译工具，类似于 Unity IL2CPP。

## 工作原理

```
.csproj → dotnet build → .NET DLL (IL) → CIL2CPP → C++ 代码 + CMakeLists.txt → C++ 编译器 → 原生可执行文件
```

1. **IL 解析** — Mono.Cecil 读取 .NET 程序集中的 IL 字节码
2. **IR 构建** — 将 IL 指令转换为中间表示（7 遍：类型外壳 → 字段/基类 → 方法壳 → VTable → 接口 → 方法体 → 合成方法）
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
│   ├── CIL2CPP.Tests/          #   编译器单元测试 (xUnit, 1140 tests)
│   └── samples/                #   示例 C# 程序
├── runtime/                    # C++ 运行时库 (CMake 项目)
│   ├── CMakeLists.txt
│   ├── cmake/                  #   CMake 包配置模板
│   ├── include/cil2cpp/        #   头文件
│   ├── src/                    #   GC、类型系统、异常、BCL
│   └── tests/                  #   运行时单元测试 (Google Test, 426 tests)
└── tools/
    └── dev.py                  # 开发者 CLI (build/test/coverage/codegen/integration)
```

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
│   ├── gc.h                    #   GC 接口（BoehmGC 封装：alloc / collect）
│   ├── exception.h             #   异常处理（setjmp/longjmp）
│   ├── type_info.h             #   TypeInfo / VTable / MethodInfo / FieldInfo
│   ├── boxing.h                #   装箱/拆箱模板（box<T> / unbox<T>）
│   ├── reflection.h            #   System.Type 反射包装（typeof / GetType / 属性查询）
│   ├── threading.h             #   多线程原语（Thread / Monitor / Interlocked）
│   ├── task.h                  #   异步 Task/TaskAwaiter/AsyncTaskMethodBuilder
│   ├── threadpool.h            #   线程池（queue_work / init / shutdown）
│   ├── collections.h           #   List<T> / Dictionary<K,V> 运行时实现
│   ├── mdarray.h               #   多维数组 T[,] 运行时实现
│   ├── stackalloc.h            #   stackalloc 平台抽象宏（alloca）
│   └── bcl/                    #   BCL 实现头文件
│       ├── System.Object.h
│       ├── System.String.h
│       └── System.Console.h
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

**依赖说明：**
- **BoehmGC (bdwgc v8.2.12)** — 保守式垃圾收集器，构建运行时时通过 FetchContent 自动下载（缓存在 `runtime/.deps/`，删除 `build/` 不会重新下载）。MSVC 需要 libatomic_ops（同样自动下载）
- **gc.lib / gcd.lib** — BoehmGC 静态库，随运行时一起安装。消费者通过 `find_package(cil2cpp)` 自动链接，无需手动配置
- Windows Debug 模式自动链接 `dbghelp`（用于栈回溯符号解析）
- Linux/macOS 自动链接 pthreads
- 所有依赖通过 CMake target 传递，消费者无需手动添加

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
│ 1. dotnet build       自动编译 .csproj，定位输出 DLL          │
│ 2. Mono.Cecil 读取    解析 DLL 中的类型、方法、IL 指令         │
│    (Debug: 同时读取 PDB 符号文件获取源码行号映射)              │
│ 3. IR 构建            IL → 中间表示（7 遍）                   │
│    Pass 1: 创建类型外壳（名称、标志）                          │
│    Pass 2: 填充字段、基类、接口                               │
│    Pass 3: 创建方法壳（签名，不含方法体）                      │
│    Pass 4: 构建 VTable                                       │
│    Pass 5: 构建接口实现映射                                   │
│    Pass 6: 转换方法体（栈模拟 → 变量赋值，VTable 已就绪）      │
│    Pass 7: 合成方法（record ToString/Equals/GetHashCode 等）   │
│ 4. C++ 代码生成       IR → .h + .cpp + main.cpp + CMake      │
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

> ✅ 已支持 ⚠️ 部分支持 ❌ 未支持

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
| Nullable\<T\> | ✅ | BCL 方法拦截（get_HasValue/get_Value/GetValueOrDefault/.ctor），box 拆包（HasValue→box\<T\>/null），泛型单态化 |
| Tuple (ValueTuple) | ✅ | BCL 方法拦截（.ctor/Equals/GetHashCode/ToString/字段访问），支持任意元素数（>7 通过嵌套 TRest），解构赋值 |
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

### CIL 指令与前缀

| 功能 | 状态 | 备注 |
|------|------|------|
| `constrained.` 前缀 | ✅ | 泛型虚方法调用前缀，单态化后安全跳过（no-op） |
| `sizeof` 操作码 | ✅ | 值类型大小查询 → C++ `sizeof()` |
| `calli` 操作码 | ✅ | 间接函数调用（函数指针），支持 `delegate*` 场景 |
| `ldtoken` / `typeof` | ✅ | 数组初始化 + 类型 token → `&TypeInfo` 指针；`typeof(T)` → `Type.GetTypeFromHandle` → 缓存的 `Type` 对象 |
| `tail.` 前缀 | ✅ | 尾调用优化提示，AOT 编译中安全跳过（no-op） |
| `readonly.` 前缀 | ✅ | `ldelema` 只读提示，AOT 编译中安全跳过（no-op） |
| `volatile.` 前缀 | ✅ | 生成 `std::atomic` 读写（`load(acquire)` / `store(release)`） |
| `unaligned.` 前缀 | ✅ | 对齐提示，安全跳过（no-op） |

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
| Span\<T\> / ReadOnlySpan\<T\> | ✅ | BCL 拦截（.ctor/get_Item/get_Length/Slice/ToArray/GetPinnableReference），ref struct 检测（`IsByRefLikeAttribute`），stackalloc 集成 |

### 异常处理

| 功能 | 状态 | 备注 |
|------|------|------|
| 异常类型 | ⚠️ | 仅 7 种：NullReference / IndexOutOfRange / InvalidCast / InvalidOperation / Overflow / Argument / ArgumentNull |
| throw | ✅ | throw → `cil2cpp::throw_exception()`；运行时 `throw_null_reference()` 等便捷函数 |
| try / catch / finally | ✅ | 编译器读取 IL ExceptionHandler 元数据 → 生成 `CIL2CPP_TRY` / `CIL2CPP_CATCH` / `CIL2CPP_FINALLY` 宏调用 |
| rethrow | ✅ | `CIL2CPP_RETHROW` |
| 异常过滤器 (`catch when`) | ✅ | ECMA-335 Filter handler，catch-all + 条件判断 + 条件 rethrow，`CIL2CPP_FILTER` / `CIL2CPP_ENDFILTER` 宏 |
| 自动 null 检查 | ✅ | `null_check()` 内联函数 |
| 栈回溯 | ⚠️ | `capture_stack_trace()` — Windows: DbgHelp, POSIX: backtrace；仅 Debug |
| using 语句 | ✅ | try/finally + BCL 接口代理（IDisposable）→ 接口分派 Dispose()，单程序集/多程序集均可工作 |
| 嵌套 try/catch/finally | ⚠️ | 宏基于 setjmp/longjmp，支持嵌套但复杂场景可能有限 |

### 标准库 (BCL)

> 多程序集模式：BCL 类型通过 `--multi-assembly` 自动翻译（树摇 + ICall 注册表）。
> 单程序集模式：`MapBclMethod()` 硬编码映射 + BCL 拦截（集合类/LINQ/Span 等）。

| 功能 | 状态 | 备注 |
|------|------|------|
| System.Object (ToString, GetHashCode, Equals, GetType) | ✅ | 手写映射 + 运行时实现；`GetType()` 返回缓存的 `Type` 对象 |
| System.String (Concat, IsNullOrEmpty, Length) | ✅ | 手写映射 + 运行时实现 |
| Console.WriteLine (全部重载) | ✅ | 手写映射，String, Int32, Int64, Single, Double, Boolean, Object |
| Console.Write / ReadLine | ✅ | 手写映射 |
| System.Math | ✅ | Abs, Max, Min, Sqrt, Floor, Ceil, Round, Pow, Sin, Cos, Tan, Asin, Acos, Atan, Atan2, Log, Log10, Exp → `<cmath>` |
| 多程序集 BCL 自动翻译 | ⚠️ | `--multi-assembly` 模式：自动加载 BCL 程序集 + 树摇 + IL 翻译；需要更多 icall 覆盖 |
| List\<T\> / Dictionary\<K,V\> | ✅ | C++ 运行时实现：`list_create`/`list_add`/`list_get`/`list_set` + `dict_create`/`dict_add`/`dict_get`；BCL 拦截，含 Enumerator |
| LINQ (Where/Select/ToList/Count/Any/First) | ✅ | 核心 LINQ 操作通过 BCL 拦截映射到 C++ 运行时函数；OrderBy/GroupBy/Join 未实现 |
| yield return / IEnumerable | ✅ | C# 编译器生成迭代器状态机类，BCL 接口代理（IEnumerable\<T\>/IEnumerator\<T\>）启用接口分派 |
| System.IO (File, Stream) | ❌ | 需要文件系统 icall 实现 |
| System.Net | ❌ | 需要网络 icall 实现 |

### 委托与事件

| 功能 | 状态 | 备注 |
|------|------|------|
| 委托 (Delegate) | ✅ | ldftn/ldvirtftn → 函数指针，newobj → `delegate_create()`，Invoke → `IRDelegateInvoke` |
| 事件 (event) | ✅ | C# 生成 add_/remove_ 方法 + 委托字段，Subscribe/Unsubscribe 通过 `Delegate.Combine/Remove` |
| 多播委托 | ✅ | `Delegate.Combine` / `Delegate.Remove` 映射到运行时 `delegate_combine` / `delegate_remove` |
| Lambda / 匿名方法 | ✅ | C# 编译器生成 `<>c` 静态类（无捕获）/ `<>c__DisplayClass`（闭包），编译器自动处理 |
| LINQ | ✅ | Where/Select/ToList/ToArray/Count/Any/First/FirstOrDefault BCL 拦截；foreach 通过 BCL 接口代理完整支持 |

### 高级功能

| 功能 | 状态 | 备注 |
|------|------|------|
| async / await | ✅ | 真正并发：线程池 + continuation + Task.Delay/WhenAll/WhenAny/Run；`Task<T>`/`TaskAwaiter<T>`/`AsyncTaskMethodBuilder<T>` BCL 拦截 |
| 多线程 | ✅ | `Thread`（创建/Start/Join）、`Monitor`（Enter/Exit/Wait/Pulse）、`lock` 语句、`Interlocked`（Increment/Decrement/Exchange/CompareExchange）、`Thread.Sleep`、`volatile` 字段 |
| 反射 (typeof / GetType) | ⚠️ | `typeof(T)` / `obj.GetType()` → 缓存 `Type` 对象；13 项属性（Name/FullName/IsValueType/IsPrimitive 等）；op_Equality/op_Inequality；ECMA-335 FieldInfo/MethodInfo 元数据数组；不支持 GetMethods/GetFields/Invoke |
| 特性 (Attribute) | ⚠️ | 元数据存储 + 运行时查询（`type_has_attribute` / `type_get_attribute`）；支持基本类型 + 字符串构造参数；数组/嵌套属性参数未实现 |
| unsafe 代码 (指针, fixed, stackalloc) | ✅ | `PointerType` 解析，`fixed`（pinned local → BoehmGC 保守扫描无需实际 pin），`stackalloc` → `localloc` → 平台 `alloca` 宏 |
| P/Invoke / DllImport | ⚠️ | extern "C" 声明 + 基本类型/String marshaling（Ansi/Unicode/Auto）；struct marshaling / callback delegate 未实现 |
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

### 尚未实现的功能

| 限制 | 说明 |
|------|------|
| SIMD / `System.Numerics.Vector` | 需要平台特定内联函数 (SSE/AVX/NEON)，技术上可行但工作量大 |
| 反射 — 动态查询 | `typeof(T)` / `GetType()` / Type 属性已支持；`GetMethods()` / `GetFields()` / `Invoke()` 未实现 |
| IAsyncEnumerable\<T\> | 需要异步迭代器状态机支持 |
| CancellationToken / TaskCompletionSource | Task 取消和手动完成基础设施 |
| LINQ OrderBy / GroupBy / Join | 仅支持 Where/Select/ToList/Count/Any/First 等核心操作 |
| P/Invoke struct marshaling | 基本类型 + String 已支持；结构体布局和回调委托未实现 |
| Attribute 复杂参数 | 基本类型 + 字符串参数已支持；数组/嵌套属性/Type 参数未实现 |
| System.IO / System.Net | 文件系统和网络 icall 未实现 |
| Parallel LINQ (PLINQ) | 需要高级线程池调度 |

### 实现层面的已知限制

以下是当前实现中已知的部分支持或行为差异，不影响大多数程序，但在特定场景下需注意：

| 限制 | 说明 |
|------|------|
| 异常类型有限 | 仅支持 7 种异常类型（NullReference / IndexOutOfRange / InvalidCast / InvalidOperation / Overflow / Argument / ArgumentNull）。抛出或捕获其他异常类型会导致链接失败 |
| `System.TypedReference` | `mkrefany`/`refanytype`/`refanyval` 指令不支持（C# 的 `__makeref`/`__reftype`/`__refvalue`）。极少使用，.NET 不鼓励 |
| 泛型约束不验证 | `where T : IComparable` 等泛型约束在编译期不验证。不满足约束的代码可以编译，但运行时可能产生未定义行为 |
| 未识别的 IL 指令 | 遇到未处理的 IL 操作码时生成 `/* WARNING: unsupported opcode */` 注释占位符，不会报错。可能导致运行时行为不正确 |
| 字符串方法有限 | 单程序集模式仅支持 `Concat`、`IsNullOrEmpty`、`Length`、`Contains`、`Substring`、`Replace`、`ToUpper`、`ToLower` 等少量方法。`string.Format`、`string.Join`、插值字符串（非 Concat 编译形式）等不支持 |
| 字符串模式匹配 | 模式匹配 IL 层面全部支持（Roslyn 编译为 isinst/ceq/switch/分支链）。但字符串 `switch` / `is "literal"` 需要 `String.op_Equality` BCL 映射（单程序集模式未映射） |

### AOT 架构根本限制

以下功能由于 AOT（Ahead-of-Time）编译模型的固有约束，**无法支持**。这与 Unity IL2CPP 和 .NET NativeAOT 的限制相同。

| 限制 | 原因 |
|------|------|
| `System.Reflection.Emit` | 运行时生成 IL 并执行——AOT 编译后无 IL 解释器/JIT |
| `DynamicMethod` | 运行时创建方法并执行——同上 |
| `Expression<T>.Compile()` | 运行时编译表达式树为可执行代码 |
| `Assembly.Load()` / `Assembly.LoadFrom()` | 运行时动态加载程序集——AOT 要求所有代码在编译期可知 |
| `Activator.CreateInstance(string typeName)` | 按名称字符串动态实例化——编译期无法确定目标类型 |
| `MethodInfo.Invoke()` | 反射调用任意方法——需要运行时解释器或 JIT |
| `Type.MakeGenericType()` | 运行时构造泛型类型——单态化必须在编译期完成 |
| `ExpandoObject` / `dynamic` | DLR (Dynamic Language Runtime) 完全依赖运行时绑定 |
| 运行时代码热更新 | 无 JIT 编译器，编译后的机器码不可替换 |

---

## BCL 策略

CIL2CPP 使用两种互补的 BCL 支持策略：

```
单程序集模式:                             多程序集模式 (--multi-assembly):
  C# 用户代码                            C# 用户代码 + 第三方库 + BCL
      ↓ IL → C++                             ↓ IL → C++ (同一流水线)
  MapBclMethod() 硬编码映射               可达性分析 (树摇)
  + BCL 拦截 (集合/LINQ/Span 等)              ↓ 仅翻译可达类型
      ↓                                  icall 层 (C++ 薄封装)
  C++ 运行时实现                              ↓
      ↓                                  printf / <cmath> / OS API / ...
  printf / <cmath> / ...
```

**icall（内部调用）** 是 .NET 中标记为 `[MethodImpl(MethodImplOptions.InternalCall)]` 的方法——BCL 的 C# 代码调用到原生实现的边界。例如 `Math.Sin()` 的 C# 代码最终调用一个 icall，该 icall 的 C++ 实现调用 `<cmath>` 的 `sin()`。

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
| IRBuilder | 273 |
| ILInstructionCategory | 173 |
| CppNameMapper | 104 |
| CppCodeGenerator | 70 |
| TypeDefinitionInfo | 65 |
| IR Instructions (全部) | 54 |
| IRModule | 44 |
| ICallRegistry | 38 |
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
| **合计** | **1140** |

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
| Exception | 55 (1 disabled) |
| String | 52 |
| Reflection | 46 |
| Collections | 42 |
| Type System | 39 |
| Array | 34 |
| Object | 28 |
| Console | 27 |
| Boxing | 26 |
| Async (Task/ThreadPool) | 19 |
| Delegate | 18 |
| Threading | 17 |
| GC | 23 |
| **合计** | **426 (1 disabled)** |

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
| 多程序集 | --multi-assembly、跨程序集类型/方法、MathLib 引用 | 5 |
| **合计** | | **23** |

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
python tools/dev.py test --compiler        # 仅编译器测试 (1140 xUnit)
python tools/dev.py test --runtime         # 仅运行时测试 (426 GTest)
python tools/dev.py test --coverage        # 测试 + 覆盖率 HTML 报告
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

## 参考

- [Unity IL2CPP](https://docs.unity3d.com/Manual/IL2CPP.html)
- [Mono.Cecil](https://github.com/jbevain/cecil)
- [BoehmGC (bdwgc)](https://github.com/ivmai/bdwgc) — 保守式垃圾收集器
- [NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [ECMA-335 CLI Specification](https://www.ecma-international.org/publications-and-standards/standards/ecma-335/)