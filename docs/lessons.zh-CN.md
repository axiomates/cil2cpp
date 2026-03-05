# 踩坑记录与架构反思

> [English Version](lessons.md)

本文记录了 CIL2CPP 开发过程中遇到的重要技术陷阱、走过的弯路、以及架构决策的反思。

---

## 架构决策反思

### 自定义 GC → BoehmGC

**原方案**：自己实现 mark-sweep GC，需要 shadow stack 追踪栈根、引用位图追踪堆引用、手动 add_root 注册全局变量。

**问题**：
- 每个生成的函数都需要插入 shadow stack push/pop 代码
- 值类型中嵌套引用类型需要精确布局信息
- 数组中的引用元素需要手动标记
- 实现成本极高，且容易出错

**教训**：BoehmGC 的保守扫描自动解决所有根追踪问题——自动扫描栈、堆、全局区，无需任何代码插桩。对于 AOT 编译器来说，保守式 GC 是更务实的选择。

### TryEmit* 拦截器 → Unity IL2CPP 架构

**原方案**：为每个 BCL 方法手写 C++ 实现（TryEmit* 拦截器），在代码生成阶段检测 BCL 方法调用并替换为 C++ 调用。

**问题**：
- BCL 方法数量巨大，手写不可持续
- 每个 .NET 版本的 BCL 实现可能变化
- 拦截器维护成本高

**教训**：Unity IL2CPP 架构更好——让 BCL 的 IL 字节码走正常编译路径，只在最底层的 `[InternalCall]` 保留 C++ 手写实现（~270 个 icall）。这样 BCL 更新时无需修改编译器。

### 单文件 C++ → 多文件分割

**原方案**：所有生成的 C++ 代码放在一个 `.cpp` 文件中。

**问题**：
- HelloWorld 的 BCL IL 全编译后，单个 .cpp 文件达到 14 万行
- MSVC 编译单个大文件时无法利用多核
- 编译时间极长

**教训**：按 IR 指令数分割为多个编译单元（`*_methods_N.cpp`），配合 MSVC `/MP` 并行编译，编译速度显著提升。分区阈值 `MinInstructionsPerPartition = 20000`。

---

## C++ 陷阱

### `#line default` 不是 C/C++ 语法

`#line default` 是 C# 预处理器特有的指令，用于恢复默认行号。在 C/C++ 中使用会导致 MSVC 报错 C2005。

**正确做法**：C/C++ 中只使用 `#line N "file"` 格式，不需要 `#line default`，编译器会继续使用最后一个 `#line` 设置。

### MSVC `GC_NOT_DLL` 链接错误

bdwgc 的 `gc_config_macros.h` 在检测到 MSVC 的 `_DLL` 宏时会自动定义 `GC_DLL`，导致所有 GC 函数声明为 `__declspec(dllimport)`，链接时报 `__imp_GC_malloc` 等未找到错误。

bdwgc 的 CMakeLists.txt 通过 `add_definitions(-DGC_NOT_DLL)` 解决了这个问题，但这是 directory-scoped 的——只对 bdwgc 自身的编译单元生效，**消费者必须自己定义 `GC_NOT_DLL`**。

### MSVC tail-padding 导致结构体偏移错误

`Task` 类型最初设计为继承 `Object`：

```cpp
struct Object { TypeInfo* __type_info; uint32_t __sync_block; }; // sizeof = 12
struct Task : Object { ... };
```

但 MSVC 会将 `sizeof(Object)` 填充到 16 字节（8 字节对齐），导致 `Task` 的字段从偏移 16 开始。而编译器生成的 flat struct 将字段紧密排列在偏移 12。

**修复**：Task 不继承 Object，而是内联 `__type_info` + `__sync_block` 字段。

### `testing::CaptureStdout()` 与 `SetConsoleOutputCP()` 冲突

