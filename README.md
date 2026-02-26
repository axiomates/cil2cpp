# CIL2CPP

An AOT compiler that compiles .NET/C# programs into native C++ code, similar to Unity IL2CPP.

**Positioning**: General-purpose AOT compiler targeting .NET NativeAOT-level coverage. Uses Unity IL2CPP architecture — all BCL IL method bodies are compiled directly to C++, with only the lowest-level `[InternalCall]` C++ implementations (~270 icalls) retained.

```
.csproj → dotnet build → .NET DLL (IL) → Mono.Cecil → IR (8 passes) → C++ source + CMakeLists.txt → native executable
```

> [中文文档 (Chinese)](README.zh-CN.md)

## Quick Start

### 1. Build and install runtime (one-time)

```bash
cmake -B build -S runtime
cmake --build build --config Release
cmake --build build --config Debug
cmake --install build --config Release --prefix C:/cil2cpp
cmake --install build --config Debug --prefix C:/cil2cpp
```

### 2. Generate C++ code

```bash
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output
```

### 3. Compile to native executable

```bash
cmake -B build_output -S output -DCMAKE_PREFIX_PATH=C:/cil2cpp
cmake --build build_output --config Release
```

### 4. Run

```bash
build_output/Release/HelloWorld.exe   # Windows
./build_output/HelloWorld             # Linux / macOS
```

Output:
```
Hello, CIL2CPP!
30
42
```

## Code Translation Example

**Input (C#)**:

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

**Output (C++, simplified, Windows version)**:

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

// Console.WriteLine call chain (all compiled from BCL IL):
//   Console.WriteLine → TextWriter.WriteLine → StreamWriter.Write
//     → Encoding.GetBytes → P/Invoke → platform-specific (Windows: WriteFile, Linux/macOS: write/stdout)
```

## Key Metrics

| Metric | Count |
|--------|-------|
| IL opcode coverage | 100% (all ~230 ECMA-335 opcodes) |
| ICallRegistry entries | ~270 |
| C# compiler tests | ~1240 (xUnit) |
| C++ runtime tests | 591 (Google Test) |
| End-to-end integration tests | 35 |

## Documentation

| Document | Contents |
|----------|----------|
| [Project Goals](docs/goals.md) | Project definition, comparison with Unity IL2CPP / NativeAOT, design principles, AOT limitations |
| [Technical Architecture](docs/architecture.md) | Compilation pipeline, IR 8-pass build, BCL strategy, C++ code generation, GC architecture, CMake package |
| [Capabilities & Limitations](docs/capabilities.md) | Phase completion table, C# feature support, ICall details, System.IO/P/Invoke status, stub analysis |
| [Development Roadmap](docs/roadmap.md) | Phase H (Native Libs integration), mid/long-term plans, blocker analysis, risks |
| [Development Tools](docs/tools.md) | Prerequisites, build commands, dev.py usage, test system, Debug/Release configuration |
| [Lessons Learned](docs/lessons.md) | Architecture reflections (GC/BCL/multi-file), C++/Cecil/IR/exception/async pitfalls |

## Project Structure

```
cil2cpp/
├── compiler/                   # C# compiler (.NET 8)
│   ├── CIL2CPP.CLI/            #   CLI entry point
│   ├── CIL2CPP.Core/           #   Core: IL parsing → IR → C++ code generation
│   └── CIL2CPP.Tests/          #   Compiler tests (xUnit, ~1240 tests)
├── runtime/                    # C++ runtime (C++20, CMake)
│   ├── include/cil2cpp/        #   Headers (32 files)
│   ├── src/                    #   GC / type system / exception / BCL icall
│   └── tests/                  #   Runtime tests (GTest, 591 tests)
├── tests/                      # Test C# projects
├── tools/dev.py                # Developer CLI
└── docs/                       # Project documentation
```

## References

- [Unity IL2CPP](https://docs.unity3d.com/Manual/IL2CPP.html)
- [Mono.Cecil](https://github.com/jbevain/cecil)
- [BoehmGC (bdwgc)](https://github.com/ivmai/bdwgc)
- [NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [ECMA-335 CLI Specification](https://www.ecma-international.org/publications-and-standards/standards/ecma-335/)
