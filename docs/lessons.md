# Lessons Learned & Architecture Reflections

This document records important technical pitfalls, wrong turns, and architecture decision reflections encountered during CIL2CPP development.

> [中文版 (Chinese)](lessons.zh-CN.md)

---

## Architecture Decision Reflections

### Custom GC → BoehmGC

**Original approach**: Implement a custom mark-sweep GC requiring shadow stack for stack root tracking, reference bitmaps for heap reference tracking, and manual add_root for global variables.

**Problems**:
- Every generated function needed shadow stack push/pop code insertion
- Value types with nested reference types needed precise layout information
- Reference elements in arrays needed manual marking
- Extremely high implementation cost and error-prone

**Lesson**: BoehmGC's conservative scanning automatically solves all root tracking problems — auto-scans stack, heap, and global area without any code instrumentation. For AOT compilers, a conservative GC is the more pragmatic choice.

### TryEmit* Interceptors → Unity IL2CPP Architecture

**Original approach**: Hand-write C++ implementations for each BCL method (TryEmit* interceptors), detecting BCL method calls during code generation and replacing them with C++ calls.

**Problems**:
- Enormous number of BCL methods, hand-writing is unsustainable
- BCL implementation may change between .NET versions
- High interceptor maintenance cost

**Lesson**: Unity IL2CPP architecture is better — let BCL IL bytecode go through the normal compilation path, keeping hand-written C++ implementations only at the lowest level `[InternalCall]` (~270 icalls). This way BCL updates don't require compiler changes.

### Single-File C++ → Multi-File Splitting

**Original approach**: All generated C++ code in a single `.cpp` file.

**Problems**:
- HelloWorld's full BCL IL compilation resulted in a single .cpp file reaching 140k lines
- MSVC cannot utilize multiple cores when compiling a single large file
- Extremely long compilation times

**Lesson**: Split by IR instruction count into multiple compilation units (`*_methods_N.cpp`), combined with MSVC `/MP` parallel compilation for significant speed improvement. Partition threshold: `MinInstructionsPerPartition = 20000`.

---

## C++ Pitfalls

### `#line default` is Not C/C++ Syntax

`#line default` is a C# preprocessor-specific directive for restoring default line numbers. Using it in C/C++ causes MSVC error C2005.

**Correct approach**: Only use `#line N "file"` format in C/C++; no `#line default` needed — the compiler continues using the last `#line` setting.

### MSVC `GC_NOT_DLL` Linker Error

bdwgc's `gc_config_macros.h` auto-defines `GC_DLL` when it detects MSVC's `_DLL` macro, causing all GC functions to be declared as `__declspec(dllimport)`, resulting in `__imp_GC_malloc` linker errors.

bdwgc's CMakeLists.txt solves this via `add_definitions(-DGC_NOT_DLL)`, but this is directory-scoped — it only applies to bdwgc's own compilation units. **Consumers must define `GC_NOT_DLL` themselves**.

### MSVC Tail-Padding Causing Struct Offset Errors

`Task` was originally designed to inherit from `Object`:

```cpp
struct Object { TypeInfo* __type_info; uint32_t __sync_block; }; // sizeof = 12
struct Task : Object { ... };
```

But MSVC pads `sizeof(Object)` to 16 bytes (8-byte alignment), causing `Task` fields to start at offset 16. Meanwhile, compiler-generated flat structs pack fields tightly at offset 12.

**Fix**: Task doesn't inherit from Object; instead, it inlines `__type_info` + `__sync_block` fields.

### `testing::CaptureStdout()` Conflicts with `SetConsoleOutputCP()`

Google Test's `CaptureStdout()` conflicts with `SetConsoleOutputCP(CP_UTF8)` on Windows, causing output capture failure or garbled output.

**Fix**: Avoid using CaptureStdout in tests that call functions initializing the console.

---

## Mono.Cecil Pitfalls

### `IsClass` Returns True for Structs

Cecil's `TypeDefinition.IsClass` returns true for value types (structs). This is because in ECMA-335, structs are also marked with `class` semantic flags.

**Correct value type check**: Check `BaseTypeName == "System.ValueType"` or `BaseTypeName == "System.Enum"`.

### `ExceptionHandler.HandlerEnd` Can Be Null

When an exception handler is the last block in the method, Cecil's `ExceptionHandler.HandlerEnd` is null (meaning "to end of method").

**Correct usage**: `handler.HandlerEnd?.Offset ?? int.MaxValue`

### Objects Invalidated After AssemblyDefinition Dispose

All Mono.Cecil type/method/field objects are owned by `AssemblyDefinition`. Once `AssemblyReader` is disposed, all Cecil-backed objects become invalid.

**Lesson**: Ensure AssemblyReader stays alive throughout the entire IR build process.

### System.Runtime.dll is a Type Forwarder