Google Test 的 `CaptureStdout()` 在 Windows 上与 `SetConsoleOutputCP(CP_UTF8)` 冲突，会导致输出捕获失败或乱码。

**修复**：避免在调用初始化控制台的函数的测试中使用 CaptureStdout。

---

## Mono.Cecil 陷阱

### `IsClass` 对 struct 也返回 true

Cecil 的 `TypeDefinition.IsClass` 对值类型（struct）也返回 true。这是因为 ECMA-335 中 struct 也标记了 `class` 语义标志。

**正确判断值类型**：检查 `BaseTypeName == "System.ValueType"` 或 `BaseTypeName == "System.Enum"`。

### `ExceptionHandler.HandlerEnd` 可能为 null

当异常处理器是方法的最后一个块时，Cecil 的 `ExceptionHandler.HandlerEnd` 为 null（表示"到方法结尾"）。

**正确用法**：`handler.HandlerEnd?.Offset ?? int.MaxValue`

### AssemblyDefinition dispose 后对象失效

Mono.Cecil 的所有类型/方法/字段对象都由 `AssemblyDefinition` 持有。一旦 `AssemblyReader` 被 dispose，所有通过 Cecil 获取的对象都会失效。

**教训**：确保在整个 IR 构建过程中 AssemblyReader 保持存活。

### System.Runtime.dll 是类型转发器

`System.Runtime.dll` 几乎不包含实际类型定义，只有 `TypeForwardedTo` 属性指向 `System.Private.CoreLib.dll`。需要通过 Cecil 的 `ExportedType` 解析实际类型位置。

---

## IR 构建陷阱

### `CppNameMapper.GetDefaultValue` 双名问题

这个函数最初只匹配 IL 类型名（如 `System.Int32`），但代码生成器实际传入的是 C++ 类型名（如 `int32_t`）。必须同时处理两套命名。

### AddAutoDeclarations 与手动 auto 冲突

代码生成器的 `AddAutoDeclarations` pass 会检测 `__tN` 变量的首次使用并自动添加 `auto` 前缀。如果在 `IRRawCpp` 中手动写了 `auto __t0 = ...`，会导致 MSVC 报错 C3536（变量被声明两次）。

**修复**：在 IRRawCpp 中不要手动写 `auto`，让 AddAutoDeclarations 统一处理。

### `ldloca` 产生的地址需要括号

`ldloca` 将局部变量地址压栈，产生 `&loc_N`。当后续用 `->` 访问字段时，需要写成 `(&loc_N)->field`，否则运算符优先级错误。

### 泛型名 mangling 的尾下划线差异

`MangleTypeName` 和 `MangleGenericInstanceTypeName` 对泛型类型名的处理不同：
- `MangleTypeName("List<int>")` → `List_1_System_Int32_`（尾部有下划线，因为 `>` → `_`）
- `MangleGenericInstanceTypeName("List<int>")` → `List_1_System_Int32`（无尾部下划线）

在用作字典 key 时必须注意使用哪个版本。

---

## 异常处理陷阱

### try-finally 吞掉异常

原始实现中，`CIL2CPP_END_TRY` 宏在 finally 块执行完毕后不会重新抛出未捕获的异常。

**修复**：添加 `__exc_caught` 标志。在 `CIL2CPP_CATCH` / `CIL2CPP_CATCH_ALL` 中设置为 true。`END_TRY` 检查：如果 `__pending && !__exc_caught`，则调用 `throw_exception(__pending)` 重新抛出。

### `leave` 编译为 `goto` 跳过 finally

IL 的 `leave` 指令应该在离开 try 块时执行 finally。但编译为 C++ 的 `goto` 会直接跳过 finally 块。

**修复**：编译器收集所有 try-finally 区域。当 `leave` 的目标跨越 try-finally 边界（offset 在 TryStart..TryEnd 内，目标 >= TryEnd）时，不生成 goto，让执行流自然进入 CIL2CPP_FINALLY。

### skipDeadCode 在 handler 入口未重置

