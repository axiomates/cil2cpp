using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private void EmitStoreLocal(IRBasicBlock block, Stack<string> stack, IRMethod method, int index)
    {
        var val = stack.Count > 0 ? stack.Pop() : "0";
        block.Instructions.Add(new IRAssign
        {
            Target = GetLocalName(method, index),
            Value = val,
        });
    }

    private void EmitBinaryOp(IRBasicBlock block, Stack<string> stack, string op, ref int tempCounter)
    {
        var right = stack.Count > 0 ? stack.Pop() : "0";
        var left = stack.Count > 0 ? stack.Pop() : "0";
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRBinaryOp
        {
            Left = left, Right = right, Op = op, ResultVar = tmp
        });
        stack.Push(tmp);
    }

    private void EmitComparisonBranch(IRBasicBlock block, Stack<string> stack, string op, ILInstruction instr)
    {
        var right = stack.Count > 0 ? stack.Pop() : "0";
        var left = stack.Count > 0 ? stack.Pop() : "0";
        var target = (Instruction)instr.Operand!;
        block.Instructions.Add(new IRConditionalBranch
        {
            Condition = $"{left} {op} {right}",
            TrueLabel = $"IL_{target.Offset:X4}"
        });
    }

    private void EmitMethodCall(IRBasicBlock block, Stack<string> stack, MethodReference methodRef,
        bool isVirtual, ref int tempCounter)
    {
        // Special: Nullable<T> methods — emit inline C++ instead of unresolved BCL calls
        if (TryEmitNullableCall(block, stack, methodRef, ref tempCounter))
            return;

        // Special: ValueTuple methods — emit inline C++ instead of unresolved BCL calls
        if (TryEmitValueTupleCall(block, stack, methodRef, ref tempCounter))
            return;

        // Special: Delegate.Invoke — emit IRDelegateInvoke instead of normal call
        var declaringCacheKey = ResolveCacheKey(methodRef.DeclaringType);
        if (methodRef.Name == "Invoke" && methodRef.HasThis
            && _typeCache.TryGetValue(declaringCacheKey, out var invokeType)
            && invokeType.IsDelegate)
        {
            var invokeArgs = new List<string>();
            for (int i = 0; i < methodRef.Parameters.Count; i++)
                invokeArgs.Add(stack.Count > 0 ? stack.Pop() : "0");
            invokeArgs.Reverse();

            var delegateExpr = stack.Count > 0 ? stack.Pop() : "nullptr";

            var invoke = new IRDelegateInvoke
            {
                DelegateExpr = delegateExpr,
                ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(methodRef.ReturnType.FullName),
            };
            foreach (var p in methodRef.Parameters)
                invoke.ParamTypes.Add(CppNameMapper.GetCppTypeForDecl(p.ParameterType.FullName));
            invoke.Arguments.AddRange(invokeArgs);

            if (methodRef.ReturnType.FullName != "System.Void")
            {
                var tmp = $"__t{tempCounter++}";
                invoke.ResultVar = tmp;
                stack.Push(tmp);
            }
            block.Instructions.Add(invoke);
            return;
        }

        // Special: RuntimeHelpers.InitializeArray(Array, RuntimeFieldHandle)
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && methodRef.Name == "InitializeArray")
        {
            var fieldHandle = stack.Count > 0 ? stack.Pop() : "0";
            var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"std::memcpy(cil2cpp::array_data({arr}), {fieldHandle}, sizeof({fieldHandle}));"
            });
            return;
        }

        var irCall = new IRCall();

        // Map known BCL methods (hardcoded priority mappings)
        var mappedName = MapBclMethod(methodRef);

        // Fallback: ICall registry for [InternalCall] methods
        if (mappedName == null)
        {
            var resolved = TryResolveMethodRef(methodRef);
            if (resolved != null && (resolved.ImplAttributes & MethodImplAttributes.InternalCall) != 0)
            {
                mappedName = ICallRegistry.Lookup(
                    methodRef.DeclaringType.FullName,
                    methodRef.Name,
                    methodRef.Parameters.Count);
            }
        }

        if (mappedName != null)
        {
            irCall.FunctionName = mappedName;
        }
        else if (methodRef is GenericInstanceMethod gim)
        {
            // Generic method instantiation — use the monomorphized name
            var elemMethod = gim.ElementMethod;
            var declType = elemMethod.DeclaringType.FullName;
            var typeArgs = gim.GenericArguments.Select(a => a.FullName).ToList();
            var key = MakeGenericMethodKey(declType, elemMethod.Name, typeArgs);

            if (_genericMethodInstantiations.TryGetValue(key, out var gmInfo))
            {
                irCall.FunctionName = gmInfo.MangledName;
            }
            else
            {
                // Fallback: use the same mangling logic as CollectGenericMethod
                irCall.FunctionName = MangleGenericMethodName(declType, elemMethod.Name, typeArgs);
            }
        }
        else
        {
            var typeCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
            irCall.FunctionName = CppNameMapper.MangleMethodName(typeCpp, methodRef.Name);
        }

        // Collect arguments (in reverse order from stack)
        var args = new List<string>();
        for (int i = 0; i < methodRef.Parameters.Count; i++)
        {
            args.Add(stack.Count > 0 ? stack.Pop() : "0");
        }
        args.Reverse();

        // 'this' for instance methods
        if (methodRef.HasThis)
        {
            var thisArg = stack.Count > 0 ? stack.Pop() : "__this";
            irCall.Arguments.Add(thisArg);
        }

        irCall.Arguments.AddRange(args);

        // For virtual BCL methods on System.Object (ToString, Equals, GetHashCode),
        // prefer vtable dispatch so user overrides are called correctly
        if (mappedName != null && isVirtual && methodRef.HasThis
            && methodRef.DeclaringType.FullName == "System.Object"
            && methodRef.Name is "ToString" or "Equals" or "GetHashCode")
        {
            mappedName = null;
        }

        // Virtual dispatch detection
        if (isVirtual && methodRef.HasThis && mappedName == null)
        {
            var declaringTypeName = declaringCacheKey;
            var resolved = _typeCache.GetValueOrDefault(declaringTypeName);

            if (resolved != null && resolved.IsInterface)
            {
                // Interface dispatch — find slot by name (skipping constructors)
                int ifaceSlot = 0;
                bool found = false;
                foreach (var m in resolved.Methods)
                {
                    if (m.IsConstructor || m.IsStaticConstructor) continue;
                    if (m.Name == methodRef.Name && m.Parameters.Count == methodRef.Parameters.Count) { found = true; break; }
                    ifaceSlot++;
                }
                if (found)
                {
                    irCall.IsVirtual = true;
                    irCall.IsInterfaceCall = true;
                    irCall.InterfaceTypeCppName = resolved.CppName;
                    irCall.VTableSlot = ifaceSlot;
                    irCall.VTableReturnType = CppNameMapper.GetCppTypeForDecl(methodRef.ReturnType.FullName);
                    irCall.VTableParamTypes = BuildVTableParamTypes(methodRef);
                }
            }
            else if (resolved != null && !resolved.IsValueType)
            {
                // Class virtual dispatch
                var entry = resolved.VTable.FirstOrDefault(e => e.MethodName == methodRef.Name
                    && (e.Method == null || e.Method.Parameters.Count == methodRef.Parameters.Count));
                if (entry != null)
                {
                    irCall.IsVirtual = true;
                    irCall.VTableSlot = entry.Slot;
                    irCall.VTableReturnType = CppNameMapper.GetCppTypeForDecl(methodRef.ReturnType.FullName);
                    irCall.VTableParamTypes = BuildVTableParamTypes(methodRef);
                }
            }
            else if (resolved == null && declaringTypeName == "System.Object")
            {
                // System.Object is not in _typeCache but has well-known vtable slots
                var slot = methodRef.Name switch
                {
                    "ToString" => ObjectVTableSlots.ToStringSlot,
                    "Equals" => ObjectVTableSlots.EqualsSlot,
                    "GetHashCode" => ObjectVTableSlots.GetHashCodeSlot,
                    _ => -1
                };
                if (slot >= 0)
                {
                    irCall.IsVirtual = true;
                    irCall.VTableSlot = slot;
                    irCall.VTableReturnType = CppNameMapper.GetCppTypeForDecl(methodRef.ReturnType.FullName);
                    irCall.VTableParamTypes = BuildVTableParamTypes(methodRef);
                }
            }
        }

        // Return value
        if (methodRef.ReturnType.FullName != "System.Void")
        {
            var tmp = $"__t{tempCounter++}";
            irCall.ResultVar = tmp;
            stack.Push(tmp);
        }
        block.Instructions.Add(irCall);
    }

    private void EmitNewObj(IRBasicBlock block, Stack<string> stack, MethodReference ctorRef,
        ref int tempCounter)
    {
        // Special: Nullable<T> constructor (newobj pattern — rare, usually ldloca+call)
        if (TryEmitNullableNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: ValueTuple constructor (newobj pattern — value type, can't use gc::alloc)
        if (TryEmitValueTupleNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        var cacheKey = ResolveCacheKey(ctorRef.DeclaringType);
        var typeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        // Detect delegate constructor: base is MulticastDelegate/Delegate, ctor(object, IntPtr)
        if (ctorRef.Parameters.Count == 2
            && _typeCache.TryGetValue(cacheKey, out var delegateType)
            && delegateType.IsDelegate)
        {
            // Stack has: [target (object), functionPtr (IntPtr)]
            var fptr = stack.Count > 0 ? stack.Pop() : "nullptr";
            var target = stack.Count > 0 ? stack.Pop() : "nullptr";
            block.Instructions.Add(new IRDelegateCreate
            {
                DelegateTypeCppName = typeCpp,
                TargetExpr = target,
                FunctionPtrExpr = fptr,
                ResultVar = tmp
            });
            stack.Push(tmp);
            return;
        }

        var ctorName = CppNameMapper.MangleMethodName(typeCpp, ".ctor");

        // Collect constructor arguments
        var args = new List<string>();
        for (int i = 0; i < ctorRef.Parameters.Count; i++)
        {
            args.Add(stack.Count > 0 ? stack.Pop() : "0");
        }
        args.Reverse();

        block.Instructions.Add(new IRNewObj
        {
            TypeCppName = typeCpp,
            CtorName = ctorName,
            ResultVar = tmp,
            CtorArgs = { },
        });

        // Add ctor args
        var newObj = (IRNewObj)block.Instructions.Last();
        newObj.CtorArgs.AddRange(args);

        stack.Push(tmp);
    }

    private string? MapBclMethod(MethodReference methodRef)
    {
        var fullType = methodRef.DeclaringType.FullName;
        var name = methodRef.Name;

        // Console methods
        if (fullType == "System.Console")
        {
            if (name == "WriteLine")
            {
                return "cil2cpp::System::Console_WriteLine";
            }
            if (name == "Write")
            {
                return "cil2cpp::System::Console_Write";
            }
            if (name == "ReadLine")
            {
                return "cil2cpp::System::Console_ReadLine";
            }
        }

        // String methods
        if (fullType == "System.String")
        {
            return name switch
            {
                "Concat" => "cil2cpp::string_concat",
                "IsNullOrEmpty" => "cil2cpp::string_is_null_or_empty",
                "get_Length" => "cil2cpp::string_length",
                _ => null
            };
        }

        // Object methods
        if (fullType == "System.Object")
        {
            return name switch
            {
                "ToString" => "cil2cpp::object_to_string",
                "GetHashCode" => "cil2cpp::object_get_hash_code",
                "Equals" => "cil2cpp::object_equals",
                "GetType" => "cil2cpp::object_get_type",
                ".ctor" => null, // Object ctor is a no-op
                _ => null
            };
        }

        // Delegate methods
        if (fullType is "System.Delegate" or "System.MulticastDelegate")
        {
            return name switch
            {
                "Combine" => "cil2cpp::delegate_combine",
                "Remove" => "cil2cpp::delegate_remove",
                _ => null
            };
        }

        // Math methods
        if (fullType == "System.Math")
        {
            // Abs has multiple overloads — use explicit C++ functions to avoid ambiguity
            if (name == "Abs" && methodRef.Parameters.Count == 1)
            {
                return methodRef.Parameters[0].ParameterType.FullName switch
                {
                    "System.Single" => "std::fabsf",
                    "System.Double" => "std::fabs",
                    _ => "std::abs" // int, long, short, sbyte — works via <cstdlib>
                };
            }

            return name switch
            {
                "Max" => "std::max",
                "Min" => "std::min",
                "Sqrt" => "std::sqrt",
                "Floor" => "std::floor",
                "Ceiling" => "std::ceil",
                "Round" => "std::round",
                "Pow" => "std::pow",
                "Sin" => "std::sin",
                "Cos" => "std::cos",
                "Tan" => "std::tan",
                "Asin" => "std::asin",
                "Acos" => "std::acos",
                "Atan" => "std::atan",
                "Atan2" => "std::atan2",
                "Log" => "std::log",
                "Log10" => "std::log10",
                "Exp" => "std::exp",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Try to resolve a MethodReference to its MethodDefinition.
    /// Returns null if resolution fails (e.g., assembly not loaded).
    /// </summary>
    private static MethodDefinition? TryResolveMethodRef(MethodReference methodRef)
    {
        try
        {
            return methodRef.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private void BuildVTable(IRType irType)
    {
        // Start with base type's vtable
        if (irType.BaseType != null)
        {
            foreach (var entry in irType.BaseType.VTable)
            {
                irType.VTable.Add(new IRVTableEntry
                {
                    Slot = entry.Slot,
                    MethodName = entry.MethodName,
                    Method = entry.Method,
                    DeclaringType = entry.DeclaringType,
                });
            }
        }
        else if (!irType.IsInterface && !irType.IsValueType)
        {
            // Root type (base = System.Object, not in _typeCache)
            // Seed with System.Object virtual method slots so overrides can replace them
            irType.VTable.Add(new IRVTableEntry { Slot = ObjectVTableSlots.ToStringSlot, MethodName = "ToString", Method = null, DeclaringType = null });
            irType.VTable.Add(new IRVTableEntry { Slot = ObjectVTableSlots.EqualsSlot, MethodName = "Equals", Method = null, DeclaringType = null });
            irType.VTable.Add(new IRVTableEntry { Slot = ObjectVTableSlots.GetHashCodeSlot, MethodName = "GetHashCode", Method = null, DeclaringType = null });
        }

        // Override or add virtual methods
        foreach (var method in irType.Methods.Where(m => m.IsVirtual))
        {
            var existing = irType.VTable.FirstOrDefault(e => e.MethodName == method.Name
                && (e.Method == null || e.Method.Parameters.Count == method.Parameters.Count));
            if (existing != null)
            {
                // Override
                existing.Method = method;
                existing.DeclaringType = irType;
                method.VTableSlot = existing.Slot;
            }
            else
            {
                // New virtual method
                var slot = irType.VTable.Count;
                irType.VTable.Add(new IRVTableEntry
                {
                    Slot = slot,
                    MethodName = method.Name,
                    Method = method,
                    DeclaringType = irType,
                });
                method.VTableSlot = slot;
            }
        }
    }

    private void BuildInterfaceImpls(IRType irType)
    {
        foreach (var iface in irType.Interfaces)
        {
            var impl = new IRInterfaceImpl { Interface = iface };
            foreach (var ifaceMethod in iface.Methods)
            {
                // Skip constructors — only map actual interface methods
                if (ifaceMethod.IsConstructor || ifaceMethod.IsStaticConstructor) continue;
                var implMethod = FindImplementingMethod(irType, ifaceMethod.Name, ifaceMethod.Parameters.Count);
                impl.MethodImpls.Add(implMethod); // null if not found — keeps slot alignment
            }
            irType.InterfaceImpls.Add(impl);
        }
    }

    private static IRMethod? FindImplementingMethod(IRType type, string methodName, int paramCount)
    {
        var current = type;
        while (current != null)
        {
            var method = current.Methods.FirstOrDefault(m => m.Name == methodName && !m.IsAbstract && !m.IsStatic
                && m.Parameters.Count == paramCount);
            if (method != null) return method;
            current = current.BaseType;
        }
        return null;
    }

    private List<string> BuildVTableParamTypes(MethodReference methodRef)
    {
        var types = new List<string>();
        types.Add(CppNameMapper.MangleTypeName(methodRef.DeclaringType.FullName) + "*");
        foreach (var p in methodRef.Parameters)
            types.Add(CppNameMapper.GetCppTypeForDecl(p.ParameterType.FullName));
        return types;
    }

    private string GetArgName(IRMethod method, int index)
    {
        if (!method.IsStatic)
        {
            if (index == 0) return "__this";
            index--;
        }

        if (index >= 0 && index < method.Parameters.Count)
            return method.Parameters[index].CppName;
        return $"__arg{index}";
    }

    private string GetLocalName(IRMethod method, int index)
    {
        if (index >= 0 && index < method.Locals.Count)
            return method.Locals[index].CppName;
        return $"loc_{index}";
    }

    // Exception event helpers
    private enum ExceptionEventKind { TryBegin, CatchBegin, FinallyBegin, HandlerEnd }

    private record ExceptionEvent(ExceptionEventKind Kind, string? CatchTypeName = null, int TryStart = 0, int TryEnd = 0);

    private static void AddExceptionEvent(SortedDictionary<int, List<ExceptionEvent>> events,
        int offset, ExceptionEvent evt)
    {
        if (!events.ContainsKey(offset))
            events[offset] = new List<ExceptionEvent>();
        events[offset].Add(evt);
    }

    private void EmitConversion(IRBasicBlock block, Stack<string> stack, string targetType, ref int tempCounter)
    {
        var val = stack.Count > 0 ? stack.Pop() : "0";
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRConversion { SourceExpr = val, TargetType = targetType, ResultVar = tmp });
        stack.Push(tmp);
    }

    private void EmitCctorGuardIfNeeded(IRBasicBlock block, string ilTypeName, string typeCppName)
    {
        if (_typeCache.TryGetValue(ilTypeName, out var irType) && irType.HasCctor)
        {
            block.Instructions.Add(new IRStaticCtorGuard { TypeCppName = typeCppName });
        }
    }

    private static string GetArrayElementType(Code code) => code switch
    {
        Code.Ldelem_I1 or Code.Stelem_I1 => "int8_t",
        Code.Ldelem_I2 or Code.Stelem_I2 => "int16_t",
        Code.Ldelem_I4 or Code.Stelem_I4 => "int32_t",
        Code.Ldelem_I8 or Code.Stelem_I8 => "int64_t",
        Code.Ldelem_U1 => "uint8_t",
        Code.Ldelem_U2 => "uint16_t",
        Code.Ldelem_U4 => "uint32_t",
        Code.Ldelem_R4 or Code.Stelem_R4 => "float",
        Code.Ldelem_R8 or Code.Stelem_R8 => "double",
        Code.Ldelem_Ref or Code.Stelem_Ref => "cil2cpp::Object*",
        Code.Ldelem_I or Code.Stelem_I => "intptr_t",
        _ => "cil2cpp::Object*"
    };
}