`System.Runtime.dll` contains almost no actual type definitions — only `TypeForwardedTo` attributes pointing to `System.Private.CoreLib.dll`. Actual type locations must be resolved through Cecil's `ExportedType`.

---

## IR Build Pitfalls

### `CppNameMapper.GetDefaultValue` Dual-Name Problem

This function originally only matched IL type names (e.g., `System.Int32`), but the code generator actually passes C++ type names (e.g., `int32_t`). Both naming schemes must be handled.

### AddAutoDeclarations Conflicts with Manual `auto`

The code generator's `AddAutoDeclarations` pass detects first-use of `__tN` variables and automatically prepends `auto`. If `auto __t0 = ...` is manually written in `IRRawCpp`, MSVC reports error C3536 (variable declared twice).

**Fix**: Don't manually write `auto` in IRRawCpp; let AddAutoDeclarations handle it uniformly.

### `ldloca` Addresses Need Parentheses

`ldloca` pushes a local variable's address onto the stack, producing `&loc_N`. When subsequently accessing fields with `->`, it must be written as `(&loc_N)->field`, otherwise operator precedence is wrong.

### Generic Name Mangling Trailing Underscore Differences

`MangleTypeName` and `MangleGenericInstanceTypeName` handle generic type names differently:
- `MangleTypeName("List<int>")` → `List_1_System_Int32_` (trailing underscore because `>` → `_`)
- `MangleGenericInstanceTypeName("List<int>")` → `List_1_System_Int32` (no trailing underscore)

Be careful which version to use when used as dictionary keys.

---

## Exception Handling Pitfalls

### try-finally Swallowing Exceptions

In the original implementation, the `CIL2CPP_END_TRY` macro didn't re-throw uncaught exceptions after the finally block executed.

**Fix**: Added `__exc_caught` flag. Set to true in `CIL2CPP_CATCH` / `CIL2CPP_CATCH_ALL`. `END_TRY` checks: if `__pending && !__exc_caught`, call `throw_exception(__pending)` to re-throw.

### `leave` Compiled as `goto` Skips finally

IL's `leave` instruction should execute finally when leaving a try block. But compiling to C++ `goto` directly skips the finally block.

**Fix**: The compiler collects all try-finally regions. When `leave`'s target crosses a try-finally boundary (offset within TryStart..TryEnd, target >= TryEnd), no goto is generated — execution naturally falls into CIL2CPP_FINALLY.

### skipDeadCode Not Reset at Handler Entries

After `throw`/`ret`/`br`/`leave`, `skipDeadCode = true` is set to skip subsequent unreachable code. But exception handler entries (CatchBegin/FinallyBegin etc.) are reachable (reached via runtime dispatch), not branch targets.

**Fix**: Reset `skipDeadCode = false` at CatchBegin / FinallyBegin / FilterBegin / FilterHandlerBegin.

### throw_exception Must Skip Active Handlers

`throw_exception` traversing the ExceptionContext chain must skip entries with state != 0 (catch=1, finally=2). Otherwise, throwing from within a catch/finally block would match the currently executing handler, causing an infinite loop.

---

## Async Pitfalls

### Debug State Machines are Classes, Not Structs

The C# compiler generates async state machines as classes (BaseType = System.Object) in Debug mode, only as structs in Release mode. Don't assert IsValueType.

### Task\<T\> Allocation Size Insufficient

`task_create_pending()` only allocates `sizeof(Task)` of memory, not including the generic `f_result` field. For `Task<T>`, `gc::alloc(sizeof(Task_T), nullptr)` + `task_init_pending()` must be used to allocate sufficient memory.

### Flat Structs Don't Inherit Runtime Base Classes

Compiler-generated flat structs (e.g., `Task_1_System_Int32`) don't inherit the runtime's `Task` base class — they're only field-layout-compatible. Therefore `static_cast<Task*>` cannot be used; `reinterpret_cast<cil2cpp::Task*>` is required.

---

## BCL Integration Lessons

### BCL Generic Type Method Body Location

BCL generic types (Nullable\<T\>, ValueTuple, etc.) have method bodies in `System.Runtime.dll`, not in the user assembly. During generic monomorphization (`CreateGenericSpecializations`), these methods' IL body conversion must be skipped, with calls intercepted in `EmitMethodCall` / `EmitNewObj` instead.

### ICall Overload Conflicts

`RegisterICall` matches by `type::method/paramCount`, unable to distinguish overloads with the same parameter count but different parameter types (e.g., `String.Equals(String, StringComparison)` vs `Equals(String, String)`).

**Fix**: Use `RegisterICallTyped` with `firstParamType` specified for disambiguation.

### ICall void* for this

Runtime icall `__this` parameters must use `void*` rather than `Object*`, because compiler-generated flat structs don't inherit from `cil2cpp::Object`.