`throw`/`ret`/`br`/`leave` 之后设置 `skipDeadCode = true`，跳过后续不可达代码。但异常处理器的入口（CatchBegin/FinallyBegin 等）是可达的（通过运行时分派到达），不是分支目标。

**修复**：在 CatchBegin / FinallyBegin / FilterBegin / FilterHandlerBegin 时重置 `skipDeadCode = false`。

### throw_exception 必须跳过活跃的 handler

`throw_exception` 遍历 ExceptionContext 链时，必须跳过 state != 0 的条目（catch=1, finally=2）。否则从 catch/finally 块内抛出异常时会匹配到当前正在执行的 handler，导致无限循环。

---

## 异步陷阱

### Debug 状态机是 class 不是 struct

C# 编译器在 Debug 模式下将 async 状态机生成为 class（BaseType = System.Object），Release 模式下才生成为 struct。不要断言 IsValueType。

### Task\<T\> 分配大小不足

`task_create_pending()` 只分配 `sizeof(Task)` 的内存，不包含泛型 `f_result` 字段。对于 `Task<T>`，必须使用 `gc::alloc(sizeof(Task_T), nullptr)` + `task_init_pending()` 来分配足够大小的内存。

### flat struct 不继承 runtime 基类

编译器生成的 flat struct（如 `Task_1_System_Int32`）不继承运行时的 `Task` 基类，只是字段布局兼容。因此不能用 `static_cast<Task*>`，必须用 `reinterpret_cast<cil2cpp::Task*>`。

---

## BCL 集成教训

### BCL 泛型类型方法体位置

BCL 泛型类型（Nullable\<T\>、ValueTuple 等）的方法体在 `System.Runtime.dll` 中，而非用户程序集。当进行泛型单态化（`CreateGenericSpecializations`）时，需要跳过这些方法的 IL 方法体转换，改为在 `EmitMethodCall` / `EmitNewObj` 中拦截调用。

### ICall overload 冲突

`RegisterICall` 按 `type::method/paramCount` 匹配，无法区分参数类型不同但参数数量相同的重载（如 `String.Equals(String, StringComparison)` vs `Equals(String, String)`）。

**修复**：使用 `RegisterICallTyped` 并指定 `firstParamType` 来区分。

### ICall void* for this

运行时 icall 的 `__this` 参数必须使用 `void*` 而非 `Object*`，因为编译器生成的 flat struct 不继承 `cil2cpp::Object`。

---

## 树摇与 OOM 教训

### 安全上限（Budget Cap）是错误方案

**原方案**：IRBuilder 三个 fixpoint 循环（Pass 0.5/3.5/3.6）设置 50 次迭代上限，防止无限循环和 OOM。

**问题**：
- 上限只是掩盖问题，不解决根因
- 静默截断可能产生不正确的编译结果（丢失类型/方法）
- 或者上限太高根本挡不住 OOM

**教训**：找到发散根因并修复，依赖自然终止条件（集合耗尽）。Budget cap = 技术债，永远要找真正的根因。

### OOM 诊断：先加日志，不要猜

**背景**：NuGetSimpleTest（2 个 Newtonsoft.Json 调用）消耗 24GB 内存，输出为 0。最初假设是 cctor 级联爆炸。

**错误方向**：先实施 lazy cctor——类型数仅从 3,411 降到 3,356，假设错误。

**正确做法**：给 Pass 0.5 fixpoint 循环加诊断打印，发现每轮 +100/+65/+50 新泛型类型，永不收敛。打印类型名后立刻定位到 `System.Text.RegularExpressions.Symbolic` 递归嵌套。

**教训**：不要猜 OOM 根因。加日志确认哪个循环在发散、产生什么数据。数据胜过假设。

### 无限递归泛型嵌套是 AOT 编译器的固有风险

某些 BCL 类型（`Symbolic.BDD`、`DerivativeEffect`、`SymbolicRegexNode`）的泛型参数互相引用，创建无限深度嵌套实例化：

