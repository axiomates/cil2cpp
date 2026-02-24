# Phase V.1: Task BCL IL 依赖链分析

> 日期：2026-02-25
>
> 状态：**分析完成**（纯研究，不修改代码）

## 1. 目标

分析 .NET 8 BCL `System.Threading.Tasks.Task` 方法体引用的所有类型和依赖链，确认从 IL 编译的可行范围，为 Phase V.2（Task 架构重构）提供决策依据。

---

## 2. 当前架构

### 2.1 RuntimeProvided 异步类型（6 个）

| 类型 | 定义位置 | 字段 |
|------|---------|------|
| `Task` | `task.h` | 4 自定义 + 7 BCL 字段 |
| `TaskAwaiter` | `task.h` | `f_task` (Task*) |
| `AsyncTaskMethodBuilder` | `task.h` | `f_task` (Task*) |
| `ValueTask` | `async_enumerable.h` | `f_task` (Task*) |
| `ValueTaskAwaiter` | `async_enumerable.h` | `f_task` (Task*) |
| `AsyncIteratorMethodBuilder` | `async_enumerable.h` | `f_dummy` (int) |

### 2.2 Task 结构体布局（runtime task.h）

```
Offset  Size  字段                       来源
0       8     __type_info                Object 头
8       4     __sync_block               Object 头
12      4     f_status                   ★ 运行时自定义（0=created, 1=completed, 2=faulted）
16      8     f_exception                ★ 运行时自定义
24      8     f_continuations            ★ 运行时自定义（链表）
32      8     f_lock                     ★ 运行时自定义（std::mutex*）
40      4     f_m_taskId                 BCL 字段
44      8     f_m_action                 BCL 字段
52      8     f_m_stateObject            BCL 字段
60      8     f_m_taskScheduler          BCL 字段
68      4     f_m_stateFlags             BCL 字段（volatile）
72      8     f_m_continuationObject     BCL 字段
80      8     f_m_contingentProperties   BCL 字段
Total: ~88 bytes
```

### 2.3 Generated Task\<T\> 布局（从 BCL IL 编译）

```
Offset  Size  字段                       来源
0       8     __type_info                Object 头
8       4     __sync_block               Object 头
12      4     f_m_taskId                 ← BCL 字段直接从 offset 12 开始！
16      8     f_m_action
24      8     f_m_stateObject
32      8     f_m_taskScheduler
40      4     f_m_stateFlags
44      8     f_m_continuationObject
52      8     f_m_contingentProperties
60      T     f_m_result                 ← 泛型特化字段
```

**关键差异**：运行时 Task 在 offset 12-39 放 4 个自定义字段，Generated Task\<T\> 在 offset 12 直接放 BCL 字段。两者不兼容。

### 2.4 运行时 C++ 基础设施

| 函数 | 用途 |
|------|------|
| `task_create_pending()` | 分配 Task + mutex |
| `task_create_completed()` | 分配已完成 Task |
| `task_complete(Task*)` | 线程安全完成 + 执行 continuation |
| `task_fault(Task*, Exception*)` | 故障 + 执行 continuation |
| `task_add_continuation(Task*, callback, state)` | 注册 continuation 回调 |
| `task_wait(Task*)` | 自旋等待完成 |
| `task_when_all/when_any/delay/run` | 组合器 |

这些函数操作 f_status、f_continuations、f_lock — 全部是自定义字段。

---

## 3. BCL Task 方法编译现状

### 3.1 已编译的 Task 方法（~66 个）

大部分属性 getter/setter 和内部逻辑成功从 BCL IL 编译：

- **状态查询**：get_Id, get_IsCompleted, get_IsFaulted, get_IsCanceled, get_IsCompletedSuccessfully, get_Options, IsCompletedMethod
- **原子操作**：AtomicStateUpdate, AtomicStateUpdateSlow, MarkStarted
- **生命周期**：Finish, FinishSlow, FinishStageThree, FinishContinuations
- **内部逻辑**：InnerInvoke, HandleException, InternalCancel, ProcessChildCompletion, AddException, ThrowAsync
- **辅助**：ContingentProperties 全部方法、TaskExceptionHolder 核心方法、TaskScheduler getter
- **泛型特化**：Task\<int\>, Task\<bool\>, Task\<VoidTaskResult\> 的 ctor/InnerInvoke/cctor

