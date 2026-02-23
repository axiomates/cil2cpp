# 技术架构

## 技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| 编译器 | C# / .NET | 8.0 |
| IL 解析 | Mono.Cecil | NuGet 最新 |
| 运行时 | C++ | 20 |
| GC | BoehmGC (bdwgc) | v8.2.12 (FetchContent) |
| 构建系统 | dotnet + CMake | CMake 3.20+ |
| 运行时分发 | CMake install + find_package | |
| 编译器测试 | xUnit + coverlet | xUnit 2.9 |
| 运行时测试 | Google Test | v1.15.2 (FetchContent) |
| 集成测试 | Python (`tools/dev.py integration`) | stdlib only |
| Unicode/国际化 | ICU4C | 78.2 |

## 项目布局

```
cil2cpp/
├── compiler/                   # C# 编译器 (.NET 项目)
│   ├── CIL2CPP.CLI/            #   命令行入口 (System.CommandLine)
│   ├── CIL2CPP.Core/           #   核心编译逻辑
│   │   ├── IL/                 #     IL 解析 (Mono.Cecil 封装)
│   │   │   ├── AssemblyReader.cs
│   │   │   ├── TypeDefinitionInfo.cs
│   │   │   └── ILInstruction.cs
│   │   ├── IR/                 #     中间表示
│   │   │   ├── IRBuilder.cs          # 8 遍构建入口
│   │   │   ├── IRBuilder.Emit.cs     # IL → IR 指令翻译
│   │   │   ├── IRBuilder.Methods.cs  # 方法体构建
│   │   │   ├── IRModule.cs           # 模块（类型集合）
│   │   │   ├── IRType.cs             # 类型
│   │   │   ├── IRMethod.cs           # 方法
│   │   │   ├── IRInstruction.cs      # 25+ 指令子类
│   │   │   └── CppNameMapper.cs      # IL ↔ C++ 名称映射
│   │   └── CodeGen/            #     C++ 代码生成
│   │       ├── CppCodeGenerator.cs       # 生成入口
│   │       ├── CppCodeGenerator.Header.cs  # .h 生成
│   │       ├── CppCodeGenerator.Source.cs  # .cpp 生成
│   │       ├── CppCodeGenerator.CMake.cs   # CMakeLists.txt 生成
│   │       └── CppCodeGenerator.KnownStubs.cs  # 手写 stub
│   └── CIL2CPP.Tests/          #   编译器单元测试 (xUnit)
│       └── Fixtures/           #     测试 fixture 缓存
├── tests/                      # 测试用 C# 项目（编译器输入）
│   ├── HelloWorld/
│   ├── ArrayTest/
│   ├── FeatureTest/
│   └── MultiAssemblyTest/
├── runtime/                    # C++ 运行时库 (CMake)
│   ├── CMakeLists.txt
│   ├── cmake/                  #   CMake 包配置模板
│   ├── include/cil2cpp/        #   头文件（29 个）
│   ├── src/                    #   源码（GC、类型系统、异常、BCL icall）
│   │   ├── gc/
│   │   ├── exception/
│   │   ├── type_system/
│   │   ├── io/
│   │   └── bcl/
│   └── tests/                  #   运行时单元测试 (Google Test)
├── tools/
│   └── dev.py                  # 开发者 CLI
└── docs/                       # 项目文档
```

## 编译流水线全景

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
             │ CppCodeGenerator ──→ C++ 头文件 + 多源文件 + CMake    │
             │                      (多文件分割 + 试渲染 + 自动 stub)│     .h / *_data.cpp
             └─────────────────────────────────────────────────────┘ ──→ *_methods_N.cpp
                                                                        CMakeLists.txt
                                                                            ↓
                                                                         cmake + C++ 编译器
                                                                            ↓
    C++ 运行时 (cil2cpp::runtime) ──────────────────────── find_package ──→ 链接
    BoehmGC / 类型系统 / 异常 / 线程池                                       ↓
                                                                       原生可执行文件
