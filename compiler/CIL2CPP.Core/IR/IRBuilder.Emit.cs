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
        // ===== Compiler Intrinsics — emit inline C++ instead of function calls =====

        // INumber<T>.CreateTruncating<TOther>(TOther) — numeric cast intrinsic
        if (methodRef.Name == "CreateTruncating" && methodRef is GenericInstanceMethod
            && methodRef.Parameters.Count == 1 && !methodRef.HasThis)
        {
            var val = stack.Count > 0 ? stack.Pop() : "0";
            var retType = CppNameMapper.GetCppTypeForDecl(
                ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType));
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{retType} {tmp} = static_cast<{retType}>({val});",
                ResultVar = tmp,
                ResultTypeCpp = retType,
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.SizeOf<T>() — sizeof intrinsic
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "SizeOf" && methodRef is GenericInstanceMethod gimSz)
        {
            var typeArg = gimSz.GenericArguments[0];
            var resolvedArg = typeArg is GenericParameter gpSz && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gpSz.Name, out var rSz) ? rSz : typeArg.FullName;
            var cppType = CppNameMapper.GetCppTypeName(resolvedArg);
            if (cppType.EndsWith("*")) cppType = cppType.TrimEnd('*');
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {tmp} = static_cast<int32_t>(sizeof({cppType}));",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.As<TFrom,TTo>(ref TFrom) — reinterpret cast intrinsic
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "As" && methodRef is GenericInstanceMethod gimAs
            && gimAs.GenericArguments.Count == 2)
        {
            var val = stack.Count > 0 ? stack.Pop() : "nullptr";
            var toTypeArg = gimAs.GenericArguments[1];
            var resolvedTo = toTypeArg is GenericParameter gpAs && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gpAs.Name, out var rAs) ? rAs : toTypeArg.FullName;
            var cppTo = CppNameMapper.GetCppTypeForDecl(resolvedTo);
            if (!cppTo.EndsWith("*")) cppTo += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<{cppTo}>({val});",
                ResultVar = tmp,
                ResultTypeCpp = cppTo,
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.Add<T>(ref T, int) — pointer arithmetic intrinsic
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "Add" && methodRef.Parameters.Count == 2)
        {
            var offset = stack.Count > 0 ? stack.Pop() : "0";
            var ptr = stack.Count > 0 ? stack.Pop() : "nullptr";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {ptr} + {offset};"
            });
            stack.Push(tmp);
            return;
        }

        // Vector128<T>.get_Count — return 0 (disable SIMD paths, force scalar fallback)
        if (methodRef.DeclaringType.FullName.StartsWith("System.Runtime.Intrinsics.Vector128")
            && (methodRef.Name == "get_Count" || methodRef.Name == "get_IsHardwareAccelerated"))
        {
            if (methodRef.HasThis && stack.Count > 0) stack.Pop(); // discard 'this'
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp { Code = $"int32_t {tmp} = 0;" });
            stack.Push(tmp);
            return;
        }

        // Vector128 operations — stub as no-ops (SIMD disabled)
        if (methodRef.DeclaringType.FullName.StartsWith("System.Runtime.Intrinsics.Vector128"))
        {
            // Pop all arguments
            for (int i = 0; i < methodRef.Parameters.Count; i++)
                if (stack.Count > 0) stack.Pop();
            if (methodRef.HasThis && stack.Count > 0) stack.Pop();
            if (!IsVoidReturnType(methodRef.ReturnType))
            {
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp { Code = $"auto {tmp} = 0; // Vector128 stub" });
                stack.Push(tmp);
            }
            return;
        }

        // Array.Empty<T>() — return a zero-length array
        // EmptyArray<T> is a nested generic type inside System.Array (RuntimeProvidedType),
        // so its statics can't be generated. Replace with inline array creation.
        if (methodRef.DeclaringType.FullName == "System.Array"
            && methodRef.Name == "Empty" && methodRef.Parameters.Count == 0
            && methodRef is GenericInstanceMethod gimEmpty)
        {
            var elemTypeArg = gimEmpty.GenericArguments[0];
            var resolvedElem = elemTypeArg is GenericParameter gpE && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gpE.Name, out var rE) ? rE : elemTypeArg.FullName;
            var elemCppType = CppNameMapper.MangleTypeName(resolvedElem);
            if (CppNameMapper.IsPrimitive(resolvedElem))
                _module.RegisterPrimitiveTypeInfo(resolvedElem);
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::array_create(&{elemCppType}_TypeInfo, 0);",
                ResultVar = tmp,
                ResultTypeCpp = "cil2cpp::Array*",
            });
            stack.Push(tmp);
            return;
        }

        // RuntimeHelpers.GetSubArray<T>(T[], Range) — compiler intrinsic for array slicing
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && methodRef.Name == "GetSubArray")
        {
            var range = stack.Count > 0 ? stack.Pop() : "{}";
            var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
            var startTmp = $"__t{tempCounter++}";
            var endTmp = $"__t{tempCounter++}";
            var lenTmp = $"__t{tempCounter++}";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {startTmp} = {range}.f__start.f__value < 0 " +
                       $"? {range}.f__start.f__value + cil2cpp::array_length({arr}) + 1 " +
                       $": {range}.f__start.f__value;"
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {endTmp} = {range}.f__end.f__value < 0 " +
                       $"? {range}.f__end.f__value + cil2cpp::array_length({arr}) + 1 " +
                       $": {range}.f__end.f__value;"
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {lenTmp} = {endTmp} - {startTmp};"
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::array_get_subarray({arr}, {startTmp}, {lenTmp});"
            });
            stack.Push(tmp);
            return;
        }

        // Span<T>/ReadOnlySpan<T> op_Implicit — struct conversion intrinsics
        // Handles: Array → Span<T>, Array → ReadOnlySpan<T>, Span<T> → ReadOnlySpan<T>
        if (methodRef.Name == "op_Implicit" && methodRef.Parameters.Count == 1
            && (methodRef.DeclaringType.FullName.StartsWith("System.Span`1")
                || methodRef.DeclaringType.FullName.StartsWith("System.ReadOnlySpan`1")))
        {
            var source = stack.Count > 0 ? stack.Pop() : "{}";
            var retTypeName = ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType);
            var retCpp = CppNameMapper.GetCppTypeName(retTypeName);
            if (retCpp.EndsWith("*")) retCpp = retCpp.TrimEnd('*');
            var paramTypeName = methodRef.Parameters[0].ParameterType.FullName;
            var tmp = $"__t{tempCounter++}";

            if (paramTypeName.EndsWith("[]"))
            {
                // Determine element pointer type for the cast (array_data returns void*)
                var elemPtrType = "void*";
                if (methodRef.DeclaringType is GenericInstanceType spanGit && spanGit.GenericArguments.Count > 0)
                {
                    var elemArgName = ResolveGenericTypeRef(spanGit.GenericArguments[0], methodRef.DeclaringType);
                    var elemCpp = CppNameMapper.GetCppTypeForDecl(elemArgName);
                    elemPtrType = elemCpp.EndsWith("*") ? elemCpp : elemCpp + "*";
                }
                // Array → Span/ReadOnlySpan: construct from array data + length
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{retCpp} {tmp} = {{0}}; " +
                           $"{tmp}.f_reference = ({elemPtrType})cil2cpp::array_data({source}); " +
                           $"{tmp}.f_length = cil2cpp::array_length({source});"
                });
            }
            else
            {
                // Span → ReadOnlySpan (same layout): copy fields
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{retCpp} {tmp} = {{0}}; " +
                           $"{tmp}.f_reference = {source}.f_reference; " +
                           $"{tmp}.f_length = {source}.f_length;"
                });
            }
            stack.Push(tmp);
            return;
        }

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

            // Resolve generic type params (T, TArg, etc.) through active type param map
            var resolvedRetType = ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType);
            var invoke = new IRDelegateInvoke
            {
                DelegateExpr = delegateExpr,
                ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(resolvedRetType),
            };
            foreach (var p in methodRef.Parameters)
            {
                var resolvedParam = ResolveGenericTypeRef(p.ParameterType, methodRef.DeclaringType);
                invoke.ParamTypes.Add(CppNameMapper.GetCppTypeForDecl(resolvedParam));
            }
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

        // ICall registry lookup for [InternalCall] and runtime-provided methods
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
            // Resolve type arguments through active type param map (method-level generics)
            // Must handle complex types containing generic params (ArrayType, GenericInstanceType, etc.)
            var typeArgs = gim.GenericArguments.Select(a => ResolveTypeRefOperand(a)).ToList();
            // Include parameter types in key (matches CollectGenericMethod)
            var paramSig = string.Join(",", elemMethod.Parameters.Select(p => p.ParameterType.FullName));
            var key = MakeGenericMethodKey(declType, elemMethod.Name, typeArgs, paramSig);

            if (_genericMethodInstantiations.TryGetValue(key, out var gmInfo))
            {
                irCall.FunctionName = gmInfo.MangledName;
            }
            else
            {
                // Transitive generic instantiation: inner generic calls within a specialized
                // generic method body need to be collected for their own specialization.
                var mangledName = MangleGenericMethodName(declType, elemMethod.Name, typeArgs);
                var cecilMethod = elemMethod.Resolve();
                // Only register if all type args are fully resolved (no !!0/!0 or named generic params)
                bool allResolved = cecilMethod != null
                    && !typeArgs.Any(a => a.Contains("!!") || a.Contains("!0")
                        || ContainsUnresolvedGenericParam(a));
                if (allResolved)
                {
                    // Check for name collision and disambiguate if needed
                    if (_genericMethodInstantiations.Values.Any(v => v.MangledName == mangledName))
                    {
                        var disambigMap = new Dictionary<string, string>();
                        for (int gi = 0; gi < cecilMethod.GenericParameters.Count && gi < typeArgs.Count; gi++)
                            disambigMap[cecilMethod.GenericParameters[gi].Name] = typeArgs[gi];
                        var disambigSuffix = string.Join("_", cecilMethod.Parameters
                            .Select(p => CppNameMapper.MangleTypeName(
                                ResolveGenericTypeName(p.ParameterType, disambigMap))));
                        mangledName += $"__{disambigSuffix}";
                    }
                    _genericMethodInstantiations[key] = new GenericMethodInstantiationInfo(
                        declType, elemMethod.Name, typeArgs, mangledName, cecilMethod);
                }
                irCall.FunctionName = mangledName;
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
            // Check for disambiguated overload name
            // Resolve generic params (e.g., T → System.Boolean in generic instantiations)
            var ilParamKey = string.Join(",", methodRef.Parameters.Select(p =>
                ResolveGenericTypeRef(p.ParameterType, methodRef.DeclaringType)));
            var lookupKey = $"{funcName}|{ilParamKey}";
            if (_module.DisambiguatedMethodNames.TryGetValue(lookupKey, out var disambiguated))
                funcName = disambiguated;

            irCall.FunctionName = funcName;
        }

        // Collect arguments (in reverse order from stack)
        var args = new List<string>();
        for (int i = 0; i < methodRef.Parameters.Count; i++)
        {
            args.Add(stack.Count > 0 ? stack.Pop() : "0");
        }
        args.Reverse();

        // Interlocked _obj methods: void* params/return, cast to/from concrete types
        bool isInterlockedObj = mappedName != null && mappedName.EndsWith("_obj")
            && mappedName.Contains("Interlocked");
        if (isInterlockedObj)
        {
            // First arg is T** → cast to void* (void** → void* implicit)
            if (args.Count > 0)
                args[0] = $"(void*){args[0]}";
            // Remaining args are T → cast to void*
            for (int i = 1; i < args.Count; i++)
                args[i] = $"(void*){args[i]}";
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
                    // Use void* intermediate cast for flat struct model (no C++ inheritance)
                    // Skip only cil2cpp:: namespace types (Object, String, etc. are already aliased)
                    if (!declTypeCpp.StartsWith("cil2cpp::"))
                        thisArg = $"({declTypeCpp}*)(void*){thisArg}";
                }
            }
            irCall.Arguments.Add(thisArg);
        }

        // Cast arguments to expected parameter types when they're concrete reference types.
        // This handles derived→base pointer casts that C++ can't do implicitly without inheritance.
        // Apply to both direct calls and icall/mapped calls (icalls also need type casts,
        // e.g. Monitor_Enter expects cil2cpp::Object* but receives typed pointers).
        if (!irCall.IsVirtual)
        {
            CastArgumentsToParameterTypes(args, methodRef, mappedName != null);
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
                    && !m.IsAbstract && !m.IsInternalCall
                    && ParameterTypesMatchRef(m, methodRef));
                if (overrideMethod != null)
                {
                    // Direct call to the value type's override
                    irCall.FunctionName = overrideMethod.CppName;
                    isVirtual = false; // Suppress vtable dispatch
                    // Fix the 'this' argument — strip interface/base cast and re-cast
                    // to the constrained type (e.g. IDisposable* → Enumerator*)
                    if (irCall.Arguments.Count > 0)
                    {
                        var thisArg = irCall.Arguments[0];
                        var cppTypeName = GetMangledTypeNameForRef(constrainedType);
                        // Strip existing C-style cast prefix
                        if (thisArg.StartsWith("(") && thisArg.Contains(')'))
                        {
                            var closeIdx = thisArg.IndexOf(')');
                            thisArg = thisArg[(closeIdx + 1)..];
                        }
                        irCall.Arguments[0] = $"({cppTypeName}*){thisArg}";
                    }
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

        // Static abstract/virtual interface method resolution (constrained. T; call)
        // .NET 7+ allows constrained. prefix with call (not just callvirt) for static
        // virtual/abstract interface members. Resolve to the constrained type's implementation.
        if (constrainedType != null && !methodRef.HasThis && mappedName == null)
        {
            var constrainedIrType = _typeCache.GetValueOrDefault(ResolveCacheKey(constrainedType));
            bool resolvedStaticConstraint = false;
            if (constrainedIrType != null)
            {
                // Find the explicit interface implementation on the constrained type
                // The method name in IL is the interface method name (e.g., "op_BitwiseOr")
                // but the explicit impl on the type may also match by name + params
                var staticImpl = constrainedIrType.Methods.FirstOrDefault(m =>
                    m.IsStatic && m.Name == methodRef.Name
                    && m.BasicBlocks.Count > 0
                    && ParameterTypesMatchRef(m, methodRef));
                if (staticImpl != null)
                {
                    irCall.FunctionName = staticImpl.CppName;
                    resolvedStaticConstraint = true;
                }
                else
                {
                    // Try matching by parameter count only (explicit interface impls
                    // may have mangled names that differ from the interface method name)
                    var candidates = constrainedIrType.Methods.Where(m =>
                        m.IsStatic && m.Name == methodRef.Name
                        && m.Parameters.Count == methodRef.Parameters.Count).ToList();
                    if (candidates.Count == 1)
                    {
                        irCall.FunctionName = candidates[0].CppName;
                        resolvedStaticConstraint = true;
                    }
                }
            }

            // Fallback: if static constrained call wasn't resolved, try operator intrinsics
            // for primitive types. These are well-known operators from numeric interfaces
            // (IBitwiseOperators, IComparisonOperators, etc.) that map to C++ operators.
            if (!resolvedStaticConstraint)
            {
                var cppOp = TryGetIntrinsicOperator(methodRef.Name);
                if (cppOp != null && args.Count >= 2)
                {
                    var tmp = $"__t{tempCounter++}";
                    var cppType = CppNameMapper.GetCppTypeName(
                        ResolveTypeRefOperand(constrainedType));
                    // For bitwise ops on float/double, operate on the integer representation
                    bool isBitwiseOp = cppOp is "|" or "&" or "^";
                    bool isFloat = cppType is "float" or "double";
                    if (isBitwiseOp && isFloat)
                    {
                        // Use memcpy to reinterpret float<->int for bitwise ops
                        var intType = cppType == "float" ? "uint32_t" : "uint64_t";
                        var a = $"__bw_a{tempCounter}";
                        var b = $"__bw_b{tempCounter}";
                        block.Instructions.Add(new IRRawCpp
                            { Code = $"{intType} {a}; std::memcpy(&{a}, &{args[0]}, sizeof({intType}));" });
                        block.Instructions.Add(new IRRawCpp
                            { Code = $"{intType} {b}; std::memcpy(&{b}, &{args[1]}, sizeof({intType}));" });
                        block.Instructions.Add(new IRRawCpp
                            { Code = $"{intType} __bw_r{tempCounter} = {a} {cppOp} {b};" });
                        block.Instructions.Add(new IRRawCpp
                            { Code = $"{cppType} {tmp}; std::memcpy(&{tmp}, &__bw_r{tempCounter}, sizeof({cppType}));" });
                    }
                    else
                    {
                        block.Instructions.Add(new IRRawCpp
                            { Code = $"{tmp} = ({cppType})({args[0]} {cppOp} {args[1]});" });
                    }
                    stack.Push(tmp);
                    irCall.Arguments.Clear();
                    args.Clear();
                    return;
                }
                else if (cppOp != null && args.Count == 1)
                {
                    var tmp = $"__t{tempCounter++}";
                    var cppType = CppNameMapper.GetCppTypeName(
                        ResolveTypeRefOperand(constrainedType));
                    block.Instructions.Add(new IRRawCpp
                        { Code = $"{tmp} = ({cppType})({cppOp}{args[0]});" });
                    stack.Push(tmp);
                    irCall.Arguments.Clear();
                    args.Clear();
                    return;
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
            var retType = ResolveCallReturnType(methodRef);
            irCall.ResultTypeCpp = retType;
            stack.Push(tmp);

            // Interlocked _obj methods return void* — cast to expected type
            if (isInterlockedObj && retType.EndsWith("*") && retType != "void*")
            {
                var castTmp = $"__t{tempCounter++}";
                irCall.ResultVar = tmp; // keep tmp for intermediate void* result
                irCall.ResultTypeCpp = "void*";
                block.Instructions.Add(irCall);
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{castTmp} = ({retType}){tmp};",
                    ResultVar = castTmp,
                    ResultTypeCpp = retType,
                });
                stack.Pop(); // remove tmp
                stack.Push(castTmp);
                return; // already added irCall
            }
        }
        block.Instructions.Add(irCall);
    }

    /// <summary>
    /// Resolve the C++ return type for a method call, handling both class-level
    /// and method-level generic parameters.
    /// </summary>
    private string ResolveCallReturnType(MethodReference methodRef)
    {
        var returnType = methodRef.ReturnType;

        // Resolve method-level generic parameters (!!0, !!1, etc.)
        // Handles both direct GenericParameter and GenericInstanceType with method generic args
        if (methodRef is Mono.Cecil.GenericInstanceMethod gim)
        {
            returnType = SubstituteMethodGenericParams(returnType, gim);
        }

        // Resolve class-level generic parameters (!0, !1, etc.)
        var resolvedName = ResolveGenericTypeRef(returnType, methodRef.DeclaringType);

        // Fallback: if still contains unresolved generic params (!0, !!0), use Object*
        if (resolvedName.Contains("!!") || System.Text.RegularExpressions.Regex.IsMatch(resolvedName, @"![\d]"))
            return "cil2cpp::Object*";

        return CppNameMapper.GetCppTypeForDecl(resolvedName);
    }

    /// <summary>
    /// Cast function call arguments to their expected parameter types using C-style casts.
    /// Handles derived→base pointer casts in the flat struct model (no C++ inheritance).
    /// Only casts pointer types to avoid breaking value type arguments.
    /// </summary>
    private void CastArgumentsToParameterTypes(List<string> args, MethodReference methodRef, bool isIcall = false)
    {
        for (int i = 0; i < args.Count && i < methodRef.Parameters.Count; i++)
        {
            var paramType = methodRef.Parameters[i].ParameterType;

            // ByReference (ref/out) parameters: resolve element type and cast to T*
            // Needed for BCL patterns where conv.u converts pointers to uintptr_t
            // before passing to ref parameters (e.g. Enum.TryParseByValueOrName)
            if (paramType is ByReferenceType byRefType)
            {
                var elemType = byRefType.ElementType;
                // Resolve method-level generic parameters (direct and nested)
                if (methodRef is GenericInstanceMethod gim3)
                    elemType = SubstituteMethodGenericParams(elemType, gim3);
                // Resolve type-level generic parameters
                else if (elemType is GenericParameter gp3
                    && gp3.Type == GenericParameterType.Type
                    && methodRef.DeclaringType is GenericInstanceType git3
                    && gp3.Position < git3.GenericArguments.Count)
                    elemType = git3.GenericArguments[gp3.Position];
                var elemResolved = ResolveGenericTypeRef(elemType, methodRef.DeclaringType);
                if (elemResolved.Contains("!!") || System.Text.RegularExpressions.Regex.IsMatch(elemResolved, @"![\d]"))
                    continue;
                var elemCpp = CppNameMapper.GetCppTypeForDecl(elemResolved);
                // ByRef always adds one pointer level: ref int → int32_t*, ref string → String**
                var ptrType = elemCpp + "*";
                if (args[i] != "nullptr" && args[i] != "0" && !args[i].StartsWith($"({ptrType})"))
                    args[i] = $"({ptrType}){args[i]}";
                continue;
            }

            // Resolve generic parameters (both direct GenericParameter and GenericInstanceType
            // containing method-level generic params like EnumInfo<TStorage>)
            if (methodRef is GenericInstanceMethod gim2)
            {
                paramType = SubstituteMethodGenericParams(paramType, gim2);
            }

            // Skip value types (enums, structs) — use Cecil for authoritative check
            bool isValueParam = false;
            try { isValueParam = paramType.Resolve()?.IsValueType == true; }
            catch { isValueParam = CppNameMapper.IsValueType(paramType.FullName); }
            if (isValueParam) continue;

            var resolvedName = ResolveGenericTypeRef(paramType, methodRef.DeclaringType);

            // Skip if contains unresolved generic params
            if (resolvedName.Contains("!!") || System.Text.RegularExpressions.Regex.IsMatch(resolvedName, @"![\d]"))
                continue;

            var expectedType = CppNameMapper.GetCppTypeForDecl(resolvedName);

            // Only cast pointer parameters (reference types)
            if (!expectedType.EndsWith("*")) continue;

            // Skip if it's a void pointer
            if (expectedType == "void*") continue;

            // Skip if the expected type contains unresolved generic type params (e.g. System_Span_1_T)
            var rawExpected = expectedType.TrimEnd('*').Trim();
            if (rawExpected.EndsWith("_T") || rawExpected.EndsWith("_T_")
                || rawExpected.Contains("_T_") || rawExpected.Contains("`"))
                continue;

            // Skip common primitives that don't need casting
            if (args[i] == "nullptr" || args[i] == "0") continue;

            // Don't cast if the arg is a literal or numeric expression
            if (args[i].Length == 0) continue;
            if (char.IsDigit(args[i][0]) || args[i][0] == '-' || args[i][0] == '\'') continue;

            // Skip if arg already has the right cast
            if (args[i].StartsWith($"({expectedType})")) continue;

            // Apply cast with void* intermediate for flat struct model (no C++ inheritance)
            args[i] = $"({expectedType})(void*){args[i]}";
        }
    }

    private void EmitNewObj(IRBasicBlock block, Stack<string> stack, MethodReference ctorRef,
        ref int tempCounter)
    {
        // Special: BCL exception types (System.Exception, InvalidOperationException, etc.)
        if (TryEmitExceptionNewObj(block, stack, ctorRef, ref tempCounter))
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
        // Check for disambiguated overload name (constructors with different param types)
        // Must resolve generic parameters (e.g., T → System.Boolean in StrongBox<bool>)
        {
            var ilParamKey = string.Join(",", ctorRef.Parameters.Select(p =>
                ResolveGenericTypeRef(p.ParameterType, ctorRef.DeclaringType)));
            var lookupKey = $"{ctorName}|{ilParamKey}";
            if (_module.DisambiguatedMethodNames.TryGetValue(lookupKey, out var disambiguatedCtor))
                ctorName = disambiguatedCtor;
        }

        // Collect constructor arguments
        var args = new List<string>();
        for (int i = 0; i < ctorRef.Parameters.Count; i++)
        {
            args.Add(stack.Count > 0 ? stack.Pop() : "0");
        }
        args.Reverse();

        // Cast constructor arguments to expected parameter types
        // (handles derived→base pointer casts in flat struct model)
        CastArgumentsToParameterTypes(args, ctorRef);

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
            block.Instructions.Add(new IRRawCpp { Code = $"{tmp}->f_message = (cil2cpp::String*){args[0]};" });
        if (paramCount >= 2)
            block.Instructions.Add(new IRRawCpp { Code = $"{tmp}->f_innerException = (cil2cpp::Exception*){args[1]};" });

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

    /// <summary>
    /// Ensures VTable is built for this type, recursively building base type first.
    /// Needed because base types (e.g. System.Object from BCL) may appear
    /// after derived types in the module list.
    /// </summary>
    private void BuildVTableRecursive(IRType irType, HashSet<IRType> built)
    {
        if (!built.Add(irType)) return;
        if (irType.BaseType != null)
            BuildVTableRecursive(irType.BaseType, built);
        BuildVTable(irType);
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
    private string ResolveGenericTypeRef(TypeReference typeRef, TypeReference declaringType)
    {
        // Resolve generic parameters — first check method-level map, then type-level
        if (typeRef is GenericParameter gp)
        {
            // Method-level generic param (from ConvertMethodBodyWithGenerics)
            if (_activeTypeParamMap != null && _activeTypeParamMap.TryGetValue(gp.Name, out var mapped))
                return mapped;
            // Type-level generic param (from GenericInstanceType declaring type)
            if (declaringType is GenericInstanceType git && gp.Position < git.GenericArguments.Count)
            {
                var resolved = git.GenericArguments[gp.Position];
                // Recursively resolve if the type arg is itself a GenericParameter
                // (e.g., IComparable<!!0>.CompareTo(!0) where !!0 = TStorage → System.Byte)
                if (resolved is GenericParameter gp3 && _activeTypeParamMap != null
                    && _activeTypeParamMap.TryGetValue(gp3.Name, out var mapped3))
                    return mapped3;
                return resolved.FullName;
            }
            return typeRef.FullName;
        }

        if (typeRef is GenericInstanceType returnGit)
        {
            // Generic instance with generic params in arguments — resolve each argument
            bool anyResolved = false;
            var argNames = new List<string>();
            foreach (var arg in returnGit.GenericArguments)
            {
                if (arg is GenericParameter gp2)
                {
                    if (_activeTypeParamMap != null && _activeTypeParamMap.TryGetValue(gp2.Name, out var mapped2))
                    {
                        argNames.Add(mapped2);
                        anyResolved = true;
                        continue;
                    }
                    if (declaringType is GenericInstanceType git2 && gp2.Position < git2.GenericArguments.Count)
                    {
                        var resolvedArg = git2.GenericArguments[gp2.Position];
                        // Recursively resolve nested GenericParameter
                        if (resolvedArg is GenericParameter gp4 && _activeTypeParamMap != null
                            && _activeTypeParamMap.TryGetValue(gp4.Name, out var mapped4))
                            argNames.Add(mapped4);
                        else
                            argNames.Add(resolvedArg.FullName);
                        anyResolved = true;
                        continue;
                    }
                }
                argNames.Add(arg.FullName);
            }
            if (anyResolved)
                return $"{returnGit.ElementType.FullName}<{string.Join(",", argNames)}>";
        }

        return typeRef.FullName;
    }

    /// <summary>
    /// Check if a resolved type name string still contains unresolved generic parameter names.
    /// Used to prevent registering generic method specializations with unresolved type args.
    /// </summary>
    private static bool ContainsUnresolvedGenericParam(string typeName)
    {
        // Check for common patterns: single-char params (T, K, V), multi-char (TKey, TValue, TStorage)
        // Only flag if the name looks like an IL type name (no namespace dots) that would be a GP name
        if (System.Text.RegularExpressions.Regex.IsMatch(typeName, @"(^|[\[<,])![\d]"))
            return true;
        return false;
    }

    /// <summary>
    /// Substitute method-level generic parameters in a TypeReference using a GenericInstanceMethod.
    /// Handles both direct GenericParameter and GenericInstanceType containing method generic params.
    /// E.g., EnumInfo`1&lt;TStorage&gt; with gim=GetNameInlined&lt;byte&gt; → resolves TStorage to System.Byte.
    /// </summary>
    private static TypeReference SubstituteMethodGenericParams(TypeReference typeRef, GenericInstanceMethod gim)
    {
        if (typeRef is GenericParameter gp)
        {
            if (gp.Type == GenericParameterType.Method && gp.Position < gim.GenericArguments.Count)
                return gim.GenericArguments[gp.Position];
            return typeRef;
        }

        if (typeRef is GenericInstanceType git)
        {
            bool anySubstituted = false;
            var newArgs = new TypeReference[git.GenericArguments.Count];
            for (int j = 0; j < git.GenericArguments.Count; j++)
            {
                var arg = git.GenericArguments[j];
                if (arg is GenericParameter gp2 && gp2.Type == GenericParameterType.Method
                    && gp2.Position < gim.GenericArguments.Count)
                {
                    newArgs[j] = gim.GenericArguments[gp2.Position];
                    anySubstituted = true;
                }
                else
                {
                    newArgs[j] = arg;
                }
            }
            if (anySubstituted)
            {
                var newGit = new GenericInstanceType(git.ElementType);
                foreach (var a in newArgs)
                    newGit.GenericArguments.Add(a);
                return newGit;
            }
        }

        if (typeRef is ByReferenceType byRef)
        {
            var resolved = SubstituteMethodGenericParams(byRef.ElementType, gim);
            if (resolved != byRef.ElementType)
                return new ByReferenceType(resolved);
        }

        return typeRef;
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
    private bool ParameterTypesMatchRef(IRMethod irMethod, MethodReference methodRef)
    {
        if (irMethod.Parameters.Count != methodRef.Parameters.Count) return false;
        for (int i = 0; i < irMethod.Parameters.Count; i++)
        {
            var irTypeName = irMethod.Parameters[i].ILTypeName;
            var refTypeName = ResolveMethodRefParamType(methodRef, i);
            if (irTypeName != refTypeName) return false;
        }
        return true;
    }

    /// <summary>
    /// Resolve a method reference's parameter type to a concrete IL type name.
    /// Handles generic parameters by resolving through:
    /// 1. The declaring GenericInstanceType (e.g., IComparable&lt;byte&gt;.CompareTo(T) → T=byte)
    /// 2. The active type parameter map (for generic method specializations where
    ///    the GIT arguments themselves are generic parameters, e.g., IComparable&lt;TStorage&gt;)
    /// </summary>
    private string ResolveMethodRefParamType(MethodReference methodRef, int paramIndex)
    {
        var paramType = methodRef.Parameters[paramIndex].ParameterType;

        // Non-generic parameter — return as-is
        if (paramType is not GenericParameter gp)
            return paramType.FullName;

        // Try resolving through declaring GenericInstanceType
        if (methodRef.DeclaringType is GenericInstanceType git
            && gp.Position < git.GenericArguments.Count)
        {
            var resolved = git.GenericArguments[gp.Position];
            // If the resolved arg is ALSO a generic parameter, resolve through active map
            if (resolved is GenericParameter gp2 && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gp2.Name, out var mapped))
                return mapped;
            return resolved.FullName;
        }

        // Try resolving directly through active type parameter map
        if (_activeTypeParamMap != null && _activeTypeParamMap.TryGetValue(gp.Name, out var directMapped))
            return directMapped;

        return paramType.FullName;
    }

    private void EmitCctorGuardIfNeeded(IRBasicBlock block, string ilTypeName, string typeCppName)
    {
        if (_typeCache.TryGetValue(ilTypeName, out var irType) && irType.HasCctor)
        {
            block.Instructions.Add(new IRStaticCtorGuard { TypeCppName = typeCppName });
        }
    }

    /// <summary>
    /// Map static abstract interface method names to C++ operator symbols.
    /// Used for constrained calls on primitive types (IBitwiseOperators, IComparisonOperators, etc.)
    /// where the explicit interface implementation isn't compiled.
    /// </summary>
    private static string? TryGetIntrinsicOperator(string methodName) => methodName switch
    {
        // IBitwiseOperators
        "op_BitwiseOr" => "|",
        "op_BitwiseAnd" => "&",
        "op_ExclusiveOr" => "^",
        "op_OnesComplement" => "~",
        // IAdditionOperators / ISubtractionOperators
        "op_Addition" or "op_CheckedAddition" => "+",
        "op_Subtraction" or "op_CheckedSubtraction" => "-",
        // IMultiplyOperators / IDivisionOperators
        "op_Multiply" or "op_CheckedMultiply" => "*",
        "op_Division" or "op_CheckedDivision" => "/",
        // IModulusOperators
        "op_Modulus" => "%",
        // IShiftOperators
        "op_LeftShift" => "<<",
        "op_RightShift" or "op_UnsignedRightShift" => ">>",
        // IComparisonOperators
        "op_LessThan" => "<",
        "op_LessThanOrEqual" => "<=",
        "op_GreaterThan" => ">",
        "op_GreaterThanOrEqual" => ">=",
        // IEqualityOperators
        "op_Equality" => "==",
        "op_Inequality" => "!=",
        // IUnaryOperators
        "op_UnaryPlus" => "+",
        "op_UnaryNegation" or "op_CheckedUnaryNegation" => "-",
        _ => null
    };

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