### 3.2 Stubbed 的 Task 方法（~65 个去重）

**按依赖链分组：**

#### Chain 1: ThreadPool（阻塞 Task.ScheduleAndStart）

```
Task.ScheduleAndStart
  → TaskScheduler.InternalQueueTask
    → TaskScheduler.QueueTask (abstract → ThreadPoolTaskScheduler)
      → ThreadPool.RequestWorkerThread
        → PortableThreadPool.RequestWorker [UndeclaredFunction]

ThreadPool.QueueUserWorkItem [UndeclaredFunction]
ThreadPoolWorkQueue.Enqueue / Dispatch [RenderedBodyError]
PortableThreadPool: 5 个 UndeclaredFunction
  - GetOrCreateThreadLocalCompletionCountObject
  - NotifyWorkItemProgress / NotifyWorkItemComplete
  - RequestWorker / ReportThreadStatus
```

**~12 个 ThreadPool 方法**全部 stubbed。根因：PortableThreadPool 是泛型特化缺失（IRBuilder 未创建特化类型）。

#### Chain 2: TplEventSource/EventSource（阻塞 Task.FinishStageTwo, RunContinuations）

```
Task.FinishStageTwo → TplEventSource.TaskCompleted
Task.RunContinuations → TplEventSource.RunningContinuationList
Task.FireTaskScheduledIfNeeded → TplEventSource.TaskScheduled

TplEventSource 全部 16 个方法 stubbed
根因：EventSource 依赖 CLR 内部类型
  - 32 个 EventSource.IsEnabled 调用 [UndeclaredFunction]
  - EventSource._ctor [UndeclaredFunction]
```

#### Chain 3: ExecutionContext（阻塞 Task.ExecuteWithThreadLocal）

```
Task.ExecuteWithThreadLocal
  → ExecutionContext.RunForThreadPoolUnsafe [RenderedBodyError]
  → ExecutionContext.SetLocalValue [RenderedBodyError]
```

2 个方法，RenderedBodyError 类型，可能可修复。

#### Chain 4: AwaitTaskContinuation

```
AwaitTaskContinuation.Run [RenderedBodyError]
AwaitTaskContinuation.RunOrScheduleAction [RenderedBodyError]
AwaitTaskContinuation.UnsafeScheduleAction [RenderedBodyError]
ContinueWithTaskContinuation.Run [RenderedBodyError]
```

4 个方法，依赖 ThreadPool 和 ExecutionContext。

#### 其他 stubbed 方法

- Task 核心 13 个：CancellationCleanupLogic, ExecuteWithThreadLocal, FinishStageTwo, FireTaskScheduledIfNeeded, LogFinishCompletionNotification, NewId, RunContinuations, RunOrQueueCompletionAction, ScheduleAndStart + 4 个 AwaitTaskContinuation
- TaskScheduler 4 个：InternalQueueTask, QueueTask, TryExecuteTaskInline, TryRunInline
- TaskExceptionHolder 2 个：AddFaultException [UndeclaredFunction], CreateExceptionObject [RenderedBodyError]
- 泛型集合 6 个：ArraySortHelper\<Task\>, List\<Task\>.RemoveAll 等

### 3.3 Stub 根因分布

| 根因 | 方法数 | 性质 |
|------|--------|------|
| TplEventSource/EventSource 依赖 | ~22 | CLR 内部类型，纯诊断 |
| ThreadPool/PortableThreadPool | ~15 | 泛型特化缺失 + RenderedBodyError |
| RenderedBodyError（编译器 bug） | ~18 | 可修复 |
| UndeclaredFunction（泛型缺失） | ~5 | 需 IRBuilder 修复 |
| 泛型集合（Task 参数化） | ~5 | 间接依赖 |

---

## 4. 可行性评估

### 方案 A：保持当前混合模型（推荐）

**优点**：
- 当前 async/await 完全可用（HelloWorld、集成测试全部通过）
- 手优化的 continuation 系统性能可控
- 无风险

**缺点**：
- 6 个 RuntimeProvided 类型
- 自定义运行时字段与 BCL 字段布局不兼容
- BCL 高层 Task 方法（ScheduleAndStart 等）无法从 IL 编译

### 方案 B：完整 BCL Task 迁移

**所需工作**：

