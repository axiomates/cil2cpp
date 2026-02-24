# 开发工具与环境配置

## 前置要求

| 工具 | 版本要求 | 用途 |
|------|---------|------|
| .NET 8 SDK | 8.0+ | 构建编译器 + 编译输入的 C# 项目 |
| CMake | 3.20+ | 构建运行时和生成的 C++ 代码 |
| C++ 编译器 | C++20 | 编译运行时和生成的代码 |
| Python | 3.8+ | 开发者 CLI 工具 (stdlib only) |

### C++ 编译器支持

| 平台 | 编译器 | 最低版本 |
|------|--------|---------|
| Windows | MSVC 2022 | Visual Studio 17.0+ |
| Linux | GCC | 12+ |
| Linux | Clang | 15+ |
| macOS | Apple Clang | 14+ (Xcode 14+) |

### 可选依赖（覆盖率报告）

> 快速安装：`python tools/dev.py setup` 会自动检测并安装

- **[OpenCppCoverage](https://github.com/OpenCppCoverage/OpenCppCoverage)** — C++ 覆盖率（Windows）
  ```bash
  winget install OpenCppCoverage.OpenCppCoverage
  ```
- **[ReportGenerator](https://github.com/danielpalme/ReportGenerator)** — 合并覆盖率 HTML 报告
  ```bash
  dotnet tool install -g dotnet-reportgenerator-globaltool
  ```
- **lcov + lcov_cobertura** — C++ 覆盖率（Linux，替代 OpenCppCoverage）

---

## 快速上手（4 步）

### 步骤 1：构建并安装运行时（一次性）

```bash
# 配置
cmake -B build -S runtime

# 编译（Release + Debug）
cmake --build build --config Release
cmake --build build --config Debug

# 安装到指定路径
cmake --install build --config Release --prefix C:/cil2cpp
cmake --install build --config Debug --prefix C:/cil2cpp
```

> BoehmGC 通过 FetchContent 自动下载，缓存在 `runtime/.deps/`，删 `build/` 不重新下载。

> **捷径**：步骤 2-4 可用 `python tools/dev.py compile HelloWorld` 一步完成。

### 步骤 2：生成 C++ 代码

```bash
# Release（默认）
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output

# Debug（#line 指令 + IL 偏移注释 + 栈回溯）
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i tests/HelloWorld/HelloWorld.csproj -o output -c Debug
```

| 选项 | 说明 | 默认值 |
|------|------|--------|
| `-i, --input` | 输入 .csproj 文件（必填） | — |
| `-o, --output` | 输出目录（必填） | — |
| `-c, --configuration` | 构建配置 | `Release` |

### 步骤 3：编译为原生可执行文件

```bash
cmake -B build_output -S output -DCMAKE_PREFIX_PATH=C:/cil2cpp
cmake --build build_output --config Release
```

### 步骤 4：运行

```bash
# Windows
build_output\Release\HelloWorld.exe

# Linux / macOS
./build_output/HelloWorld
```

---

## 生成的文件

| 文件 | 内容 | 条件 |
|------|------|------|
| `<Name>.h` | 结构体声明、方法签名、TypeInfo、静态字段 | 始终 |
| `<Name>_data.cpp` | TypeInfo 定义、VTable、字符串字面量、P/Invoke | 始终 |
| `<Name>_methods_N.cpp` | 方法实现（按 IR 指令数分区，每分区 ~20000） | 始终 |
| `<Name>_stubs.cpp` | 未实现方法的默认 stub | 有 stub 时 |
| `main.cpp` | 运行时初始化 → 入口方法 → 运行时关闭 | 仅可执行程序 |
| `CMakeLists.txt` | CMake 配置 | 始终 |
| `stubbed_methods.txt` | Stub 诊断报告 | 有 stub 时 |

## 可执行程序与类库

| C# 项目 | 检测条件 | 生成结果 |
|---------|---------|---------|
| 有 `static void Main()` | 可执行程序 | `main.cpp` + `add_executable` |
| 无入口点（类库） | 静态库 | 无 `main.cpp`，`add_library(STATIC)` |

## Debug 与 Release 配置

| 特性 | Release | Debug |
|------|---------|-------|
| `#line` 指令（映射回 C# 源码） | — | Yes |
| `/* IL_XXXX */` 偏移注释 | — | Yes |
| PDB 符号读取 | — | Yes |
| 运行时栈回溯 | 禁用 | Windows: DbgHelp, POSIX: backtrace |
| `CIL2CPP_DEBUG` 定义 | — | Yes |
| C++ 优化 | MSVC: `/O2`, GCC: `-O2` | MSVC: `/Zi /Od /RTC1`, GCC: `-g -O0` |

Debug 模式下用 Visual Studio 调试时，`#line` 指令让断点和单步执行定位到原始 C# 源文件。

---

## 开发者 CLI（`tools/dev.py`）

Python 交互式 CLI（仅标准库），统一所有构建/测试/覆盖率/代码生成操作。

### 子命令模式

```bash
python tools/dev.py build                  # 编译 compiler + runtime
python tools/dev.py build --compiler       # 仅编译 compiler
python tools/dev.py build --runtime        # 仅编译 runtime
python tools/dev.py test --all             # 运行全部测试
python tools/dev.py test --compiler        # 仅编译器测试 (xUnit)
python tools/dev.py test --runtime         # 仅运行时测试 (GTest)
python tools/dev.py test --coverage        # 测试 + 覆盖率 HTML 报告
python tools/dev.py test --compiler --filter ILOpcode  # 筛选测试
python tools/dev.py install                # 安装 runtime (Debug + Release)
python tools/dev.py codegen HelloWorld     # 快速代码生成测试
python tools/dev.py compile HelloWorld     # 一步编译：codegen → cmake → build
python tools/dev.py compile HelloWorld --run  # 编译并运行
python tools/dev.py compile -i myapp.csproj   # 编译任意项目
python tools/dev.py integration            # 集成测试
python tools/dev.py setup                  # 检查前置 + 安装可选依赖
```

### 交互式菜单

无参数运行时进入交互式菜单：

```bash
python tools/dev.py
```

### 覆盖率报告

```bash
python tools/dev.py test --coverage
# 1. C# 覆盖率 (coverlet) — dotnet test --collect:"XPlat Code Coverage"
# 2. C++ 覆盖率 (OpenCppCoverage) — 收集 runtime 测试覆盖
# 3. 合并两份 Cobertura XML → ReportGenerator → 统一 HTML 报告
# → 自动打开浏览器查看报告
# → 报告路径: CoverageResults/CoverageReport/index.html
```

---

## 测试体系

项目包含三层测试：编译器单元测试、运行时单元测试、端到端集成测试。

### C# 编译器测试（~1240 个，xUnit）

```bash
dotnet test compiler/CIL2CPP.Tests
```

**Fixture 缓存机制**：`SampleAssemblyFixture` 使用 xUnit 的 `ICollectionFixture<T>` 模式，每个 sample（HelloWorld、ArrayTest、FeatureTest 等）的编译结果在整个测试运行期间只创建一次并缓存。

> **重要**：新增测试时必须使用 `GetXxxReleaseContext()` / `GetXxxReleaseModule()` 获取缓存的编译结果，禁止在测试方法中直接 `new AssemblySet()` + `new ReachabilityAnalyzer()`（每次约 12 秒）。

### C++ 运行时测试（591 个，Google Test）

```bash
cmake -B runtime/tests/build -S runtime/tests
cmake --build runtime/tests/build --config Debug
ctest --test-dir runtime/tests/build -C Debug --output-on-failure
```

### 端到端集成测试（35 个）

完整编译流水线：C# `.csproj` → codegen → CMake configure → C++ build → run → 验证输出。

```bash
python tools/dev.py integration
```

### 全部测试

```bash
# 推荐
python tools/dev.py test --all

# 或手动
dotnet test compiler/CIL2CPP.Tests
cmake --build runtime/tests/build --config Debug && ctest --test-dir runtime/tests/build -C Debug --output-on-failure
python tools/dev.py integration
```

---

## 安装路径约定

| 用途 | 路径 | 说明 |
|------|------|------|
| 开发环境 | `C:/cil2cpp` | 手动 cmake --install |
| 集成测试 | `C:/cil2cpp_test` | dev.py integration 自动安装 |
| 消费者 | 自定义 | `find_package(cil2cpp REQUIRED)` → `cil2cpp::runtime` |
