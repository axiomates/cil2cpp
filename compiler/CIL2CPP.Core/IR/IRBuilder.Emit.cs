using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private void EmitStoreLocal(IRBasicBlock block, Stack<StackEntry> stack, IRMethod method, int index, ref int tempCounter)
    {
        var entry = stack.PopEntry();
        var val = entry.Expr;

        // Track compile-time constant locals for dead branch elimination
        if (entry.CompileTimeConstant.HasValue)
            _ctx.Value.CompileTimeConstantLocals[index] = entry.CompileTimeConstant.Value;
        else
            _ctx.Value.CompileTimeConstantLocals.Remove(index);

        // Stack aliasing fix: before modifying a local, check if any remaining stack entry
        // symbolically references that local. If so, materialize those entries into temporaries.
        // This handles IL patterns like:  ldloc.0; ldc.i4.0; stloc.0; stloc.s 4
        // where the stack holds "loc_0" but stloc.0 is about to change its value.
        var localName = GetLocalName(method, index);
        MaterializeStackAliases(block, stack, localName, ref tempCounter);

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
            // Cast void*/pointer to intptr_t/uintptr_t — in .NET these are native int (pointer-sized),
            // but in C++ intptr_t is an integer type, not implicitly convertible from void*.
            else if (local.CppTypeName is "intptr_t" or "uintptr_t")
            {
                val = $"({local.CppTypeName}){val}";
            }
            // SIMD struct locals: when a SIMD operation is replaced with 0 (e.g., AsByte() on
            // a dead-code path), storing int 0 to a Vector128/256/512 struct is a C++ type error.
            // Use value-initialization {} instead.
            else if (val is "0" && local.CppTypeName.StartsWith("System_Runtime_Intrinsics_Vector"))
            {
                val = "{}";
            }
        }
        block.Instructions.Add(new IRAssign
        {
            Target = localName,
            Value = val,
        });
    }

    /// <summary>
    /// If any stack entry's expression is exactly the given variable name, materialize it
    /// into a temporary so that subsequent modification of the variable doesn't corrupt the
    /// stack value. This is required because the CIL stack machine preserves values by-value,
    /// but our symbolic stack uses variable names (by-reference semantics).
    /// </summary>
    private static void MaterializeStackAliases(IRBasicBlock block, Stack<StackEntry> stack, string varName, ref int tempCounter)
    {
        // Quick check: does any stack entry reference this variable?
        bool hasAlias = false;
        foreach (var entry in stack)
        {
            if (entry.Expr == varName)
            {
                hasAlias = true;
                break;
            }
        }
        if (!hasAlias) return;

        // Materialize: emit a temporary to capture the current value
        // Use the actual CppType from the stack entry (not "auto") so that cross-scope
        // pre-declarations and sizeof() in memset replacements use the correct type.
        var tmp = $"__t{tempCounter++}";
        string typeName = "auto";
        foreach (var entry in stack)
        {
            if (entry.Expr == varName && entry.CppType != null)
            {
                typeName = entry.CppType;
                break;
            }
        }
        block.Instructions.Add(new IRDeclareLocal { TypeName = typeName, VarName = tmp, InitValue = varName });

        // Replace all matching stack entries with the temporary
        var entries = stack.ToArray(); // index 0 = top of stack
        stack.Clear();
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            if (entries[i].Expr == varName)
                stack.Push(new StackEntry(tmp, entries[i].CppType));
            else
                stack.Push(entries[i]);
        }
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

        // Use StackEntry.CppType for pointer type detection
        var leftPtrType = ClassifyPointerType(items.Length > 1 ? items[1].CppType ?? "" : "");
        var rightPtrType = ClassifyPointerType(items[0].CppType ?? "");

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
        }
        else if (rightPtrType != null && op == "+")
        {
            // integer + ptr: commutative, put pointer first for valid C++ arithmetic.
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{tmp} = ({rightPtrType})((uint8_t*){right} + {left});",
                ResultVar = tmp,
                ResultTypeCpp = rightPtrType,
            });
        }
        else // rightPtrType != null && op == "-"
        {
            // integer - ptr: NOT commutative. Cast both to uint8_t* to preserve
            // correct operand order (left - right) with byte-level granularity.
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{tmp} = (intptr_t)((uint8_t*){left} - (uint8_t*){right});",
                ResultVar = tmp,
                ResultTypeCpp = "intptr_t",
            });
        }

        // Push with pointer type so downstream consumers (comparisons, further arithmetic) know it's a pointer
        var resultType = (op == "-" && rightPtrType != null && leftPtrType == null)
            ? null  // int - ptr yields intptr_t, not a pointer
            : leftPtrType ?? rightPtrType;
        stack.Push(resultType != null ? new StackEntry(tmp, resultType) : new StackEntry(tmp));
        return true;
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

        // Cast bool operands in comparisons to int32_t to prevent MSVC C4805.
        // Example: Boolean::Equals compares uint8_t (ldind.u1) with bool (unbox<bool>).
        if (op is "==" or "!=" or "<" or ">" or "<=" or ">=")
        {
            if (leftEntry.CppType == "bool" && rightEntry.CppType != "bool")
                left = $"(int32_t){left}";
            if (rightEntry.CppType == "bool" && leftEntry.CppType != "bool")
                right = $"(int32_t){right}";
        }

        // Pointer arithmetic (+, -): C++ doesn't allow arithmetic on void* (unknown size).
        // IL treats native int and pointers interchangeably; add/sub on native int is byte-level.
        // Cast void* operands to uint8_t* so pointer arithmetic works at byte granularity.
        if (op is "+" or "-")
        {
            bool leftIsPtr = leftEntry.IsPointer
                || (method != null && IsPointerTypedOperand(left, method));
            bool rightIsPtr = rightEntry.IsPointer
                || (method != null && IsPointerTypedOperand(right, method));
            // Cast pointer operands to uint8_t* for byte-level arithmetic.
            // Only cast when the other operand is not also a pointer (ptr + int, not ptr + ptr).
            if (leftIsPtr && !rightIsPtr)
                left = $"(uint8_t*){left}";
            else if (rightIsPtr && !leftIsPtr)
                right = $"(uint8_t*){right}";
            else if (leftIsPtr && rightIsPtr && op == "-")
            {
                // ptr - ptr: result is ptrdiff_t, cast both to uint8_t* for byte difference
                left = $"(uint8_t*){left}";
                right = $"(uint8_t*){right}";
            }
        }

        // Arithmetic ops (*, /, %) on pointer operands: C++ doesn't allow arithmetic
        // on pointer types (MSVC C2296). IL treats native int and pointers interchangeably
        // (e.g., charEnd_ptr / 2 to get element count). Cast to intptr_t.
        if (op is "/" or "*" or "%")
        {
            bool leftIsPtr = leftEntry.IsPointer
                || (method != null && IsPointerTypedOperand(left, method));
            bool rightIsPtr = rightEntry.IsPointer
                || (method != null && IsPointerTypedOperand(right, method));
            if (leftIsPtr)
                left = $"(intptr_t){left}";
            if (rightIsPtr)
                right = $"(intptr_t){right}";
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
            // Cast bool operands to int32_t to prevent MSVC C4805.
            // IL and/or/xor treat all operands as integers (ECMA-335 III.3.1);
            // C++ bool in bitwise ops with int triggers the warning.
            if (!leftIsPtr && leftEntry.CppType == "bool")
                left = $"(int32_t){left}";
            if (!rightIsPtr && rightEntry.CppType == "bool")
                right = $"(int32_t){right}";
        }

        // Float/double remainder: C++ % operator is invalid for floating-point (MSVC C2296/C2297).
        // Detect float/double operands and flag for std::fmod emission.
        bool isFloatRemainder = false;
        if (op == "%")
        {
            var lt = leftEntry.CppType;
            var rt = rightEntry.CppType;
            if (lt is "double" or "float" or "System_Single" or "System_Double"
                || rt is "double" or "float" or "System_Single" or "System_Double")
            {
                isFloatRemainder = true;
            }
        }

        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRBinaryOp
        {
            Left = left, Right = right, Op = op, ResultVar = tmp,
            IsUnsigned = isUnsigned, IsFloatRemainder = isFloatRemainder
        });
        // Comparison operators (ceq/cgt/clt) produce int32_t per ECMA-335 III.4.1.
        // Track this type so ternary merge logic can safely merge comparison results
        // across branch paths (e.g., brtrue T; ceq; br M; T: ldc.i4.1; M: call).
        string? resultType = op is "==" or "!=" or "<" or ">" or "<=" or ">="
            ? "int32_t" : null;
        // Propagate compile-time constants through comparison operators.
        // Handles patterns like: IsSupported (=0) → ldc.i4.0 → ceq → brtrue
        // When one operand has CompileTimeConstant and the other is a simple integer literal,
        // we can infer the literal's value and compute the result at compile time.
        int? resultConstant = null;
        if (resultType != null)
        {
            int? leftConst = leftEntry.CompileTimeConstant ?? TryParseSimpleLiteral(leftEntry.Expr);
            int? rightConst = rightEntry.CompileTimeConstant ?? TryParseSimpleLiteral(rightEntry.Expr);
            // Only propagate if at least one operand was a true compile-time constant (from feature switches)
            // to avoid false positives from regular integer comparisons.
            if (leftConst.HasValue && rightConst.HasValue
                && (leftEntry.CompileTimeConstant.HasValue || rightEntry.CompileTimeConstant.HasValue))
            {
                int l = leftConst.Value, r = rightConst.Value;
                resultConstant = op switch
                {
                    "==" => l == r ? 1 : 0,
                    "!=" => l != r ? 1 : 0,
                    "<" => l < r ? 1 : 0,
                    ">" => l > r ? 1 : 0,
                    "<=" => l <= r ? 1 : 0,
                    ">=" => l >= r ? 1 : 0,
                    _ => null,
                };
            }
        }
        stack.Push(new StackEntry(tmp, resultType, resultConstant));
    }

    private void EmitComparisonBranch(IRBasicBlock block, Stack<StackEntry> stack, string op, ILInstruction instr,
        bool isUnsigned = false, Dictionary<int, StackEntry[]>? branchTargetStacks = null, IRMethod? method = null,
        List<(int Start, int End)>? deadRanges = null)
    {
        var rightEntry = stack.PopEntry();
        var leftEntry = stack.PopEntry();
        var right = rightEntry.Expr;
        var left = leftEntry.Expr;

        // Skip branches to dead code ranges (SIMD dead branches)
        var target = (Instruction)instr.Operand!;
        if (ReachabilityAnalyzer.IsOffsetInDeadRange(deadRanges, target.Offset))
            return;

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
        // Signed comparisons: use signed_lt/gt/ge/le to ensure signed semantics
        // even when C++ operand types are unsigned (e.g., uint64_t from ulong fields)
        else if (op == ">")
            condition = $"cil2cpp::signed_gt({left}, {right})";
        else if (op == "<")
            condition = $"cil2cpp::signed_lt({left}, {right})";
        else if (op == ">=")
            condition = $"cil2cpp::signed_ge({left}, {right})";
        else if (op == "<=")
            condition = $"cil2cpp::signed_le({left}, {right})";
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
            stack.Push(new StackEntry(tmp, retType));
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
            stack.Push(new StackEntry(tmp, targetType));
            return;
        }

        // Unsafe.SizeOf<T>() — sizeof intrinsic
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "SizeOf" && methodRef is GenericInstanceMethod gimSz)
        {
            var typeArg = gimSz.GenericArguments[0];
            var resolvedArg = ResolveTypeRefOperand(typeArg);
            var cppType = CppNameMapper.GetCppTypeName(resolvedArg);
            // Reference types ARE pointers — sizeof(T*) = pointer size is correct
            if (!CppNameMapper.IsValueType(resolvedArg) && !cppType.EndsWith("*"))
                cppType += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {tmp} = static_cast<int32_t>(sizeof({cppType}));",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return;
        }

        // Unsafe.As<TFrom,TTo>(ref TFrom) — reinterpret cast intrinsic
        // This is a byref operation: takes ref TFrom, returns ref TTo.
        // The result type must always be one pointer level higher than the element type.
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "As" && methodRef is GenericInstanceMethod gimAs
            && gimAs.GenericArguments.Count == 2)
        {
            var val = stack.PopExprOr("nullptr");
            var toTypeArg = gimAs.GenericArguments[1];
            var resolvedTo = ResolveTypeRefOperand(toTypeArg);
            var cppTo = CppNameMapper.GetCppTypeForDecl(resolvedTo);
            // Always add * — this is a byref-to-byref operation.
            // Value type T → T*, reference type T (already T*) → T**
            cppTo += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<{cppTo}>({val});",
                ResultVar = tmp,
                ResultTypeCpp = cppTo,
            });
            stack.Push(new StackEntry(tmp, cppTo));
            return;
        }

        // Unsafe.BitCast<TFrom,TTo>(TFrom) — value-level type punning intrinsic (C++20 std::bit_cast)
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "BitCast" && methodRef is GenericInstanceMethod gimBc
            && gimBc.GenericArguments.Count == 2)
        {
            var val = stack.PopExprOr("0");
            var toTypeArg = gimBc.GenericArguments[1];
            var resolvedTo = ResolveTypeRefOperand(toTypeArg);
            var cppTo = CppNameMapper.GetCppTypeName(resolvedTo);
            if (!CppNameMapper.IsValueType(resolvedTo) && !cppTo.EndsWith("*"))
                cppTo += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{cppTo} {tmp}; std::memcpy(&{tmp}, &{val}, sizeof({cppTo}));",
                ResultVar = tmp,
                ResultTypeCpp = cppTo,
            });
            stack.Push(new StackEntry(tmp, cppTo));
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
            // void* arithmetic is invalid in C++ — cast through uint8_t* for byte-level offset
            var ptrExpr = (ptrEntry.CppType == "void*") ? $"(uint8_t*){ptr}" : ptr;
            var resultCast = (ptrEntry.CppType == "void*") ? $"(void*)" : "";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {resultCast}({ptrExpr} + {offset});",
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
            var resolvedType = ResolveTypeRefOperand(typeArg);
            var cppType = CppNameMapper.GetCppTypeName(resolvedType);
            // Reference types ARE pointers — memcpy reads a pointer from memory
            if (!CppNameMapper.IsValueType(resolvedType) && !cppType.EndsWith("*"))
                cppType += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{cppType} {tmp}; std::memcpy(&{tmp}, (void*){src}, sizeof({cppType}));",
                ResultVar = tmp,
                ResultTypeCpp = cppType,
            });
            stack.Push(new StackEntry(tmp, cppType));
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
            var resolvedType = ResolveTypeRefOperand(typeArg);
            var cppType = CppNameMapper.GetCppTypeName(resolvedType);
            // Reference types ARE pointers — memcpy writes a pointer to memory
            if (!CppNameMapper.IsValueType(resolvedType) && !cppType.EndsWith("*"))
                cppType += "*";
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
            var ptrEntry = stack.PopEntry();
            var ptr = ptrEntry.Expr.Length > 0 ? ptrEntry.Expr : "nullptr";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {ptr} - {offset};",
                ResultVar = tmp,
                ResultTypeCpp = ptrEntry.CppType,
            });
            stack.Push(new StackEntry(tmp, ptrEntry.CppType));
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
            stack.Push(new StackEntry(tmp, "bool"));
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
            stack.Push(new StackEntry(tmp, "intptr_t"));
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
            stack.Push(new StackEntry(tmp, "bool"));
            return;
        }

        // Unsafe.NullRef<T>() — null reference
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "NullRef" && methodRef.Parameters.Count == 0)
        {
            stack.Push(new StackEntry("nullptr", "void*"));
            return;
        }

        // Unsafe.IsAddressLessThan<T>(ref T, ref T) — pointer comparison
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "IsAddressLessThan" && methodRef.Parameters.Count == 2)
        {
            var right = stack.PopExprOr("nullptr");
            var left = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ((void*){left} < (void*){right});",
                ResultVar = tmp,
                ResultTypeCpp = "bool",
            });
            stack.Push(new StackEntry(tmp, "bool"));
            return;
        }

        // Unsafe.IsAddressGreaterThan<T>(ref T, ref T) — pointer comparison
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "IsAddressGreaterThan" && methodRef.Parameters.Count == 2)
        {
            var right = stack.PopExprOr("nullptr");
            var left = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ((void*){left} > (void*){right});",
                ResultVar = tmp,
                ResultTypeCpp = "bool",
            });
            stack.Push(new StackEntry(tmp, "bool"));
            return;
        }

        // Unsafe.AddByteOffset<T>(ref T, IntPtr/nuint) — byte offset addition
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "AddByteOffset" && methodRef.Parameters.Count == 2)
        {
            var offset = stack.PopExpr();
            var ptrEntry = stack.PopEntry();
            var ptr = ptrEntry.Expr.Length > 0 ? ptrEntry.Expr : "nullptr";
            var tmp = $"__t{tempCounter++}";
            // Use tracked CppType for result cast; fall back to decltype if not available
            var castExpr = ptrEntry.CppType != null ? $"({ptrEntry.CppType})" : $"(decltype({ptr}))";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {castExpr}((uint8_t*){ptr} + (intptr_t){offset});",
                ResultVar = tmp,
                ResultTypeCpp = ptrEntry.CppType,
            });
            stack.Push(new StackEntry(tmp, ptrEntry.CppType));
            return;
        }

        // Unsafe.SubtractByteOffset<T>(ref T, IntPtr/nuint) — byte offset subtraction
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "SubtractByteOffset" && methodRef.Parameters.Count == 2)
        {
            var offset = stack.PopExpr();
            var ptrEntry = stack.PopEntry();
            var ptr = ptrEntry.Expr.Length > 0 ? ptrEntry.Expr : "nullptr";
            var tmp = $"__t{tempCounter++}";
            var castExpr = ptrEntry.CppType != null ? $"({ptrEntry.CppType})" : $"(decltype({ptr}))";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {castExpr}((uint8_t*){ptr} - (intptr_t){offset});",
                ResultVar = tmp,
                ResultTypeCpp = ptrEntry.CppType,
            });
            stack.Push(new StackEntry(tmp, ptrEntry.CppType));
            return;
        }

        // Unsafe.As<T>(object) — cast object to T (single type arg version)
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "As" && methodRef is GenericInstanceMethod gimAs1
            && gimAs1.GenericArguments.Count == 1 && methodRef.Parameters.Count == 1)
        {
            var val = stack.PopExprOr("nullptr");
            var toTypeArg = gimAs1.GenericArguments[0];
            var resolvedTo = ResolveTypeRefOperand(toTypeArg);
            var cppTo = CppNameMapper.GetCppTypeForDecl(resolvedTo);
            if (!cppTo.EndsWith("*")) cppTo += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<{cppTo}>({val});",
                ResultVar = tmp,
                ResultTypeCpp = cppTo,
            });
            stack.Push(new StackEntry(tmp, cppTo));
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
        // Unsafe.Unbox<T>(object) — unbox to ref T
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.Unsafe"
            && methodRef.Name == "Unbox" && methodRef is GenericInstanceMethod gimUnbox
            && gimUnbox.GenericArguments.Count == 1)
        {
            var obj = stack.PopExprOr("nullptr");
            var typeArg = gimUnbox.GenericArguments[0];
            var resolvedType = ResolveTypeRefOperand(typeArg);
            var cppType = CppNameMapper.GetCppTypeName(resolvedType);
            if (!cppType.EndsWith("*")) cppType += "*";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ({cppType})cil2cpp::unbox({obj});",
                ResultVar = tmp,
                ResultTypeCpp = cppType,
            });
            stack.Push(new StackEntry(tmp, cppType));
            return;
        }

        // MemoryMarshal JIT intrinsics — their IL bodies use Unsafe.* which are also intrinsics
        if (methodRef.DeclaringType.FullName == "System.Runtime.InteropServices.MemoryMarshal"
            && methodRef is GenericInstanceMethod mmGim)
        {
            if (TryEmitMemoryMarshalIntrinsic(block, stack, methodRef, mmGim, ref tempCounter))
                return;
        }
        // SIMD IsSupported/IsHardwareAccelerated → return 0 (disable SIMD, force scalar fallback)
        // SIMD get_Count → return correct structural constant for Vector64/128/256/512<T>
        // Covers: Vector64/128/256/512, System.Numerics.Vector (not Vector2/3/4), all X86/Wasm intrinsics
        {
            var declType = methodRef.DeclaringType.FullName;
            bool isSimdType = declType.StartsWith("System.Runtime.Intrinsics.")
                || declType == "System.Numerics.Vector"
                || declType.StartsWith("System.Numerics.Vector`");

            if (isSimdType && methodRef.Name is "get_IsSupported" or "get_IsHardwareAccelerated")
            {
                if (methodRef.HasThis && stack.Count > 0) stack.Pop();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp { Code = $"int32_t {tmp} = 0;", ResultVar = tmp, ResultTypeCpp = "int32_t" });
                stack.Push(new StackEntry(tmp, "int32_t", CompileTimeConstant: 0));
                return;
            }

            // Vector64/128/256/512<T>.Count → structural constant (vectorSize / sizeof(T)).
            // These are NOT hardware-dependent — Vector128<byte>.Count is always 16, etc.
            // System.Numerics.Vector<T>.Count is hardware-dependent (behind IsHardwareAccelerated guards) → return 0.
            if (isSimdType && methodRef.Name == "get_Count")
            {
                if (methodRef.HasThis && stack.Count > 0) stack.Pop();
                int countValue = 0;
                // Extract vector byte size from declaring type name (Vector64=8, Vector128=16, etc.)
                int vectorBytes = 0;
                if (declType.StartsWith("System.Runtime.Intrinsics.Vector64")) vectorBytes = 8;
                else if (declType.StartsWith("System.Runtime.Intrinsics.Vector128")) vectorBytes = 16;
                else if (declType.StartsWith("System.Runtime.Intrinsics.Vector256")) vectorBytes = 32;
                else if (declType.StartsWith("System.Runtime.Intrinsics.Vector512")) vectorBytes = 64;
                if (vectorBytes > 0 && methodRef.DeclaringType is GenericInstanceType git && git.GenericArguments.Count > 0)
                {
                    var elemType = git.GenericArguments[0].FullName;
                    int elemSize = elemType switch
                    {
                        "System.Byte" or "System.SByte" => 1,
                        "System.Int16" or "System.UInt16" or "System.Char" => 2,
                        "System.Int32" or "System.UInt32" or "System.Single" => 4,
                        "System.Int64" or "System.UInt64" or "System.Double" => 8,
                        "System.IntPtr" or "System.UIntPtr" => 8, // 64-bit target
                        _ => 0
                    };
                    if (elemSize > 0)
                        countValue = vectorBytes / elemSize;
                }
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp { Code = $"int32_t {tmp} = {countValue};", ResultVar = tmp, ResultTypeCpp = "int32_t" });
                stack.Push(new StackEntry(tmp, "int32_t", CompileTimeConstant: countValue));
                return;
            }

            // BCL vectorization support properties that wrap IsHardwareAccelerated — always false in AOT.
            // These are cross-cutting helpers that check SIMD support and guard vectorized code paths.
            if (methodRef.Name == "get_IsVectorizationSupported" && !methodRef.HasThis
                && methodRef.Parameters.Count == 0)
            {
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp { Code = $"int32_t {tmp} = 0;", ResultVar = tmp, ResultTypeCpp = "int32_t" });
                stack.Push(new StackEntry(tmp, "int32_t", CompileTimeConstant: 0));
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
            var resolvedElem = ResolveTypeRefOperand(elemTypeArg);
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
            stack.Push(new StackEntry(tmp, "cil2cpp::Array*"));
            return;
        }

        // Multi-dimensional array methods: T[,].Get(), .Set(), .ctor(), .Address()
        // These don't exist as IL method bodies — they're synthesized by the CLR.
        // Emit as calls to runtime mdarray functions.
        if (methodRef.DeclaringType is Mono.Cecil.ArrayType mdArrType && mdArrType.Rank >= 2)
        {
            var elemTypeRef = mdArrType.ElementType;
            var resolvedElem = ResolveTypeRefOperand(elemTypeRef);
            var elemCppType = CppNameMapper.MangleTypeNameClean(resolvedElem);
            var elemDeclType = CppNameMapper.GetCppTypeForDecl(resolvedElem);
            var rank = mdArrType.Rank;
            if (CppNameMapper.IsPrimitive(resolvedElem))
                _module.RegisterPrimitiveTypeInfo(resolvedElem);

            if (methodRef.Name == "Get")
            {
                // T[,].Get(int, int, ...) → dereference element pointer
                // elemDeclType is the C++ storage type (int32_t for value, String* for ref)
                // We cast void* → (elemDeclType*) and dereference to get elemDeclType
                var indices = new string[rank];
                for (int d = rank - 1; d >= 0; d--)
                    indices[d] = stack.PopExpr();
                var arr = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t __md_idx_{tmp}[] = {{ {string.Join(", ", indices)} }}; " +
                           $"auto {tmp} = *({elemDeclType}*)cil2cpp::mdarray_get_element_ptr({arr}, __md_idx_{tmp});",
                    ResultVar = tmp,
                    ResultTypeCpp = elemDeclType,
                });
                stack.Push(new StackEntry(tmp, elemDeclType));
                return;
            }
            else if (methodRef.Name == "Set")
            {
                // T[,].Set(int, int, ..., T value) → store into element pointer
                var value = stack.PopExpr();
                var indices = new string[rank];
                for (int d = rank - 1; d >= 0; d--)
                    indices[d] = stack.PopExpr();
                var arr = stack.PopExpr();
                var tmp = $"__md_set_{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t __md_idx_{tmp}[] = {{ {string.Join(", ", indices)} }}; " +
                           $"*({elemDeclType}*)cil2cpp::mdarray_get_element_ptr({arr}, __md_idx_{tmp}) = ({elemDeclType}){value};",
                });
                return;
            }
            else if (methodRef.Name == "Address")
            {
                // T[,].Address(int, int, ...) → element pointer (for ref access)
                var indices = new string[rank];
                for (int d = rank - 1; d >= 0; d--)
                    indices[d] = stack.PopExpr();
                var arr = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t __md_idx_{tmp}[] = {{ {string.Join(", ", indices)} }}; " +
                           $"auto {tmp} = ({elemDeclType}*)cil2cpp::mdarray_get_element_ptr({arr}, __md_idx_{tmp});",
                    ResultVar = tmp,
                    ResultTypeCpp = $"{elemDeclType}*",
                });
                stack.Push(new StackEntry(tmp, $"{elemDeclType}*"));
                return;
            }
        }

        // GC.AllocateUninitializedArray<T>(int length, bool pinned) — AOT compile-time specialization
        // The BCL implementation uses runtime-internal allocation. For AOT, emit array_create directly.
        // This avoids the void* return type mismatch that causes RenderedBodyError stubs.
        if (methodRef.DeclaringType.FullName == "System.GC"
            && methodRef.Name == "AllocateUninitializedArray"
            && methodRef is GenericInstanceMethod gimAlloc
            && gimAlloc.GenericArguments.Count == 1)
        {
            // Pop arguments: (int length, bool pinned) — pinned is ignored in our GC
            var pinned = stack.PopExprOr("0");
            var length = stack.PopExprOr("0");
            var typeArg = gimAlloc.GenericArguments[0];
            var resolvedElem = ResolveTypeRefOperand(typeArg);
            var elemCppType = CppNameMapper.MangleTypeNameClean(resolvedElem);
            if (CppNameMapper.IsPrimitive(resolvedElem))
                _module.RegisterPrimitiveTypeInfo(resolvedElem);
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::array_create(&{elemCppType}_TypeInfo, {length});",
                ResultVar = tmp,
                ResultTypeCpp = "cil2cpp::Array*",
            });
            stack.Push(new StackEntry(tmp, "cil2cpp::Array*"));
            return;
        }

        // Activator.CreateInstance<T>() — AOT compile-time specialization
        // The BCL implementation uses RuntimeType.ActivatorCache which calls into the CLR runtime.
        // For AOT, we replace this with gc::alloc + default .ctor call.
        // The .ctor is needed because BCL types may have field initializers compiled into it
        // (e.g., SafeFileHandle._fileType = -1). ReachabilityAnalyzer seeds T..ctor().
        if (methodRef.DeclaringType.FullName == "System.Activator"
            && methodRef.Name == "CreateInstance" && methodRef.Parameters.Count == 0
            && methodRef is GenericInstanceMethod gimActivator
            && gimActivator.GenericArguments.Count == 1)
        {
            var typeArg = gimActivator.GenericArguments[0];
            var resolvedType = ResolveTypeRefOperand(typeArg);
            var cppType = CppNameMapper.MangleTypeName(resolvedType);
            var cleanType = CppNameMapper.MangleTypeNameClean(resolvedType);

            // Verify T has a parameterless constructor. If not, Activator.CreateInstance<T>()
            // would throw MissingMethodException at runtime in .NET. Emit throw for AOT.
            // Use _typeCache with the resolved type name (handles generic parameter resolution).
            bool hasParameterlessCtor = true;
            if (_typeCache.TryGetValue(resolvedType, out var activatorIrType))
            {
                hasParameterlessCtor = activatorIrType.Methods.Any(m =>
                    m.Name == ".ctor" && !m.IsStatic
                    && m.Parameters.Count == 0);
            }
            else
            {
                // Fallback: try Cecil resolve (works for non-generic typeArg)
                // Resolution can throw on corrupt metadata or cross-assembly references — null is safe
                TypeDefinition? activatorTargetDef = null;
                try { activatorTargetDef = typeArg.Resolve(); } catch (Exception) { }
                if (activatorTargetDef != null)
                {
                    hasParameterlessCtor = activatorTargetDef.Methods.Any(m =>
                        m.Name == ".ctor" && m.Parameters.Count == 0);
                }
            }
            if (!hasParameterlessCtor)
            {
                var tmp2 = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::throw_missing_method(); // {cppType} has no default constructor",
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp2} = ({cppType}*)nullptr;",
                    ResultVar = tmp2,
                    ResultTypeCpp = $"{cppType}*",
                });
                stack.Push(new StackEntry(tmp2, $"{cppType}*"));
                return;
            }

            var tmp = $"__t{tempCounter++}";
            // Allocate with TypeInfo
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ({cppType}*)cil2cpp::gc::alloc(sizeof({cppType}), &{cleanType}_TypeInfo);",
                ResultVar = tmp,
                ResultTypeCpp = $"{cppType}*",
            });
            // Call default .ctor — applies field initializers (e.g., _fileType = -1)
            // Check ICallRegistry first: if the ctor has an ICall mapping, use the ICall
            // function directly (the mangled ctor is likely a stub with no body).
            var icallFunc = ICallRegistry.Lookup(resolvedType, ".ctor", 0);
            if (icallFunc != null)
            {
                // ICall ctors take void* for __this
                block.Instructions.Add(new IRCall
                {
                    FunctionName = icallFunc,
                    Arguments = { $"(void*){tmp}" },
                });
            }
            else
            {
                // Regular ctor takes typed pointer
                var ctorFunc = CppNameMapper.MangleMethodName(cppType, ".ctor");
                block.Instructions.Add(new IRCall
                {
                    FunctionName = ctorFunc,
                    Arguments = { $"({cppType}*){tmp}" },
                });
            }
            stack.Push(new StackEntry(tmp, $"{cppType}*"));
            return;
        }

        // RuntimeHelpers.GetMethodTable(Object) — compiler intrinsic
        // The BCL IL uses pointer arithmetic relative to CoreCLR's 8-byte object header
        // to reach the MethodTable pointer. Our Object header is 16 bytes (TypeInfo* + sync_block),
        // so the IL-compiled version reads garbage. Return __type_info directly.
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && methodRef.Name == "GetMethodTable" && methodRef.Parameters.Count == 1)
        {
            var obj = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = (System_Runtime_CompilerServices_MethodTable*)(((cil2cpp::Object*)({obj}))->__type_info);",
                ResultVar = tmp,
                ResultTypeCpp = "System_Runtime_CompilerServices_MethodTable*"
            });
            stack.Push(new StackEntry(tmp, "System_Runtime_CompilerServices_MethodTable*"));
            return;
        }

        // RuntimeHelpers.GetRawData(Object) — compiler intrinsic
        // The BCL IL uses RawData.f_Data at offset 12 (sizeof(MethodTable*) + sizeof(uint32_t)),
        // but our Object has sizeof(Object) = 16 due to alignment padding after sync_block.
        // box<>/unbox<> use sizeof(Object) for the data offset, so GetRawData must match.
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && methodRef.Name == "GetRawData" && methodRef.Parameters.Count == 1)
        {
            var obj = stack.PopExprOr("nullptr");
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<uint8_t*>((cil2cpp::Object*)({obj})) + sizeof(cil2cpp::Object);",
                ResultVar = tmp,
                ResultTypeCpp = "uint8_t*"
            });
            stack.Push(new StackEntry(tmp, "uint8_t*"));
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
                Code = $"int32_t {startTmp} = {range}.f__Start_k__BackingField.f__value < 0 " +
                       $"? {range}.f__Start_k__BackingField.f__value + cil2cpp::array_length({arr}) + 1 " +
                       $": {range}.f__Start_k__BackingField.f__value;",
                ResultVar = startTmp,
                ResultTypeCpp = "int32_t",
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {endTmp} = {range}.f__End_k__BackingField.f__value < 0 " +
                       $"? {range}.f__End_k__BackingField.f__value + cil2cpp::array_length({arr}) + 1 " +
                       $": {range}.f__End_k__BackingField.f__value;",
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
            stack.Push(new StackEntry(tmp, "cil2cpp::Array*"));
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
                           $"{tmp}.f__reference = ({elemPtrType})cil2cpp::array_data({source}); " +
                           $"{tmp}.f__length = cil2cpp::array_length({source});",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
            }
            else if (paramTypeName.Contains("ArraySegment"))
            {
                // ArraySegment<T> → Span/ReadOnlySpan<T>: extract array data + offset + count
                var elemPtrType = "void*";
                if (methodRef.DeclaringType is GenericInstanceType spanGit2 && spanGit2.GenericArguments.Count > 0)
                {
                    var elemArgName = ResolveGenericTypeRef(spanGit2.GenericArguments[0], methodRef.DeclaringType);
                    if (!IsUnresolvedElementType(elemArgName))
                    {
                        var elemCpp = CppNameMapper.GetCppTypeForDecl(elemArgName);
                        elemPtrType = elemCpp + "*";
                    }
                }
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{retCpp} {tmp} = {{0}}; " +
                           $"{tmp}.f__reference = ({elemPtrType})cil2cpp::array_data((cil2cpp::Array*){source}.f__array) + {source}.f__offset; " +
                           $"{tmp}.f__length = {source}.f__count;",
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
                           $"{tmp}.f__reference = {source}.f__reference; " +
                           $"{tmp}.f__length = {source}.f__length;",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
            }
            stack.Push(new StackEntry(tmp, retCpp));
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
                    Code = $"{spanAccess(thisPtr, "f__reference")} = ({elemPtrType})cil2cpp::array_data({array}) + {start}; " +
                           $"{spanAccess(thisPtr, "f__length")} = {length};"
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
                           $"{spanAccess(thisPtr, "f__reference")} = ({elemPtrType})cil2cpp::array_data({array}); " +
                           $"{spanAccess(thisPtr, "f__length")} = cil2cpp::array_length({array}); " +
                           $"}} else {{ " +
                           $"{spanAccess(thisPtr, "f__reference")} = nullptr; " +
                           $"{spanAccess(thisPtr, "f__length")} = 0; }}"
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
                    Code = $"{spanAccess(thisPtr, "f__reference")} = ({elemPtrType}){pointer}; " +
                           $"{spanAccess(thisPtr, "f__length")} = {length};"
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
                    Code = $"{spanAccess(thisPtr, "f__reference")} = ({elemPtrType}){reference}; " +
                           $"{spanAccess(thisPtr, "f__length")} = {length};"
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
                stack.Push(new StackEntry(tmp, invoke.ReturnTypeCpp));
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
            // Look up blob size from ArrayInitDataBlobs — sizeof() doesn't work on extern incomplete arrays
            var blob = _module.ArrayInitDataBlobs.FirstOrDefault(b => b.Id == fieldHandle);
            var sizeExpr = blob != null ? $"{blob.Data.Length}" : $"sizeof({fieldHandle})";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"std::memcpy(cil2cpp::array_data({arr}), {fieldHandle}, {sizeExpr});"
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
                Code = $"{spanCpp} {tmp} = {{0}}; {tmp}.f__reference = ({elemCpp}*){fieldHandle}; {tmp}.f__length = {lengthExpr};",
                ResultVar = tmp,
                ResultTypeCpp = spanCpp,
            });
            stack.Push(new StackEntry(tmp, spanCpp));
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
            stack.Push(new StackEntry(tmp, "bool"));
            return;
        }

        // JIT intrinsic: RuntimeHelpers.IsBitwiseEquatable<T>()
        // The BCL IL throws InvalidOperationException (never meant to execute),
        // but the JIT replaces it at compile time. We do the same for AOT.
        if (methodRef.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && methodRef.Name == "IsBitwiseEquatable"
            && methodRef is GenericInstanceMethod isBitGim && isBitGim.GenericArguments.Count == 1)
        {
            var typeArg = ResolveTypeRefOperand(isBitGim.GenericArguments[0]);
            bool isBitwiseEq = IsBitwiseEquatable(typeArg);
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {(isBitwiseEq ? "true" : "false")};", ResultVar = tmp, ResultTypeCpp = "bool" });
            stack.Push(new StackEntry(tmp, "bool"));
            return;
        }

        // Buffer.Memmove<T>(ref T dest, ref T src, nuint elementCount)
        // The generic version passes element count, but our ICall expects byte count.
        // Intercept here to multiply by sizeof(T) before calling the runtime.
        if (methodRef.DeclaringType.FullName == "System.Buffer"
            && (methodRef.Name == "Memmove" || methodRef.Name == "__Memmove")
            && methodRef is GenericInstanceMethod memmoveGim && memmoveGim.GenericArguments.Count == 1)
        {
            var typeArg = memmoveGim.GenericArguments[0];
            var resolvedArg = ResolveTypeRefOperand(typeArg);
            var cppType = CppNameMapper.GetCppTypeName(resolvedArg);
            // For reference types (pointers), sizeof should be sizeof(void*) since array
            // elements are pointer-sized. For value types, use sizeof(StructType).
            // Check both: the C++ type name ending with *, AND whether it's a registered
            // value type — interfaces/classes don't end with * from GetCppTypeName but
            // are still reference types (sizeof should be pointer-sized).
            bool isValueType = cppType.EndsWith("*") ? false
                : CppNameMapper.IsRegisteredValueType(resolvedArg)
                  || CppNameMapper.IsRegisteredValueType(cppType);
            string sizeExpr;
            if (isValueType)
                sizeExpr = $"sizeof({cppType})";
            else
                sizeExpr = "sizeof(void*)";
            var elemCount = stack.PopExpr();
            var src = stack.PopExpr();
            var dest = stack.PopExpr();
            block.Instructions.Add(new IRCall
            {
                FunctionName = "cil2cpp::icall::Buffer_Memmove",
                Arguments = { $"(void*){dest}", $"(void*){src}", $"(uintptr_t)({elemCount}) * {sizeExpr}" },
            });
            return;
        }

        // SpanHelpers scalar search interceptions — BCL IL uses SIMD-dependent control
        // flow (Vector512/256/128 branches, runtime type checks) that is impractical for AOT.
        // Replace with simple scalar loops via cil2cpp::icall helpers.
        if (TryEmitSpanHelpersSearch(block, stack, methodRef, ref tempCounter))
            return;

        // BitOperations intrinsics — PopCount/ResetLowestSetBit use X86.Popcnt hardware
        // intrinsics that are unavailable in AOT. Replace with portable C++ equivalents.
        if (TryEmitBitOperationsIntrinsic(block, stack, methodRef, ref tempCounter))
            return;

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

        // AOT intrinsic: LambdaCompiler.Compile(LambdaExpression) → interpreter path.
        // Desktop BCL uses LambdaCompiler (Reflection.Emit / JIT-only).
        // Replace with: new LightCompiler().CompileTop(expr).CreateDelegate()
        // This is the same path NativeAOT BCL takes (FEATURE_COMPILE not defined).
        if (mappedName == null
            && methodRef.DeclaringType?.FullName == "System.Linq.Expressions.Compiler.LambdaCompiler"
            && methodRef.Name == "Compile"
            && methodRef.Parameters.Count == 1)
        {
            // Pop the LambdaExpression argument (static method, 1 param)
            var lambdaEntry = stack.PopEntry();
            if (EmitExpressionCompileInterpreterRedirect(block, stack, ref tempCounter, lambdaEntry.Expr))
                return;
            // Fallback: push argument back and let normal emission handle it
            stack.Push(lambdaEntry);
        }

        if (mappedName != null)
        {
            irCall.FunctionName = mappedName;
        }
        else if (methodRef is GenericInstanceMethod gim)
        {
            // Generic method instantiation — use the monomorphized name
            var elemMethod = gim.ElementMethod;
            // Resolve declaring type through active type param map — without this,
            // calls from within generic method bodies use unresolved type params (e.g., TResult)
            // in the declaring type, causing name mismatches with the actual function definitions
            // that use resolved types (e.g., System.Int32).
            var declType = ResolveTypeRefOperand(elemMethod.DeclaringType);
            // Resolve type arguments through active type param map (method-level generics)
            // Must handle complex types containing generic params (ArrayType, GenericInstanceType, etc.)
            var typeArgs = gim.GenericArguments.Select(a => ResolveTypeRefOperand(a)).ToList();
            // Include parameter types in key (matches CollectGenericMethod)
            // Resolve param types too — raw Cecil paramSig may contain unresolved generic params
            var paramSig = string.Join(",", elemMethod.Parameters.Select(p =>
                _ctx.Value.ActiveTypeParamMap != null
                    ? ResolveGenericTypeName(p.ParameterType, _ctx.Value.ActiveTypeParamMap)
                    : p.ParameterType.FullName));
            var key = MakeGenericMethodKey(declType, elemMethod.Name, typeArgs, paramSig);

            // Pre-compute outside lock: mangled name + Cecil resolution
            var mangledName = MangleGenericMethodName(declType, elemMethod.Name, typeArgs);
            var cecilMethod = elemMethod.Resolve();
            bool allResolved = cecilMethod != null
                && !typeArgs.Any(a => a.Contains("!!") || a.Contains("!0")
                    || ContainsUnresolvedGenericParam(a));

            // Lock for compound check+register (atomic disambiguation)
            lock (_genericMethodInstLock)
            {
                if (_genericMethodInstantiations.TryGetValue(key, out var gmInfo))
                {
                    irCall.FunctionName = gmInfo.MangledName;
                }
                else
                {
                    if (allResolved)
                    {
                        // Check for name collision and disambiguate if needed
                        if (_genericMethodMangledNames.Contains(mangledName))
                        {
                            var disambigMap = new Dictionary<string, string>();
                            for (int gi = 0; gi < cecilMethod.GenericParameters.Count && gi < typeArgs.Count; gi++)
                                disambigMap[cecilMethod.GenericParameters[gi].Name] = typeArgs[gi];
                            if (_ctx.Value.ActiveTypeParamMap != null)
                                foreach (var (k, v) in _ctx.Value.ActiveTypeParamMap)
                                    disambigMap.TryAdd(k, v);
                            var disambigSuffix = string.Join("_", cecilMethod.Parameters
                                .Select(p => CppNameMapper.MangleTypeName(
                                    ResolveGenericTypeName(p.ParameterType, disambigMap))));
                            mangledName += $"__{disambigSuffix}";
                        }
                        _genericMethodMangledNames.Add(mangledName);
                        _genericMethodInstantiations[key] = new GenericMethodInstantiationInfo(
                            declType, elemMethod.Name, typeArgs, mangledName, cecilMethod);
                    }
                    irCall.FunctionName = mangledName;
                }
            }
        }
        else
        {
            var typeCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
            var funcName = CppNameMapper.MangleMethodName(typeCpp, methodRef.Name);
            // op_Explicit/op_Implicit: disambiguate by return type (matches ConvertMethod)
            if (methodRef.Name is "op_Explicit" or "op_Implicit" or "op_CheckedExplicit" or "op_CheckedImplicit")
            {
                var resolvedRetType = ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType);
                var retMangled = CppNameMapper.MangleTypeName(resolvedRetType);
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
                    // Count overloads excluding generic methods with same name but different arity.
                    // Generic method instantiations get a type-arg suffix in C++ (e.g., FromCanceled_System_Int32)
                    // so they don't collide with non-generic overloads at the C++ name level.
                    var isGenericMethod = methodRef is GenericInstanceMethod || methodRef.HasGenericParameters;
                    var overloadCount = declTypeDef.Methods.Count(m =>
                        m.Name == baseName
                        && m.HasGenericParameters == (methodRef is GenericInstanceMethod || methodRef.HasGenericParameters));
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
            // Store IL param key for deferred fixup (Pass 3.7) — the target type
            // may not be disambiguated yet when this body is compiled.
            if (ilParamKey.Length > 0)
                irCall.DeferredDisambigKey = ilParamKey;
        }

        // Collect arguments (in reverse order from stack)
        var args = new List<string>();
        var argEntries = new List<StackEntry>();
        for (int i = 0; i < methodRef.Parameters.Count; i++)
        {
            var entry = stack.PopEntry();
            args.Add(entry.Expr);
            argEntries.Add(entry);
        }
        args.Reverse();
        argEntries.Reverse();

        // For icalls: RuntimeTypeHandle/RuntimeMethodHandle/RuntimeFieldHandle are value type
        // structs wrapping a single pointer. The C++ icall expects void*, so extract the inner
        // pointer field. Only apply when the stack entry is actually a struct (has matching CppType),
        // NOT when it's already a pointer (e.g. TypeInfo* from ldtoken).
        if (mappedName != null)
        {
            for (int i = 0; i < args.Count && i < methodRef.Parameters.Count; i++)
            {
                var paramType = methodRef.Parameters[i].ParameterType;
                var entryType = argEntries[i].CppType;
                if (paramType.FullName == "System.RuntimeTypeHandle"
                    && entryType != null
                    && entryType.Contains("RuntimeTypeHandle")
                    && !entryType.EndsWith("*"))
                {
                    args[i] = $"(void*){args[i]}.f_m_type";
                    continue;
                }

                // Value-type struct → void* for ICalls: when a generic parameter resolves
                // to a value type (e.g., Marshal.StructureToPtr<T> where T is a struct),
                // the C++ ICall expects void* but the argument is a stack-local struct.
                // Take its address and cast to void*.
                if (entryType != null && !entryType.EndsWith("*")
                    && (paramType is GenericParameter || paramType.FullName == "System.Object"))
                {
                    args[i] = $"(void*)&{args[i]}";
                }
            }
        }

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
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CIL2CPP] Warning: vararg method resolution failed for {methodRef.FullName}: {ex.Message}");
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
        string? rawThisArg = null; // Pre-cast this pointer for constrained call fixup
        if (methodRef.HasThis)
        {
            var thisEntry = stack.PopEntry();
            var thisArg = thisEntry.Expr == "0" ? "__this" : thisEntry.Expr;
            rawThisArg = thisArg; // Save before any cast
            if (mappedName != null && methodRef.DeclaringType.FullName == "System.Object")
            {
                // BCL mapped Object methods expect cil2cpp::Object*
                thisArg = $"(cil2cpp::Object*){thisArg}";
            }
            else if (mappedName != null && methodRef.HasThis)
            {
                // BCL mapped value type instance methods:
                // 'this' is a pointer (&x). Most icalls expect a value (dereference),
                // but value types whose ICalls take self* need the pointer (no dereference):
                //   ArgIterator — mutable value type
                //   IntPtr/UIntPtr — ToPointer ICall takes intptr_t*/uintptr_t*
                bool isValueTarget = false;
                try { isValueTarget = methodRef.DeclaringType.Resolve()?.IsValueType == true; }
                catch { isValueTarget = CppNameMapper.IsValueType(methodRef.DeclaringType.FullName); }
                if (isValueTarget
                    && methodRef.DeclaringType.FullName is not "System.ArgIterator"
                        and not "System.IntPtr" and not "System.UIntPtr")
                    thisArg = $"*({thisArg})";
                else if (!isValueTarget)
                {
                    // ICall for reference type: cast 'this' to the correct pointer type.
                    // The stack may have Object* (from array_get, generic code) but the ICall
                    // expects the concrete type (e.g., cil2cpp::string_length expects String*).
                    // Runtime types have C++ struct definitions → cast to cil2cpp::Type*.
                    // BCL types may not have struct definitions → cast to void* (ICalls accept void*).
                    var fullName = methodRef.DeclaringType.FullName;
                    var runtimeCppName = fullName switch
                    {
                        "System.String" => "cil2cpp::String",
                        "System.Array" => "cil2cpp::Array",
                        "System.Object" => "cil2cpp::Object",
                        "System.Exception" => "cil2cpp::Exception",
                        "System.Delegate" or "System.MulticastDelegate" => "cil2cpp::Delegate",
                        _ => null
                    };
                    if (runtimeCppName != null)
                        thisArg = $"({runtimeCppName}*)(void*){thisArg}";
                    else
                        thisArg = $"(void*){thisArg}";
                }
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
                else
                {
                    // Value type instance method: 'this' must be a pointer.
                    // If stack has a value (from ldloc, not ldloca), take its address.
                    // Use CppType tracking to detect if already a pointer.
                    if (!thisEntry.IsPointer && !thisEntry.IsAddressOf)
                        thisArg = $"&({thisArg})";
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
            bool isValueType = constrainedIrType != null
                ? constrainedIrType.IsValueType
                : constrainedType.IsValueType;
            if (isValueType)
            {
                // Find the method override on the constrained type (only methods with bodies)
                var overrideMethod = constrainedIrType?.Methods.FirstOrDefault(m =>
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
                        var cppTypeName = GetMangledTypeNameForRef(constrainedType);
                        var typeInfoName = $"{cppTypeName}_TypeInfo";
                        // Use the pre-cast raw this pointer (saved before Object* cast was applied)
                        var rawPtr = rawThisArg ?? irCall.Arguments[0];
                        irCall.Arguments[0] = $"(cil2cpp::Object*)cil2cpp::box_raw({rawPtr}, sizeof({cppTypeName}), &{typeInfoName})";
                    }
                }
            }
            else
            {
                // ECMA-335 III.2.1: For reference types, ptr is a managed pointer
                // to the reference variable/slot. Dereference it to get the actual
                // object reference. The ptr is typed as T* in our codegen but really
                // points to a slot containing a T* (so it's effectively T**).
                // Cast to Object** and dereference to get Object*.
                if (irCall.Arguments.Count > 0)
                {
                    // Use the pre-cast raw this pointer (saved before Object* cast was applied)
                    var rawPtr = rawThisArg ?? irCall.Arguments[0];
                    irCall.Arguments[0] = $"*((cil2cpp::Object**){rawPtr})";
                }
            }
        }

        // Static abstract/virtual interface method resolution (constrained. T; call)
        // .NET 7+ allows constrained. prefix with call (not just callvirt) for static
        // virtual/abstract interface members. Resolve to the constrained type's implementation.
        if (constrainedType != null && !methodRef.HasThis && mappedName == null)
        {
            var constrainedCppType = CppNameMapper.GetCppTypeName(
                ResolveTypeRefOperand(constrainedType));
            bool isPrimitive = IsCppPrimitiveType(constrainedCppType);

            // For primitive types, prefer intrinsic operators/properties over explicit
            // interface impls. DIM methods (IBitwiseOperators, INumberBase, etc.) may not
            // have compiled bodies, causing UndeclaredFunction stubs. Intrinsics are always correct.
            if (isPrimitive)
            {
                var cppOp = TryGetIntrinsicOperator(methodRef.Name);
                if (cppOp != null)
                {
                    if (EmitIntrinsicOperator(block, stack, args, irCall, cppOp,
                        constrainedCppType, ref tempCounter))
                        return;
                }

                var intrinsicVal = TryGetIntrinsicNumericProperty(methodRef.Name, constrainedCppType);
                if (intrinsicVal != null)
                {
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{constrainedCppType} {tmp} = {intrinsicVal};",
                        ResultVar = tmp,
                        ResultTypeCpp = constrainedCppType,
                    });
                    stack.Push(new StackEntry(tmp, constrainedCppType));
                    irCall.Arguments.Clear();
                    args.Clear();
                    return;
                }
            }

            var constrainedCacheKey = ResolveCacheKey(constrainedType);
            var constrainedIrType = _typeCache.GetValueOrDefault(constrainedCacheKey);
            bool resolvedStaticConstraint = false;
            if (constrainedIrType != null)
            {
                // Find the explicit interface implementation on the constrained type.
                // The method name in IL is the interface method name (e.g., "CastFrom"),
                // but explicit interface impls have names like "System.IUtfChar<System.Char>.CastFrom".
                // Match by exact name OR by suffix (after the last '.').
                // NOTE: relaxed BasicBlocks check — method body may not be compiled yet
                // at the time of constrained call resolution. Deferred generic type bodies
                // (Pass 3.4) and generic method specializations (Pass 3.5) may reference
                // each other's methods before bodies are compiled.
                var staticImpl = constrainedIrType.Methods.FirstOrDefault(m =>
                    m.IsStatic && MatchesMethodName(m.Name, methodRef.Name)
                    && ParameterTypesMatchRef(m, methodRef));
                // Fallback for static abstract interface methods with parameter count only:
                // when ParameterTypesMatchRef fails (e.g., unresolvable nested generic params),
                // try matching by param count if there's exactly one candidate.
                // This is already handled below at the "candidates" check.
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
                        // Safety check: reject if parameter types obviously mismatch.
                        // This prevents resolving SIMD overloads to scalar overloads when
                        // the type was created on demand with only non-SIMD methods.
                        bool paramMismatch = false;
                        var candidate = candidates[0];
                        for (int pi = 0; pi < candidate.Parameters.Count; pi++)
                        {
                            var irParamType = candidate.Parameters[pi].ILTypeName ?? candidate.Parameters[pi].CppTypeName;
                            var refParamType = ResolveMethodRefParamType(methodRef, pi);
                            // If one contains a SIMD vector type and the other doesn't, reject
                            if (IsSimdTypeName(irParamType) != IsSimdTypeName(refParamType))
                            { paramMismatch = true; break; }
                        }
                        if (!paramMismatch)
                        {
                            irCall.FunctionName = candidate.CppName;
                            resolvedStaticConstraint = true;
                        }
                    }
                }
            }

            // ICall override for resolved constrained calls.
            // When a constrained call resolves to a method on the constrained type
            // (e.g., DontNegate.NegateIfNeeded), check if there's an ICall mapping
            // for that specific type+method and redirect accordingly.
            if (resolvedStaticConstraint && constrainedIrType?.ILFullName != null)
            {
                var constrainedIcall = ICallRegistry.Lookup(
                    constrainedIrType.ILFullName, methodRef.Name, methodRef.Parameters.Count);
                if (constrainedIcall != null)
                    irCall.FunctionName = constrainedIcall;
            }

            // On-demand static method shell creation for constrained calls.
            // When the constrained IRType exists but has no matching static method yet,
            // create a method shell eagerly. EnsureConstrainedMethodBodiesExist will
            // compile the body later.
            // Skip SIMD parameter calls — these are dead-code branches and should not
            // create shells that resolve to the wrong (scalar) overload.
            bool hasSimdParams = false;
            for (int pi = 0; pi < methodRef.Parameters.Count && !hasSimdParams; pi++)
            {
                var refParamType = ResolveMethodRefParamType(methodRef, pi);
                if (IsSimdTypeName(refParamType))
                    hasSimdParams = true;
            }
            // Also skip when the method ref has unresolvable method-level generic params (!!0, etc.)
            // Use Cecil's type system for structural checking instead of string pattern matching.
            bool hasUnresolvedGenericParams = false;
            if (methodRef is not GenericInstanceMethod) // Resolved generic instances are OK
            {
                foreach (var p in methodRef.Parameters)
                {
                    if (ContainsMethodGenericParameter(p.ParameterType))
                    { hasUnresolvedGenericParams = true; break; }
                }
                if (!hasUnresolvedGenericParams)
                    hasUnresolvedGenericParams = ContainsMethodGenericParameter(methodRef.ReturnType);
            }
            // Skip GenericInstanceMethod calls — the implementation on the constrained type
            // is itself a generic method definition (e.g., Single.TryConvertFromSaturating<TOther>).
            // These need the generic method specialization pipeline, not a non-generic shell.
            bool isGenericMethodCall = methodRef is GenericInstanceMethod;
            if (!resolvedStaticConstraint && constrainedIrType != null
                && constrainedIrType.IsValueType && !hasSimdParams && !hasUnresolvedGenericParams
                && !isGenericMethodCall)
            {
                // First, check if a shell with matching Name + param count already exists
                // (may have been created by a previous constrained call to the same method)
                var existingShell = constrainedIrType.Methods.FirstOrDefault(m =>
                    m.IsStatic && MatchesMethodName(m.Name, methodRef.Name)
                    && m.Parameters.Count == methodRef.Parameters.Count);
                if (existingShell != null)
                {
                    irCall.FunctionName = existingShell.CppName;
                    resolvedStaticConstraint = true;
                }
                else
                {
                    var expectedCppName = CppNameMapper.MangleMethodName(
                        constrainedIrType.CppName, methodRef.Name);

                    // Build an augmented type param map that includes the interface's
                    // generic params. When methodRef.DeclaringType is INumber<T> (resolved to
                    // INumber<Char>), map the interface's own generic params (TSelf) to their
                    // concrete values so parameter types like TSelf resolve to System.Char.
                    var shellParamMap = _ctx.Value.ActiveTypeParamMap != null
                        ? new Dictionary<string, string>(_ctx.Value.ActiveTypeParamMap)
                        : new Dictionary<string, string>();
                    {
                        var declType = methodRef.DeclaringType;
                        // Resolve through _ctx.Value.ActiveTypeParamMap first to get concrete generic args
                        if (declType is GenericInstanceType git)
                        {
                            var openIface = git.ElementType.Resolve();
                            if (openIface?.HasGenericParameters == true)
                            {
                                for (int gi = 0; gi < openIface.GenericParameters.Count && gi < git.GenericArguments.Count; gi++)
                                {
                                    var gpName = openIface.GenericParameters[gi].Name; // e.g. "TSelf"
                                    var concreteArg = _ctx.Value.ActiveTypeParamMap != null
                                        ? ResolveGenericTypeName(git.GenericArguments[gi], _ctx.Value.ActiveTypeParamMap)
                                        : git.GenericArguments[gi].FullName;
                                    shellParamMap.TryAdd(gpName, concreteArg);
                                }
                            }
                        }
                    }
                    var shellMethod = new IRMethod
                    {
                        Name = methodRef.Name,
                        CppName = expectedCppName,
                        DeclaringType = constrainedIrType,
                        ReturnTypeCpp = ResolveTypeForDecl(
                            shellParamMap.Count > 0
                                ? ResolveGenericTypeName(methodRef.ReturnType, shellParamMap)
                                : methodRef.ReturnType.FullName),
                        IsStatic = true,
                    };
                    for (int pi = 0; pi < methodRef.Parameters.Count; pi++)
                    {
                        var paramDef = methodRef.Parameters[pi];
                        var paramTypeName = shellParamMap.Count > 0
                            ? ResolveGenericTypeName(paramDef.ParameterType, shellParamMap)
                            : paramDef.ParameterType.FullName;
                        var paramName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{pi}";
                        shellMethod.Parameters.Add(new IRParameter
                        {
                            Name = paramName,
                            CppName = CppNameMapper.MangleIdentifier(paramName),
                            CppTypeName = ResolveTypeForDecl(paramTypeName),
                            ILTypeName = paramTypeName,
                            Index = pi,
                        });
                    }

                    // Disambiguate overloaded method names
                    var existingWithSameName = constrainedIrType.Methods.Count(m => m.CppName == shellMethod.CppName);
                    if (existingWithSameName > 0)
                    {
                        var paramSuffix = string.Join("_", shellMethod.Parameters.Select(p =>
                            CppNameMapper.MangleTypeName(p.ILTypeName ?? p.CppTypeName)));
                        shellMethod.CppName = $"{shellMethod.CppName}__{paramSuffix}";
                    }

                    constrainedIrType.Methods.Add(shellMethod);
                    irCall.FunctionName = shellMethod.CppName;
                    resolvedStaticConstraint = true;
                }
            }

            // Fallback: if static constrained call wasn't resolved, try operator intrinsics
            // for non-primitive types too. These are well-known operators from numeric interfaces
            // (IBitwiseOperators, IComparisonOperators, etc.) that map to C++ operators.
            // Skip SIMD vector types — opaque structs don't support native C++ operators.
            if (!resolvedStaticConstraint && !IsSimdTypeName(constrainedCppType))
            {
                var cppOp = TryGetIntrinsicOperator(methodRef.Name);
                if (cppOp != null)
                {
                    if (EmitIntrinsicOperator(block, stack, args, irCall, cppOp,
                        constrainedCppType, ref tempCounter))
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
                else if (methodRef is GenericInstanceMethod gvmRefIface
                    && gvmRefIface.ElementMethod.Resolve() is { IsVirtual: true, HasGenericParameters: true })
                {
                    // Generic interface method dispatch: interface methods with generic parameters
                    // can't be found by ParameterTypesMatchRef (open vs resolved type args).
                    // Use type-check dispatch chain on all implementing types.
                    TrySetupGenericVirtualDispatch(irCall, gvmRefIface, resolved);
                }
            }
            else if (resolved != null && !resolved.IsValueType)
            {
                // Class virtual dispatch — match by name and parameter types
                // When multiple vtable entries match (e.g. inherited + newslot), prefer
                // the one whose declaring type matches the method reference's declaring type.
                var candidates = resolved.VTable.Where(e => e.MethodName == methodRef.Name
                    && (e.Method == null || ParameterTypesMatchRef(e.Method, methodRef))).ToList();
                var declaringCppName = CppNameMapper.GetCppTypeName(declaringTypeName);
                var entry = candidates.FirstOrDefault(e => e.DeclaringType?.CppName == declaringCppName)
                    ?? candidates.FirstOrDefault();
                if (entry != null)
                {
                    irCall.IsVirtual = true;
                    irCall.VTableSlot = entry.Slot;
                    irCall.VTableReturnType = CppNameMapper.GetCppTypeForDecl(
                        ResolveGenericTypeRef(methodRef.ReturnType, methodRef.DeclaringType));
                    irCall.VTableParamTypes = BuildVTableParamTypes(methodRef);
                }
                else if (methodRef is GenericInstanceMethod gvmRef
                    && gvmRef.ElementMethod.Resolve() is { IsVirtual: true, HasGenericParameters: true })
                {
                    // Generic virtual method dispatch: the vtable can't hold per-specialization
                    // function pointers, so emit a type-check dispatch chain instead.
                    TrySetupGenericVirtualDispatch(irCall, gvmRef, resolved);
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
                    "Finalize" => ObjectVTableSlots.FinalizeSlot,
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

            // ICalls return void* for reference types — cast to expected C++ pointer type.
            // Covers Interlocked _obj methods, Type_GetMethod, Type_GetField, etc.
            if (mappedName != null && retType.EndsWith("*") && retType != "void*")
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
                // After method-level substitution, if result is a GenericParameter that the
                // caller's type param map can resolve, use that directly. This avoids wrong
                // resolution when the callee's declaring type has different generic args.
                // Example: INumberBase<!!0>::TryConvertToSaturating<!0>(!!0, !0&) where
                // !0 = caller's TSelf but callee's declaring type resolves it to !!0 = TOther.
                string elemResolved;
                if (elemType is GenericParameter gpAfterSubst
                    && _ctx.Value.ActiveTypeParamMap != null
                    && _ctx.Value.ActiveTypeParamMap.TryGetValue(gpAfterSubst.Name, out var directMapped))
                    elemResolved = directMapped;
                else
                    elemResolved = ResolveGenericTypeRef(elemType, methodRef.DeclaringType);
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

            // Char parameter with literal 0: MSVC can't disambiguate char16_t vs int overloads.
            // Cast 0 to (char16_t)0 to resolve the ambiguity.
            if (paramType.FullName == "System.Char" && args[i] == "0")
            {
                args[i] = "(char16_t)0";
                continue;
            }

            // Skip value types (enums, structs) — use Cecil for authoritative check.
            // BUT: PointerType (e.g., System.UInt16*) must NOT be skipped — Cecil's
            // PointerType.Resolve() resolves the ELEMENT type (UInt16.IsValueType=true),
            // but the parameter is actually a pointer (uint16_t*) which needs a cast.
            // IntPtr/UIntPtr are value types but aliased to intptr_t/uintptr_t (scalars).
            // They need casts from pointer types (C++ rejects implicit void*→intptr_t).
            if (paramType is not Mono.Cecil.PointerType
                && paramType is not Mono.Cecil.ArrayType
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
            // Use bitcast_to<> instead of C-style cast to handle struct args in dead code
            // branches (e.g., Enum.TryFormat<StructType> testing typeof(IntPtr)).
            if (expectedType is "intptr_t" or "uintptr_t")
            {
                if (args[i] != "nullptr" && args[i] != "0" && !args[i].StartsWith($"({expectedType})"))
                    args[i] = $"cil2cpp::bitcast_to<{expectedType}>({args[i]})";
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
        // Multi-dimensional array newobj: int[,]::.ctor(int, int) → mdarray_create
        if (ctorRef.DeclaringType is Mono.Cecil.ArrayType mdArrCtor && mdArrCtor.Rank >= 2)
        {
            var elemTypeRef = mdArrCtor.ElementType;
            var resolvedElem = ResolveTypeRefOperand(elemTypeRef);
            var elemCppType = CppNameMapper.MangleTypeNameClean(resolvedElem);
            var rank = mdArrCtor.Rank;
            if (CppNameMapper.IsPrimitive(resolvedElem))
                _module.RegisterPrimitiveTypeInfo(resolvedElem);
            var mdDims = new string[rank];
            for (int d = rank - 1; d >= 0; d--)
                mdDims[d] = stack.PopExpr();
            var mdTmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t __md_lens_{mdTmp}[] = {{ {string.Join(", ", mdDims)} }}; " +
                       $"auto {mdTmp} = cil2cpp::mdarray_create(&{elemCppType}_TypeInfo, {rank}, __md_lens_{mdTmp});",
                ResultVar = mdTmp,
                ResultTypeCpp = "cil2cpp::MdArray*",
            });
            stack.Push(new StackEntry(mdTmp, "cil2cpp::MdArray*"));
            return;
        }

        // BCL exception types: allocate using runtime C++ struct, then call the constructor.
        // Their structs are aliased (using System_ArgumentNullException = cil2cpp::ArgumentNullException)
        // and their constructors compile from IL.
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
                           $"{spanTmp}.f__reference = ({elemPtrType})cil2cpp::array_data({array}) + {start}; " +
                           $"{spanTmp}.f__length = {length};",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(new StackEntry(spanTmp, spanTypeCpp));
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
                           $"{spanTmp}.f__reference = ({elemPtrType})cil2cpp::array_data({array}); " +
                           $"{spanTmp}.f__length = cil2cpp::array_length({array}); }}",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(new StackEntry(spanTmp, spanTypeCpp));
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
                           $"{spanTmp}.f__reference = ({elemPtrType}){pointer}; " +
                           $"{spanTmp}.f__length = {length};",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(new StackEntry(spanTmp, spanTypeCpp));
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
                           $"{spanTmp}.f__reference = ({elemPtrType}){reference}; " +
                           $"{spanTmp}.f__length = {length};",
                    ResultVar = spanTmp,
                    ResultTypeCpp = spanTypeCpp,
                });
                stack.Push(new StackEntry(spanTmp, spanTypeCpp));
                return;
            }
        }

        // String..ctor(ReadOnlySpan<char>) — needs special allocation for flexible char array.
        // Standard gc::alloc(sizeof(String)) doesn't reserve space for chars.
        // Use string_fast_allocate(length) + memcpy instead.
        // Defensive: null/garbage spans produce empty strings (safe fallback).
        if (ctorRef.DeclaringType.FullName == "System.String"
            && ctorRef.Parameters.Count == 1
            && ctorRef.Parameters[0].ParameterType.FullName.StartsWith("System.ReadOnlySpan`1"))
        {
            var span = stack.PopExpr();
            var tmp2 = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp2} = ({span}.f__reference && {span}.f__length > 0) " +
                       $"? cil2cpp::string_fast_allocate({span}.f__length) " +
                       $": cil2cpp::string_fast_allocate(0); " +
                       $"if ({span}.f__reference && {span}.f__length > 0) " +
                       $"std::memcpy(&{tmp2}->f__firstChar, {span}.f__reference, {span}.f__length * sizeof(char16_t));",
                ResultVar = tmp2,
                ResultTypeCpp = "cil2cpp::String*",
            });
            stack.Push(new StackEntry(tmp2, "cil2cpp::String*"));
            return;
        }

        // String..ctor(char*) — null-terminated UTF-16 string.
        // Must use string_fast_allocate to get correct size, then memcpy.
        if (ctorRef.DeclaringType.FullName == "System.String"
            && ctorRef.Parameters.Count == 1
            && ctorRef.Parameters[0].ParameterType.IsPointer)
        {
            var ptr = stack.PopExpr();
            var tmp2 = $"__t{tempCounter++}";
            var lenTmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {lenTmp} = 0; " +
                       $"if ((char16_t*)(void*){ptr}) {{ while (((char16_t*)(void*){ptr})[{lenTmp}]) {lenTmp}++; }} " +
                       $"auto {tmp2} = cil2cpp::string_fast_allocate({lenTmp}); " +
                       $"if ({lenTmp} > 0) std::memcpy(&{tmp2}->f__firstChar, (char16_t*)(void*){ptr}, {lenTmp} * sizeof(char16_t));",
                ResultVar = tmp2,
                ResultTypeCpp = "cil2cpp::String*",
            });
            stack.Push(new StackEntry(tmp2, "cil2cpp::String*"));
            return;
        }

        // String..ctor(char*, int startIndex, int length) — substring from pointer.
        if (ctorRef.DeclaringType.FullName == "System.String"
            && ctorRef.Parameters.Count == 3
            && ctorRef.Parameters[0].ParameterType.IsPointer
            && ctorRef.Parameters[1].ParameterType.FullName == "System.Int32"
            && ctorRef.Parameters[2].ParameterType.FullName == "System.Int32")
        {
            var length = stack.PopExpr();
            var startIndex = stack.PopExpr();
            var ptr = stack.PopExpr();
            var tmp2 = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp2} = cil2cpp::string_fast_allocate({length}); " +
                       $"if ({length} > 0) std::memcpy(&{tmp2}->f__firstChar, " +
                       $"((char16_t*)(void*){ptr}) + {startIndex}, {length} * sizeof(char16_t));",
                ResultVar = tmp2,
                ResultTypeCpp = "cil2cpp::String*",
            });
            stack.Push(new StackEntry(tmp2, "cil2cpp::String*"));
            return;
        }

        // String..ctor(char c, int count) — repeat character.
        if (ctorRef.DeclaringType.FullName == "System.String"
            && ctorRef.Parameters.Count == 2
            && ctorRef.Parameters[0].ParameterType.FullName == "System.Char"
            && ctorRef.Parameters[1].ParameterType.FullName == "System.Int32")
        {
            var count = stack.PopExpr();
            var ch = stack.PopExpr();
            var tmp2 = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp2} = cil2cpp::string_fast_allocate({count}); " +
                       $"for (int32_t __i = 0; __i < {count}; __i++) {tmp2}->chars[__i] = (char16_t){ch};",
                ResultVar = tmp2,
                ResultTypeCpp = "cil2cpp::String*",
            });
            stack.Push(new StackEntry(tmp2, "cil2cpp::String*"));
            return;
        }

        // String..ctor(char[] value) — from char array.
        if (ctorRef.DeclaringType.FullName == "System.String"
            && ctorRef.Parameters.Count == 1
            && ctorRef.Parameters[0].ParameterType.IsArray)
        {
            var arr = stack.PopExpr();
            var tmp2 = $"__t{tempCounter++}";
            var lenTmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"int32_t {lenTmp} = {arr} ? cil2cpp::array_length((cil2cpp::Array*){arr}) : 0; " +
                       $"auto {tmp2} = cil2cpp::string_fast_allocate({lenTmp}); " +
                       $"if ({lenTmp} > 0) std::memcpy(&{tmp2}->f__firstChar, " +
                       $"(char16_t*)cil2cpp::array_data((cil2cpp::Array*){arr}), {lenTmp} * sizeof(char16_t));",
                ResultVar = tmp2,
                ResultTypeCpp = "cil2cpp::String*",
            });
            stack.Push(new StackEntry(tmp2, "cil2cpp::String*"));
            return;
        }

        // String..ctor(char[] value, int startIndex, int length) — substring from array.
        if (ctorRef.DeclaringType.FullName == "System.String"
            && ctorRef.Parameters.Count == 3
            && ctorRef.Parameters[0].ParameterType.IsArray
            && ctorRef.Parameters[1].ParameterType.FullName == "System.Int32"
            && ctorRef.Parameters[2].ParameterType.FullName == "System.Int32")
        {
            var length = stack.PopExpr();
            var startIndex = stack.PopExpr();
            var arr = stack.PopExpr();
            var tmp2 = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp2} = cil2cpp::string_fast_allocate({length}); " +
                       $"if ({length} > 0) std::memcpy(&{tmp2}->f__firstChar, " +
                       $"(char16_t*)cil2cpp::array_data((cil2cpp::Array*){arr}) + {startIndex}, " +
                       $"{length} * sizeof(char16_t));",
                ResultVar = tmp2,
                ResultTypeCpp = "cil2cpp::String*",
            });
            stack.Push(new StackEntry(tmp2, "cil2cpp::String*"));
            return;
        }

        var cacheKey = ResolveCacheKey(ctorRef.DeclaringType);
        var typeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        // SIMD Vector<T> newobj — dead code (guarded by IsSupported/IsHardwareAccelerated == false).
        // Same interception as EmitMethodCall's SIMD no-op (line ~714). Without this,
        // generic type mismatches (e.g., T=Char → Vector<UInt16>) cause ctor name resolution
        // failures and param count mismatches in the code generator's gate checks.
        {
            var declType = ctorRef.DeclaringType.FullName;
            // Strip generic instance suffix for pattern matching
            var rawDeclType = declType.Contains('`') && declType.Contains('<')
                ? declType[..declType.IndexOf('<')] : declType;
            if (rawDeclType.StartsWith("System.Runtime.Intrinsics.")
                || rawDeclType == "System.Numerics.Vector"
                || rawDeclType.StartsWith("System.Numerics.Vector`"))
            {
                // Pop ctor arguments from stack
                for (int i = 0; i < ctorRef.Parameters.Count; i++)
                    if (stack.Count > 0) stack.Pop();
                // Push default-initialized value
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{typeCpp} {tmp} = {{}}; // SIMD Vector newobj stub",
                    ResultVar = tmp,
                    ResultTypeCpp = typeCpp,
                });
                stack.Push(new StackEntry(tmp, typeCpp));
                return;
            }
        }

        // Detect delegate constructor: base is MulticastDelegate/Delegate, ctor(object, IntPtr)
        var isDelegateCtor = false;
        if (ctorRef.Parameters.Count == 2)
        {
            if (_typeCache.TryGetValue(cacheKey, out var delegateType) && delegateType.IsDelegate)
                isDelegateCtor = true;
            else
            {
                // Fallback: check Cecil for BCL delegate types not in _typeCache
                // Resolution can fail for cross-assembly types — isDelegateCtor stays false (safe default)
                try
                {
                    var resolved = ctorRef.DeclaringType.Resolve();
                    isDelegateCtor = resolved?.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate";
                }
                catch (Exception) { }
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
        string? newobjDeferredKey = null;
        {
            var ilParamKey = string.Join(",", ctorRef.Parameters.Select(p =>
                ResolveGenericTypeRef(p.ParameterType, ctorRef.DeclaringType)));
            if (ilParamKey.Length > 0) newobjDeferredKey = ilParamKey;
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
                            .Where(m => m.Name == ".ctor" && m.Parameters.Count == ctorRef.Parameters.Count)
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
                                var cParams = c.Parameters.Select(p => p.CppTypeName).ToList();
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
                // Fallback 2: if IRType lookup didn't resolve, use Cecil metadata
                // to disambiguate constructor overloads by appending parameter type suffix
                if (!resolved)
                {
                    var declTypeDef = ctorRef.DeclaringType.Resolve();
                    if (declTypeDef != null)
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

        // Parameterless constructor call but no parameterless constructor exists:
        // This happens with Activator.CreateInstance<T>() / CreateViaDefaultConstructor<T>()
        // on types that only have parameterized constructors. At runtime, .NET would throw
        // MissingMethodException. Emit throw + push dummy result for stack balance.
        if (ctorRef.Parameters.Count == 0)
        {
            var declTypeDef = ctorRef.DeclaringType.Resolve();
            if (declTypeDef != null)
            {
                var hasParameterlessCtor = declTypeDef.Methods.Any(m =>
                    m.Name == ".ctor" && m.Parameters.Count == 0);
                if (!hasParameterlessCtor)
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"cil2cpp::throw_missing_method(); // {typeCpp} has no default constructor",
                    });
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{tmp} = nullptr;",
                        ResultVar = tmp,
                        ResultTypeCpp = typeCpp + "*",
                    });
                    stack.Push(new StackEntry(tmp, typeCpp + "*"));
                    return;
                }
            }
        }

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
            try { isValueType = ctorRef.DeclaringType.Resolve()?.IsValueType == true; }
            catch { isValueType = CppNameMapper.IsValueType(ctorRef.DeclaringType.FullName); }
        }
        if (isValueType)
        {
            block.Instructions.Add(new IRDeclareLocal { TypeName = typeCpp, VarName = tmp });
            var allArgs = new List<string> { $"&{tmp}" };
            allArgs.AddRange(args);
            // Check ICall registry: scalar alias types (IntPtr, UIntPtr) have ICall constructors
            // because their IL bodies reference non-existent f_value fields on C++ scalars.
            var icallCtor = ICallRegistry.Lookup(ctorRef);
            block.Instructions.Add(new IRCall
            {
                FunctionName = icallCtor ?? ctorName,
                Arguments = { },
                DeferredDisambigKey = icallCtor != null ? null : newobjDeferredKey,
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
                        DeferredDisambigKey = newobjDeferredKey,
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
                    DeferredDisambigKey = newobjDeferredKey,
                });

                // Add ctor args
                var newObj = (IRNewObj)block.Instructions.Last();
                newObj.CtorArgs.AddRange(args);
            }

            stack.Push(new StackEntry(tmp, typeCpp + "*"));  // reference type — pointer
        }
    }


    /// <summary>
    /// Handles newobj for BCL exception types (System.Exception, InvalidOperationException, etc.)
    /// Allocates using the runtime C++ struct (cil2cpp::Exception etc.) and calls the actual
    /// IL-compiled constructor. This is needed because System.Exception is a CoreRuntimeType
    /// whose methods are mostly provided by the runtime, but constructors compile from IL
    /// on derived types (SystemException, ArgumentException, etc.).
    /// </summary>
    private bool TryEmitExceptionNewObj(IRBasicBlock block, Stack<StackEntry> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        var runtimeCppName = CppNameMapper.GetRuntimeExceptionCppName(ctorRef.DeclaringType.FullName);
        if (runtimeCppName == null) return false;

        var paramCount = ctorRef.Parameters.Count;
        var tmp = $"__t{tempCounter++}";

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

        // Call the actual constructor (compiled from IL on derived types).
        // For System.Exception itself (BlanketGated), constructors are simple field setters
        // that may not be compiled — fall back to direct field assignment.
        if (paramCount > 0)
        {
            var typeCpp = CppNameMapper.MangleTypeName(ctorRef.DeclaringType.FullName);
            var ctorName = CppNameMapper.MangleMethodName(typeCpp, ".ctor");

            // Disambiguate overloaded constructors
            var declTypeDef = ctorRef.DeclaringType.Resolve();
            if (declTypeDef != null)
            {
                var overloadCount = declTypeDef.Methods.Count(m => m.Name == ".ctor");
                if (overloadCount > 1)
                {
                    var ilSuffix = string.Join("_", ctorRef.Parameters.Select(p =>
                        CppNameMapper.MangleTypeName(ResolveGenericTypeRef(p.ParameterType, ctorRef.DeclaringType).TrimEnd('*', '&', ' '))));
                    ctorName = $"{ctorName}__{ilSuffix}";
                }
            }

            // Check if constructor is on System.Exception itself (BlanketGated base type).
            // Its constructors may not be compiled from IL since it's a CoreRuntimeType.
            // Fall back to direct field assignment for Exception base constructors.
            if (ctorRef.DeclaringType.FullName == "System.Exception")
            {
                // Exception(string message) — set f__message directly
                // Exception(string message, Exception inner) — set f__message + f__innerException
                for (int i = 0; i < paramCount; i++)
                {
                    var paramTypeName = ctorRef.Parameters[i].ParameterType.FullName;
                    if (paramTypeName == "System.String")
                        block.Instructions.Add(new IRRawCpp { Code = $"{tmp}->f__message = (cil2cpp::String*){args[i]};" });
                    else if (paramTypeName == "System.Exception")
                        block.Instructions.Add(new IRRawCpp { Code = $"{tmp}->f__innerException = (cil2cpp::Exception*){args[i]};" });
                }
            }
            else
            {
                // Derived exception types: call the actual constructor.
                // Cast tmp from runtime type (cil2cpp::IOException*) to mangled type (System_IO_IOException*)
                // since the constructor's __this parameter uses the mangled name.
                CastArgumentsToParameterTypes(args, ctorRef);
                var allArgs = new List<string> { $"({typeCpp}*)(void*){tmp}" };
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

        stack.Push(new StackEntry(tmp, runtimeCppName + "*"));
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
        if (!built.Add(irType))
        {
            // Re-entry: if the type was previously processed but its VTable is still empty
            // (e.g., it was added to the built set before its methods were populated during
            // CreateGenericSpecializations, or it was transitively deferred), rebuild it now.
            if (irType.VTable.Count == 0)
            {
                // If transitively deferred (base was deferred), check if base is now ready
                if (_deferredVtableTypes.Contains(irType) && irType.BaseType != null
                    && !_deferredVtableTypes.Contains(irType.BaseType))
                {
                    _deferredVtableTypes.Remove(irType);
                    _vtableDeferralProgress = true;
                    // Ensure base vtable is built first
                    if (irType.BaseType.VTable.Count == 0)
                        BuildVTableRecursive(irType.BaseType, built);
                }
                if (irType.Methods.Any(m => m.IsVirtual) || (irType.BaseType?.VTable.Count > 0))
                    BuildVTable(irType);
            }
            return;
        }
        if (irType.BaseType != null)
            BuildVTableRecursive(irType.BaseType, built);
        BuildVTable(irType);
    }

    private void BuildVTable(IRType irType)
    {
        // Idempotent: skip if already built (enables incremental VTable building
        // across multiple passes — first in Pass 3.3b for early types, then Pass 4 for late types)
        if (irType.VTable.Count > 0)
            return;


        // Defer vtable building for types whose base type is not yet resolved.
        // Building now would create an incomplete vtable, causing method index mismatches
        // when the base type is later resolved and the correct inherited methods are added.
        if (_deferredVtableTypes.Contains(irType)) return;

        // Start with base type's vtable
        if (irType.BaseType != null)
        {
            // If base type's vtable is deferred or empty (base chain not yet resolved),
            // defer this type too — building now would produce an incomplete vtable.
            if (irType.BaseType.VTable.Count == 0 && _deferredVtableTypes.Contains(irType.BaseType))
            {
                _deferredVtableTypes.Add(irType);
                return;
            }
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
            irType.VTable.Add(new IRVTableEntry { Slot = ObjectVTableSlots.FinalizeSlot, MethodName = "Finalize", Method = null, DeclaringType = null });
        }

        // Build reverse lookup for O(1) vtable slot matching: name → last entry with that name.
        // "Last wins" semantics: later entries override earlier ones (method hiding).
        // Dictionary iteration overwrites earlier entries, matching LastOrDefault behavior.
        var slotByName = new Dictionary<string, IRVTableEntry>();
        foreach (var entry in irType.VTable)
            slotByName[entry.MethodName] = entry;

        // Override or add virtual methods
        bool isCoreRuntime = CoreRuntimeTypes.Contains(irType.ILFullName);
        foreach (var method in irType.Methods.Where(m => m.IsVirtual))
        {
            // newslot methods always create a new vtable slot (C# 'new virtual')
            // Non-newslot methods attempt to override an existing slot
            IRVTableEntry? existing = null;
            if (!method.IsNewSlot)
            {
                if (slotByName.TryGetValue(method.Name, out var candidate))
                {
                    if (candidate.Method != null
                        ? ParameterTypesMatch(candidate.Method, method)
                        : SeedSlotParamsMatch(candidate.MethodName, method))
                    {
                        existing = candidate;
                    }
                    else
                    {
                        // Rare: same name but parameter mismatch (overloaded virtuals).
                        // Fall back to linear scan for correct overload resolution.
                        existing = irType.VTable.LastOrDefault(e => e.MethodName == method.Name
                            && (e.Method != null
                                ? ParameterTypesMatch(e.Method, method)
                                : SeedSlotParamsMatch(e.MethodName, method)));
                    }
                }
            }
            else
            {
                // Even newslot methods should fill seeded placeholder slots (Method == null).
                // System.Object's methods are newslot=true (they ARE the original declarations),
                // but we pre-seed slots 0-3 so overrides in derived types find them.
                // Without this, Object's methods create new slots 4-7, leaving seeds orphaned.
                if (slotByName.TryGetValue(method.Name, out var candidate)
                    && candidate.Method == null && SeedSlotParamsMatch(candidate.MethodName, method))
                {
                    existing = candidate;
                }
            }

            // For CoreRuntimeTypes, don't replace existing vtable entries with bodyless overrides.
            // The runtime provides implementations via extern "C" named after the ORIGINAL
            // declaring type (e.g., System_Reflection_MemberInfo_get_Name), not the overriding
            // type (System_RuntimeType_get_Name). Keeping the original method reference in the
            // vtable preserves the correct CppName for codegen to find the extern "C" function.
            // Note: for non-blanket-gated types (Enum, Type) where some methods compile from IL,
            // FixupCoreRuntimeVTables() updates vtable entries post-body-compilation.
            bool skipOverride = isCoreRuntime && !method.IsAbstract && method.BasicBlocks.Count == 0
                && !method.IsConstructor && !method.IsStaticConstructor && existing != null;

            if (existing != null)
            {
                if (!skipOverride)
                {
                    // Override
                    existing.Method = method;
                    existing.DeclaringType = irType;
                }
                method.VTableSlot = existing.Slot;
            }
            else
            {
                // New virtual method (or newslot override)
                var slot = irType.VTable.Count;
                var entry = new IRVTableEntry
                {
                    Slot = slot,
                    MethodName = method.Name,
                    Method = method,
                    DeclaringType = irType,
                };
                irType.VTable.Add(entry);
                slotByName[method.Name] = entry;
                method.VTableSlot = slot;
            }
        }
    }

    /// <summary>
    /// Post-body-compilation fixup: update vtable entries for CoreRuntimeType overrides
    /// that were skipped during Pass 4 (BuildVTable) because BasicBlocks.Count was 0.
    /// Now that method bodies have been compiled, overrides with real bodies should replace
    /// the inherited base-class entries so virtual dispatch resolves correctly.
    /// Example: System.Enum.GetHashCode overrides Object.GetHashCode — without this fixup,
    /// the enum vtable keeps `object_get_hash_code` (pointer-based), breaking dictionary lookups.
    /// </summary>
    private void FixupCoreRuntimeVTables()
    {
        foreach (var irType in _module.Types)
        {
            if (!CoreRuntimeTypes.Contains(irType.ILFullName)) continue;
            if (RuntimeTypeRegistry.IsBlanketGated(irType.ILFullName)) continue;

            foreach (var method in irType.Methods)
            {
                if (!method.IsVirtual || method.IsAbstract) continue;
                if (method.BasicBlocks.Count == 0) continue; // Still no body — skip
                if (method.IrStubReason != null) continue; // CLR-internal stub — not compiled from IL
                // Don't promote methods blocked from emission (e.g. RuntimeType.MakeGenericType
                // calls AOT-incompatible TypeBuilderInstantiation — runtime provides extern "C" fallback)
                if (RuntimeTypeRegistry.ShouldBlockInstanceMethod(irType.ILFullName, method)) continue;
                if (method.VTableSlot < 0 || method.VTableSlot >= irType.VTable.Count) continue;

                var entry = irType.VTable[method.VTableSlot];
                // Only fixup if the entry still points to a different (base) method
                if (entry.Method != method)
                {
                    entry.Method = method;
                    entry.DeclaringType = irType;
                }
            }

            // Propagate fixups to derived types that inherit this vtable
            PropagateVTableFixups(irType);
        }
    }

    /// <summary>
    /// After fixing up a CoreRuntimeType's vtable, propagate the changes to all derived types
    /// that inherited the old (base class) entries.
    /// </summary>
    private void PropagateVTableFixups(IRType baseType)
    {
        foreach (var irType in _module.Types)
        {
            if (irType.BaseType != baseType) continue;
            // For each slot in the base type, if the derived type's entry still references
            // the old base class method (not its own override), update it
            for (int i = 0; i < baseType.VTable.Count && i < irType.VTable.Count; i++)
            {
                var baseEntry = baseType.VTable[i];
                var derivedEntry = irType.VTable[i];
                // Only update if derived entry points to a different type's method
                // AND the base entry was updated (has a real body now)
                if (derivedEntry.Method == null || derivedEntry.DeclaringType == irType) continue;
                if (baseEntry.Method != null && baseEntry.Method.BasicBlocks.Count > 0
                    && derivedEntry.Method.CppName != baseEntry.Method.CppName)
                {
                    derivedEntry.Method = baseEntry.Method;
                    derivedEntry.DeclaringType = baseEntry.DeclaringType;
                }
            }
        }
    }

    /// <summary>
    /// Fix bodyless vtable slots caused by Cecil resolving incorrect base types
    /// for generic specializations. E.g., SafeCrypt32Handle&lt;T&gt;.BaseType is reported
    /// as SafeHandle instead of SafeHandleZeroOrMinusOneIsInvalid, so the intermediate
    /// type's override of get_IsInvalid is never inherited — the generic specialization
    /// gets a bodyless method shell that renders as nullptr.
    /// This pass finds compiled implementations for bodyless/abstract slots by looking at
    /// sibling types that share the same base type, then propagates fixes downward.
    /// </summary>
    private void FixupAbstractVTableSlots()
    {
        FixupAbstractVTableSlotsImpl();
    }

    /// <summary>
    /// Pass 6.9: Compile methods discovered during deferred generic body compilation.
    /// Generic specialization bodies (e.g., SafeHandleMarshaller&lt;T&gt;) may call non-generic
    /// methods through resolved type parameters (newobj !0::.ctor()) that the reachability
    /// analyzer couldn't trace. This pass finds bodyless methods that are actually called
    /// by compiled code and compiles them from Cecil IL.
    /// </summary>
    private void CompileMissingCallees()
    {
        // Build Cecil lookup: CppName → (MethodInfo, IRMethod) for bodyless methods
        var bodylessByName = new Dictionary<string, (IL.MethodInfo methodDef, IRMethod irMethod)>();
        foreach (var typeDef in _allTypes)
        {
            if (typeDef.HasGenericParameters) continue;
            if (!_typeCache.TryGetValue(typeDef.FullName, out var irType)) continue;

            foreach (var methodDef in typeDef.Methods)
            {
                if (!methodDef.HasBody) continue;
                if (methodDef.IsAbstract) continue;

                var cecilMethod = methodDef.GetCecilMethod();

                // Find matching IRMethod with no body
                foreach (var irMethod in irType.Methods)
                {
                    if (irMethod.BasicBlocks.Count > 0) continue;
                    if (irMethod.Name != methodDef.Name) continue;
                    if (irMethod.IsInternalCall || irMethod.HasICallMapping) continue;
                    if (irMethod.Parameters.Count != cecilMethod.Parameters.Count) continue;

                    bool paramsMatch = true;
                    for (int i = 0; i < cecilMethod.Parameters.Count; i++)
                    {
                        if (irMethod.Parameters[i].ILTypeName != cecilMethod.Parameters[i].ParameterType.FullName)
                        {
                            paramsMatch = false;
                            break;
                        }
                    }
                    if (!paramsMatch) continue;

                    bodylessByName.TryAdd(irMethod.CppName, (methodDef, irMethod));
                    break;
                }
            }
        }

        if (bodylessByName.Count == 0) return;

        bool anyCompiled = true;
        while (anyCompiled)
        {
            anyCompiled = false;

            // Collect all called function names from compiled method bodies
            var calledFunctions = new HashSet<string>();
            foreach (var type in _module.Types)
            foreach (var method in type.Methods)
            {
                if (method.BasicBlocks.Count == 0) continue;
                foreach (var block in method.BasicBlocks)
                foreach (var instr in block.Instructions)
                {
                    if (instr is IRCall call && !string.IsNullOrEmpty(call.FunctionName))
                        calledFunctions.Add(call.FunctionName);
                    else if (instr is IRNewObj newObj && !string.IsNullOrEmpty(newObj.CtorName))
                        calledFunctions.Add(newObj.CtorName);
                }
            }

            // Compile bodyless methods that are actually called
            foreach (var (cppName, (methodDef, irMethod)) in bodylessByName)
            {
                if (irMethod.BasicBlocks.Count > 0) continue; // Already compiled
                if (!calledFunctions.Contains(cppName)) continue;

                var cecilMethod = methodDef.GetCecilMethod();
                if (HasClrInternalDependencies(cecilMethod, out _)) continue;
                if (ReferencesSimdTypes(cecilMethod)) continue;
                if (CallsClrInternalMethods(cecilMethod)) continue;
                // Skip nested types of CoreRuntime types (e.g., RuntimeType/ActivatorCache).
                // Their method bodies reference CLR-internal functions not in our runtime.
                var declCecilType = cecilMethod.DeclaringType;
                if (declCecilType.IsNested && declCecilType.DeclaringType != null
                    && RuntimeTypeRegistry.IsCoreRuntime(declCecilType.DeclaringType.FullName)) continue;

                // Populate locals
                irMethod.Locals.Clear();
                foreach (var localDef in cecilMethod.Body.Variables)
                {
                    irMethod.Locals.Add(new IRLocal
                    {
                        Index = localDef.Index,
                        CppName = $"loc_{localDef.Index}",
                        CppTypeName = ResolveTypeForDecl(
                            ResolveGenericTypeName(localDef.VariableType, new Dictionary<string, string>())),
                    });
                }

                ConvertMethodBody(methodDef, irMethod);
                anyCompiled = true;
            }
        }
    }

    /// <summary>
    /// Check if a method body calls any methods with CLR-internal parameter or return types.
    /// Used by CompileMissingCallees to avoid compiling methods that would produce invalid C++.
    /// </summary>
    private static bool CallsClrInternalMethods(MethodDefinition cecilMethod)
    {
        if (!cecilMethod.HasBody) return false;
        foreach (var instr in cecilMethod.Body.Instructions)
        {
            if (instr.Operand is not MethodReference calledMr) continue;
            if (ClrInternalTypeNames.Contains(calledMr.ReturnType.FullName))
                return true;
            if (ClrInternalTypeNames.Contains(calledMr.DeclaringType.FullName))
                return true;
            foreach (var p in calledMr.Parameters)
            {
                if (ClrInternalTypeNames.Contains(p.ParameterType.FullName))
                    return true;
            }
        }
        return false;
    }

    private void FixupAbstractVTableSlotsImpl()
    {
        // Phase 1: Index — collect compiled implementations for truly abstract slots
        // Key: (baseTypeName, slotIndex) → compiled IRMethod (has BasicBlocks)
        // IMPORTANT: Only collect for abstract base slots, NOT for bodyless non-abstract methods.
        // Non-abstract methods with BasicBlocks.Count == 0 may be runtime-provided (core_methods.cpp
        // extern "C") — replacing them with a sibling type's override would be incorrect.
        // Example: MemberInfo.get_DeclaringType (runtime-provided) must not be replaced by
        //          Type.get_DeclaringType (which returns null for non-nested types).
        var slotFixups = new Dictionary<(string baseTypeName, int slot), IRMethod>();

        foreach (var type in _module.Types)
        {
            if (type.BaseType == null) continue;
            for (int i = 0; i < type.VTable.Count && i < type.BaseType.VTable.Count; i++)
            {
                var baseEntry = type.BaseType.VTable[i];
                var myEntry = type.VTable[i];
                // Base has a bodyless/abstract slot, but this type has a compiled implementation.
                // Skip slots where the abstract method is declared on a CoreRuntime type —
                // those have extern "C" implementations in core_methods.cpp and must not be
                // replaced by sibling overrides (e.g., Type.get_DeclaringType would leak
                // into MethodBase, breaking MemberInfo.DeclaringType dispatch).
                if (baseEntry.Method != null
                    && (baseEntry.Method.IsAbstract || baseEntry.Method.BasicBlocks.Count == 0)
                    && myEntry.Method != null && !myEntry.Method.IsAbstract
                    && myEntry.Method.BasicBlocks.Count > 0)
                {
                    var declType = baseEntry.Method.DeclaringType;
                    if (declType != null && CoreRuntimeTypes.Contains(declType.ILFullName))
                        continue; // Skip — runtime provides implementation
                    var key = (type.BaseType.ILFullName, i);
                    slotFixups.TryAdd(key, myEntry.Method);
                }
            }
        }

        if (slotFixups.Count == 0) return;

        // Phase 2: Fix up types that have bodyless/abstract slots in their vtable
        var fixedTypes = new HashSet<IRType>();
        foreach (var type in _module.Types)
        {
            for (int i = 0; i < type.VTable.Count; i++)
            {
                var entry = type.VTable[i];
                if (entry.Method == null) continue;
                // Skip entries that already have a compiled body
                if (!entry.Method.IsAbstract && entry.Method.BasicBlocks.Count > 0) continue;
                // Skip methods declared on CoreRuntime types — they have extern "C" implementations
                // in core_methods.cpp. Allowing sibling fixups here would replace them with wrong
                // overrides (e.g., MemberInfo.get_DeclaringType → Type.get_DeclaringType which
                // returns null, breaking MethodInfo.DeclaringType on a different branch).
                var entryDeclType = entry.Method.DeclaringType;
                if (entryDeclType != null && CoreRuntimeTypes.Contains(entryDeclType.ILFullName))
                    continue;

                // Walk up the base type chain looking for a fixup
                var bt = type.BaseType;
                while (bt != null)
                {
                    var key = (bt.ILFullName, i);
                    if (slotFixups.TryGetValue(key, out var fixup))
                    {
                        entry.Method = fixup;
                        entry.DeclaringType = fixup.DeclaringType;
                        fixedTypes.Add(type);
                        break;
                    }
                    bt = bt.BaseType;
                }
            }
        }

        // Phase 3: Propagate fixes to derived types
        foreach (var fixedType in fixedTypes)
        {
            PropagateBodylessVTableFixups(fixedType);
        }
    }

    /// <summary>
    /// Recursively propagate bodyless vtable fixups to derived types.
    /// </summary>
    private void PropagateBodylessVTableFixups(IRType baseType)
    {
        foreach (var irType in _module.Types)
        {
            if (irType.BaseType != baseType) continue;
            bool changed = false;
            for (int i = 0; i < baseType.VTable.Count && i < irType.VTable.Count; i++)
            {
                var baseEntry = baseType.VTable[i];
                var derivedEntry = irType.VTable[i];
                if (derivedEntry.Method != null
                    && (derivedEntry.Method.IsAbstract || derivedEntry.Method.BasicBlocks.Count == 0)
                    && baseEntry.Method != null && !baseEntry.Method.IsAbstract
                    && baseEntry.Method.BasicBlocks.Count > 0)
                {
                    derivedEntry.Method = baseEntry.Method;
                    derivedEntry.DeclaringType = baseEntry.DeclaringType;
                    changed = true;
                }
            }
            if (changed)
            {
                PropagateBodylessVTableFixups(irType);
            }
        }
    }

    private void BuildInterfaceImpls(IRType irType)
    {
        // Build InterfaceImpls for directly declared interfaces
        foreach (var iface in irType.Interfaces)
        {
            BuildSingleInterfaceImpl(irType, iface);
        }

        // Also build InterfaceImpls for interfaces inherited from the base type chain.
        // CLR semantics: each type must have its own interface vtable entries for ALL
        // interfaces in its hierarchy, so that derived types overriding abstract (or
        // virtual) base interface methods get concrete function pointers in their
        // dispatch tables. Without this, the runtime walks to the base type's interface
        // vtable and finds nullptr for abstract methods, causing a null dispatch crash.
        // Example: UnionIterator<Char> overrides Iterator<Char>.MoveNext (abstract)
        // and needs its own IEnumerator interface vtable with concrete MoveNext.
        // Note: inherited interfaces are added to InterfaceImpls only (not Interfaces),
        // preserving the directly-declared-only semantics of the Interfaces list.
        var seen = new HashSet<string>(irType.Interfaces.Select(i => i.ILFullName));
        var bt = irType.BaseType;
        while (bt != null)
        {
            foreach (var baseIface in bt.Interfaces)
            {
                if (seen.Add(baseIface.ILFullName))
                    BuildSingleInterfaceImpl(irType, baseIface);
            }
            bt = bt.BaseType;
        }
    }

    private void BuildSingleInterfaceImpl(IRType irType, IRType iface)
    {
        var impl = new IRInterfaceImpl { Interface = iface };
        foreach (var ifaceMethod in iface.Methods)
        {
            // Skip constructors — only map actual interface methods
            if (ifaceMethod.IsConstructor || ifaceMethod.IsStaticConstructor) continue;

            // Skip static abstract/virtual interface methods — they are resolved at compile time
            // via constrained. + call IL patterns, not through runtime interface dispatch tables.
            if (ifaceMethod.IsStatic) continue;

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
        // Resolve the declaring type through _ctx.Value.ActiveTypeParamMap for generic method contexts
        // (e.g., IArraySortHelper`2<TKey,TValue> → IArraySortHelper`2<Byte,String>)
        var resolvedDeclaringType = ResolveCacheKey(methodRef.DeclaringType);
        types.Add(CppNameMapper.GetCppTypeForDecl(resolvedDeclaringType));
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
            if (arrType.Rank >= 2)
            {
                // Multi-dimensional: preserve rank info (e.g., "System.Int32[0...,0...]")
                var commas = new string(',', arrType.Rank - 1);
                return elemResolved + $"[{commas}]";
            }
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
        // Handle RequiredModifierType (modreq) — e.g., T& modreq(InAttribute) for readonly refs
        if (typeRef is RequiredModifierType rmt)
            return ResolveGenericTypeRef(rmt.ElementType, declaringType);
        // Handle OptionalModifierType (modopt)
        if (typeRef is OptionalModifierType omt)
            return ResolveGenericTypeRef(omt.ElementType, declaringType);
        // Handle PinnedType — locals with 'pinned' modifier
        if (typeRef is PinnedType pnt)
            return ResolveGenericTypeRef(pnt.ElementType, declaringType);

        // Resolve generic parameters — type-level from declaring type, method-level from method map
        if (typeRef is GenericParameter gp)
        {
            // Type-level generic param (!0, !1) — resolve from declaring type FIRST,
            // but ONLY if the generic parameter actually belongs to that declaring type.
            // Without the owner check, Dictionary's TKey (position 0) would incorrectly
            // resolve against ReadOnlySpan<KVP<!0,!1>>'s arg[0] (which is KVP<!0,!1>),
            // producing double-wrapped KVP<KVP<string,string>,string> instead of KVP<string,string>.
            if (gp.Type == GenericParameterType.Type
                && declaringType is GenericInstanceType git && gp.Position < git.GenericArguments.Count
                && gp.Owner is TypeReference gpOwner
                && gpOwner.FullName == git.ElementType.FullName)
            {
                return ResolveGenericTypeRef(git.GenericArguments[gp.Position], null);
            }
            // Method-level generic param (!!0, !!1) or type-level without declaring type context
            if (_ctx.Value.ActiveTypeParamMap != null && _ctx.Value.ActiveTypeParamMap.TryGetValue(gp.Name, out var mapped))
                return mapped;
            // Final fallback: try declaring type by position, but only if owner matches
            if (declaringType is GenericInstanceType git2 && gp.Position < git2.GenericArguments.Count
                && gp.Owner is TypeReference gpOwner2
                && gpOwner2.FullName == git2.ElementType.FullName)
            {
                return ResolveGenericTypeRef(git2.GenericArguments[gp.Position], null);
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
                    // Type-level param: resolve from declaring type FIRST, but only if owner matches
                    if (gp2.Type == GenericParameterType.Type
                        && declaringType is GenericInstanceType git2 && gp2.Position < git2.GenericArguments.Count
                        && gp2.Owner is TypeReference gp2Owner
                        && gp2Owner.FullName == git2.ElementType.FullName)
                    {
                        argNames.Add(ResolveGenericTypeRef(git2.GenericArguments[gp2.Position], null));
                        anyResolved = true;
                        continue;
                    }
                    // Method-level param or type-level without declaring type: check method map
                    if (_ctx.Value.ActiveTypeParamMap != null && _ctx.Value.ActiveTypeParamMap.TryGetValue(gp2.Name, out var mapped2))
                    {
                        argNames.Add(mapped2);
                        anyResolved = true;
                        continue;
                    }
                }
                // Recursively resolve nested GenericInstanceType arguments
                // (e.g., AsyncLocalValueChangedArgs`1<T> → AsyncLocalValueChangedArgs`1<CultureInfo>)
                var resolvedArgName = ResolveGenericTypeRef(arg, declaringType);
                if (resolvedArgName != arg.FullName)
                    anyResolved = true;
                argNames.Add(resolvedArgName);
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
        // Check for IL generic param notation: !0, !1, !!0, etc.
        if (System.Text.RegularExpressions.Regex.IsMatch(typeName, @"(^|[\[<,])![\d]"))
            return true;
        // Check for named generic params (TResult, TKey, TValue, TStorage, etc.)
        // These are single-word PascalCase identifiers without dots/namespace — no concrete type
        // in .NET BCL looks like this (all concrete types have namespaces like System.Byte).
        if (IsUnresolvedElementType(typeName))
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
        // .NET generic parameter naming conventions (no dots/namespace):
        //   - Single uppercase letter: T, K, V, S
        //   - "T" + uppercase: TKey, TValue, TResult, TSource, TElement, TStorage
        // User-defined types without namespace (Product, Order, Entity) must NOT match.
        if (!resolvedName.Contains('.') && !resolvedName.Contains('/')
            && resolvedName.All(c => char.IsLetterOrDigit(c)))
        {
            if (resolvedName.Length == 1 && char.IsUpper(resolvedName[0]))
                return true;
            if (resolvedName.Length >= 2 && resolvedName.Length <= 12
                && resolvedName[0] == 'T' && char.IsUpper(resolvedName[1]))
                return true;
        }
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
                // Recursively substitute — handles nested GenericInstanceType args
                // (e.g., Func<Ctx, TState, Outcome<TResult>> where Outcome<TResult>
                // is a GenericInstanceType containing a method-level generic param)
                var substituted = SubstituteMethodGenericParams(arg, gim);
                if (substituted != arg)
                {
                    newArgs[j] = substituted;
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

        if (typeRef is RequiredModifierType rmt)
        {
            var resolved = SubstituteMethodGenericParams(rmt.ElementType, gim);
            if (resolved != rmt.ElementType)
                return new RequiredModifierType(rmt.ModifierType, resolved);
        }

        if (typeRef is OptionalModifierType omt)
        {
            var resolved = SubstituteMethodGenericParams(omt.ElementType, gim);
            if (resolved != omt.ElementType)
                return new OptionalModifierType(omt.ModifierType, resolved);
        }

        return typeRef;
    }

    /// <summary>
    /// Register a BCL delegate type that doesn't exist in _typeCache.
    /// During parallel Pass 6 body compilation, writes are deferred to avoid _typeCache
    /// race conditions. Deferred types are drained into _typeCache + _module after parallel phase.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _deferredBclDelegateKeys = new();
    private readonly System.Collections.Concurrent.ConcurrentBag<(string IlName, IRType Type)> _deferredBclDelegates = new();

    private void RegisterBclDelegateType(string ilFullName, string cppName)
    {
        // Already in cache from earlier passes
        if (_typeCache.ContainsKey(ilFullName)) return;
        // Dedup across threads — TryAdd is atomic
        if (!_deferredBclDelegateKeys.TryAdd(ilFullName, 0)) return;

        var lastDot = ilFullName.LastIndexOf('.');
        var bclDelegate = new IRType
        {
            ILFullName = ilFullName,
            Name = lastDot >= 0 ? ilFullName[(lastDot + 1)..] : ilFullName,
            Namespace = lastDot >= 0 ? ilFullName[..lastDot] : "",
            CppName = cppName,
            IsDelegate = true,
            IsPublic = true, // BCL delegates are always public
        };
        _deferredBclDelegates.Add((ilFullName, bclDelegate));
    }

    /// <summary>
    /// Apply deferred BCL delegate registrations after parallel Pass 6 body compilation.
    /// Must be called before Pass 6.1 (VTable construction needs these types).
    /// </summary>
    private void DrainDeferredBclDelegates()
    {
        while (_deferredBclDelegates.TryTake(out var entry))
        {
            // Idempotent: skip if already added (safe to call multiple times)
            if (_typeCache.ContainsKey(entry.IlName)) continue;
            _typeCache[entry.IlName] = entry.Type;
            AddTypeToModule(entry.Type);
        }
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
    /// Look up the actual C++ type of a field from our IRType model.
    /// Used to cross-check against Cecil's FieldReference type, which may differ for inherited fields
    /// when accessed through a derived class (e.g., Dictionary&lt;string,ResourceLocator&gt; in IL
    /// vs Dictionary&lt;string,object&gt; in the struct declaration).
    /// </summary>
    private string? LookupIRFieldTypeCpp(FieldReference fieldRef)
    {
        // Resolve the type from _typeCache
        var declTypeName = fieldRef.DeclaringType.FullName;
        IRType? irType;
        if (!_typeCache.TryGetValue(declTypeName, out irType))
        {
            // Fallback: try ResolveCacheKey for GenericInstanceType keys
            var cacheKey = ResolveCacheKey(fieldRef.DeclaringType);
            if (cacheKey == declTypeName || !_typeCache.TryGetValue(cacheKey, out irType))
                return null;
        }
        // Walk from furthest ancestor to the declaring type, matching C++ struct layout:
        // inherited fields are emitted first, own fields with same name are skipped.
        // First match wins (inherited field takes precedence for same-name fields).
        var chain = new List<IRType>();
        var cur = irType;
        while (cur != null && cur.ILFullName != "System.Object")
        {
            chain.Add(cur);
            cur = cur.BaseType;
        }
        chain.Reverse(); // furthest ancestor first
        foreach (var type in chain)
        {
            foreach (var f in type.Fields)
            {
                if (f.Name == fieldRef.Name && !string.IsNullOrEmpty(f.FieldTypeName))
                    return CppNameMapper.GetCppTypeForDecl(f.FieldTypeName);
            }
        }
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

        // intptr_t/uintptr_t values used with ldfld: in IL, native int can be a pointer
        // to a struct (P/Invoke returns, SafeHandle.DangerousGetHandle results).
        // Treat as pointer access to generate ->field instead of .field.
        if (stackCppType is "intptr_t" or "uintptr_t") return false;

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

    /// <summary>
    /// Push a local variable onto the stack, propagating compile-time constant info
    /// from _ctx.Value.CompileTimeConstantLocals for dead branch elimination.
    /// </summary>
    private void PushLocalWithConstant(Stack<StackEntry> stack, IRMethod method, int index)
    {
        int? constant = _ctx.Value.CompileTimeConstantLocals.TryGetValue(index, out var c) ? c : null;
        stack.Push(new StackEntry(GetLocalName(method, index), GetLocalType(method, index), constant));
    }

    /// <summary>
    /// Try to parse a simple integer literal expression ("0", "1", "-1", etc.)
    /// for use in compile-time constant propagation through ceq/cgt/clt.
    /// Only returns a value for simple literal strings, not complex expressions.
    /// </summary>
    private static int? TryParseSimpleLiteral(string expr)
        => int.TryParse(expr, out var v) ? v : null;

    // Exception event helpers
    private enum ExceptionEventKind { TryBegin, CatchBegin, FinallyBegin, FaultBegin, FilterBegin, FilterHandlerBegin, HandlerEnd }

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
    /// Match a method against a seeded Object vtable placeholder (Method == null).
    /// Seeded slots represent Object's virtual methods with known parameter counts.
    /// Without this check, overloads like Enum.ToString(string, IFormatProvider) can
    /// incorrectly claim slot 0 (meant for parameterless ToString).
    /// </summary>
    private static bool SeedSlotParamsMatch(string seedMethodName, IRMethod method)
    {
        var expectedParamCount = seedMethodName switch
        {
            "ToString" => 0,
            "Equals" => 1,
            "GetHashCode" => 0,
            "Finalize" => 0,
            _ => method.Parameters.Count, // Unknown seed — allow match
        };
        return method.Parameters.Count == expectedParamCount;
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

    /// <summary>
    /// Set up generic virtual dispatch for a callvirt on a method-level generic virtual method.
    /// Standard vtable dispatch can't handle this because each vtable slot holds one function pointer
    /// while the method has multiple generic specializations. Instead, emit a type-check chain:
    /// if (this->type_info == &TypeA) TypeA_Method_Args(...);
    /// else if (this->type_info == &TypeB) TypeB_Method_Args(...);
    /// </summary>
    private void TrySetupGenericVirtualDispatch(IRCall irCall, GenericInstanceMethod gvmRef, IRType baseType)
    {
        var elemMethod = gvmRef.ElementMethod.Resolve();
        if (elemMethod == null) return;

        // Resolve method-level generic type arguments
        var typeArgs = gvmRef.GenericArguments
            .Select(a => ResolveTypeRefOperand(a)).ToList();

        // Guard against recursive generic growth — e.g., ExecuteCore<T, ValueTuple<X, Func<..., TState>>>
        // where each compiled body creates a new callsite with deeper nesting.
        // Same principle as IsRecursiveGenericInstantiation but for resolved string names.
        if (HasRecursiveTypeArgs(typeArgs))
            return;

        var baseTypeName = baseType.ILFullName;
        var methodName = elemMethod.Name;
        var targets = new List<(string TypeInfoName, string FunctionName, string DeclTypeCppName)>();

        // Find all constructed subtypes (including the base type itself) that override this method.
        // For interface dispatch, also check non-constructed types (they may be instantiated
        // via factory methods in generic code that isn't tracked by ConstructedTypes).
        foreach (var (ilName, irType) in _typeCache)
        {
            if (!baseType.IsInterface && !_module.ConstructedTypes.Contains(ilName)) continue;
            if (irType.IsInterface || irType.IsAbstract) continue;

            // Check if this type is a subtype of the base type
            if (!IsSubtypeOf(irType, baseTypeName)) continue;

            // Find the override method: walk Cecil type hierarchy (generic methods
            // are skipped in Pass 3 so they're not in IRType.Methods)
            var (overrideDeclType, overrideCecil) = FindGenericVirtualOverride(
                irType, methodName, elemMethod.Parameters.Count);
            if (overrideDeclType == null || overrideCecil == null) continue;
            // Don't dispatch to abstract methods (they have no body)
            if (overrideCecil.IsAbstract) continue;

            var typeInfoName = irType.CppName + "_TypeInfo";
            var mangledName = MangleGenericMethodName(overrideDeclType.ILFullName, methodName, typeArgs);

            // Ensure the specialization is registered for compilation.
            // Use stored MangledName if already registered — it may include a disambiguation
            // suffix (e.g., __paramTypes) added by TryCollectResolvedGenericMethod when two
            // overloads produce the same base mangled name (Humanize<String> with 1 vs 3 params).
            var paramSig = string.Join(",", elemMethod.Parameters.Select(p =>
                _ctx.Value.ActiveTypeParamMap != null
                    ? ResolveGenericTypeName(p.ParameterType, _ctx.Value.ActiveTypeParamMap)
                    : p.ParameterType.FullName));
            var key = MakeGenericMethodKey(overrideDeclType.ILFullName, methodName, typeArgs, paramSig);
            lock (_genericMethodInstLock)
            {
                if (_genericMethodInstantiations.TryGetValue(key, out var existingInfo))
                {
                    mangledName = existingInfo.MangledName;
                }
                else
                {
                    _genericMethodMangledNames.Add(mangledName);
                    _genericMethodInstantiations[key] = new GenericMethodInstantiationInfo(
                        overrideDeclType.ILFullName, methodName, typeArgs, mangledName, overrideCecil);
                }
            }

            targets.Add((typeInfoName, mangledName, overrideDeclType.CppName));
        }

        if (targets.Count > 0)
            irCall.GenericVirtualTargets = targets;
    }

    /// <summary>
    /// Detect recursive growth in resolved generic type argument strings.
    /// Counts generic nesting depth (number of '&lt;' brackets) in each arg —
    /// nesting deeper than 4 levels indicates unbounded recursive instantiation
    /// (e.g., ValueTuple&lt;X, Func&lt;Y, ValueTuple&lt;X, Func&lt;Y, ...&gt;&gt;&gt;&gt;).
    /// </summary>
    private static bool HasRecursiveTypeArgs(List<string> typeArgs)
    {
        foreach (var arg in typeArgs)
        {
            int depth = 0, maxDepth = 0;
            for (int i = 0; i < arg.Length; i++)
            {
                if (arg[i] == '<') { depth++; if (depth > maxDepth) maxDepth = depth; }
                else if (arg[i] == '>') depth--;
            }
            if (maxDepth > 4) return true;
        }
        return false;
    }

    /// <summary>Check if irType is a subtype of the type with the given IL full name.</summary>
    private bool IsSubtypeOf(IRType irType, string ancestorILName)
    {
        if (irType.ILFullName == ancestorILName) return true;
        // Walk class inheritance chain
        var current = irType.BaseType;
        while (current != null)
        {
            if (current.ILFullName == ancestorILName) return true;
            current = current.BaseType;
        }
        // Check interface implementations (for generic virtual dispatch on interfaces).
        // InterfaceImpls may be empty for generic specializations, so also check Cecil data.
        if (CheckInterfaceImplsRecursive(irType, ancestorILName))
            return true;
        // Fallback: check Cecil type metadata for interfaces (handles generic specializations
        // where InterfaceImpls isn't populated from the open generic definition)
        return CheckCecilInterfaces(irType, ancestorILName);
    }

    private bool CheckInterfaceImplsRecursive(IRType irType, string ifaceName)
    {
        var current = irType;
        while (current != null)
        {
            foreach (var iface in current.InterfaceImpls)
            {
                if (iface.Interface.ILFullName == ifaceName) return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private bool CheckCecilInterfaces(IRType irType, string ifaceName)
    {
        // For generic specializations, InterfaceImpls may be empty.
        // Walk the IRType base chain and check Cecil type definitions for interface implementations.
        var current = irType;
        while (current != null)
        {
            // Find the Cecil definition for this type (use open generic name for generic types)
            var ilName = current.ILFullName;
            var openName = ilName.Contains('<') ? ilName.Substring(0, ilName.IndexOf('<')) : ilName;
            foreach (var typeDef in _allTypes)
            {
                if (typeDef.FullName != openName) continue;
                var cecilType = typeDef.GetCecilType();
                foreach (var cecilIface in cecilType.Interfaces)
                {
                    // Resolve the interface name with the type's generic arguments
                    var resolvedIfaceName = ResolveInterfaceNameForGenericType(
                        cecilIface.InterfaceType, current);
                    if (resolvedIfaceName == ifaceName) return true;
                }
                break;
            }
            current = current.BaseType;
        }
        return false;
    }

    private string ResolveInterfaceNameForGenericType(Mono.Cecil.TypeReference ifaceRef, IRType irType)
    {
        // For a generic interface like IOrderedEnumerable`1<!TElement>, resolve the
        // generic parameters using the IRType's generic arguments.
        if (ifaceRef is Mono.Cecil.GenericInstanceType git)
        {
            var resolvedArgs = new List<string>();
            foreach (var arg in git.GenericArguments)
            {
                if (arg is Mono.Cecil.GenericParameter gp)
                {
                    // Resolve from the IRType's generic arguments
                    if (gp.Position < irType.GenericArguments.Count)
                        resolvedArgs.Add(irType.GenericArguments[gp.Position]);
                    else
                        resolvedArgs.Add(arg.FullName);
                }
                else
                    resolvedArgs.Add(arg.FullName);
            }
            var baseName = git.ElementType.FullName;
            return $"{baseName}<{string.Join(",", resolvedArgs)}>";
        }
        return ifaceRef.FullName;
    }

    /// <summary>
    /// Find the nearest type in the hierarchy that declares a generic virtual method override.
    /// Method-level generic methods are NOT in IRType.Methods (skipped in Pass 3), so we
    /// walk the Cecil type hierarchy to find the override.
    /// Returns (IRType of the override's declaring type, Cecil MethodDefinition of the override).
    /// </summary>
    private (IRType? DeclType, Mono.Cecil.MethodDefinition? CecilMethod) FindGenericVirtualOverride(
        IRType concreteType, string methodName, int paramCount)
    {
        var current = concreteType;
        while (current != null)
        {
            // Find the Cecil type definition
            var cecilMethod = FindCecilGenericMethod(current.ILFullName, methodName, paramCount);
            if (cecilMethod != null)
                return (current, cecilMethod);
            current = current.BaseType;
        }
        return (null, null);
    }

    /// <summary>
    /// Find a Cecil MethodDefinition for a generic virtual method on a type by IL full name.
    /// </summary>
    private Mono.Cecil.MethodDefinition? FindCecilGenericMethod(string ilTypeName, string methodName, int paramCount)
    {
        // For closed generic types (e.g. "OrderedEnumerable`1<TodoItem>"),
        // also try matching the open generic name (e.g. "OrderedEnumerable`1").
        var openName = ilTypeName.Contains('<')
            ? ilTypeName.Substring(0, ilTypeName.IndexOf('<'))
            : null;

        foreach (var typeDef in _allTypes)
        {
            if (typeDef.FullName != ilTypeName && (openName == null || typeDef.FullName != openName))
                continue;
            var cecilType = typeDef.GetCecilType();
            // Match both direct names and explicit interface implementations
            // (e.g., "System.Linq.IOrderedEnumerable<TElement>.CreateOrderedEnumerable")
            var method = cecilType.Methods.FirstOrDefault(m =>
                m.IsVirtual && m.Parameters.Count == paramCount && m.HasGenericParameters
                && (m.Name == methodName || m.Name.EndsWith("." + methodName)));
            if (method != null) return method;
            return null;
        }
        return null;
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
        return ResolveTypeRefForMatching(paramType, methodRef);
    }

    /// <summary>
    /// Resolve a TypeReference to a concrete IL type name for parameter matching.
    /// Handles: GenericParameter (resolve via GIT or active map),
    /// GenericInstanceType with unresolved args (Vector256&lt;T&gt; → Vector256&lt;Byte&gt;),
    /// and plain types (return FullName).
    /// </summary>
    private string ResolveTypeRefForMatching(TypeReference typeRef, MethodReference methodRef)
    {
        if (typeRef is GenericParameter gp)
        {
            // Try resolving through declaring GenericInstanceType, but only if the
            // GenericParameter belongs to the declaring type (not an outer/unrelated type).
            // Without this check, Position-based indexing into the GIT args gives wrong
            // results for parameters from outer generic types (e.g., TTrigger from
            // StateMachine`2 resolved through IDictionary`2's args).
            if (methodRef.DeclaringType is GenericInstanceType git
                && gp.Position < git.GenericArguments.Count
                && gp.Owner is TypeReference gpOwner
                && gpOwner.FullName == git.ElementType.FullName)
            {
                var resolved = git.GenericArguments[gp.Position];
                if (resolved is GenericParameter gp2 && _ctx.Value.ActiveTypeParamMap != null
                    && _ctx.Value.ActiveTypeParamMap.TryGetValue(gp2.Name, out var mapped))
                    return mapped;
                // Recursively resolve — the GIT arg may be a GenericInstanceType with
                // its own unresolved parameters (e.g., StateRepresentation<TState,TTrigger>).
                return ResolveTypeRefForMatching(resolved, methodRef);
            }
            // Try resolving directly through active type parameter map
            if (_ctx.Value.ActiveTypeParamMap != null && _ctx.Value.ActiveTypeParamMap.TryGetValue(gp.Name, out var directMapped))
                return directMapped;
            return typeRef.FullName;
        }

        // ArrayType wrapping a generic parameter (e.g., T[] → System.Double[])
        if (typeRef is ArrayType arrayType)
        {
            var resolvedElement = ResolveTypeRefForMatching(arrayType.ElementType, methodRef);
            if (resolvedElement != arrayType.ElementType.FullName)
                return resolvedElement + "[]";
            return typeRef.FullName;
        }

        // ByReferenceType wrapping a generic parameter (e.g., T& → System.Double&)
        if (typeRef is ByReferenceType byRefType)
        {
            var resolvedElement = ResolveTypeRefForMatching(byRefType.ElementType, methodRef);
            if (resolvedElement != byRefType.ElementType.FullName)
                return resolvedElement + "&";
            return typeRef.FullName;
        }

        // PointerType wrapping a generic parameter (e.g., T* → System.Double*)
        if (typeRef is PointerType ptrType)
        {
            var resolvedElement = ResolveTypeRefForMatching(ptrType.ElementType, methodRef);
            if (resolvedElement != ptrType.ElementType.FullName)
                return resolvedElement + "*";
            return typeRef.FullName;
        }

        // GenericInstanceType with potentially unresolved arguments (e.g., Vector256<T>)
        if (typeRef is GenericInstanceType git2)
        {
            bool hasUnresolved = false;
            var resolvedArgs = new List<string>();
            foreach (var arg in git2.GenericArguments)
            {
                var resolvedArg = ResolveTypeRefForMatching(arg, methodRef);
                resolvedArgs.Add(resolvedArg);
                if (resolvedArg != arg.FullName) hasUnresolved = true;
            }
            if (hasUnresolved)
            {
                // Reconstruct the fully resolved type name
                return $"{git2.ElementType.FullName}<{string.Join(",", resolvedArgs)}>";
            }
        }

        return typeRef.FullName;
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

    /// <summary>
    /// Returns true if a C++ type name is a known primitive type where intrinsic
    /// operators/properties are correct (no need for explicit DIM interface impls).
    /// </summary>
    private static bool IsCppPrimitiveType(string cppType) => cppType is
        "int8_t" or "uint8_t" or "int16_t" or "uint16_t" or
        "int32_t" or "uint32_t" or "int64_t" or "uint64_t" or
        "intptr_t" or "uintptr_t" or "float" or "double" or
        "bool" or "char16_t";

    /// <summary>
    /// Returns the intrinsic value for INumberBase static abstract property getters
    /// (get_Zero, get_One) on primitive types.
    /// </summary>
    private static string? TryGetIntrinsicNumericProperty(string methodName, string cppType) => methodName switch
    {
        "get_Zero" => cppType switch
        {
            "float" => "0.0f",
            "double" => "0.0",
            "bool" => "false",
            _ => $"({cppType})0"
        },
        "get_One" => cppType switch
        {
            "float" => "1.0f",
            "double" => "1.0",
            "bool" => "true",
            _ => $"({cppType})1"
        },
        _ => null
    };

    /// <summary>
    /// Emits an intrinsic C++ operator for a constrained call (IBitwiseOperators, etc.).
    /// Returns true if emitted, false if args count doesn't match.
    /// </summary>
    private bool EmitIntrinsicOperator(IRBasicBlock block, Stack<StackEntry> stack,
        List<string> args, IRCall irCall, string cppOp, string cppType, ref int tempCounter)
    {
        if (args.Count >= 2)
        {
            var tmp = $"__t{tempCounter++}";
            bool isBitwiseOp = cppOp is "|" or "&" or "^";
            bool isFloat = cppType is "float" or "double";
            if (isBitwiseOp && isFloat)
            {
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
            stack.Push(new StackEntry(tmp, cppType));
            irCall.Arguments.Clear();
            args.Clear();
            return true;
        }
        else if (args.Count == 1)
        {
            var tmp = $"__t{tempCounter++}";
            bool isUnaryBitwise = cppOp == "~";
            bool isFloat = cppType is "float" or "double";
            if (isUnaryBitwise && isFloat)
            {
                // Bitwise NOT on float/double: reinterpret through integer type
                var intType = cppType == "float" ? "uint32_t" : "uint64_t";
                var a = $"__bw_a{tempCounter}";
                block.Instructions.Add(new IRRawCpp
                    { Code = $"{intType} {a}; std::memcpy(&{a}, &{args[0]}, sizeof({intType}));" });
                block.Instructions.Add(new IRRawCpp
                    { Code = $"{intType} __bw_r{tempCounter} = ~{a};" });
                block.Instructions.Add(new IRRawCpp
                    { Code = $"{cppType} {tmp}; std::memcpy(&{tmp}, &__bw_r{tempCounter}, sizeof({cppType}));", ResultVar = tmp, ResultTypeCpp = cppType });
            }
            else
            {
                block.Instructions.Add(new IRRawCpp
                    { Code = $"{tmp} = ({cppType})({cppOp}{args[0]});", ResultVar = tmp, ResultTypeCpp = cppType });
            }
            stack.Push(new StackEntry(tmp, cppType));
            irCall.Arguments.Clear();
            args.Clear();
            return true;
        }
        return false;
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

    /// <summary>
    /// Intercept SpanHelpers scalar search methods whose BCL IL uses SIMD-dependent
    /// control flow (Vector512/256/128 IsSupported branches, runtime typeof(T)==typeof(byte)
    /// checks). These patterns crash when AOT compiled because the SIMD stubs are empty
    /// and the complex branch structure confuses optimization. Replace with simple scalar
    /// loops implemented in the C++ runtime.
    /// </summary>
    private bool TryEmitSpanHelpersSearch(IRBasicBlock block, Stack<StackEntry> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var declType = methodRef.DeclaringType.FullName;
        if (declType != "System.SpanHelpers") return false;

        var name = methodRef.Name;
        var paramCount = methodRef.Parameters.Count;

        // IndexOfAnyValueType<T>(ref T, T, T, int) → 2-value search
        // NonPackedIndexOfAnyValueType<TValue, TNeg>(ref T, T, T, int) → same signature, complex impl
        if ((name == "IndexOfAnyValueType" || name == "NonPackedIndexOfAnyValueType")
            && paramCount == 4 && methodRef is GenericInstanceMethod)
        {
            var length = stack.PopExpr();
            var v1 = stack.PopExpr();
            var v0 = stack.PopExpr();
            var searchSpace = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_index_of_any2({searchSpace}, {v0}, {v1}, {length});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // IndexOfAnyValueType<T>(ref T, T, T, T, int) → 3-value search
        // NonPackedIndexOfAnyValueType<TValue, TNeg>(ref T, T, T, T, int)
        if ((name == "IndexOfAnyValueType" || name == "NonPackedIndexOfAnyValueType")
            && paramCount == 5 && methodRef is GenericInstanceMethod)
        {
            var length = stack.PopExpr();
            var v2 = stack.PopExpr();
            var v1 = stack.PopExpr();
            var v0 = stack.PopExpr();
            var searchSpace = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_index_of_any3({searchSpace}, {v0}, {v1}, {v2}, {length});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // IndexOfValueType<T>(ref T, T, int) → 1-value search
        // NonPackedIndexOfValueType<TValue, TNeg>(ref T, T, int)
        if ((name == "IndexOfValueType" || name == "NonPackedIndexOfValueType")
            && paramCount == 3 && methodRef is GenericInstanceMethod)
        {
            var length = stack.PopExpr();
            var v0 = stack.PopExpr();
            var searchSpace = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_index_of({searchSpace}, {v0}, {length});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // LastIndexOfAnyValueType<T>(ref T, T, T, int) → 2-value reverse search
        if ((name == "LastIndexOfAnyValueType" || name == "NonPackedLastIndexOfAnyValueType")
            && paramCount == 4 && methodRef is GenericInstanceMethod)
        {
            var length = stack.PopExpr();
            var v1 = stack.PopExpr();
            var v0 = stack.PopExpr();
            var searchSpace = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_last_index_of_any2({searchSpace}, {v0}, {v1}, {length});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // IndexOfAnyExceptValueType<T>(ref T, T, int) → find first NOT equal to value
        if ((name == "IndexOfAnyExceptValueType" || name == "NonPackedIndexOfAnyExceptValueType")
            && paramCount == 3 && methodRef is GenericInstanceMethod)
        {
            var length = stack.PopExpr();
            var v0 = stack.PopExpr();
            var searchSpace = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_index_of_any_except({searchSpace}, {v0}, {length});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // LastIndexOfValueType<T>(ref T, T, int) → 1-value reverse search
        if ((name == "LastIndexOfValueType" || name == "NonPackedLastIndexOfValueType")
            && paramCount == 3 && methodRef is GenericInstanceMethod)
        {
            var length = stack.PopExpr();
            var v0 = stack.PopExpr();
            var searchSpace = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_last_index_of({searchSpace}, {v0}, {length});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // SequenceEqual(ref byte, ref byte, nuint) → memcmp
        // The BCL implementation uses SIMD intrinsics (Vector128/256/512) that are stubbed in AOT.
        if (name == "SequenceEqual" && paramCount == 3 && methodRef is not GenericInstanceMethod)
        {
            var length = stack.PopExpr();
            var second = stack.PopExpr();
            var first = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = (std::memcmp({first}, {second}, (size_t){length}) == 0);",
                ResultVar = tmp,
                ResultTypeCpp = "bool",
            });
            stack.Push(new StackEntry(tmp, "bool"));
            return true;
        }

        // SequenceCompareTo(ref byte, int, ref byte, int) → memcmp with length handling
        // SequenceCompareTo(ref char, int, ref char, int) → same pattern
        if (name == "SequenceCompareTo" && paramCount == 4 && methodRef is not GenericInstanceMethod)
        {
            var secondLength = stack.PopExpr();
            var second = stack.PopExpr();
            var firstLength = stack.PopExpr();
            var first = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            // Determine element size from parameter type
            var firstParam = methodRef.Parameters[0].ParameterType;
            var elemSize = firstParam.FullName.Contains("Char") ? "sizeof(char16_t)" : "1";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_sequence_compare_to({first}, {firstLength}, {second}, {secondLength}, {elemSize});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // IndexOf(ref byte, int, ref byte, int) → memmem-style substring search
        // IndexOf(ref char, int, ref char, int) → same for char16_t
        if (name == "IndexOf" && paramCount == 4 && methodRef is not GenericInstanceMethod)
        {
            var valueLength = stack.PopExpr();
            var value = stack.PopExpr();
            var searchSpaceLength = stack.PopExpr();
            var searchSpace = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            var firstParam = methodRef.Parameters[0].ParameterType;
            var elemSize = firstParam.FullName.Contains("Char") ? "sizeof(char16_t)" : "1";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::span_index_of_seq({searchSpace}, {searchSpaceLength}, {value}, {valueLength}, {elemSize});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // Fill<char>(ref char, nuint, char) → memset-style fill
        if (name == "Fill" && paramCount == 3 && methodRef is GenericInstanceMethod)
        {
            var value = stack.PopExpr();
            var numElements = stack.PopExpr();
            var refData = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"cil2cpp::span_fill({refData}, {numElements}, {value});",
            });
            return true;
        }

        return false;
    }

    /// <summary>
    /// Intercept BitOperations methods that use hardware intrinsics (X86.Popcnt, etc.)
    /// unavailable in AOT. Replace with portable C++ implementations.
    /// </summary>
    /// <summary>
    /// AOT intrinsic: replace LambdaCompiler.Compile(LambdaExpression) with interpreter path.
    /// Emits: new LightCompiler().CompileTop(lambdaExpr).CreateDelegate()
    /// Returns true if successful, false if interpreter types not available.
    /// </summary>
    private bool EmitExpressionCompileInterpreterRedirect(
        IRBasicBlock block, Stack<StackEntry> stack, ref int tempCounter, string lambdaArg)
    {
        // Find interpreter types in the module
        var lcType = _module.Types.FirstOrDefault(t =>
            t.ILFullName == "System.Linq.Expressions.Interpreter.LightCompiler");
        var ldcType = _module.Types.FirstOrDefault(t =>
            t.ILFullName == "System.Linq.Expressions.Interpreter.LightDelegateCreator");
        if (lcType == null || ldcType == null) return false;

        var lcCtor = lcType.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        var compileTop = lcType.Methods.FirstOrDefault(m => m.Name == "CompileTop" && m.Parameters.Count == 1);
        var createDelegate = ldcType.Methods.FirstOrDefault(m => m.Name == "CreateDelegate" && m.Parameters.Count == 0);
        if (lcCtor == null || compileTop == null || createDelegate == null) return false;

        // 1. new LightCompiler()
        var lcTmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRNewObj
        {
            TypeCppName = lcType.CppName,
            CtorName = lcCtor.CppName,
            ResultVar = lcTmp,
        });

        // 2. LightCompiler.CompileTop(lambdaExpr)
        var ldcTmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRCall
        {
            FunctionName = compileTop.CppName,
            Arguments = { $"({lcType.CppName}*){lcTmp}", $"(System_Linq_Expressions_LambdaExpression*){lambdaArg}" },
            ResultVar = ldcTmp,
            ResultTypeCpp = $"{ldcType.CppName}*",
        });

        // 3. LightDelegateCreator.CreateDelegate()
        var delegateTmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRCall
        {
            FunctionName = createDelegate.CppName,
            Arguments = { $"({ldcType.CppName}*){ldcTmp}" },
            ResultVar = delegateTmp,
            ResultTypeCpp = "System_Delegate*",
        });

        stack.Push(new StackEntry(delegateTmp, "System_Delegate*"));
        return true;
    }

    private bool TryEmitBitOperationsIntrinsic(IRBasicBlock block, Stack<StackEntry> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (methodRef.DeclaringType.FullName != "System.Numerics.BitOperations") return false;

        var name = methodRef.Name;
        var paramCount = methodRef.Parameters.Count;

        // PopCount(uint32/uint64) → cil2cpp::bit_pop_count / bit_pop_count64
        if (name == "PopCount" && paramCount == 1)
        {
            var value = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            var is64 = methodRef.Parameters[0].ParameterType.FullName.Contains("64");
            var func = is64 ? "cil2cpp::bit_pop_count64" : "cil2cpp::bit_pop_count";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {func}({value});",
                ResultVar = tmp,
                ResultTypeCpp = "int32_t",
            });
            stack.Push(new StackEntry(tmp, "int32_t"));
            return true;
        }

        // ResetLowestSetBit(uint32/uint64) → value & (value - 1)
        if (name == "ResetLowestSetBit" && paramCount == 1)
        {
            var value = stack.PopExpr();
            var tmp = $"__t{tempCounter++}";
            var is64 = methodRef.Parameters[0].ParameterType.FullName.Contains("64");
            var cppType = is64 ? "uint64_t" : "uint32_t";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = ({cppType})({value}) & (({cppType})({value}) - 1);",
                ResultVar = tmp,
                ResultTypeCpp = cppType,
            });
            stack.Push(new StackEntry(tmp, cppType));
            return true;
        }

        return false;
    }

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

        // For methods accessing span.f__reference, verify the span type has that field.
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
                    && spanIrType.Fields.Any(f => f.Name is "_reference" or "f__reference"))
                    return true;
            }
            return false;
        }

        switch (methodName)
        {
            // GetReference<T>(Span<T>) / GetReference<T>(ReadOnlySpan<T>) → span.f__reference
            case "GetReference":
            {
                if (methodRef.Parameters.Count != 1 || !SpanTypeHasFields()) return false;
                var span = stack.PopExprOr("{}");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = ({elemPtrCpp}){span}.f__reference;",
                    ResultVar = tmp,
                    ResultTypeCpp = elemPtrCpp,
                });
                stack.Push(new StackEntry(tmp, elemPtrCpp));
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
                    Code = $"{tmp} = {span}.f__length != 0 ? ({elemPtrCpp}){span}.f__reference : ({elemPtrCpp})(uintptr_t)1;",
                    ResultVar = tmp,
                    ResultTypeCpp = elemPtrCpp,
                });
                stack.Push(new StackEntry(tmp, elemPtrCpp));
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
                stack.Push(new StackEntry(tmp, elemPtrCpp));
                return true;
            }

            // Read<T>(ReadOnlySpan<byte>) → *(T*)span.f__reference
            case "Read":
            {
                if (methodRef.Parameters.Count != 1) return false;
                var span = stack.PopExprOr("{}");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = *({elemCpp}*){span}.f__reference;",
                    ResultVar = tmp,
                    ResultTypeCpp = elemCpp,
                });
                stack.Push(new StackEntry(tmp, elemCpp));
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
                    Code = $"{retCpp} {tmp} = {{0}}; {tmp}.f__reference = ({elemPtrCpp}){refPtr}; {tmp}.f__length = {length};",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
                stack.Push(new StackEntry(tmp, retCpp));
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
                           $"{tmp}.f__reference = (uint8_t*){span}.f__reference; " +
                           $"{tmp}.f__length = {span}.f__length * {elemSizeof};",
                    ResultVar = tmp,
                    ResultTypeCpp = retCpp,
                });
                stack.Push(new StackEntry(tmp, retCpp));
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

    /// <summary>
    /// Compile-time evaluation of RuntimeHelpers.IsBitwiseEquatable&lt;T&gt;().
    /// Returns true if T can be compared bitwise (primitive value types, enums).
    /// The JIT replaces this intrinsic at compile time; the BCL IL throws InvalidOperationException.
    /// </summary>
    private bool IsBitwiseEquatable(string ilTypeName)
    {
        // Primitive types that are bitwise equatable
        if (ilTypeName is "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
            or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
            or "System.Boolean" or "System.Char"
            or "System.IntPtr" or "System.UIntPtr")
            return true;

        // Enums are bitwise equatable (backed by integer types)
        if (_typeCache.TryGetValue(ilTypeName, out var irType) && irType.IsEnum)
            return true;

        // Float/double are NOT bitwise equatable (NaN != NaN, +0 == -0)
        // Reference types and complex value types are not bitwise equatable
        return false;
    }
}