| # | 工作项 | 复杂度 | 说明 |
|---|--------|--------|------|
| B.1 | TplEventSource → no-op ICall | 低 | ~16 个空函数注册为 ICall，纯诊断可安全跳过 |
| B.2 | EventSource.IsEnabled → ICall | 低 | return false，解锁 32 个调用点 |
| B.3 | ThreadPool ICall 层 | 中 | ~10 个 icall（QueueUserWorkItem, RequestWorker 等），映射到 cil2cpp 线程池 |
| B.4 | ExecutionContext 修复 | 低 | 2 个 RenderedBodyError，可能是指针类型问题 |
| B.5 | Task struct 字段布局重设计 | **高** | 删除 4 个自定义字段，改用 BCL 原生字段 |
| B.6 | Continuation 系统重写 | **高** | 从链表回调改为 BCL 的 m_continuationObject 机制 |
| B.7 | 线程安全重写 | **高** | 从 std::mutex* 改为 BCL 的 m_stateFlags 原子操作 |
| B.8 | Task\<T\> 布局对齐 | 中 | 确保 Generated Task\<T\> 和运行时 Task 布局一致 |
| B.9 | 回归测试 | 中 | 所有 23 个 async 测试 + 35 个集成测试 |

**风险**：B.5-B.7 是核心架构变更，失败会导致所有 async 功能 broken。

**预估工期**：2-4 周（假设编译器质量足够）。

### 方案 C：渐进式迁移（折中）

分步推进，每步独立验证：

1. **C.1: TplEventSource no-op**（低风险）
   - 注册 ~18 个 no-op ICall
   - 解锁 16 个 TplEventSource 方法 + 32 个 EventSource.IsEnabled 调用点
   - 预计 Task.FinishStageTwo、Task.RunContinuations 解锁
   - **收益**：~22 方法从 stub → compiled，不改变运行时行为

2. **C.2: ExecutionContext RenderedBodyError 修复**（低风险）
   - 修复 2 个编译器 bug
   - 解锁 Task.ExecuteWithThreadLocal
   - **收益**：~3 方法

3. **C.3: ThreadPool ICall**（中风险）
   - 将 BCL ThreadPool 的 ~10 个方法映射到 cil2cpp 线程池
   - 解锁 Task.ScheduleAndStart 及整个调度链
   - **收益**：~15 方法
   - **前提**：需要 PortableThreadPool 泛型特化（IRBuilder 修复）

4. **C.4: Task struct 重构**（高风险，视 C.1-C.3 结果决定）
   - 这一步可以推迟或跳过
   - 如果 C.1-C.3 已让关键 Task 方法编译，可能不需要

---

## 5. 结论与建议

### 5.1 核心发现

1. **66 个 Task 方法已从 BCL IL 成功编译**，包括所有状态查询和大部分内部逻辑
2. **65 个 Task 方法 stubbed**，三条主要依赖链：ThreadPool（15）、TplEventSource（22）、RenderedBodyError（18）
3. **TplEventSource 是最大的 stub 源**（22 个方法），但它纯粹是诊断用途，no-op 化零风险
4. **字段布局不兼容**是核心架构障碍——运行时 Task 有 4 个自定义字段（28 bytes），Generated Task\<T\> 没有
5. **当前 async/await 功能完整**——stubbed 方法不影响运行（运行时通过 task_complete/task_add_continuation 绕过 BCL 调度）

### 5.2 推荐路径

**短期（Phase V.1.1）**：方案 C.1 — TplEventSource no-op ICall
- 零风险，解锁 ~22 个 stub
- 可与 Phase III RenderedBodyError 修复并行

**中期（Phase V.1.2）**：方案 C.2 + C.3 — ExecutionContext + ThreadPool ICall
- 需 RenderedBodyError < 150（当前 249）和 IRBuilder 泛型特化修复
- 解锁 ~18 个 stub

**长期（Phase V.2+）**：视 C.1-C.3 结果评估 Task struct 重构必要性
- 如果关键 Task 方法已编译，可能无需重构
- 如果需要进一步减少 RuntimeProvided 数量（32 → 25），则需要 B.5-B.7

### 5.3 Phase V 依赖关系更新