```

## IR 构建：8 遍流水线

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

### 关键类

- `IRModule` — 包含 `List<IRType>`、入口点、数组初始化数据、基本类型信息
- `IRType` — 包含字段、静态字段、方法、VTable 条目、基类引用
- `IRMethod` — 包含 `List<IRBasicBlock>`，每个基本块有 `List<IRInstruction>`
- `IRInstruction` — 25+ 个具体子类（IRBinaryOp, IRCall, IRCast, IRBox 等）
- `CppNameMapper` — IL 类型名 ↔ C++ 类型名的双向映射与标识符 mangling

## BCL 编译策略

### Unity IL2CPP 模型

CIL2CPP 采用与 Unity IL2CPP 相同的策略：**所有有 IL 方法体的 BCL 方法直接从 IL 编译为 C++**，与用户代码走完全相同的编译路径。仅在最底层保留 `[InternalCall]` 方法的 C++ 手写实现（icall）。

```
方法调用
  ↓
ICallRegistry 查找 (~243 个映射)
  ├─ 命中 → [InternalCall] 方法，无 IL 方法体
  │         GC / Monitor / Interlocked / Buffer / Math / IO / Globalization 等
  │         → 调用 C++ 运行时实现
  │
  └─ 未命中 → 正常 IL 编译（BCL 方法与用户方法相同路径）
              → 生成 C++ 函数