```
KVP<BDD>> → KVP<KVP<BDD>>> → KVP<KVP<KVP<BDD>>>> → ...
```

JIT 不需要提前实例化所以不会出问题。AOT 必须单态化所有可达泛型——自然 fixpoint 终止条件（"没有新类型"）永远不会到达。

**修复**：自引用泛型检测（`IsRecursiveGenericInstantiation`）——检测同一开放类型在自身参数中嵌套 ≥2 次。此方案替代了原先的双重保险（命名空间过滤 + 任意 `MaxGenericNestingDepth=5`），后者会误伤深层但非递归的类型。`System.Text.RegularExpressions.Symbolic` 在 ReachabilityAnalyzer 中作为树摇优化被排除。

**教训**：AOT 泛型单态化需要结构性递归检测，而非任意深度限制。"同一开放类型在自身泛型参数中出现 ≥2 次"是正确的判定谓词。

### Cctor 级联：遵循 ECMA-335 §II.10.5.3.3

**原方案**：`MarkTypeReachable` 无条件 seed 每个可达类型的静态构造函数（cctor）。类型 A 的 cctor 引用类型 B → seed B 的 cctor → 指数爆炸。

**正确模型**：按 ECMA-335 规范，cctor 仅在以下情况触发：
1. 首次静态字段访问（`ldsfld`/`stsfld`/`ldsflda`）
2. 首次静态方法调用（`call`/`callvirt` 对 static 方法）
3. 首次对象构造（`newobj` → `MarkTypeConstructed`）

实例字段访问（`ldfld`/`stfld`）和函数指针加载（`ldftn`/`ldvirtftn`）不触发 cctor。

**教训**：RTA + lazy cctor 是互补的——RTA 减少虚派发扇出，lazy cctor 减少静态初始化传播。

### 字段类型级联：引用类型 vs 值类型

C++ 中引用类型字段是 8 字节指针，只需前置声明，不需要完整类型定义。值类型字段嵌入 struct，需要完整布局信息。

**原方案**：`MarkTypeReachable` 对所有字段类型做完整可达性级联——导致巨大的传递闭包膨胀。

**修复**：`MarkTypeForLayout` ——轻量级可达性，只级联值类型字段，不 seed cctor/finalizer/interface。单独的 `_fullyProcessedTypes` 守卫集允许 layout-only 类型在后续被方法体发现时升级为 fully-reachable。

**教训**：区分"需要结构体布局"和"需要编译方法"是 AOT 树摇的关键优化点。

### HasCctor 必须与可达性同步

实施 lazy cctor 后，某些类型的 cctor 不再被 seed（如 `SpinWait`、`DateTimeOffset`）。但代码生成器仍根据 Cecil 元数据设置 `HasCctor=true`，生成 `_ensure_cctor()` 调用。

**结果**：链接报错——找不到 cctor 函数定义（`C3861`）。

**修复**：`HasCctor = 类型有 cctor 方法 AND 该方法在可达方法集中`。

**教训**：IR 属性必须反映实际编译决策，不能只看元数据。

### AOT 排除命名空间 — 大部分已移除

**原方案**：排除命名空间（`Symbolic`、`ComponentModel`、`Serialization`、`CodeDom`、`Resources`）作为粗粒度修剪，在 ReachabilityAnalyzer + IRBuilder 中双重过滤。

**Phase X 清理**：`FilteredGenericNamespaces` 和大部分命名空间黑名单已移除。仅保留真正 AOT 不兼容的排除项（如 `System.Text.RegularExpressions.Symbolic` 用于防止递归泛型 OOM）。需求驱动泛型发现系统现在自然处理范围，无需黑名单。

**教训**：命名空间排除是临时权宜之计。正确的方案是需求驱动编译（只编译实际调用的内容）。黑名单掩盖了过度发现的问题，而不是修复它。