```
Phase III (RenderedBodyError < 150)
       ↓
V.1.1: TplEventSource no-op ← 可立即开始
       ↓
V.1.2: ExecutionContext fix + ThreadPool ICall ← 需 III + IRBuilder fix
       ↓
V.2: Task struct 重构 ← 需 V.1.2 验证
       ↓
V.3-V.5: TaskAwaiter/ValueTask/CTS → IL ← 需 V.2
```

---

## 附录 A: Stubbed 方法完整列表

### Task 核心（13 个 — MissingBody + RenderedBodyError）
- CancellationCleanupLogic, ExecuteWithThreadLocal, FinishStageTwo
- FireTaskScheduledIfNeeded, LogFinishCompletionNotification, NewId
- RunContinuations, RunOrQueueCompletionAction, ScheduleAndStart
- AwaitTaskContinuation: Run, RunOrScheduleAction, UnsafeScheduleAction
- ContinueWithTaskContinuation: Run

### TaskScheduler（4 个）
- InternalQueueTask, QueueTask, TryExecuteTaskInline, TryRunInline

### TplEventSource（16 个 — EventSource 依赖）
- .ctor, AwaitTaskContinuationScheduled, DebugFacilityMessage, DebugFacilityMessage1
- NewID, OnEventCommand, RunningContinuationList, SetActivityId
- TaskCompleted, TaskScheduled, TaskStarted
- TraceOperationBegin, TraceOperationEnd, TraceOperationRelation
- TraceSynchronousWorkBegin, TraceSynchronousWorkEnd

### ThreadPool（12 个）
- get_EnableWorkerTracking, GetNextConfigUInt32Value
- GetOrCreateThreadLocalCompletionCountObject
- NotifyWorkItemComplete, NotifyWorkItemProgress
- QueueUserWorkItem, ReportThreadStatus, RequestWorkerThread
- ThreadPoolWorkQueue: Dispatch, Enqueue, RefreshLoggingEnabled, RefreshLoggingEnabledFull

### ExecutionContext（2 个）
- RunForThreadPoolUnsafe, SetLocalValue

### 其他
- TaskExceptionHolder: AddFaultException, CreateExceptionObject
- TaskScheduler: PublishUnobservedTaskException
- WindowsThreadPool: RequestWorkerThread
- IThreadPoolWorkItem: Execute
- 泛型集合 Task 参数化: ArraySortHelper\<Task\>×4, List\<Task\>.RemoveAll, EqualityComparer\<Task\>.Equals 等

## 附录 B: 已编译方法摘要（~66 个）

Task 属性/状态: get_Id, get_IsCompleted, get_IsFaulted, get_IsCanceled, get_Options, get_CreationOptions, get_CompletedTask, get_InternalCurrent, get_CapturedContext, get_ExceptionRecorded, get_IsExceptionObservedByParent, get_IsDelegateInvoked, get_ExecutingTaskScheduler, get_IsCompletedSuccessfully, get_IsCancellationRequested, get_IsCancellationAcknowledged, IsCompletedMethod, OptionsMethod

Task 核心逻辑: AtomicStateUpdate, AtomicStateUpdateSlow, MarkStarted, EnsureContingentPropertiesInitialized, Finish, FinishSlow, FinishStageThree, FinishContinuations, InnerInvoke, HandleException, InternalCancel, InternalCancelContinueWithInitialState, RecordInternalCancellationRequest, SetCancellationAcknowledged, ProcessChildCompletion, NotifyParentIfPotentiallyAttachedTask, AddException(×2), AddExceptionsFromChildren, ExecuteFromThreadPool, ExecuteEntryUnsafe, ExecuteEntryCancellationRequestedOrCanceled, ThrowAsync, Dispose(×2), AddToActiveTasks, RemoveFromActiveTasks

辅助类型: ContingentProperties(.ctor, SetCompleted, SetEvent, UnregisterCancellationCallback), TaskExceptionHolder(.ctor, Finalize, Add, get_ContainsFaultList, SetCancellationException, MarkAsHandled, MarkAsUnhandled), TaskScheduler(.ctor, _cctor, get_Default, get_Current, get_InternalCurrent, get_Id, TryDequeue, NotifyWorkItemProgress, AddToActiveTaskSchedulers, PublishUnobservedTaskException), TaskCache, TaskFactory, TaskContinuation

泛型特化: Task\<int\>, Task\<bool\>, Task\<VoidTaskResult\> — .ctor, InnerInvoke, _cctor