```

### RuntimeProvidedTypes（运行时提供的类型）

以下类型的**结构体定义**由 C++ 运行时提供（不从 BCL IL 编译结构体）。基于第一性原理分析：C++ runtime 直接访问字段 / BCL IL 引用 CLR 内部类型 / 嵌入 C++ 类型。

**必须 RuntimeProvided（26 个）**：
- **对象模型**: Object, ValueType, Enum — GC 头 + 类型系统基础
- **字符串/数组**: String, Array — 变长布局 + GC 互操作
- **异常**: Exception — setjmp/longjmp 机制
- **委托**: Delegate, MulticastDelegate — 函数指针调度
- **类型系统**: Type, RuntimeType — 类型元数据
- **线程**: Thread — TLS + OS 线程管理
- **反射 struct ×12**: MemberInfo, MethodInfo, FieldInfo, ParameterInfo + Runtime* 子类 + Assembly — .NET 8 BCL 反射 IL 深度依赖 QCall/MetadataImport，无法 AOT 编译
- **Task**: 4 个自定义运行时字段 + f_lock (std::mutex*) + MSVC padding 问题
- **Varargs**: TypedReference, ArgIterator

**短期可迁移为 IL（8 个，Phase IV）**：
- **IAsyncStateMachine** — 纯接口，无需 struct
- **CancellationToken** — 只有 f_source 指针
- **WaitHandle 层级 ×6** — BCL IL 可编译，需注册 OS 原语 ICall

**长期需架构重构（6 个，Phase V）**：
- **TaskAwaiter / AsyncTaskMethodBuilder / ValueTask / ValueTaskAwaiter / AsyncIteratorMethodBuilder** — 依赖 Task struct 布局
- **CancellationTokenSource** — 依赖 ITimer + ManualResetEvent 链

详见 [roadmap.md](roadmap.md) Phase IV-V。

### 编译器内置 intrinsics

少数 JIT intrinsic 方法由编译器内联处理（不经过 ICallRegistry）：

- `Unsafe.SizeOf<T>` / `Unsafe.As<T>` / `Unsafe.Add<T>` → C++ sizeof / reinterpret_cast / 指针运算
- `Array.Empty<T>()` → 静态空数组实例
- `RuntimeHelpers.InitializeArray` → memcpy 静态数据

## 三层架构

```
┌─────────────────────────────────────────────────────────┐
│  Layer 1: CIL 指令翻译                                   │
│  ConvertInstruction() switch — ~230 opcodes             │
│  覆盖率: 100%                                           │
│  这一层决定: 能否将 IL 方法体翻译为 C++                   │
├─────────────────────────────────────────────────────────┤
│  Layer 2: BCL 方法编译                                   │
│  所有有 IL 方法体的 BCL 方法从 IL 编译为 C++              │
│  限制: CLR 内部类型依赖 → 自动 stub 化                    │
│  这一层决定: 哪些标准库方法可用                           │
├─────────────────────────────────────────────────────────┤
│  Layer 3: 运行时 icall                                  │
│  ~243 个 [InternalCall] 方法 → C++ 运行时实现            │
│  限制: 未实现的 icall → 功能不可用                        │
│  这一层决定: GC、线程、字符串布局、IO 等底层能力           │
└─────────────────────────────────────────────────────────┘
```

## C++ 代码生成策略

生成的 C++ 代码将每个 .NET 类型映射为 C++ struct，每个方法映射为独立的 C 函数：

- **引用类型** → `struct` + `__type_info` 指针 + `__sync_block` + 用户字段，通过 `gc::alloc()` 堆分配
- **值类型** → 普通 `struct`（无对象头），栈分配，按值传递
- **实例方法** → `RetType FuncName(ThisType* __this, ...)` — 显式 this 参数
- **静态字段** → `<Type>_statics` 全局结构体 + `_ensure_cctor()` 初始化守卫
- **虚方法调用** → `obj->__type_info->vtable->methods[slot]` 函数指针调用
- **接口分派** → `type_get_interface_vtable()` 查找接口实现表

### 多文件分割与并行编译

生成的 C++ 代码自动拆分为多个编译单元，利用 MSVC `/MP` 并行编译加速：

```
output/
├── HelloWorld.h              ← 统一头文件（所有类型声明 + 前向引用）
├── HelloWorld_data.cpp       ← 静态数据（TypeInfo、VTable、字符串字面量、P/Invoke）
├── HelloWorld_methods_0.cpp  ← 方法实现分区 0
├── HelloWorld_methods_1.cpp  ← 方法实现分区 1
├── ...                       ← 按指令数自动分区（每分区 ~20000 条 IR 指令）
├── HelloWorld_stubs.cpp      ← 未实现方法的默认 stub
├── main.cpp                  ← 入口点（可执行项目）
└── CMakeLists.txt            ← 自动列出所有源文件 + /MP 编译选项
```

分区策略：按 IR 指令数均匀分割（`MinInstructionsPerPartition = 20000`），每个 `.cpp` 文件约 13k-17k 行 C++ 代码。

### 多层安全网：自动 stub 机制

BCL 中部分方法的 IL 引用了 CLR 内部类型，无法编译为 C++。编译器有 4 层安全网：

1. **HasClrInternalDependencies** — IR 级：方法引用 CLR 内部类型 → 替换为返回默认值的 stub
2. **HasKnownBrokenPatterns** — 预渲染：JIT intrinsics、自递归等已知问题 → 跳过
3. **RenderedBodyHasErrors** — 试渲染：将方法体渲染为 C++ 后检测编译错误模式 → stub 化
4. **GenerateMissingMethodStubImpls** — 兜底：所有声明但未定义的函数 → 生成默认 stub

每次 codegen 生成 `stubbed_methods.txt` 报告，列出所有被 stub 化的方法及原因。

## 垃圾收集器 (GC)

运行时使用 [BoehmGC (bdwgc)](https://github.com/ivmai/bdwgc) 作为垃圾收集器——与 Mono 相同的保守式 GC。

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
- 消费者通过 `find_package(cil2cpp)` 自动链接

### GC 功能状态

| 功能 | 状态 |
|------|------|
| 对象分配 + TypeInfo | `gc::alloc()` → `GC_MALLOC()` + 设置 `__type_info` |
| 数组分配 | `alloc_array()` → `GC_MALLOC()` + 元素类型 + 长度 |
| 自动根扫描 | BoehmGC 保守扫描，无需 shadow stack |
| Finalizer 注册 | `GC_register_finalizer_no_order()` |
| Finalizer 检测 | 编译器检测 `Finalize()` → TypeInfo.finalizer |
| GC 统计 | `GC_get_heap_size()` / `GC_get_total_bytes()` |
| 增量回收 | `GC_enable_incremental()` + `gc::collect_a_little()` |

## Runtime 安装与 CMake 包

运行时编译为静态库，通过 CMake install + find_package 分发：

```cmake
# 消费者 CMakeLists.txt
find_package(cil2cpp REQUIRED)
target_link_libraries(MyApp PRIVATE cil2cpp::runtime)
```

安装后的目录结构：

```
<prefix>/
├── include/cil2cpp/    # 29 个头文件
├── lib/
│   ├── cil2cpp_runtime.lib   # Release
│   ├── cil2cpp_runtimed.lib  # Debug (DEBUG_POSTFIX "d")
│   ├── gc.lib                # BoehmGC Release
│   ├── gcd.lib               # BoehmGC Debug
│   └── cmake/cil2cpp/        # CMake 包配置
│       ├── cil2cppConfig.cmake
│       ├── cil2cppTargets.cmake
│       └── ...
```

安装路径约定：
- 开发环境：`C:/cil2cpp`
- 集成测试：`C:/cil2cpp_test`