---

## Tree-Shaking & OOM Lessons

### Budget Caps Are the Wrong Solution

**Original approach**: IRBuilder's three fixpoint loops (Pass 0.5/3.5/3.6) had a 50-iteration safety cap to prevent infinite loops and OOM.

**Problems**:
- Caps merely masked the problem without addressing root cause
- Silent truncation could produce incorrect compilation (missing types/methods)
- Or the cap was too high to prevent OOM anyway

**Lesson**: Find and fix the divergence root cause, then rely on natural termination conditions (set exhaustion). A budget cap is technical debt — always find the real cause.

### Diagnosing OOM: Add Logging First, Don't Guess

**Context**: NuGetSimpleTest (2 Newtonsoft.Json calls) consumed 24GB RAM and produced 0 output. Initial hypothesis was cctor cascade explosion.

**Wrong turn**: Implemented lazy cctor seeding first — types only dropped from 3,411 to 3,356. Hypothesis was wrong.

**Correct approach**: Added diagnostic prints to the Pass 0.5 fixpoint loop, revealing +100/+65/+50 new generic types per iteration, never converging. Printing the type names immediately identified `System.Text.RegularExpressions.Symbolic` recursive nesting.

**Lesson**: Don't guess at OOM causes. Add logging to identify which loop is diverging and what data it's producing. Data beats hypotheses.

### Infinite Recursive Generic Nesting Is an Inherent AOT Risk

Certain BCL types (`Symbolic.BDD`, `DerivativeEffect`, `SymbolicRegexNode`) have generic parameters that reference each other, creating infinitely nested instantiations:

```
KVP<BDD>> → KVP<KVP<BDD>>> → KVP<KVP<KVP<BDD>>>> → ...
```

JIT doesn't need ahead-of-time instantiation so this never surfaces. AOT must monomorphize all reachable generics — natural fixpoint termination ("no new types") never arrives.

**Fix**: Dual protection — (a) namespace filtering for known-problematic namespaces, (b) `MaxGenericNestingDepth=5` structural limit in `CollectGenericType`.

**Lesson**: AOT generic monomorphization requires a depth limit. "Set stops growing" is insufficient as a sole termination condition.

### Cctor Cascade: Follow ECMA-335 §II.10.5.3.3

**Original approach**: `MarkTypeReachable` unconditionally seeded the static constructor (cctor) for every reachable type. Type A's cctor references Type B → seeds B's cctor → exponential explosion.

**Correct model**: Per ECMA-335, cctor triggers only at:
1. First static field access (`ldsfld`/`stsfld`/`ldsflda`)
2. First static method invocation (`call`/`callvirt` on static method)
3. First object construction (`newobj` → `MarkTypeConstructed`)

Instance field access (`ldfld`/`stfld`) and function pointer loading (`ldftn`/`ldvirtftn`) do NOT trigger cctor.

**Lesson**: RTA + lazy cctor are complementary — RTA reduces virtual dispatch fan-out, lazy cctor reduces static initialization propagation.

### Field Type Cascade: Reference Types vs Value Types

In C++, reference-type fields are 8-byte pointers requiring only a forward declaration, not a full type definition. Value-type fields are embedded in the struct and need complete layout information.

**Original approach**: `MarkTypeReachable` cascaded full reachability to ALL field types — causing massive transitive closure expansion.

**Fix**: `MarkTypeForLayout` — lightweight reachability that only cascades value-type fields, without seeding cctor/finalizer/interfaces. A separate `_fullyProcessedTypes` guard set allows layout-only types to be upgraded to fully-reachable when later discovered through method bodies.

**Lesson**: Distinguishing "needs struct layout" from "needs methods compiled" is a critical AOT tree-shaking optimization.

### HasCctor Must Be Synchronized with Reachability

After implementing lazy cctor, some types' cctors were no longer seeded (e.g., `SpinWait`, `DateTimeOffset`). But the code generator still set `HasCctor=true` based on Cecil metadata, emitting `_ensure_cctor()` calls.

**Result**: Linker error — cctor function definition not found (`C3861`).

**Fix**: `HasCctor = type has cctor method AND that method is in the reachable methods set`.

**Lesson**: IR attributes must reflect actual compilation decisions, not just metadata presence.

### AOT-Excluded Namespaces Are Effective Coarse-Grained Pruning

Excluded namespaces: `Symbolic`, `ComponentModel`, `Serialization`, `CodeDom`, `Resources`. These are meaningless in AOT scenarios (reflection-based serialization, dynamic code, designers).

Dual filtering: `ReachabilityAnalyzer.IsAotExcludedNamespace` + `IRBuilder.FilteredGenericNamespaces`.

**Lesson**: Namespace exclusion is effective but requires clear AOT-incompatibility justification for each entry. Don't exclude blindly.
