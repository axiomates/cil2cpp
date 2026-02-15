using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private void EmitStoreLocal(IRBasicBlock block, Stack<string> stack, IRMethod method, int index)
    {
        var val = stack.Count > 0 ? stack.Pop() : "0";
        // For pointer-type locals, add explicit cast to handle implicit upcasts
        // (e.g., Dog* → Animal*) since generated C++ structs don't use C++ inheritance.
        if (index < method.Locals.Count)
        {
            var local = method.Locals[index];
            if (local.CppTypeName.EndsWith("*") && local.CppTypeName != "void*")
            {
                val = $"({local.CppTypeName}){val}";
            }
        }
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

        // cgt.un with nullptr: "ptr > nullptr" is invalid in C++.
        // IL uses "ldloc; ldnull; cgt.un" as an idiom for "ptr != null".
        if (op == ">" && (right == "nullptr" || left == "nullptr"))
            op = "!=";
        // Similarly, clt.un with nullptr is "nullptr != ptr" pattern
        if (op == "<" && (right == "nullptr" || left == "nullptr"))
            op = "!=";

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
        bool isVirtual, ref int tempCounter, TypeReference? constrainedType = null)
    {
        // Constrained call redirect: when constrained. callvirt on a BCL value type calls a
        // System.Object virtual method (Equals/GetHashCode/ToString), redirect the method
        // reference to the constrained type so BCL interception (ValueTuple, etc.) can handle it.
        // Only for RuntimeProvided types whose methods are intercepted (no actual C++ bodies/vtable entries).
        if (constrainedType != null && isVirtual && methodRef.HasThis
            && (methodRef.DeclaringType.FullName == "System.Object"
                || methodRef.DeclaringType.FullName == "System.ValueType")
            && methodRef.Name is "GetHashCode" or "Equals" or "ToString")
        {
            var constrainedIrType = _typeCache.GetValueOrDefault(ResolveCacheKey(constrainedType));
            // Check if the constrained type is a BCL value type with intercepted methods
            // (ValueTuple, Nullable, etc.) — these have no actual C++ function bodies in the vtable
            var isBclIntercepted = constrainedIrType != null && constrainedIrType.IsValueType
                && (IsValueTupleType(constrainedType) || IsNullableType(constrainedType));
            if (isBclIntercepted)
            {
                // Create a new MethodReference with the constrained type as declaring type
                var redirected = new MethodReference(methodRef.Name, methodRef.ReturnType, constrainedType)
                {
                    HasThis = methodRef.HasThis,
                    ExplicitThis = methodRef.ExplicitThis,
                    CallingConvention = methodRef.CallingConvention,
                };
                foreach (var p in methodRef.Parameters)
                    redirected.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
                methodRef = redirected;
                isVirtual = false; // Value type — no vtable dispatch
                constrainedType = null; // Consumed
            }
        }

        // ===== BCL Type Interceptions =====
        // These intercept BCL method calls and emit inline C++ instead of normal method calls.
        // In single-assembly mode, ALL interceptions are active (BCL IL is not available).
        // In multi-assembly mode, interceptions are still active because BCL methods are currently
        // compiled as stubs. When BCL IL compilation is fully implemented, interceptions for simple
        // value types (Nullable, ValueTuple, Index, Range) can be bypassed.
        //
        // Future bypass candidates (when BCL IL compiles correctly):
        //   Nullable, ValueTuple, Index, Range
        // Must always intercept (runtime-tied / no IL):
        //   Async, Thread, Type, Span, MdArray, EqualityComparer, List, Dictionary

        if (TryEmitNullableCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitValueTupleCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitAsyncCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitIndexCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitRangeCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitGetSubArray(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitThreadCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitTypeCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitSpanCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitEqualityComparerCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitMdArrayCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitListCall(block, stack, methodRef, ref tempCounter))
            return;
        if (TryEmitDictionaryCall(block, stack, methodRef, ref tempCounter))
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

            if (!IsVoidReturnType(methodRef.ReturnType))
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

        // Emit cctor guard for static method calls (ECMA-335 II.10.5.3.1)
        if (!methodRef.HasThis)
        {
            var declaringTypeName = ResolveCacheKey(methodRef.DeclaringType);
            var typeCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
            EmitCctorGuardIfNeeded(block, declaringTypeName, typeCpp);
        }

        var irCall = new IRCall();

        // Unified BCL method lookup — covers both [InternalCall] methods and
        // managed BCL methods with C++ runtime implementations.
        var mappedName = ICallRegistry.Lookup(methodRef);

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
            var funcName = CppNameMapper.MangleMethodName(typeCpp, methodRef.Name);
            // op_Explicit/op_Implicit: disambiguate by return type (matches ConvertMethod)
            if (methodRef.Name is "op_Explicit" or "op_Implicit")
            {
                var retMangled = CppNameMapper.MangleTypeName(methodRef.ReturnType.FullName);
                funcName = $"{funcName}_{retMangled}";
            }
            irCall.FunctionName = funcName;
        }

        // Collect arguments (in reverse order from stack)
        var args = new List<string>();
        for (int i = 0; i < methodRef.Parameters.Count; i++)
        {
            args.Add(stack.Count > 0 ? stack.Pop() : "0");
        }
        args.Reverse();

        // Interlocked _obj methods need Object*/Object** casts for generic type arguments
        if (mappedName != null && mappedName.EndsWith("_obj")
            && mappedName.Contains("Interlocked"))
        {
            // First arg is T** → cast to Object**
            if (args.Count > 0)
                args[0] = $"(cil2cpp::Object**){args[0]}";
            // Remaining args are T → cast to Object*
            for (int i = 1; i < args.Count; i++)
                args[i] = $"(cil2cpp::Object*){args[i]}";
        }

        // 'this' for instance methods
        if (methodRef.HasThis)
        {
            var thisArg = stack.Count > 0 ? stack.Pop() : "__this";
            if (mappedName != null && methodRef.DeclaringType.FullName == "System.Object")
            {
                // BCL mapped Object methods expect cil2cpp::Object*
                thisArg = $"(cil2cpp::Object*){thisArg}";
            }
            else if (mappedName != null && methodRef.HasThis)
            {
                // BCL mapped value type instance methods (Int32.ToString, etc.)
                // 'this' is a pointer (&x) but the mapped function expects a value — dereference
                bool isValueTarget = false;
                try { isValueTarget = methodRef.DeclaringType.Resolve()?.IsValueType == true; }
                catch { }
                if (isValueTarget)
                    thisArg = $"*({thisArg})";
            }
            else if (!irCall.IsVirtual && mappedName == null
                && methodRef.DeclaringType.FullName != "System.Object")
            {
                // For non-virtual calls / callvirt without vtable slot: cast 'this' to declaring type
                // (C++ structs don't have inheritance, so Dog* ≠ Animal*)
                // Skip for: value types, runtime types (cil2cpp::), runtime-provided types
                var declCacheKey = ResolveCacheKey(methodRef.DeclaringType);
                var isValueDecl = false;
                if (_typeCache.TryGetValue(declCacheKey, out var declIrType))
                    isValueDecl = declIrType.IsValueType;
                else
                {
                    // Not in _typeCache — check Cecil TypeReference (BCL value types)
                    try { isValueDecl = methodRef.DeclaringType.Resolve()?.IsValueType == true; }
                    catch { /* resolution failed — assume not value type */ }
                }
                if (!isValueDecl)
                {
                    var declTypeCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
                    if (!declTypeCpp.StartsWith("cil2cpp::") && !RuntimeProvidedTypes.Contains(methodRef.DeclaringType.FullName))
                        thisArg = $"({declTypeCpp}*){thisArg}";
                }
            }
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

        // Constrained call on value type: convert virtual dispatch to direct call or box
        // ECMA-335 III.2.1: constrained. callvirt on value type T:
        //   - If T overrides the method: call T's override directly (no boxing)
        //   - Otherwise: box T and do virtual dispatch on the boxed object
        if (constrainedType != null && isVirtual && methodRef.HasThis && mappedName == null)
        {
            var constrainedTypeName = ResolveTypeRefOperand(constrainedType);
            var constrainedIrType = _typeCache.GetValueOrDefault(ResolveCacheKey(constrainedType));
            if (constrainedIrType != null && constrainedIrType.IsValueType)
            {
                // Find the method override on the constrained type (only methods with bodies)
                var overrideMethod = constrainedIrType.Methods.FirstOrDefault(m =>
                    m.Name == methodRef.Name && !m.IsStaticConstructor && !m.IsStatic
                    && m.BasicBlocks.Count > 0
                    && ParameterTypesMatchRef(m, methodRef));
                if (overrideMethod != null)
                {
                    // Direct call to the value type's override
                    irCall.FunctionName = overrideMethod.CppName;
                    isVirtual = false; // Suppress vtable dispatch
                }
                else
                {
                    // No override found — box the value type and do vtable dispatch
                    // The `this` arg is currently a pointer to the value type; box it
                    if (irCall.Arguments.Count > 0)
                    {
                        var thisPtr = irCall.Arguments[0]; // e.g., "(cil2cpp::Object*)&loc_0"
                        var cppTypeName = GetMangledTypeNameForRef(constrainedType);
                        var typeInfoName = $"{cppTypeName}_TypeInfo";
                        // Strip the (cil2cpp::Object*) cast to get the raw value pointer
                        var rawPtr = thisPtr;
                        if (rawPtr.StartsWith("(cil2cpp::Object*)"))
                            rawPtr = rawPtr["(cil2cpp::Object*)".Length..];
                        irCall.Arguments[0] = $"(cil2cpp::Object*)cil2cpp::box_raw({rawPtr}, sizeof({cppTypeName}), &{typeInfoName})";
                    }
                }
            }
        }

        // Virtual dispatch detection
        if (isVirtual && methodRef.HasThis && mappedName == null)
        {
            var declaringTypeName = declaringCacheKey;
            var resolved = _typeCache.GetValueOrDefault(declaringTypeName);

            if (resolved != null && resolved.IsInterface)
            {
                // Interface dispatch — find slot by name and parameter types
                int ifaceSlot = 0;
                bool found = false;
                foreach (var m in resolved.Methods)
                {
                    if (m.IsConstructor || m.IsStaticConstructor) continue;
                    if (m.Name == methodRef.Name && ParameterTypesMatchRef(m, methodRef)) { found = true; break; }
                    ifaceSlot++;
                }
                if (found)
                {
                    irCall.IsVirtual = true;
                    irCall.IsInterfaceCall = true;
                    irCall.InterfaceTypeCppName = resolved.CppName;
                    irCall.VTableSlot = ifaceSlot;
                    irCall.VTableReturnType = CppNameMapper.GetCppTypeForDecl(
                        ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType));
                    irCall.VTableParamTypes = BuildVTableParamTypes(methodRef);
                }
            }
            else if (resolved != null && !resolved.IsValueType)
            {
                // Class virtual dispatch — match by name and parameter types
                var entry = resolved.VTable.FirstOrDefault(e => e.MethodName == methodRef.Name
                    && (e.Method == null || ParameterTypesMatchRef(e.Method, methodRef)));
                if (entry != null)
                {
                    irCall.IsVirtual = true;
                    irCall.VTableSlot = entry.Slot;
                    irCall.VTableReturnType = CppNameMapper.GetCppTypeForDecl(
                        ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType));
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
                    irCall.VTableReturnType = CppNameMapper.GetCppTypeForDecl(
                        ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType));
                    irCall.VTableParamTypes = BuildVTableParamTypes(methodRef);
                }
            }
        }

        // Return value — skip for void methods (including modreq-wrapped void like init-only setters)
        if (!IsVoidReturnType(methodRef.ReturnType))
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
        // Special: BCL exception types (System.Exception, InvalidOperationException, etc.)
        if (TryEmitExceptionNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: Nullable<T> constructor (newobj pattern — rare, usually ldloca+call)
        if (TryEmitNullableNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: ValueTuple constructor (newobj pattern — value type, can't use gc::alloc)
        if (TryEmitValueTupleNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: Async BCL types (rare — builder uses Create() factory, not newobj)
        if (TryEmitAsyncNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: System.Index constructor
        if (TryEmitIndexNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: System.Range constructor
        if (TryEmitRangeNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: System.Threading.Thread constructor
        if (TryEmitThreadNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: Span<T> / ReadOnlySpan<T> constructor
        if (TryEmitSpanNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: EqualityComparer<T> constructor
        if (TryEmitEqualityComparerNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: Multi-dimensional array constructor (newobj T[,]::.ctor)
        if (TryEmitMdArrayNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: List<T> constructor
        if (TryEmitListNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Special: Dictionary<K,V> constructor
        if (TryEmitDictionaryNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        var cacheKey = ResolveCacheKey(ctorRef.DeclaringType);
        var typeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        // Detect delegate constructor: base is MulticastDelegate/Delegate, ctor(object, IntPtr)
        var isDelegateCtor = false;
        if (ctorRef.Parameters.Count == 2)
        {
            if (_typeCache.TryGetValue(cacheKey, out var delegateType) && delegateType.IsDelegate)
                isDelegateCtor = true;
            else
            {
                // Fallback: check Cecil for BCL delegate types not in _typeCache
                try
                {
                    var resolved = ctorRef.DeclaringType.Resolve();
                    isDelegateCtor = resolved?.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate";
                }
                catch { }
            }
        }
        if (isDelegateCtor)
        {
            // Ensure BCL delegate type has a TypeInfo (register if not in _typeCache)
            if (!_typeCache.ContainsKey(cacheKey))
                RegisterBclDelegateType(cacheKey, typeCpp);

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

        // Value types: allocate on stack instead of heap
        if (_typeCache.TryGetValue(cacheKey, out var irType) && irType.IsValueType)
        {
            block.Instructions.Add(new IRDeclareLocal { TypeName = typeCpp, VarName = tmp });
            var allArgs = new List<string> { $"&{tmp}" };
            allArgs.AddRange(args);
            block.Instructions.Add(new IRCall
            {
                FunctionName = ctorName,
                Arguments = { },
            });
            var call = (IRCall)block.Instructions.Last();
            call.Arguments.AddRange(allArgs);
            stack.Push(tmp);
        }
        else
        {
            // Runtime-provided types: use cil2cpp:: struct name for sizeof/cast,
            // but keep mangled name for TypeInfo reference
            var runtimeCpp = GetRuntimeProvidedCppTypeName(ctorRef.DeclaringType.FullName);
            if (runtimeCpp != null)
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({runtimeCpp}*)cil2cpp::gc::alloc(sizeof({runtimeCpp}), &{typeCpp}_TypeInfo);"
                });
                // Call constructor if it has args
                if (args.Count > 0)
                {
                    var allArgs = new List<string> { tmp };
                    allArgs.AddRange(args);
                    block.Instructions.Add(new IRCall
                    {
                        FunctionName = ctorName,
                        Arguments = { },
                    });
                    var call = (IRCall)block.Instructions.Last();
                    call.Arguments.AddRange(allArgs);
                }
            }
            else
            {
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
            }

            stack.Push(tmp);
        }
    }

    /// <summary>
    /// Intercepts newobj for BCL exception types (System.Exception, InvalidOperationException, etc.)
    /// and emits runtime exception creation code instead of trying to reference non-existent
    /// generated structs/constructors.
    /// </summary>
    private bool TryEmitExceptionNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        var runtimeCppName = CppNameMapper.GetRuntimeExceptionCppName(ctorRef.DeclaringType.FullName);
        if (runtimeCppName == null) return false;

        var tmp = $"__t{tempCounter++}";
        var paramCount = ctorRef.Parameters.Count;

        // Pop constructor args
        var args = new List<string>();
        for (int i = 0; i < paramCount; i++)
            args.Add(stack.Count > 0 ? stack.Pop() : "0");
        args.Reverse();

        // Allocate: (ExcType*)cil2cpp::gc::alloc(sizeof(ExcType), &ExcType_TypeInfo)
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = ({runtimeCppName}*)cil2cpp::gc::alloc(sizeof({runtimeCppName}), &{runtimeCppName}_TypeInfo);"
        });

        // Set fields based on constructor signature
        // .ctor() — no args
        // .ctor(string message) — set message
        // .ctor(string message, Exception inner) — set message + inner
        if (paramCount >= 1)
            block.Instructions.Add(new IRRawCpp { Code = $"{tmp}->message = (cil2cpp::String*){args[0]};" });
        if (paramCount >= 2)
            block.Instructions.Add(new IRRawCpp { Code = $"{tmp}->inner_exception = (cil2cpp::Exception*){args[1]};" });

        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Map runtime-provided IL type names to their C++ struct type names.
    /// Returns null if the type is not runtime-provided or doesn't need mapping.
    /// </summary>
    private static string? GetRuntimeProvidedCppTypeName(string ilFullName) => ilFullName switch
    {
        "System.Object" => "cil2cpp::Object",
        "System.String" => "cil2cpp::String",
        "System.Array" => "cil2cpp::Array",
        _ => null
    };

    /// <summary>
    /// Check if a return type is void (handles modreq-wrapped void from init-only setters).
    /// </summary>
    private static bool IsVoidReturnType(Mono.Cecil.TypeReference returnType)
    {
        if (returnType.FullName == "System.Void") return true;
        // Init-only setters wrap void with RequiredModifierType (modreq IsExternalInit)
        if (returnType is Mono.Cecil.RequiredModifierType modReq)
            return modReq.ElementType.FullName == "System.Void";
        if (returnType is Mono.Cecil.OptionalModifierType modOpt)
            return modOpt.ElementType.FullName == "System.Void";
        return false;
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
            // newslot methods always create a new vtable slot (C# 'new virtual')
            // Non-newslot methods attempt to override an existing slot
            IRVTableEntry? existing = null;
            if (!method.IsNewSlot)
            {
                // Use LastOrDefault: when method hiding creates duplicate-named entries,
                // 'override' targets the most-derived slot (added last)
                existing = irType.VTable.LastOrDefault(e => e.MethodName == method.Name
                    && (e.Method == null || ParameterTypesMatch(e.Method, method)));
            }

            if (existing != null)
            {
                // Override
                existing.Method = method;
                existing.DeclaringType = irType;
                method.VTableSlot = existing.Slot;
            }
            else
            {
                // New virtual method (or newslot override)
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

                // First: check explicit interface overrides (.override directive)
                var implMethod = FindExplicitOverride(irType, iface, ifaceMethod);

                // Fallback: implicit name + parameter matching
                implMethod ??= FindImplementingMethod(irType, ifaceMethod);

                // DIM fallback: if no class impl, use the interface's default method body
                // At Pass 5 time, bodies haven't been converted yet (Pass 6), so check
                // !IsAbstract which indicates the Cecil method has a body
                if (implMethod == null && !ifaceMethod.IsAbstract)
                {
                    implMethod = ifaceMethod;
                }

                impl.MethodImpls.Add(implMethod); // null if not found — keeps slot alignment
            }
            irType.InterfaceImpls.Add(impl);
        }
    }

    /// <summary>
    /// Searches for a method that explicitly implements the given interface method
    /// via the .override directive (C# explicit interface implementation: void IFoo.Method()).
    /// </summary>
    private static IRMethod? FindExplicitOverride(IRType type, IRType iface, IRMethod ifaceMethod)
    {
        var current = type;
        while (current != null)
        {
            var method = current.Methods.FirstOrDefault(m =>
                m.ExplicitOverrides.Any(o =>
                    o.InterfaceTypeName == iface.ILFullName && o.MethodName == ifaceMethod.Name)
                && !m.IsAbstract && !m.IsStatic
                && ParameterTypesMatch(m, ifaceMethod));
            if (method != null) return method;
            current = current.BaseType;
        }
        return null;
    }

    private static IRMethod? FindImplementingMethod(IRType type, IRMethod ifaceMethod)
    {
        var current = type;
        while (current != null)
        {
            var method = current.Methods.FirstOrDefault(m => m.Name == ifaceMethod.Name && !m.IsAbstract && !m.IsStatic
                && ParameterTypesMatch(m, ifaceMethod));
            if (method != null) return method;
            current = current.BaseType;
        }
        return null;
    }

    private List<string> BuildVTableParamTypes(MethodReference methodRef)
    {
        var types = new List<string>();
        // Use GetCppTypeForDecl to correctly map System.Object → cil2cpp::Object*, etc.
        types.Add(CppNameMapper.GetCppTypeForDecl(methodRef.DeclaringType.FullName));
        foreach (var p in methodRef.Parameters)
            types.Add(CppNameMapper.GetCppTypeForDecl(
                ResolveGenericTypeRef(p.ParameterType, methodRef.DeclaringType)));
        return types;
    }

    /// <summary>
    /// Resolve generic parameter references (!0, !1, etc.) in a type reference
    /// using the generic arguments from the declaring type.
    /// </summary>
    private static string ResolveGenericTypeRef(TypeReference typeRef, TypeReference declaringType)
    {
        if (declaringType is not GenericInstanceType git) return typeRef.FullName;

        if (typeRef is GenericParameter gp)
        {
            if (gp.Position < git.GenericArguments.Count)
                return git.GenericArguments[gp.Position].FullName;
        }

        if (typeRef is GenericInstanceType returnGit)
        {
            // Generic instance with !0 in arguments — resolve each argument
            bool anyResolved = false;
            var argNames = new List<string>();
            foreach (var arg in returnGit.GenericArguments)
            {
                if (arg is GenericParameter gp2 && gp2.Position < git.GenericArguments.Count)
                {
                    argNames.Add(git.GenericArguments[gp2.Position].FullName);
                    anyResolved = true;
                }
                else
                {
                    argNames.Add(arg.FullName);
                }
            }
            if (anyResolved)
                return $"{returnGit.ElementType.FullName}<{string.Join(",", argNames)}>";
        }

        return typeRef.FullName;
    }

    /// <summary>
    /// Register a BCL delegate type that doesn't exist in _typeCache.
    /// Creates a minimal IRType so the TypeInfo gets declared and defined.
    /// </summary>
    private void RegisterBclDelegateType(string ilFullName, string cppName)
    {
        var lastDot = ilFullName.LastIndexOf('.');
        var bclDelegate = new IRType
        {
            ILFullName = ilFullName,
            Name = lastDot >= 0 ? ilFullName[(lastDot + 1)..] : ilFullName,
            Namespace = lastDot >= 0 ? ilFullName[..lastDot] : "",
            CppName = cppName,
            IsDelegate = true,
        };
        _typeCache[ilFullName] = bclDelegate;
        _module.Types.Add(bclDelegate);
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

    /// <summary>
    /// Determines whether a field access should use '.' (value) vs '->' (pointer).
    /// Value type locals accessed directly (ldloc) use '.'; addresses (&amp;loc) use '->'.
    /// </summary>
    private static bool IsValueTypeAccess(TypeReference declaringType, string objExpr, IRMethod method)
    {
        // Address-of expressions are always pointers
        if (objExpr.StartsWith("&")) return false;

        // __this is always a pointer
        if (objExpr == "__this") return false;

        // Check if the declaring type is a value type
        var resolved = declaringType.Resolve();
        bool isValueType = resolved?.IsValueType ?? false;
        if (!isValueType)
        {
            // Also check our own registry (for generic specializations etc.)
            isValueType = CppNameMapper.IsValueType(declaringType.FullName);
        }
        if (!isValueType) return false;

        // If the object expression is a local variable of value type, it's a value access
        // Local names follow the pattern loc_N
        if (objExpr.StartsWith("loc_")) return true;

        // Temp variables holding value types also use value access.
        // Only pointer-typed temps (from alloc/newobj) get cast to Type* and won't match here
        // because the declaring type's IsValueType would still be true, but the obj expr
        // would contain a cast like (Type*)__tN. Plain __tN without cast = value type.
        if (objExpr.StartsWith("__t") && !objExpr.Contains("*")) return true;

        // Method parameters that are value types
        if (method.Parameters.Any(p => p.CppName == objExpr)) return true;

        return false;
    }

    private string GetLocalName(IRMethod method, int index)
    {
        if (index >= 0 && index < method.Locals.Count)
            return method.Locals[index].CppName;
        return $"loc_{index}";
    }

    // Exception event helpers
    private enum ExceptionEventKind { TryBegin, CatchBegin, FinallyBegin, FilterBegin, FilterHandlerBegin, HandlerEnd }

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

    /// <summary>
    /// Check if two IR methods have matching parameter types (for vtable override resolution).
    /// </summary>
    private static bool ParameterTypesMatch(IRMethod a, IRMethod b)
    {
        if (a.Parameters.Count != b.Parameters.Count) return false;
        for (int i = 0; i < a.Parameters.Count; i++)
        {
            if (a.Parameters[i].CppTypeName != b.Parameters[i].CppTypeName)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check if an IR method matches a Cecil MethodReference by parameter types
    /// (for vtable dispatch slot lookup).
    /// </summary>
    private static bool ParameterTypesMatchRef(IRMethod irMethod, MethodReference methodRef)
    {
        if (irMethod.Parameters.Count != methodRef.Parameters.Count) return false;
        for (int i = 0; i < irMethod.Parameters.Count; i++)
        {
            var irTypeName = irMethod.Parameters[i].ILTypeName;
            var refTypeName = methodRef.Parameters[i].ParameterType.FullName;
            if (irTypeName != refTypeName) return false;
        }
        return true;
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
