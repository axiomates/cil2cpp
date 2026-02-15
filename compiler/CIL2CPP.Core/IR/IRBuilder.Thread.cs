using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// System.Threading.Thread method interception.
/// Thread is a reference type whose methods are not in user assemblies.
/// We intercept calls and emit inline C++ delegating to runtime functions.
/// Also handles Thread.MemoryBarrier.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Create synthetic IRType for System.Threading.Thread.
    /// Thread is a reference type (not value type) with runtime backing.
    /// </summary>
    private void CreateThreadSyntheticType()
    {
        if (!_typeCache.ContainsKey("System.Threading.Thread"))
        {
            var threadType = new IRType
            {
                ILFullName = "System.Threading.Thread",
                CppName = "System_Threading_Thread",
                Name = "Thread",
                Namespace = "System.Threading",
                IsValueType = false,
                IsSealed = true,
                IsRuntimeProvided = true,
            };
            _module.Types.Add(threadType);
            _typeCache["System.Threading.Thread"] = threadType;
        }
    }

    /// <summary>
    /// Handle calls to System.Threading.Thread methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitThreadCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (methodRef.DeclaringType.FullName != "System.Threading.Thread") return false;

        switch (methodRef.Name)
        {
            case ".ctor":
            {
                // Thread(ThreadStart start) — instance method via ldloca+call pattern
                // Stack: [thisAddr, delegate]
                var startDelegate = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{{ auto __tmp_thread = cil2cpp::thread::create(" +
                           $"reinterpret_cast<cil2cpp::Delegate*>({startDelegate})); " +
                           $"std::memcpy({thisArg}, __tmp_thread, sizeof(cil2cpp::ManagedThread)); }}"
                });
                return true;
            }
            case "Start":
            {
                // void Start() — instance method
                // Stack: [thisRef]
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::thread::start(reinterpret_cast<cil2cpp::ManagedThread*>({thisArg}));"
                });
                return true;
            }
            case "Join":
            {
                if (methodRef.Parameters.Count == 0)
                {
                    // void Join() — instance method
                    var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"cil2cpp::thread::join(reinterpret_cast<cil2cpp::ManagedThread*>({thisArg}));"
                    });
                }
                else
                {
                    // bool Join(int) — instance method with timeout
                    var timeout = stack.Count > 0 ? stack.Pop() : "0";
                    var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = cil2cpp::thread::join_timeout(" +
                               $"reinterpret_cast<cil2cpp::ManagedThread*>({thisArg}), {timeout});"
                    });
                    stack.Push(tmp);
                }
                return true;
            }
            case "get_IsAlive":
            {
                // bool IsAlive { get; } — instance property
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::thread::is_alive(" +
                           $"reinterpret_cast<cil2cpp::ManagedThread*>({thisArg}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_ManagedThreadId":
            {
                // int ManagedThreadId { get; } — instance property
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::thread::get_managed_id(" +
                           $"reinterpret_cast<cil2cpp::ManagedThread*>({thisArg}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "Sleep":
            {
                // static void Sleep(int) — handled by ICallRegistry, but intercept here too
                var ms = stack.Count > 0 ? stack.Pop() : "0";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::thread::sleep({ms});"
                });
                return true;
            }
            case "MemoryBarrier":
            {
                // static void MemoryBarrier() — full memory fence
                block.Instructions.Add(new IRRawCpp
                {
                    Code = "std::atomic_thread_fence(std::memory_order_seq_cst);"
                });
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Handle newobj for System.Threading.Thread.
    /// Returns true if handled.
    /// </summary>
    private bool TryEmitThreadNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (ctorRef.DeclaringType.FullName != "System.Threading.Thread") return false;

        var tmp = $"__t{tempCounter++}";

        if (ctorRef.Parameters.Count >= 1)
        {
            // Thread(ThreadStart start) — most common
            var startDelegate = stack.Count > 0 ? stack.Pop() : "nullptr";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                       $"cil2cpp::thread::create(reinterpret_cast<cil2cpp::Delegate*>({startDelegate})));"
            });
        }
        else
        {
            // Default ctor — should not happen for Thread, but handle gracefully
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                       $"cil2cpp::thread::create(nullptr));"
            });
        }
        stack.Push(tmp);
        return true;
    }
}
