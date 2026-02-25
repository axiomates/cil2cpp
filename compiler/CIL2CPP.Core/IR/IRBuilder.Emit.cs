using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private void EmitStoreLocal(IRBasicBlock block, Stack<StackEntry> stack, IRMethod method, int index)
    {
        var val = stack.PopExpr();
        // For pointer-type locals, add explicit cast to handle implicit upcasts
        // (e.g., Dog* → Animal*) since generated C++ structs don't use C++ inheritance.
        // Also handles void* locals: in .NET, IntPtr/UIntPtr and void* are interchangeable
        // (native int = pointer), but in C++ uintptr_t/intptr_t are integer types incompatible
        // with void*. The C-style cast (void*)val handles both pointer→void* (implicit) and
        // uintptr_t→void* (reinterpret_cast semantics).
        if (index < method.Locals.Count)
        {
            var local = method.Locals[index];
            if (local.CppTypeName.EndsWith("*"))
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

    /// <summary>
    /// IL add/sub on typed pointers uses byte offsets, but C++ pointer arithmetic
    /// scales by element size. When one operand is a typed pointer (element size > 1),
    /// cast through uint8_t* for correct byte-level arithmetic.
    /// Returns true if pointer arithmetic was emitted, false to fall through to normal emit.
    /// </summary>
    private bool TryEmitPointerArithmetic(IRBasicBlock block, Stack<StackEntry> stack, string op,
        IRMethod irMethod, ref int tempCounter)
    {
        if (stack.Count < 2) return false;

        var items = stack.ToArray(); // [0]=top (right), [1]=second from top (left)
        var right = items[0].Expr;
        var left = items.Length > 1 ? items[1].Expr : "0";

        // Prefer StackEntry type info, fall back to string heuristic
        var leftPtrType = ClassifyPointerType(items.Length > 1 ? items[1].CppType ?? "" : "")
                          ?? GetTypedPointerType(left, irMethod);
        var rightPtrType = ClassifyPointerType(items[0].CppType ?? "")
                           ?? GetTypedPointerType(right, irMethod);

        // Neither is a typed pointer → not pointer arithmetic
        if (leftPtrType == null && rightPtrType == null) return false;

        // Pop the two operands
        stack.Pop(); // right
        stack.Pop(); // left

        var tmp = $"__t{tempCounter++}";

        if (leftPtrType != null && rightPtrType != null && op == "-")
        {
            // ptr - ptr: IL yields byte distance, C++ yields element count.
            // Cast both to uint8_t* so subtraction gives byte count.
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{tmp} = (intptr_t)((uint8_t*){left} - (uint8_t*){right});",
                ResultVar = tmp,
                ResultTypeCpp = "intptr_t",
            });
            // Result is intptr_t, not a pointer — don't track
        }
        else if (leftPtrType != null)
        {
            // ptr +/- integer: byte-level offset. Cast through uint8_t*.
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{tmp} = ({leftPtrType})((uint8_t*){left} {op} {right});",
                ResultVar = tmp,
                ResultTypeCpp = leftPtrType,
            });
            _tempPtrTypes[tmp] = leftPtrType;
        }
        else // rightPtrType != null && op == "+"
        {
            // integer + ptr: byte-level offset. Cast through uint8_t*.
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{tmp} = ({rightPtrType})((uint8_t*){right} {op} {left});",
                ResultVar = tmp,
                ResultTypeCpp = rightPtrType,
            });
            _tempPtrTypes[tmp] = rightPtrType;
        }

        // Push with pointer type so downstream consumers (comparisons, further arithmetic) know it's a pointer
        var resultType = leftPtrType ?? rightPtrType;
        stack.Push(resultType != null ? new StackEntry(tmp, resultType) : new StackEntry(tmp));
        return true;
    }

    /// <summary>
    /// Check if an expression is a typed pointer with element size > 1 byte.
    /// Returns the pointer C++ type (e.g. "char16_t*") or null.
    /// </summary>
    private string? GetTypedPointerType(string expr, IRMethod irMethod)
    {
        // Check local variables: loc_N
        if (expr.StartsWith("loc_") && int.TryParse(expr["loc_".Length..], out var locIdx))
        {
            var local = irMethod.Locals.FirstOrDefault(l => l.Index == locIdx);
            if (local != null)
                return ClassifyPointerType(local.CppTypeName);
        }

        // Check parameters by name (e.g., "pChars", "ptr", etc.)
        var param = irMethod.Parameters.FirstOrDefault(p => p.CppName == expr);
        if (param != null)
            return ClassifyPointerType(param.CppTypeName);

        // Check previously computed temp pointer types
        if (_tempPtrTypes.TryGetValue(expr, out var tempType))
            return tempType;

        // Check explicit cast patterns: (type*)expr
        if (expr.StartsWith("(") && expr.Contains("*)"))
        {
            var closeIdx = expr.IndexOf("*)");
            if (closeIdx > 1)
            {
                var castType = expr[1..closeIdx] + "*";
                return ClassifyPointerType(castType);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the type if it's a pointer that needs special arithmetic handling, null otherwise.
    /// Includes void* because void* arithmetic is invalid in standard C++ (need uint8_t* cast).
    /// </summary>
    private static string? ClassifyPointerType(string cppTypeName)
    {
        if (!cppTypeName.EndsWith("*")) return null;
        // All pointer types need special arithmetic handling to ensure:
        // 1. Correct byte-level offset (via uint8_t* cast for multi-byte elements)
        // 2. Proper result type tracking (even for byte-sized elements like uint8_t*)
        return cppTypeName;
    }

    private void EmitBinaryOp(IRBasicBlock block, Stack<StackEntry> stack, string op, ref int tempCounter,
        bool isUnsigned = false, IRMethod? method = null)
    {
        var rightEntry = stack.PopEntry();
        var leftEntry = stack.PopEntry();
        var right = rightEntry.Expr;
        var left = leftEntry.Expr;

        // cgt.un with nullptr: "ptr > nullptr" is invalid in C++.
        // IL uses "ldloc; ldnull; cgt.un" as an idiom for "ptr != null".
        if (op == ">" && (right == "nullptr" || left == "nullptr"))
        {
            op = "!=";
            isUnsigned = false; // no unsigned cast needed for null check
        }
        // Similarly, clt.un with nullptr is "nullptr != ptr" pattern
        if (op == "<" && (right == "nullptr" || left == "nullptr"))
        {
            op = "!=";
            isUnsigned = false;
        }

        // For pointer comparisons, cast both sides to (void*) since flat struct model
        // doesn't have C++ inheritance between generated types (e.g., EqualityComparer* vs IEqualityComparer*).
        // Check BOTH left and right operands — either side being a pointer triggers the cast.
        if ((op == "==" || op == "!=")
            && left != "nullptr" && right != "nullptr"
            && (leftEntry.IsPointer || rightEntry.IsPointer
                || (method != null && (IsPointerTypedOperand(left, method)
                                       || IsPointerTypedOperand(right, method)))))
        {
            left = $"(void*){left}";
            right = $"(void*){right}";
        }

        // Bitwise ops (&, |, ^) on pointer operands: C++ doesn't allow bitwise operations
        // on pointer types (MSVC C2296). IL treats native int and pointers interchangeably
        // for bitwise ops (e.g., alignment checks: (uintptr_t)ptr & 0xF).
        // Cast pointer operands to uintptr_t so the bitwise op is on integers.
        if (op is "&" or "|" or "^")
        {
            bool leftIsPtr = leftEntry.IsPointer
                || (method != null && IsPointerTypedOperand(left, method));
            bool rightIsPtr = rightEntry.IsPointer
                || (method != null && IsPointerTypedOperand(right, method));
            if (leftIsPtr)
                left = $"(uintptr_t){left}";
            if (rightIsPtr)
                right = $"(uintptr_t){right}";
        }

        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRBinaryOp
        {
            Left = left, Right = right, Op = op, ResultVar = tmp, IsUnsigned = isUnsigned
        });
        stack.Push(tmp);
    }

    private void EmitComparisonBranch(IRBasicBlock block, Stack<StackEntry> stack, string op, ILInstruction instr,
        bool isUnsigned = false, Dictionary<int, StackEntry[]>? branchTargetStacks = null, IRMethod? method = null)
    {
        var rightEntry = stack.PopEntry();
        var leftEntry = stack.PopEntry();
        var right = rightEntry.Expr;
        var left = leftEntry.Expr;

        // Unsigned branch with nullptr → rewrite to != (null check idiom)
        if (isUnsigned && (right == "nullptr" || left == "nullptr"))
        {
            op = "!=";
            isUnsigned = false;
        }

        // For pointer comparisons, cast both sides to (void*) since flat struct model
        // doesn't have C++ inheritance between generated types.
        // Check BOTH left and right operands — either side being a pointer triggers the cast.
        if ((op == "==" || op == "!=")
            && left != "nullptr" && right != "nullptr"
            && (leftEntry.IsPointer || rightEntry.IsPointer
                || (method != null && (IsPointerTypedOperand(left, method)
                                       || IsPointerTypedOperand(right, method)))))
        {
            left = $"(void*){left}";
            right = $"(void*){right}";
        }

        var target = (Instruction)instr.Operand!;
        // Save full stack snapshot for branch target (ternary pattern support)
        if (branchTargetStacks != null && stack.Count > 0 && !branchTargetStacks.ContainsKey(target.Offset))
            branchTargetStacks[target.Offset] = stack.Reverse().ToArray();
        string condition;
        if (isUnsigned && op == ">")
            condition = $"cil2cpp::unsigned_gt({left}, {right})";
        else if (isUnsigned && op == "<")
            condition = $"cil2cpp::unsigned_lt({left}, {right})";
        else if (isUnsigned && op == ">=")
            condition = $"cil2cpp::unsigned_ge({left}, {right})";
        else if (isUnsigned && op == "<=")
            condition = $"cil2cpp::unsigned_le({left}, {right})";
        else if (isUnsigned)
            condition = $"cil2cpp::to_unsigned({left}) {op} cil2cpp::to_unsigned({right})";
        else
            condition = $"{left} {op} {right}";
        block.Instructions.Add(new IRConditionalBranch
        {
            Condition = condition,
            TrueLabel = $"IL_{target.Offset:X4}"
        });
    }

    /// <summary>
    /// Checks if a comparison operand is a known pointer-typed variable (parameter, local, or temp).
    /// Used to add (void*) casts for pointer comparisons in the flat struct model.
    /// </summary>
    private static bool IsPointerTypedOperand(string operand, IRMethod method)
    {
        if (operand == "__this") return true;
        if (operand == "nullptr") return false;

        foreach (var param in method.Parameters)
        {
            if (param.CppName == operand && param.CppTypeName.EndsWith("*"))
                return true;
        }

        foreach (var local in method.Locals)
        {
            if (local.CppName == operand && local.CppTypeName.EndsWith("*"))
                return true;
        }

        // Check temp variable types from IR method type inference
        if (operand.StartsWith("__t") && method.TempVarTypes.TryGetValue(operand, out var tempType)
            && tempType.EndsWith("*"))
            return true;

        return false;
    }

    /// <summary>
    /// Gets the C++ pointer type of a known variable (parameter or local).
    /// Returns the type name (e.g., "SomeType*") or "void*" if not found.
    /// </summary>
    private static string GetOperandPointerType(string operand, IRMethod method)
    {
        foreach (var param in method.Parameters)
        {
            if (param.CppName == operand)
                return param.CppTypeName;
        }

        foreach (var local in method.Locals)
        {
            if (local.CppName == operand)
                return local.CppTypeName;
        }

        return "void*";
    }

    private void EmitMethodCall(IRBasicBlock block, Stack<StackEntry> stack, MethodReference methodRef,
        bool isVirtual, ref int tempCounter, TypeReference? constrainedType = null)
    {
        // ===== Compiler Intrinsics — emit inline C++ instead of function calls =====

        // INumber<T>.CreateTruncating<TOther>(TOther) — numeric cast intrinsic
        if (methodRef.Name == "CreateTruncating" && methodRef is GenericInstanceMethod
            && methodRef.Parameters.Count == 1 && !methodRef.HasThis)
        {
            var val = stack.PopExpr();
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

        // IUtfChar<T>.CastFrom / CastToUInt32 — static abstract interface method intrinsics
        // These are constrained calls to IUtfChar<Char> or IUtfChar<Byte>.
        // The IL is: constrained. System.Char; call IUtfChar<Char>::CastFrom(uint32)
        // The implementation is a simple type cast.
        if (constrainedType != null && methodRef.DeclaringType is GenericInstanceType gitUtf
            && gitUtf.ElementType.Name == "IUtfChar`1"
            && methodRef.Name is "CastFrom" or "CastToUInt32")
        {
            var val = stack.PopExpr();
            string targetType;
            if (methodRef.Name == "CastToUInt32")
            {
                targetType = "uint32_t";
            }
            else
            {
                targetType = CppNameMapper.GetCppTypeName(constrainedType.FullName);
            }
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{targetType} {tmp} = static_cast<{targetType}>({val});",
                ResultVar = tmp,
                ResultTypeCpp = targetType,
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
            var val = stack.PopExprOr("nullptr");
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
            var offset = stack.PopExpr();
            var ptrEntry = stack.PopEntry();
            var ptr = ptrEntry.Expr.Length > 0 ? ptrEntry.Expr : "nullptr";
            var tmp = $"__t{tempCounter++}";
            // HACK: pointer type unknown at this point — decltype preserves it
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {ptr} + {offset};",
                ResultVar = tmp,
                ResultTypeCpp = ptrEntry.CppType,
            });
            stack.Push(new StackEntry(tmp, ptrEntry.CppType));
            return;
        }

        // Unsafe.SkipInit<T>(out T) — JIT intrinsic that does nothing
        // IL body throws PlatformNotSupportedException as fallback for non-JIT,
        // but in AOT it should be a no-op (just suppress definite assignment check)
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "SkipInit")
        {
            if (stack.Count > 0) stack.Pop(); // discard the 'out' ref argument
            return;
        }

        // Unsafe.CopyBlockUnaligned / CopyBlock — memcpy intrinsics
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name is "CopyBlockUnaligned" or "CopyBlock")
        {
            var byteCount = stack.PopExpr();
            var source = stack.PopExprOr("nullptr");
            var dest = stack.PopExprOr("nullptr");
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"std::memcpy((void*){dest}, (void*){source}, (size_t){byteCount});"
            });
            return;
        }

        // Unsafe.InitBlockUnaligned / InitBlock — memset intrinsics
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name is "InitBlockUnaligned" or "InitBlock")
        {
            var byteCount = stack.PopExpr();
            var value = stack.PopExpr();
            var dest = stack.PopExprOr("nullptr");
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"std::memset((void*){dest}, (int){value}, (size_t){byteCount});"
            });
            return;
        }

        // Unsafe.ReadUnaligned<T>(ref byte) — unaligned read intrinsic
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "ReadUnaligned" && methodRef is GenericInstanceMethod gimRu
            && gimRu.GenericArguments.Count == 1 && methodRef.Parameters.Count == 1)
        {
            var src = stack.PopExprOr("nullptr");
            var typeArg = gimRu.GenericArguments[0];
            var resolvedType = typeArg is GenericParameter gpRu && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gpRu.Name, out var rRu) ? rRu : typeArg.FullName;
            var cppType = CppNameMapper.GetCppTypeName(resolvedType);
            if (cppType.EndsWith("*")) cppType = cppType.TrimEnd('*');
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{cppType} {tmp}; std::memcpy(&{tmp}, (void*){src}, sizeof({cppType}));",
                ResultVar = tmp,
                ResultTypeCpp = cppType,
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.WriteUnaligned<T>(ref byte, T) — unaligned write intrinsic
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "WriteUnaligned" && methodRef is GenericInstanceMethod gimWu
            && gimWu.GenericArguments.Count == 1 && methodRef.Parameters.Count == 2)
        {
            var value = stack.PopExpr();
            var dest = stack.PopExprOr("nullptr");
            var typeArg = gimWu.GenericArguments[0];
            var resolvedType = typeArg is GenericParameter gpWu && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gpWu.Name, out var rWu) ? rWu : typeArg.FullName;
            var cppType = CppNameMapper.GetCppTypeName(resolvedType);
            if (cppType.EndsWith("*")) cppType = cppType.TrimEnd('*');
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{{ {cppType} __wu_val = {value}; std::memcpy((void*){dest}, &__wu_val, sizeof({cppType})); }}"
            });
            return;
        }

        // Unsafe.AsRef<T>(in T) / Unsafe.AsRef<T>(void*) — ref identity / pointer cast
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "AsRef" && methodRef.Parameters.Count == 1)
        {
            // AsRef is identity (ref T → ref T) or (void* → ref T) — just pass through
            // The argument is already a pointer in our model
            // No-op: leave stack as is
            return;
        }

        // Unsafe.Subtract<T>(ref T, int) — pointer arithmetic (subtract)
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "Subtract" && methodRef.Parameters.Count == 2)
        {
            var offset = stack.PopExpr();
            var ptr = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {ptr} - {offset};",
                ResultVar = tmp,
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.AreSame<T>(ref T, ref T) — pointer equality
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "AreSame" && methodRef.Parameters.Count == 2)
        {
            var right = stack.PopExprOr("nullptr");
            var left = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ({left} == {right});",
                ResultVar = tmp,
                ResultTypeCpp = "bool",
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.ByteOffset<T>(ref T, ref T) — byte difference between two pointers
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "ByteOffset" && methodRef.Parameters.Count == 2)
        {
            var right = stack.PopExprOr("nullptr");
            var left = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = (intptr_t)((uint8_t*){right} - (uint8_t*){left});",
                ResultVar = tmp,
                ResultTypeCpp = "intptr_t",
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.IsNullRef<T>(ref T) — null reference check
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "IsNullRef" && methodRef.Parameters.Count == 1)
        {
            var ptr = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ({ptr} == nullptr);",
                ResultVar = tmp,
                ResultTypeCpp = "bool",
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.NullRef<T>() — null reference
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "NullRef" && methodRef.Parameters.Count == 0)
        {
            stack.Push("nullptr");
            return;
        }

        // Unsafe.AddByteOffset<T>(ref T, IntPtr/nuint) — byte offset addition
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "AddByteOffset" && methodRef.Parameters.Count == 2)
        {
            var offset = stack.PopExpr();
            var ptr = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = (decltype({ptr}))((uint8_t*){ptr} + (intptr_t){offset});",
                ResultVar = tmp,
                // ResultTypeCpp left null — decltype preserves source pointer type
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.SubtractByteOffset<T>(ref T, IntPtr/nuint) — byte offset subtraction
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "SubtractByteOffset" && methodRef.Parameters.Count == 2)
        {
            var offset = stack.PopExpr();
            var ptr = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = (decltype({ptr}))((uint8_t*){ptr} - (intptr_t){offset});",
                ResultVar = tmp,
            });
            stack.Push(tmp);
            return;
        }

        // Unsafe.As<T>(object) — cast object to T (single type arg version)
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "As" && methodRef is GenericInstanceMethod gimAs1
            && gimAs1.GenericArguments.Count == 1 && methodRef.Parameters.Count == 1)
        {
            var val = stack.PopExprOr("nullptr");
            var toTypeArg = gimAs1.GenericArguments[0];
            var resolvedTo = toTypeArg is GenericParameter gpAs1 && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gpAs1.Name, out var rAs1) ? rAs1 : toTypeArg.FullName;
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

        // Unsafe.AsPointer<T>(ref T) — ref to void* cast
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "AsPointer" && methodRef.Parameters.Count == 1)
        {
            var src = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = (void*){src};",
                ResultVar = tmp,
                ResultTypeCpp = "void*",
            });
            stack.Push(new StackEntry(tmp, "void*"));
            return;
        }
        // Debug: catch AsPointer with different declaring type
        if (methodRef.Name == "AsPointer" && methodRef.Parameters.Count == 1)
        {
            Console.Error.WriteLine($"[DIAG] AsPointer not intercepted: DeclaringType='{methodRef.DeclaringType.FullName}'");
        }

        // Unsafe.Unbox<T>(object) — unbox to ref T
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "Unbox" && methodRef is GenericInstanceMethod gimUnbox
            && gimUnbox.GenericArguments.Count == 1)
        {
            var obj = stack.PopExprOr("nullptr");
            var typeArg = gimUnbox.GenericArguments[0];
            var resolvedType = typeArg is GenericParameter gpUb && _activeTypeParamMap != null
                && _activeTypeParamMap.TryGetValue(gpUb.Name, out var rUb) ? rUb : typeArg.FullName;
            var cppType = CppNameMapper.GetCppTypeName(resolvedType);
            if (!cppType.EndsWith("*")) cppType += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ({cppType})cil2cpp::unbox({obj});",
                ResultVar = tmp,
                ResultTypeCpp = cppType,
            });
            stack.Push(tmp);
            return;
        }

        // MemoryMarshal JIT intrinsics — their IL bodies use Unsafe.* which are also intrinsics
        if (methodRef.DeclaringType.FullName == "System.Runtime.InteropServices.MemoryMarshal"
            && methodRef is GenericInstanceMethod mmGim)
        {
            if (TryEmitMemoryMarshalIntrinsic(block, stack, methodRef, mmGim, ref tempCounter))
                return;
        }
        // SIMD IsSupported/IsHardwareAccelerated/get_Count → return 0 (disable SIMD, force scalar fallback)
        // Covers: Vector64/128/256/512, System.Numerics.Vector (not Vector2/3/4), all X86/Wasm intrinsics
        {
            var declType = methodRef.DeclaringType.FullName;
            bool isSimdType = declType.StartsWith("System.Runtime.Intrinsics.")
                || declType == "System.Numerics.Vector"
                || declType.StartsWith("System.Numerics.Vector`");

            if (isSimdType && methodRef.Name is "get_IsSupported" or "get_IsHardwareAccelerated" or "get_Count")
            {
                if (methodRef.HasThis && stack.Count > 0) stack.Pop();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp { Code = $"int32_t {tmp} = 0;", ResultVar = tmp, ResultTypeCpp = "int32_t" });
                stack.Push(tmp);
                return;
            }

            // SIMD operations → stub as no-ops (unreachable: guarded by IsSupported==false)
            if (isSimdType)
            {
                for (int i = 0; i < methodRef.Parameters.Count; i++)
                    if (stack.Count > 0) stack.Pop();
                if (methodRef.HasThis && stack.Count > 0) stack.Pop();
                if (!IsVoidReturnType(methodRef.ReturnType))
                {
                    var tmp = $"__t{tempCounter++}";
                    var retType = ResolveCallReturnType(methodRef);
                    block.Instructions.Add(new IRRawCpp { Code = $"{retType} {tmp} = {{}}; // SIMD stub", ResultVar = tmp, ResultTypeCpp = retType });
                    stack.Push(tmp);
                }
                return;
            }
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
            var elemCppType = CppNameMapper.MangleTypeNameClean(resolvedElem);
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
            var range = stack.PopExprOr("{}");
            var arr = stack.PopExprOr("nullptr");
            var startTmp = $"__t{tempCounter++}";
            var endTmp = $"__t{tempCounter++}";
            var lenTmp = $"__t{tempCounter++}";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {startTmp} = {range}.f__start.f__value < 0 " +
                       $"? {range}.f__start.f__value + cil2cpp::array_length({arr}) + 1 " +
                       $": {range}.f__start.f__value;",
                ResultVar = startTmp,
                ResultTypeCpp = "int32_t",
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {endTmp} = {range}.f__end.f__value < 0 " +
                       $"? {range}.f__end.f__value + cil2cpp::array_length({arr}) + 1 " +
                       $": {range}.f__end.f__value;",
                ResultVar = endTmp,
                ResultTypeCpp = "int32_t",
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {lenTmp} = {endTmp} - {startTmp};",
                ResultVar = lenTmp,
                ResultTypeCpp = "int32_t",
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::array_get_subarray({arr}, {startTmp}, {lenTmp});",
                ResultVar = tmp,
                ResultTypeCpp = "cil2cpp::Array*",
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
            var source = stack.PopExprOr("{}");
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
                    if (!IsUnresolvedElementType(elemArgName))
                    {
                        var elemCpp = CppNameMapper.GetCppTypeForDecl(elemArgName);
                        // array_data returns void*; cast to element pointer type
                        // For reference types (String*), need String** since array stores pointers
                        elemPtrType = elemCpp + "*";
                    }
                }
                // Array → Span/ReadOnlySpan: construct from array data + length
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{retCpp} {tmp} = {{0}}; " +
                           $"{tmp}.f_reference = ({elemPtrType})cil2cpp::array_data({source}); " +
                           $"{tmp}.f_length = cil2cpp::array_length({source});",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
            }
            else
            {
                // Span → ReadOnlySpan (same layout): copy fields
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{retCpp} {tmp} = {{0}}; " +
                           $"{tmp}.f_reference = {source}.f_reference; " +
                           $"{tmp}.f_length = {source}.f_length;",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
            }
            stack.Push(tmp);
            return;
        }

        // Span<T>/ReadOnlySpan<T> constructors — JIT intrinsic bodies can't be AOT compiled
        // Intercept .ctor(T[], int, int), .ctor(T[]), and .ctor(void*, int)
        if (methodRef.Name == ".ctor" && methodRef.HasThis
            && (methodRef.DeclaringType.FullName.StartsWith("System.Span`1")
                || methodRef.DeclaringType.FullName.StartsWith("System.ReadOnlySpan`1")))
        {
            // Determine element type for pointer casts
            // For reference types (e.g. String), the array stores pointers, so elemPtrType = String**
            var elemPtrType = "void*";
            if (methodRef.DeclaringType is GenericInstanceType spanCtorGit
                && spanCtorGit.GenericArguments.Count > 0)
            {
                var elemArgName = ResolveGenericTypeRef(spanCtorGit.GenericArguments[0],
                    methodRef.DeclaringType);
                if (!IsUnresolvedElementType(elemArgName))
                {
                    var elemCpp = CppNameMapper.GetCppTypeForDecl(elemArgName);
                    elemPtrType = elemCpp + "*";
                }
            }

            // Helper to emit field access — thisPtr can be &loc (ldloca) or a pointer
            // Wrap in parens to handle &loc correctly: (&loc)->f_x
            string spanAccess(string thisPtr, string field) => $"({thisPtr})->{field}";

            if (methodRef.Parameters.Count == 3)
            {
                // .ctor(T[] array, int start, int length)
                var length = stack.PopExpr();
                var start = stack.PopExpr();
                var array = stack.PopExprOr("nullptr");
                var thisPtr = stack.PopExprOr("__this");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{spanAccess(thisPtr, "f_reference")} = ({elemPtrType})cil2cpp::array_data({array}) + {start}; " +
                           $"{spanAccess(thisPtr, "f_length")} = {length};"
                });
                return;
            }
            else if (methodRef.Parameters.Count == 1
                && methodRef.Parameters[0].ParameterType.IsArray)
            {
                // .ctor(T[] array)
                var array = stack.PopExprOr("nullptr");
                var thisPtr = stack.PopExprOr("__this");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({array}) {{ " +
                           $"{spanAccess(thisPtr, "f_reference")} = ({elemPtrType})cil2cpp::array_data({array}); " +
                           $"{spanAccess(thisPtr, "f_length")} = cil2cpp::array_length({array}); " +
                           $"}} else {{ " +
                           $"{spanAccess(thisPtr, "f_reference")} = nullptr; " +
                           $"{spanAccess(thisPtr, "f_length")} = 0; }}"
                });
                return;
            }
            else if (methodRef.Parameters.Count == 2
                && methodRef.Parameters[0].ParameterType.FullName is "System.Void*"
                    or "System.IntPtr")
            {
                // .ctor(void* pointer, int length)
                var length = stack.PopExpr();
                var pointer = stack.PopExprOr("nullptr");
                var thisPtr = stack.PopExprOr("__this");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{spanAccess(thisPtr, "f_reference")} = ({elemPtrType}){pointer}; " +
                           $"{spanAccess(thisPtr, "f_length")} = {length};"
                });
                return;
            }
            else if (methodRef.Parameters.Count == 2
                && methodRef.Parameters[0].ParameterType.IsByReference)
            {
                // .ctor(ref T reference, int length) — internal ByReference ctor
                var length = stack.PopExpr();
                var reference = stack.PopExprOr("nullptr");
                var thisPtr = stack.PopExprOr("__this");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{spanAccess(thisPtr, "f_reference")} = ({elemPtrType}){reference}; " +
                           $"{spanAccess(thisPtr, "f_length")} = {length};"
                });
                return;
            }
        }

        // Special: Delegate.Invoke — emit IRDelegateInvoke instead of normal call
        var declaringCacheKey = ResolveCacheKey(methodRef.DeclaringType);
        if (methodRef.Name == "Invoke" && methodRef.HasThis
            && _typeCache.TryGetValue(declaringCacheKey, out var invokeType)
            && invokeType.IsDelegate)
        {
            var invokeArgs = new List<string>();
            for (int i = 0; i < methodRef.Parameters.Count; i++)
                invokeArgs.Add(stack.PopExpr());
            invokeArgs.Reverse();

            var delegateExpr = stack.PopExprOr("nullptr");

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
            var fieldHandle = stack.PopExpr();
            var arr = stack.PopExprOr("nullptr");
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"std::memcpy(cil2cpp::array_data({arr}), {fieldHandle}, sizeof({fieldHandle}));"
            });
            return;
        }

        // JIT intrinsic: RuntimeHelpers.CreateSpan<T>(RuntimeFieldHandle)
        // Creates a ReadOnlySpan<T> pointing directly to static init data.
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && methodRef.Name == "CreateSpan"
            && methodRef is GenericInstanceMethod createSpanGim && createSpanGim.GenericArguments.Count == 1
            && methodRef.Parameters.Count == 1)
        {
            var fieldHandle = stack.PopExpr();
            var elemTypeRef = ResolveTypeRefOperand(createSpanGim.GenericArguments[0]);
            var elemCpp = CppNameMapper.GetCppTypeName(elemTypeRef);
            if (elemCpp.EndsWith("*")) elemCpp = elemCpp.TrimEnd('*');
            // Build ReadOnlySpan<T> type name
            var spanIlName = $"System.ReadOnlySpan`1<{elemTypeRef}>";
            var spanCpp = CppNameMapper.GetCppTypeName(spanIlName);
            if (spanCpp.EndsWith("*")) spanCpp = spanCpp.TrimEnd('*');
            var tmp = $"__t{tempCounter++}";
            // Look up the data blob size from the init data ID (__arr_init_N)
            // and compute element count = byteSize / sizeof(T)
            int blobBytes = 0;
            if (fieldHandle.StartsWith("__arr_init_") && int.TryParse(fieldHandle["__arr_init_".Length..], out var blobIdx)
                && blobIdx >= 0 && blobIdx < _module.ArrayInitDataBlobs.Count)
            {
                blobBytes = _module.ArrayInitDataBlobs[blobIdx].Data.Length;
            }
            // Get element size from C++ type name for compile-time length computation
            int elemSize = elemCpp switch
            {
                "int8_t" or "uint8_t" or "bool" => 1,
                "int16_t" or "uint16_t" or "char16_t" => 2,
                "int32_t" or "uint32_t" or "float" => 4,
                "int64_t" or "uint64_t" or "double" => 8,
                _ => 0 // unknown — fall back to runtime sizeof
            };
            string lengthExpr;
            if (blobBytes > 0 && elemSize > 0)
                lengthExpr = (blobBytes / elemSize).ToString();
            else
                lengthExpr = $"static_cast<int32_t>(sizeof({fieldHandle}) / sizeof({elemCpp}))";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{spanCpp} {tmp} = {{0}}; {tmp}.f_reference = ({elemCpp}*){fieldHandle}; {tmp}.f_length = {lengthExpr};",
                ResultVar = tmp,
                ResultTypeCpp = spanCpp,
            });
            stack.Push(tmp);
            return;
        }

        // JIT intrinsic: RuntimeHelpers.IsReferenceOrContainsReferences<T>()
        // Resolve to compile-time constant based on type argument T.
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && methodRef.Name == "IsReferenceOrContainsReferences"
            && methodRef is GenericInstanceMethod isRefGim && isRefGim.GenericArguments.Count == 1)
        {
            var typeArg = ResolveTypeRefOperand(isRefGim.GenericArguments[0]);
            bool isRef = IsReferenceOrContainsReferences(typeArg);
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {(isRef ? "true" : "false")};", ResultVar = tmp, ResultTypeCpp = "bool" });
            stack.Push(tmp);
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
            if (methodRef.Name is "op_Explicit" or "op_Implicit" or "op_CheckedExplicit" or "op_CheckedImplicit")
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
            else if (methodRef.Parameters.Count > 0)
            {
                var declTypeDef = methodRef.DeclaringType.Resolve();
                // Only for non-core RuntimeProvided types (Task, Thread, etc.)
                // CoreRuntime types (Object, String, Array) have their methods in the runtime,
                // so they don't need disambiguation at emit time.
                if (declTypeDef != null
                    && RuntimeProvidedTypes.Contains(declTypeDef.FullName)
                    && !CoreRuntimeTypes.Contains(declTypeDef.FullName))
                {
                    var baseName = methodRef.Name;
                    var overloadCount = declTypeDef.Methods.Count(m => m.Name == baseName);
                    if (overloadCount > 1)
                    {
                        var ilSuffix = string.Join("_", methodRef.Parameters.Select(p =>
                        {
                            var resolved = ResolveGenericTypeRef(p.ParameterType, methodRef.DeclaringType);
                            return CppNameMapper.MangleTypeName(resolved.TrimEnd('*', '&', ' '));
                        }));
                        funcName = $"{funcName}__{ilSuffix}";
                    }
                }
            }

            irCall.FunctionName = funcName;
        }

        // Collect arguments (in reverse order from stack)
        var args = new List<string>();
        for (int i = 0; i < methodRef.Parameters.Count; i++)
        {
            args.Add(stack.PopExpr());
        }
        args.Reverse();

        // Handle varargs call sites: package extra arguments into VarArgHandle.
        // The handle is stored separately to avoid CastArgumentsToParameterTypes
        // corrupting it (it would try to cast intptr_t using the first vararg param type).
        string? varargHandleArg = null;
        if (methodRef.CallingConvention == MethodCallingConvention.VarArg)
        {
            // Resolve method definition to get fixed param count
            int fixedCount;
            try
            {
                var resolved = methodRef.Resolve();
                fixedCount = resolved?.Parameters.Count ?? args.Count;
            }
            catch
            {
                fixedCount = args.Count; // fallback: treat all as fixed
            }

            if (fixedCount < args.Count)
            {
                var varargArgs = args.GetRange(fixedCount, args.Count - fixedCount);
                args.RemoveRange(fixedCount, args.Count - fixedCount);

                var vc = tempCounter++;
                var entries = new List<string>();
                for (int i = 0; i < varargArgs.Count; i++)
                {
                    var paramType = methodRef.Parameters[fixedCount + i].ParameterType;
                    var resolvedTypeName = ResolveGenericTypeRef(paramType, methodRef.DeclaringType);
                    var cppDeclType = CppNameMapper.GetCppTypeForDecl(resolvedTypeName);
                    var typeInfoName = CppNameMapper.MangleTypeNameClean(resolvedTypeName);

                    // Ensure TypeInfo exists for primitive vararg types
                    _module.RegisterPrimitiveTypeInfo(resolvedTypeName);

                    // Store vararg value in temporary variable
                    var tempName = $"__vararg_{vc}_{i}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{cppDeclType} {tempName} = {varargArgs[i]};"
                    });
                    entries.Add($"{{(void*)&{tempName}, &{typeInfoName}_TypeInfo}}");
                }

                // Build VarArgEntry array + VarArgHandle
                var entriesName = $"__vararg_entries_{vc}";
                var handleName = $"__vararg_handle_{vc}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::VarArgEntry {entriesName}[] = {{ {string.Join(", ", entries)} }}; "
                         + $"cil2cpp::VarArgHandle {handleName} = {{{entriesName}, {varargArgs.Count}}};"
                });

                varargHandleArg = $"reinterpret_cast<intptr_t>(&{handleName})";
            }
            else
            {
                // No vararg arguments at this call site — pass null handle
                varargHandleArg = "static_cast<intptr_t>(0)";
            }
        }

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
            var thisArg = stack.PopExprOr("__this");
            if (mappedName != null && methodRef.DeclaringType.FullName == "System.Object")
            {
                // BCL mapped Object methods expect cil2cpp::Object*
                thisArg = $"(cil2cpp::Object*){thisArg}";
            }
            else if (mappedName != null && methodRef.HasThis)
            {
                // BCL mapped value type instance methods:
                // 'this' is a pointer (&x). Most icalls expect a value (dereference),
                // but mutable value types like ArgIterator need the pointer (no dereference).
                bool isValueTarget = false;
                try { isValueTarget = methodRef.DeclaringType.Resolve()?.IsValueType == true; }
                catch { }
                if (isValueTarget
                    && methodRef.DeclaringType.FullName != "System.ArgIterator")
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

        // Append vararg handle AFTER CastArgumentsToParameterTypes (which only sees fixed args)
        if (varargHandleArg != null)
            args.Add(varargHandleArg);

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
                    irCall.FunctionName = overrideMethod.CppName;
                    isVirtual = false; // Suppress vtable dispatch
                    // Fix the 'this' argument — strip interface/base cast and re-cast
                    // to the constrained type (e.g. IDisposable* → Enumerator*)
                    if (irCall.Arguments.Count > 0)
                    {
                        var thisArg = irCall.Arguments[0];
                        var cppTypeName = GetMangledTypeNameForRef(constrainedType);
                        // Strip outermost C-style cast prefix using balanced paren matching
                        // (handles nested casts like ((Type*)expr) correctly)
                        if (thisArg.StartsWith("("))
                        {
                            int depth = 0;
                            int closeIdx = -1;
                            for (int ci = 0; ci < thisArg.Length; ci++)
                            {
                                if (thisArg[ci] == '(') depth++;
                                else if (thisArg[ci] == ')') { depth--; if (depth == 0) { closeIdx = ci; break; } }
                            }
                            if (closeIdx > 0 && closeIdx < thisArg.Length - 1)
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
                // Find the explicit interface implementation on the constrained type.
                // The method name in IL is the interface method name (e.g., "CastFrom"),
                // but explicit interface impls have names like "System.IUtfChar<System.Char>.CastFrom".
                // Match by exact name OR by suffix (after the last '.').
                var staticImpl = constrainedIrType.Methods.FirstOrDefault(m =>
                    m.IsStatic && MatchesMethodName(m.Name, methodRef.Name)
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
                        m.IsStatic && MatchesMethodName(m.Name, methodRef.Name)
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
                            { Code = $"{cppType} {tmp}; std::memcpy(&{tmp}, &__bw_r{tempCounter}, sizeof({cppType}));", ResultVar = tmp, ResultTypeCpp = cppType });
                    }
                    else
                    {
                        block.Instructions.Add(new IRRawCpp
                            { Code = $"{tmp} = ({cppType})({args[0]} {cppOp} {args[1]});", ResultVar = tmp, ResultTypeCpp = cppType });
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
                        { Code = $"{tmp} = ({cppType})({cppOp}{args[0]});", ResultVar = tmp, ResultTypeCpp = cppType });
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
            stack.Push(new StackEntry(tmp, retType));

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
                stack.Push(new StackEntry(castTmp, retType));
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

            // Skip value types (enums, structs) — use Cecil for authoritative check.
            // BUT: PointerType (e.g., System.UInt16*) must NOT be skipped — Cecil's
            // PointerType.Resolve() resolves the ELEMENT type (UInt16.IsValueType=true),
            // but the parameter is actually a pointer (uint16_t*) which needs a cast.
            // IntPtr/UIntPtr are value types but aliased to intptr_t/uintptr_t (scalars).
            // They need casts from pointer types (C++ rejects implicit void*→intptr_t).
            if (paramType is not Mono.Cecil.PointerType
                && paramType.FullName is not "System.IntPtr" and not "System.UIntPtr")
            {
                bool isValueParam = false;
                try { isValueParam = paramType.Resolve()?.IsValueType == true; }
                catch { isValueParam = CppNameMapper.IsValueType(paramType.FullName); }
                if (isValueParam) continue;
            }

            var resolvedName = ResolveGenericTypeRef(paramType, methodRef.DeclaringType);

            // Skip if contains unresolved generic params
            if (resolvedName.Contains("!!") || System.Text.RegularExpressions.Regex.IsMatch(resolvedName, @"![\d]"))
                continue;

            var expectedType = CppNameMapper.GetCppTypeForDecl(resolvedName);

            // IntPtr/UIntPtr parameters (intptr_t/uintptr_t): cast pointer args to the expected type.
            // In .NET, IntPtr/UIntPtr and pointers are interchangeable (native int = pointer).
            // In C++, uintptr_t/intptr_t are integer types — MSVC rejects implicit void*→intptr_t.
            if (expectedType is "intptr_t" or "uintptr_t")
            {
                if (args[i] != "nullptr" && args[i] != "0" && !args[i].StartsWith($"({expectedType})"))
                    args[i] = $"({expectedType}){args[i]}";
                continue;
            }

            // Only cast pointer parameters (reference types)
            if (!expectedType.EndsWith("*")) continue;

            // For void* parameters: cast argument to (void*) to handle uintptr_t/intptr_t→void*
            // conversion. In .NET, IntPtr/UIntPtr and void* are interchangeable (native int = pointer),
            // but in C++ uintptr_t/intptr_t are integer types incompatible with void*.
            // The C-style cast (void*)arg handles both pointer→void* (no-op) and integer→void* (reinterpret).
            if (expectedType == "void*")
            {
                if (args[i] != "nullptr" && args[i] != "0" && !args[i].StartsWith("(void*)"))
                    args[i] = $"(void*){args[i]}";
                continue;
            }

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

    private void EmitNewObj(IRBasicBlock block, Stack<StackEntry> stack, MethodReference ctorRef,
        ref int tempCounter)
    {
        // Special: BCL exception types (System.Exception, InvalidOperationException, etc.)
        if (TryEmitExceptionNewObj(block, stack, ctorRef, ref tempCounter))
            return;

        // Span<T>/ReadOnlySpan<T> newobj — inline constructor for JIT intrinsic bodies
        if (ctorRef.DeclaringType.FullName.StartsWith("System.Span`1")
            || ctorRef.DeclaringType.FullName.StartsWith("System.ReadOnlySpan`1"))
        {
            var spanTypeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
            var spanTmp = $"__t{tempCounter++}";
            var elemPtrType = "void*";
            if (ctorRef.DeclaringType is GenericInstanceType spanGit2
                && spanGit2.GenericArguments.Count > 0)
            {
                var elemArgName = ResolveGenericTypeRef(spanGit2.GenericArguments[0],
                    ctorRef.DeclaringType);
                if (!IsUnresolvedElementType(elemArgName))
                {
                    var elemCpp = CppNameMapper.GetCppTypeForDecl(elemArgName);
                    elemPtrType = elemCpp + "*";
                }
            }

            if (ctorRef.Parameters.Count == 3)
            {
                var length = stack.PopExpr();
                var start = stack.PopExpr();
                var array = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{spanTypeCpp} {spanTmp} = {{0}}; " +
                           $"{spanTmp}.f_reference = ({elemPtrType})cil2cpp::array_data({array}) + {start}; " +
                           $"{spanTmp}.f_length = {length};",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(spanTmp);
                return;
            }
            else if (ctorRef.Parameters.Count == 1
                && ctorRef.Parameters[0].ParameterType.IsArray)
            {
                var array = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{spanTypeCpp} {spanTmp} = {{0}}; " +
                           $"if ({array}) {{ " +
                           $"{spanTmp}.f_reference = ({elemPtrType})cil2cpp::array_data({array}); " +
                           $"{spanTmp}.f_length = cil2cpp::array_length({array}); }}",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(spanTmp);
                return;
            }
            else if (ctorRef.Parameters.Count == 2
                && ctorRef.Parameters[0].ParameterType.FullName is "System.Void*" or "System.IntPtr")
            {
                var length = stack.PopExpr();
                var pointer = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{spanTypeCpp} {spanTmp} = {{0}}; " +
                           $"{spanTmp}.f_reference = ({elemPtrType}){pointer}; " +
                           $"{spanTmp}.f_length = {length};",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(spanTmp);
                return;
            }
            else if (ctorRef.Parameters.Count == 2
                && ctorRef.Parameters[0].ParameterType.IsByReference)
            {
                // newobj .ctor(ref T reference, int length) — internal ByReference ctor
                var length = stack.PopExpr();
                var reference = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{spanTypeCpp} {spanTmp} = {{0}}; " +
                           $"{spanTmp}.f_reference = ({elemPtrType}){reference}; " +
                           $"{spanTmp}.f_length = {length};",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(spanTmp);
                return;
            }
        }

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
            var fptr = stack.PopExprOr("nullptr");
            var target = stack.PopExprOr("nullptr");
            block.Instructions.Add(new IRDelegateCreate
            {
                DelegateTypeCppName = typeCpp,
                TargetExpr = target,
                FunctionPtrExpr = fptr,
                ResultVar = tmp
            });
            stack.Push(new StackEntry(tmp, typeCpp + "*"));
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
            {
                ctorName = disambiguatedCtor;
            }
            else if (ctorRef.Parameters.Count > 0)
            {
                // Fallback 1: try to find the method directly on the IRType
                var targetType = _module.Types.FirstOrDefault(t => t.CppName == typeCpp);
                if (targetType == null && typeCpp.EndsWith("_"))
                    targetType = _module.Types.FirstOrDefault(t => t.CppName == typeCpp.TrimEnd('_'));
                bool resolved = false;
                if (targetType != null)
                {
                    var ctorCount = targetType.Methods.Count(m => m.Name == ".ctor");
                    if (ctorCount > 1)
                    {
                        var candidates = targetType.Methods
                            .Where(m => m.Name == ".ctor" && m.Parameters.Count == ctorRef.Parameters.Count + 1)
                            .ToList();
                        if (candidates.Count == 1)
                        {
                            ctorName = candidates[0].CppName;
                            resolved = true;
                        }
                        else if (candidates.Count > 1)
                        {
                            foreach (var c in candidates)
                            {
                                var cParams = c.Parameters.Skip(1).Select(p => p.CppTypeName).ToList();
                                var callParams = ctorRef.Parameters.Select(p =>
                                    CppNameMapper.GetCppTypeName(ResolveGenericTypeRef(p.ParameterType, ctorRef.DeclaringType))).ToList();
                                if (cParams.SequenceEqual(callParams))
                                {
                                    ctorName = c.CppName;
                                    resolved = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                // Fallback 2: if IRType not available, use Cecil for RuntimeProvided types
                if (!resolved)
                {
                    var declTypeDef = ctorRef.DeclaringType.Resolve();
                    if (declTypeDef != null
                        && RuntimeProvidedTypes.Contains(declTypeDef.FullName)
                        && !CoreRuntimeTypes.Contains(declTypeDef.FullName))
                    {
                        var overloadCount = declTypeDef.Methods.Count(m => m.Name == ".ctor");
                        if (overloadCount > 1)
                        {
                            var ilSuffix = string.Join("_", ctorRef.Parameters.Select(p =>
                            {
                                var res = ResolveGenericTypeRef(p.ParameterType, ctorRef.DeclaringType);
                                return CppNameMapper.MangleTypeName(res.TrimEnd('*', '&', ' '));
                            }));
                            ctorName = $"{ctorName}__{ilSuffix}";
                        }
                    }
                }
            }
        }

        // Collect constructor arguments
        var args = new List<string>();
        for (int i = 0; i < ctorRef.Parameters.Count; i++)
        {
            args.Add(stack.PopExpr());
        }
        args.Reverse();

        // Cast constructor arguments to expected parameter types
        // (handles derived→base pointer casts in flat struct model)
        CastArgumentsToParameterTypes(args, ctorRef);

        // Value types: allocate on stack instead of heap
        // Check both IR type cache and Cecil for value type detection
        bool isValueType = false;
        if (_typeCache.TryGetValue(cacheKey, out var irType))
            isValueType = irType.IsValueType;
        else
        {
            // Fallback: check Cecil directly (handles BCL generic types not yet in cache)
            try { isValueType = ctorRef.DeclaringType.Resolve()?.IsValueType == true; } catch { }
        }
        if (isValueType)
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
            stack.Push(new StackEntry(tmp, typeCpp));  // value type — no pointer
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
                    Code = $"auto {tmp} = ({runtimeCpp}*)cil2cpp::gc::alloc(sizeof({runtimeCpp}), &{typeCpp}_TypeInfo);",
                    ResultVar = tmp,
                    ResultTypeCpp = runtimeCpp + "*",
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

            stack.Push(new StackEntry(tmp, typeCpp + "*"));  // reference type — pointer
        }
    }

    /// <summary>
    /// Intercepts newobj for BCL exception types (System.Exception, InvalidOperationException, etc.)
    /// and emits runtime exception creation code instead of trying to reference non-existent
    /// generated structs/constructors.
    /// </summary>
    private bool TryEmitExceptionNewObj(IRBasicBlock block, Stack<StackEntry> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        var runtimeCppName = CppNameMapper.GetRuntimeExceptionCppName(ctorRef.DeclaringType.FullName);
        if (runtimeCppName == null) return false;

        var tmp = $"__t{tempCounter++}";
        var paramCount = ctorRef.Parameters.Count;

        // Pop constructor args
        var args = new List<string>();
        for (int i = 0; i < paramCount; i++)
            args.Add(stack.PopExpr());
        args.Reverse();

        // Allocate: (ExcType*)cil2cpp::gc::alloc(sizeof(ExcType), &ExcType_TypeInfo)
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = ({runtimeCppName}*)cil2cpp::gc::alloc(sizeof({runtimeCppName}), &{runtimeCppName}_TypeInfo);",
            ResultVar = tmp,
            ResultTypeCpp = runtimeCppName + "*",
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
        // Handle compound types that may contain generic parameters
        if (typeRef is ArrayType arrType)
        {
            var elemResolved = ResolveGenericTypeRef(arrType.ElementType, declaringType);
            return elemResolved + "[]";
        }
        if (typeRef is ByReferenceType byRefType)
        {
            var elemResolved = ResolveGenericTypeRef(byRefType.ElementType, declaringType);
            return elemResolved + "&";
        }
        if (typeRef is PointerType ptrType)
        {
            var elemResolved = ResolveGenericTypeRef(ptrType.ElementType, declaringType);
            return elemResolved + "*";
        }

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
    /// Check if a resolved element type name is still an unresolved generic parameter.
    /// Names like "T", "TKey", "!0" indicate resolution failed — callers should use void* fallback.
    /// </summary>
    private static bool IsUnresolvedElementType(string resolvedName)
    {
        if (string.IsNullOrEmpty(resolvedName)) return true;
        // IL generic param notation: !0, !1, !!0, etc.
        if (resolvedName.StartsWith("!")) return true;
        // Single-word uppercase name without dots/namespace — likely generic param (T, TKey, TValue, etc.)
        if (!resolvedName.Contains('.') && !resolvedName.Contains('/') && resolvedName.Length <= 10
            && resolvedName.Length >= 1 && char.IsUpper(resolvedName[0])
            && resolvedName.All(c => char.IsLetterOrDigit(c)))
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

        if (typeRef is PointerType ptrType)
        {
            var resolved = SubstituteMethodGenericParams(ptrType.ElementType, gim);
            if (resolved != ptrType.ElementType)
                return new PointerType(resolved);
        }

        if (typeRef is ArrayType arrType)
        {
            var resolved = SubstituteMethodGenericParams(arrType.ElementType, gim);
            if (resolved != arrType.ElementType)
                return new ArrayType(resolved, arrType.Rank);
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

    /// <summary>Get C++ type of argument at the given index (accounting for this pointer).</summary>
    private string? GetArgType(IRMethod method, int index)
    {
        if (!method.IsStatic)
        {
            if (index == 0)
                return method.DeclaringType?.CppName is { } n ? n + "*" : null;
            index--;
        }
        if (index >= 0 && index < method.Parameters.Count)
            return method.Parameters[index].CppTypeName;
        return null;
    }

    /// <summary>Get C++ type of local variable at the given index.</summary>
    private static string? GetLocalType(IRMethod method, int index)
    {
        if (index >= 0 && index < method.Locals.Count)
            return method.Locals[index].CppTypeName;
        return null;
    }

    /// <summary>
    /// Determines whether a field access should use '.' (value) vs '->' (pointer).
    /// Value type locals accessed directly (ldloc) use '.'; addresses (&amp;loc) use '->'.
    /// </summary>
    private static bool IsValueTypeAccess(TypeReference declaringType, string objExpr, IRMethod method, string? stackCppType = null)
    {
        // Address-of expressions are always pointers
        if (objExpr.StartsWith("&")) return false;

        // __this is always a pointer
        if (objExpr == "__this") return false;

        // If the stack tracked this value as a pointer type, always use -> accessor.
        // This catches cases where TempVarTypes is empty (during IR building)
        // or where the pointer type was set by IRRawCpp/cast/unbox instructions.
        if (stackCppType != null && stackCppType.EndsWith("*")) return false;

        // Check if the declaring type is a value type
        var resolved = declaringType.Resolve();
        bool isValueType = resolved?.IsValueType ?? false;
        if (!isValueType)
        {
            // Also check our own registry (for generic specializations etc.)
            isValueType = CppNameMapper.IsValueType(declaringType.FullName);
        }
        if (!isValueType) return false;

        // If the object expression is a local variable of value type, it's a value access.
        // But pointer-typed locals (e.g., from ldloca or by-ref) need -> accessor.
        if (objExpr.StartsWith("loc_"))
        {
            var localMatch = method.Locals.FirstOrDefault(l => l.CppName == objExpr);
            if (localMatch != null && localMatch.CppTypeName.EndsWith("*"))
                return false; // pointer local → use ->
            return true;
        }

        // Temp variables holding value types also use value access.
        // But if TempVarTypes records the __t as pointer-typed, use -> accessor.
        if (objExpr.StartsWith("__t") && !objExpr.Contains("*"))
        {
            if (method.TempVarTypes.TryGetValue(objExpr, out var tempType) && tempType.EndsWith("*"))
                return false; // pointer temp → use ->
            return true;
        }

        // Method parameters that are value types (not pointer-typed).
        // By-ref value type params become Type* in C++ and need -> accessor.
        if (method.Parameters.Any(p => p.CppName == objExpr && !p.CppTypeName.EndsWith("*"))) return true;

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

    private void EmitConversion(IRBasicBlock block, Stack<StackEntry> stack, string targetType, ref int tempCounter)
    {
        var entry = stack.PopEntry();
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRConversion
        {
            SourceExpr = entry.Expr, TargetType = targetType, ResultVar = tmp,
            SourceCppType = entry.CppType
        });
        stack.Push(new StackEntry(tmp, targetType));
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
    /// <summary>
    /// Match a method name against an interface method name.
    /// Explicit interface implementations have names like "System.IUtfChar&lt;System.Char&gt;.CastFrom"
    /// while the interface method reference name is just "CastFrom".
    /// Matches by exact name or by suffix after the last dot.
    /// </summary>
    private static bool MatchesMethodName(string implName, string interfaceMethodName)
    {
        if (implName == interfaceMethodName) return true;
        // Explicit interface impl: "Namespace.IFace<T>.MethodName" — extract suffix after last '.'
        var lastDot = implName.LastIndexOf('.');
        if (lastDot >= 0 && implName.AsSpan(lastDot + 1).SequenceEqual(interfaceMethodName))
            return true;
        return false;
    }

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

    /// <summary>
    /// Intercept MemoryMarshal JIT intrinsics at call sites.
    /// Their IL bodies use Unsafe.*/RuntimeHelpers.* which are also JIT intrinsics,
    /// so the IL bodies can't be compiled to C++. We emit inline C++ instead.
    /// </summary>
    private bool TryEmitMemoryMarshalIntrinsic(IRBasicBlock block, Stack<StackEntry> stack,
        MethodReference methodRef, GenericInstanceMethod gim, ref int tempCounter)
    {
        var methodName = methodRef.Name;
        var typeArgs = gim.GenericArguments;
        if (typeArgs.Count == 0) return false;

        var elemIlName = ResolveTypeRefOperand(typeArgs[0]);
        // If resolution failed (unresolved generic param like "T"), fall back to void*
        if (IsUnresolvedElementType(elemIlName))
            return false; // Can't emit typed Span helpers — let normal codegen handle it
        var elemCpp = CppNameMapper.GetCppTypeForDecl(elemIlName);
        // For reference types (String*), element pointer is String**
        var elemPtrCpp = elemCpp + "*";

        // For methods accessing span.f_reference, verify the span type has that field.
        // Opaque BCL Span<T> types (empty struct declarations) don't have f_reference.
        bool SpanTypeHasFields()
        {
            if (methodRef.Parameters.Count < 1) return false;
            // Try both Span<T> and ReadOnlySpan<T> with the resolved element type
            var spanKeys = new[]
            {
                $"System.Span`1<{elemIlName}>",
                $"System.ReadOnlySpan`1<{elemIlName}>"
            };
            foreach (var key in spanKeys)
            {
                if (_typeCache.TryGetValue(key, out var spanIrType)
                    && spanIrType.Fields.Any(f => f.Name is "_reference" or "f_reference"))
                    return true;
            }
            return false;
        }

        switch (methodName)
        {
            // GetReference<T>(Span<T>) / GetReference<T>(ReadOnlySpan<T>) → span.f_reference
            case "GetReference":
            {
                if (methodRef.Parameters.Count != 1 || !SpanTypeHasFields()) return false;
                var span = stack.PopExprOr("{}");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = ({elemPtrCpp}){span}.f_reference;",
                    ResultVar = tmp,
                    ResultTypeCpp = elemPtrCpp,
                });
                stack.Push(tmp);
                return true;
            }

            // GetNonNullPinnableReference<T>(Span<T>/ReadOnlySpan<T>)
            // Returns f_reference if non-empty, or (T*)(uintptr_t)1 if empty (non-null sentinel for pinning)
            case "GetNonNullPinnableReference":
            {
                if (methodRef.Parameters.Count != 1 || !SpanTypeHasFields()) return false;
                var span = stack.PopExprOr("{}");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = {span}.f_length != 0 ? ({elemPtrCpp}){span}.f_reference : ({elemPtrCpp})(uintptr_t)1;",
                    ResultVar = tmp,
                    ResultTypeCpp = elemPtrCpp,
                });
                stack.Push(tmp);
                return true;
            }

            // GetArrayDataReference<T>(T[]) → (T*)array_data(arr)
            case "GetArrayDataReference":
            {
                if (methodRef.Parameters.Count != 1) return false;
                var arr = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = ({elemPtrCpp})cil2cpp::array_data({arr});",
                    ResultVar = tmp,
                    ResultTypeCpp = elemPtrCpp,
                });
                stack.Push(tmp);
                return true;
            }

            // Read<T>(ReadOnlySpan<byte>) → *(T*)span.f_reference
            case "Read":
            {
                if (methodRef.Parameters.Count != 1) return false;
                var span = stack.PopExprOr("{}");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = *({elemCpp}*){span}.f_reference;",
                    ResultVar = tmp,
                    ResultTypeCpp = elemCpp,
                });
                stack.Push(tmp);
                return true;
            }

            // CreateSpan<T>(ref T, int) / CreateReadOnlySpan<T>(ref T, int)
            case "CreateSpan":
            case "CreateReadOnlySpan":
            {
                if (methodRef.Parameters.Count != 2) return false;
                var length = stack.PopExpr();
                var refPtr = stack.PopExprOr("nullptr");
                // Construct the Span/ReadOnlySpan type name from the element type
                var spanPrefix = methodName == "CreateSpan" ? "System.Span`1" : "System.ReadOnlySpan`1";
                var spanIlName = $"{spanPrefix}<{elemIlName}>";
                var retCpp = CppNameMapper.GetCppTypeName(spanIlName);
                if (retCpp.EndsWith("*")) retCpp = retCpp.TrimEnd('*');
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{retCpp} {tmp} = {{0}}; {tmp}.f_reference = ({elemPtrCpp}){refPtr}; {tmp}.f_length = {length};",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
                stack.Push(tmp);
                return true;
            }

            // AsBytes<T>(ReadOnlySpan<T>) → reinterpret as ReadOnlySpan<byte>
            // AsBytes<T>(Span<T>) → reinterpret as Span<byte>
            case "AsBytes":
            {
                if (methodRef.Parameters.Count != 1) return false;
                var span = stack.PopExprOr("{}");
                var retTypeName = ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType);
                var retCpp = CppNameMapper.GetCppTypeName(retTypeName);
                if (retCpp.EndsWith("*")) retCpp = retCpp.TrimEnd('*');
                var elemSizeof = $"sizeof({elemCpp})";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{retCpp} {tmp} = {{0}}; " +
                           $"{tmp}.f_reference = (uint8_t*){span}.f_reference; " +
                           $"{tmp}.f_length = {span}.f_length * {elemSizeof};",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
                stack.Push(tmp);
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Compile-time evaluation of RuntimeHelpers.IsReferenceOrContainsReferences&lt;T&gt;().
    /// Returns true if T is a reference type or a value type containing reference fields.
    /// </summary>
    private bool IsReferenceOrContainsReferences(string ilTypeName)
    {
        // Well-known BCL primitive/simple value types
        if (ilTypeName is "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
            or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
            or "System.Single" or "System.Double" or "System.Boolean" or "System.Char"
            or "System.IntPtr" or "System.UIntPtr" or "System.Decimal"
            or "System.DateTime" or "System.TimeSpan" or "System.Guid"
            or "System.Threading.CancellationToken" or "System.Void")
            return false;

        // Check if it's a type in our IR
        if (_typeCache.TryGetValue(ilTypeName, out var irType))
        {
            if (!irType.IsValueType) return true; // reference type
            if (irType.IsEnum) return false; // enums are simple value types
            // Value type — check if any field is a reference type
            return irType.Fields.Any(f => IsReferenceOrContainsReferences(f.FieldTypeName));
        }

        // Conservative default for unknown types
        return true;
    }
}
