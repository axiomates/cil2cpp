# CIL2CPP

将 .NET/C# 程序编译为原生 C++ 代码的 AOT 编译工具，类似于 Unity IL2CPP。

**定位**：通用 AOT 编译器，对标 .NET NativeAOT 覆盖范围。采用 Unity IL2CPP 架构——所有 BCL IL 方法体直接编译为 C++，仅在最底层保留 `[InternalCall]` 的 C++ 实现（~243 个 icall）。

```
.csproj → dotnet build → .NET DLL (IL) → Mono.Cecil → IR (8 遍) → C++ 源码 + CMakeLists.txt → 原生可执行文件
```

## 快速上手

### 1. 构建并安装运行时（一次性）

```bash
cmake -B build -S runtime
cmake --build build --config Release
cmake --build build --config Debug
cmake --install build --config Release --prefix C:/cil2cpp
cmake --install build --config Debug --prefix C:/cil2cpp
```

### 2. 生成 C++ 代码

```bash
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output
```

### 3. 编译为原生可执行文件

```bash
cmake -B build_output -S output -DCMAKE_PREFIX_PATH=C:/cil2cpp
cmake --build build_output --config Release
```

### 4. 运行

```bash
build_output/Release/HelloWorld.exe   # Windows
./build_output/HelloWorld             # Linux / macOS
```

输出：
```
Hello, CIL2CPP!
30
42
```

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

**输出 (C++，简化，Windows 版本)**:

```cpp
// ---- HelloWorld.h ----
struct Calculator {
    cil2cpp::TypeInfo* __type_info;
    cil2cpp::UInt32    __sync_block;
    int32_t f_result;
};

int32_t Calculator_Add(Calculator* __this, int32_t a, int32_t b);

// ---- HelloWorld_methods_0.cpp ----
int32_t Calculator_Add(Calculator* __this, int32_t a, int32_t b) {
    return a + b;
}

void Program_Main() {
    System_Console_ensure_cctor();
    System_Console_WriteLine__System_String(
        (cil2cpp::String*)(void*)__str_45);
    auto __t0 = (Calculator*)cil2cpp::gc::alloc(
        sizeof(Calculator), &Calculator_TypeInfo);
    Calculator__ctor(__t0);
    auto __t1 = Calculator_Add(__t0, 10, 20);
    System_Console_ensure_cctor();
    System_Console_WriteLine__System_Int32(__t1);
}

// Console.WriteLine 调用链（全部由 BCL IL 编译生成）：
//   Console.WriteLine → TextWriter.WriteLine → StreamWriter.Write
//     → Encoding.GetBytes → P/Invoke → 平台相关（如 Windows: WriteFile，Linux/macOS: write/stdout）
```

## 核心指标

| 指标 | 数量 |
|------|------|
| IL 操作码覆盖率 | 100%（全部 ~230 种 ECMA-335 操作码） |
| ICallRegistry 条目 | ~243 个 |
| C# 编译器测试 | ~1240 个 (xUnit) |
| C++ 运行时测试 | 591 个 (Google Test) |
| 端到端集成测试 | 35 个 |

## 项目文档

| 文档 | 内容 |
|------|------|
| [项目目标与定位](docs/goals.md) | 项目定义、对标 Unity IL2CPP / NativeAOT、设计原则、AOT 限制 |
| [技术架构](docs/architecture.md) | 编译流水线、IR 8 遍构建、BCL 策略、C++ 代码生成、GC 架构、CMake 包 |
| [现状与限制](docs/status.md) | 阶段完成表、C# 功能支持表、ICall 明细、System.IO/P/Invoke 状态、Stub 分析 |
| [未来开发计划](docs/roadmap.md) | Phase H（Native Libs 集成）、中期/长期计划、阻断分析、风险 |
| [开发工具](docs/tools.md) | 前置要求、构建命令、dev.py 使用、测试体系、Debug/Release 配置 |
| [踩坑记录](docs/lessons.md) | 架构反思（GC/BCL/多文件）、C++/Cecil/IR/异常/异步陷阱 |

## 项目结构

```
cil2cpp/
├── compiler/                   # C# 编译器 (.NET 8)
│   ├── CIL2CPP.CLI/            #   命令行入口
│   ├── CIL2CPP.Core/           #   核心：IL 解析 → IR → C++ 代码生成
│   └── CIL2CPP.Tests/          #   编译器测试 (xUnit, ~1240 tests)
├── runtime/                    # C++ 运行时 (C++20, CMake)
│   ├── include/cil2cpp/        #   头文件（29 个）
│   ├── src/                    #   GC / 类型系统 / 异常 / BCL icall
│   └── tests/                  #   运行时测试 (GTest, 591 tests)
├── tests/                      # 测试用 C# 项目
├── tools/dev.py                # 开发者 CLI
└── docs/                       # 项目文档
```

## 参考

- [Unity IL2CPP](https://docs.unity3d.com/Manual/IL2CPP.html)
- [Mono.Cecil](https://github.com/jbevain/cecil)
- [BoehmGC (bdwgc)](https://github.com/ivmai/bdwgc)
- [NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [ECMA-335 CLI Specification](https://www.ecma-international.org/publications-and-standards/standards/ecma-335/)
