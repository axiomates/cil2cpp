using System.Text.RegularExpressions;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Post-compilation peephole optimizer that eliminates single-use temporary variables.
/// Runs on IR instructions (structured data), not on rendered C++ strings.
///
/// Transforms patterns like:
///   auto __t0 = loc_0;           →  loc_0 = loc_0 + (int32_t)1;
///   auto __t1 = (int32_t)1;
///   auto __t2 = __t0 + __t1;
///   loc_0 = __t2;
/// </summary>
public static class IRPeepholeOptimizer
{
    // Matches temp variable names: __t0, __t1, __t123, etc.
    private static readonly Regex TempVarPattern = new(@"\b(__t\d+)\b", RegexOptions.Compiled);

    /// <summary>
    /// Eliminate single-use temporary variables in all basic blocks of a method.
    /// Thread-safe: uses thread-local state for dead instruction tracking.
    /// </summary>
    public static void EliminateSingleUseTemps(IRMethod method)
    {
        ClearDeadSet();
        foreach (var block in method.BasicBlocks)
            OptimizeBlock(block.Instructions);
        ClearDeadSet();
    }

    private static void OptimizeBlock(List<IRInstruction> instructions)
    {
        if (instructions.Count < 2) return;

        // Pass 1: Count uses of __tN variables across the ENTIRE block.
        // This is critical: a temp defined in one segment may be used in another
        // (across labels/exception handlers), so per-segment counting is unsafe.
        var globalUseCounts = new Dictionary<string, int>();
        for (int i = 0; i < instructions.Count; i++)
        {
            foreach (var operand in GetReadOperands(instructions[i]))
            {
                foreach (Match m in TempVarPattern.Matches(operand))
                {
                    var name = m.Groups[1].Value;
                    globalUseCounts[name] = globalUseCounts.GetValueOrDefault(name) + 1;
                }
            }
        }

        // Pass 2: Split into segments at barrier instructions and optimize each.
        // Inlining only happens within a segment, but use counts are global.
        int segStart = 0;
        for (int i = 0; i <= instructions.Count; i++)
        {
            if (i == instructions.Count || IsBarrier(instructions[i]))
            {
                if (i - segStart >= 2)
                    OptimizeSegment(instructions, segStart, i, globalUseCounts);
                segStart = i + 1;
            }
        }

        // Remove dead instructions
        instructions.RemoveAll(IsMarkedDead);
    }

    /// <summary>
    /// Optimize a contiguous segment of instructions (no barriers within).
    /// Uses global use counts to ensure we only inline truly single-use temps.
    /// </summary>
    private static void OptimizeSegment(List<IRInstruction> instructions, int start, int end,
        Dictionary<string, int> globalUseCounts)
    {
        // Pass 2: Inline single-use temps (forward iteration for cascading)
        for (int i = start; i < end; i++)
        {
            var instr = instructions[i];
            if (IsMarkedDead(instr)) continue;

            var defVar = GetDefinedTempVar(instr);
            if (defVar == null) continue;
            if (!globalUseCounts.TryGetValue(defVar, out var uses) || uses != 1) continue;

            // Get the expression this instruction computes
            var expr = GetExpression(instr);
            if (expr == null) continue;

            // Find the single use site within the segment
            int useIdx = FindSingleUse(instructions, defVar, i + 1, end);
            if (useIdx < 0) continue;

            // Safety: side-effecting instructions can only inline into immediately next
            if (HasSideEffects(instr) && useIdx != i + 1) continue;

            // Safety: check that variables read by the expression aren't modified between def and use
            if (!IsSafeToInline(instructions, i, useIdx, expr)) continue;

            // Safety: if the consumer wraps defVar in a (void*) cast, inlining would produce
            // (void*)(TypedExpr) which erases the type. The post-render WrapCrossScopePointerAssignment
            // relies on seeing the bare variable to add the correct typed cast later.
            if (ConsumerWrapsInVoidCast(instructions[useIdx], defVar)) continue;

            // Wrap expression in parens if it contains operators (precedence safety)
            var wrappedExpr = WrapIfNeeded(expr);

            // Perform substitution in the consumer instruction
            if (SubstituteInReadOperands(instructions[useIdx], defVar, wrappedExpr))
            {
                MarkDead(instr);
            }
        }
    }

    // ========== Instruction Analysis ==========

    /// <summary>
    /// Get the __tN variable this instruction defines, or null.
    /// </summary>
    private static string? GetDefinedTempVar(IRInstruction instr)
    {
        string? varName = instr switch
        {
            IRAssign a => a.Target,
            IRBinaryOp b => b.ResultVar,
            IRUnaryOp u => u.ResultVar,
            IRCall c => c.ResultVar,
            IRFieldAccess f when !f.IsStore => f.ResultVar,
            IRStaticFieldAccess sf when !sf.IsStore => sf.ResultVar,
            IRArrayAccess aa when !aa.IsStore => aa.ResultVar,
            IRCast c => c.ResultVar,
            IRConversion c => c.ResultVar,
            IRBox b => b.ResultVar,
            IRUnbox u => u.ResultVar,
            IRLoadFunctionPointer lfp => lfp.ResultVar,
            // Skip: IRNewObj (multi-line), IRDelegateCreate (multi-line),
            //        IRDelegateInvoke (complex), IRRawCpp (opaque)
            _ => null
        };
        return varName != null && varName.StartsWith("__t") ? varName : null;
    }

    /// <summary>
    /// Get the RHS expression this instruction computes.
    /// Leverages ToCpp() to avoid duplicating complex rendering logic.
    /// Returns null for instructions that can't be inlined (multi-line, complex dispatch, etc.).
    /// </summary>
    private static string? GetExpression(IRInstruction instr)
    {
        var defVar = GetDefinedTempVar(instr);
        if (defVar == null) return null;

        // Skip types that produce multi-line, complex, or side-effecting output.
        // IRCall is excluded because the dead-code replacement logic in GenerateMethodImpl
        // depends on seeing IRCall instructions to detect undeclared function references.
        // Inlining a call expression into a consumer would bypass that detection.
        if (instr is IRNewObj or IRDelegateCreate or IRDelegateInvoke or IRRawCpp or
            IRCall or IRBox)
            return null;

        var cpp = instr.ToCpp();

        // Multi-line output can't be inlined
        if (cpp.Contains('\n')) return null;

        // Expected format: "defVar = <expr>;"
        var prefix = $"{defVar} = ";
        if (!cpp.StartsWith(prefix)) return null;
        if (!cpp.EndsWith(";")) return null;

        return cpp[prefix.Length..^1];
    }

    /// <summary>
    /// Does this instruction have side effects? (calls, stores, allocations, throws)
    /// Side-effecting defs can only be inlined into the immediately next instruction.
    /// </summary>
    private static bool HasSideEffects(IRInstruction instr) => instr switch
    {
        IRCall => true,
        IRNewObj => true,
        IRBox => true,
        IRFieldAccess f when f.IsStore => true,
        IRStaticFieldAccess sf when sf.IsStore => true,
        IRArrayAccess aa when aa.IsStore => true,
        IRNullCheck => true,
        IRThrow => true,
        IRInitObj => true,
        IRStaticCtorGuard => true,
        IRDelegateCreate => true,
        IRDelegateInvoke => true,
        IRRawCpp => true,
        _ => false
    };

    /// <summary>
    /// Is this a barrier instruction that prevents inlining across it?
    /// Labels, exception handling boundaries, and control flow instructions.
    /// </summary>
    private static bool IsBarrier(IRInstruction instr) => instr is
        IRLabel or IRTryBegin or IRTryEnd or
        IRCatchBegin or IRFinallyBegin or IRFaultBegin or IRFaultEnd or
        IRFilterBegin or IREndFilter or IRFilterHandlerEnd;

    /// <summary>
    /// Get all string operands in "read" position for use-counting and safety analysis.
    /// </summary>
    private static IEnumerable<string> GetReadOperands(IRInstruction instr)
    {
        switch (instr)
        {
            case IRAssign a:
                yield return a.Value;
                break;
            case IRBinaryOp b:
                yield return b.Left;
                yield return b.Right;
                break;
            case IRUnaryOp u:
                yield return u.Operand;
                break;
            case IRCall c:
                foreach (var arg in c.Arguments) yield return arg;
                break;
            case IRNewObj n:
                foreach (var arg in n.CtorArgs) yield return arg;
                break;
            case IRFieldAccess f:
                yield return f.ObjectExpr;
                if (f.IsStore && f.StoreValue != null) yield return f.StoreValue;
                break;
            case IRStaticFieldAccess sf:
                if (sf.IsStore && sf.StoreValue != null) yield return sf.StoreValue;
                break;
            case IRArrayAccess aa:
                yield return aa.ArrayExpr;
                yield return aa.IndexExpr;
                if (aa.IsStore && aa.StoreValue != null) yield return aa.StoreValue;
                break;
            case IRCast c:
                yield return c.SourceExpr;
                break;
            case IRConversion c:
                yield return c.SourceExpr;
                break;
            case IRBox b:
                yield return b.ValueExpr;
                break;
            case IRUnbox u:
                yield return u.ObjectExpr;
                break;
            case IRReturn r:
                if (r.Value != null) yield return r.Value;
                break;
            case IRConditionalBranch cb:
                yield return cb.Condition;
                break;
            case IRSwitch s:
                yield return s.ValueExpr;
                break;
            case IRNullCheck nc:
                yield return nc.Expr;
                break;
            case IRThrow t:
                yield return t.ExceptionExpr;
                break;
            case IRInitObj io:
                yield return io.AddressExpr;
                break;
            case IRDelegateCreate dc:
                yield return dc.TargetExpr;
                yield return dc.FunctionPtrExpr;
                break;
            case IRDelegateInvoke di:
                yield return di.DelegateExpr;
                foreach (var arg in di.Arguments) yield return arg;
                break;
            case IRLoadFunctionPointer lfp:
                if (lfp.ObjectExpr != null) yield return lfp.ObjectExpr;
                break;
            case IRRawCpp raw:
                yield return raw.Code;
                break;
        }
    }

    // ========== Substitution ==========

    /// <summary>
    /// Replace occurrences of varName in the instruction's read operands with expr.
    /// Returns true if any substitution occurred.
    /// </summary>
    private static bool SubstituteInReadOperands(IRInstruction instr, string varName, string expr)
    {
        switch (instr)
        {
            case IRAssign a:
                if (!a.Value.Contains(varName)) return false;
                a.Value = ReplaceVar(a.Value, varName, expr);
                return true;
            case IRBinaryOp b:
            {
                bool changed = false;
                if (b.Left.Contains(varName)) { b.Left = ReplaceVar(b.Left, varName, expr); changed = true; }
                if (b.Right.Contains(varName)) { b.Right = ReplaceVar(b.Right, varName, expr); changed = true; }
                return changed;
            }
            case IRUnaryOp u:
                if (!u.Operand.Contains(varName)) return false;
                u.Operand = ReplaceVar(u.Operand, varName, expr);
                return true;
            case IRCall c:
                return ReplaceInList(c.Arguments, varName, expr);
            case IRNewObj n:
                return ReplaceInList(n.CtorArgs, varName, expr);
            case IRFieldAccess f:
            {
                bool changed = false;
                if (f.ObjectExpr.Contains(varName)) { f.ObjectExpr = ReplaceVar(f.ObjectExpr, varName, expr); changed = true; }
                if (f.IsStore && f.StoreValue != null && f.StoreValue.Contains(varName))
                    { f.StoreValue = ReplaceVar(f.StoreValue, varName, expr); changed = true; }
                return changed;
            }
            case IRStaticFieldAccess sf:
                if (!sf.IsStore || sf.StoreValue == null || !sf.StoreValue.Contains(varName)) return false;
                sf.StoreValue = ReplaceVar(sf.StoreValue, varName, expr);
                return true;
            case IRArrayAccess aa:
            {
                bool changed = false;
                if (aa.ArrayExpr.Contains(varName)) { aa.ArrayExpr = ReplaceVar(aa.ArrayExpr, varName, expr); changed = true; }
                if (aa.IndexExpr.Contains(varName)) { aa.IndexExpr = ReplaceVar(aa.IndexExpr, varName, expr); changed = true; }
                if (aa.IsStore && aa.StoreValue != null && aa.StoreValue.Contains(varName))
                    { aa.StoreValue = ReplaceVar(aa.StoreValue, varName, expr); changed = true; }
                return changed;
            }
            case IRCast c:
                if (!c.SourceExpr.Contains(varName)) return false;
                c.SourceExpr = ReplaceVar(c.SourceExpr, varName, expr);
                return true;
            case IRConversion c:
                if (!c.SourceExpr.Contains(varName)) return false;
                c.SourceExpr = ReplaceVar(c.SourceExpr, varName, expr);
                return true;
            case IRBox b:
                if (!b.ValueExpr.Contains(varName)) return false;
                b.ValueExpr = ReplaceVar(b.ValueExpr, varName, expr);
                return true;
            case IRUnbox u:
                if (!u.ObjectExpr.Contains(varName)) return false;
                u.ObjectExpr = ReplaceVar(u.ObjectExpr, varName, expr);
                return true;
            case IRReturn r:
                if (r.Value == null || !r.Value.Contains(varName)) return false;
                r.Value = ReplaceVar(r.Value, varName, expr);
                return true;
            case IRConditionalBranch cb:
                if (!cb.Condition.Contains(varName)) return false;
                cb.Condition = ReplaceVar(cb.Condition, varName, expr);
                return true;
            case IRSwitch s:
                if (!s.ValueExpr.Contains(varName)) return false;
                s.ValueExpr = ReplaceVar(s.ValueExpr, varName, expr);
                return true;
            case IRNullCheck nc:
                if (!nc.Expr.Contains(varName)) return false;
                nc.Expr = ReplaceVar(nc.Expr, varName, expr);
                return true;
            case IRThrow t:
                if (!t.ExceptionExpr.Contains(varName)) return false;
                t.ExceptionExpr = ReplaceVar(t.ExceptionExpr, varName, expr);
                return true;
            case IRInitObj io:
                if (!io.AddressExpr.Contains(varName)) return false;
                io.AddressExpr = ReplaceVar(io.AddressExpr, varName, expr);
                return true;
            case IRDelegateInvoke di:
            {
                bool changed = false;
                if (di.DelegateExpr.Contains(varName)) { di.DelegateExpr = ReplaceVar(di.DelegateExpr, varName, expr); changed = true; }
                if (ReplaceInList(di.Arguments, varName, expr)) changed = true;
                return changed;
            }
            default:
                return false;
        }
    }

    // ========== Safety Analysis ==========

    /// <summary>
    /// Find the index of the single use of varName in instructions[startIdx..endIdx).
    /// Returns -1 if not found within the segment.
    /// </summary>
    private static int FindSingleUse(List<IRInstruction> instructions, string varName, int startIdx, int endIdx)
    {
        for (int i = startIdx; i < endIdx; i++)
        {
            if (IsMarkedDead(instructions[i])) continue;
            foreach (var operand in GetReadOperands(instructions[i]))
            {
                if (operand.Contains(varName) && TempVarPattern.IsMatch(operand) &&
                    Regex.IsMatch(operand, $@"\b{Regex.Escape(varName)}\b"))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Check that it's safe to inline the expression from instructions[defIdx] into instructions[useIdx].
    /// Unsafe if any variable read by the expression is modified between def and use.
    /// </summary>
    private static bool IsSafeToInline(List<IRInstruction> instructions, int defIdx, int useIdx, string expr)
    {
        if (useIdx == defIdx + 1) return true; // Adjacent — always safe

        // Collect variables referenced in the expression
        var referencedVars = new HashSet<string>();
        // Match locals (loc_N), parameters (__this, arg_N), fields, etc.
        foreach (Match m in Regex.Matches(expr, @"\b(loc_\d+|__this|arg_\d+)\b"))
            referencedVars.Add(m.Groups[1].Value);

        if (referencedVars.Count == 0) return true; // Pure constant expression

        // Check if any intervening instruction modifies a referenced variable
        for (int i = defIdx + 1; i < useIdx; i++)
        {
            var instr = instructions[i];
            if (IsMarkedDead(instr)) continue;

            // Any side-effecting instruction could modify aliased state
            if (HasSideEffects(instr)) return false;

            // Check if this instruction assigns to a referenced variable
            var target = GetAssignTarget(instr);
            if (target != null && referencedVars.Contains(target)) return false;
        }
        return true;
    }

    /// <summary>
    /// Get the assignment target of an instruction (the variable it writes to, regardless of __t prefix).
    /// </summary>
    private static string? GetAssignTarget(IRInstruction instr) => instr switch
    {
        IRAssign a => a.Target,
        IRBinaryOp b => b.ResultVar,
        IRUnaryOp u => u.ResultVar,
        IRCall c => c.ResultVar,
        IRFieldAccess f when !f.IsStore => f.ResultVar,
        IRStaticFieldAccess sf when !sf.IsStore => sf.ResultVar,
        IRArrayAccess aa when !aa.IsStore => aa.ResultVar,
        IRCast c => c.ResultVar,
        IRConversion c => c.ResultVar,
        IRBox b => b.ResultVar,
        IRUnbox u => u.ResultVar,
        _ => null
    };

    /// <summary>
    /// Check if the consumer instruction wraps defVar in a (void*) cast.
    /// e.g., IRAssign Value = "(void*)__t4" → inlining __t4 would produce (void*)(TypedExpr)
    /// which erases the type and breaks WrapCrossScopePointerAssignment's post-render type wrapping.
    /// </summary>
    private static bool ConsumerWrapsInVoidCast(IRInstruction consumer, string defVar)
    {
        var voidCast = $"(void*){defVar}";
        foreach (var operand in GetReadOperands(consumer))
        {
            if (operand.Contains(voidCast))
                return true;
        }
        return false;
    }

    // ========== String Helpers ==========

    /// <summary>
    /// Replace varName with expr in operand string, with word-boundary awareness.
    /// </summary>
    private static string ReplaceVar(string operand, string varName, string expr)
    {
        // Fast path: operand IS the variable
        if (operand == varName) return expr;

        // General case: word-boundary replacement.
        // Use MatchEvaluator (lambda) instead of raw replacement string because
        // expr may contain '$' (e.g., field names like f_CS$__8__locals1) which
        // Regex.Replace interprets as substitution patterns ($_, $1, etc.).
        return Regex.Replace(operand, $@"\b{Regex.Escape(varName)}\b", _ => expr);
    }

    /// <summary>
    /// Replace varName with expr in all strings in a list. Returns true if any changed.
    /// </summary>
    private static bool ReplaceInList(List<string> list, string varName, string expr)
    {
        bool changed = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Contains(varName))
            {
                list[i] = ReplaceVar(list[i], varName, expr);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// Wrap expression in parens if it could cause precedence issues when inlined.
    /// - Binary operators: a + b inlined into c * X → c * (a + b)
    /// - Cast expressions: (Type*)ptr inlined into X->field → ((Type*)ptr)->field
    ///   Without parens, -> binds tighter than the cast: (Type*)(ptr->field)
    /// </summary>
    private static string WrapIfNeeded(string expr)
    {
        // Simple identifier: loc_0, __this, nullptr, etc.
        if (!expr.Contains(' ') && !expr.Contains('(') && !expr.Contains('+') && !expr.Contains('-'))
            return expr;

        // Cast expressions starting with (Type*) need parens when used in member access context.
        // e.g., (Foo*)(void*)bar inlined into __t->field → ((Foo*)(void*)bar)->field
        if (expr.StartsWith("(") && expr.Contains("*)"))
            return $"({expr})";

        // Check for binary operators (space-surrounded)
        if (expr.Contains(" + ") || expr.Contains(" - ") || expr.Contains(" * ") ||
            expr.Contains(" / ") || expr.Contains(" % ") || expr.Contains(" & ") ||
            expr.Contains(" | ") || expr.Contains(" ^ ") || expr.Contains(" << ") ||
            expr.Contains(" >> ") || expr.Contains(" == ") || expr.Contains(" != ") ||
            expr.Contains(" && ") || expr.Contains(" || ") || expr.Contains(" ? "))
            return $"({expr})";

        return expr;
    }

    // ========== Dead Instruction Tracking ==========
    // Uses a lightweight HashSet instead of modifying IRInstruction to avoid touching the base class.

    [ThreadStatic]
    private static HashSet<IRInstruction>? t_deadInstructions;

    private static void MarkDead(IRInstruction instr)
    {
        t_deadInstructions ??= new HashSet<IRInstruction>(ReferenceEqualityComparer.Instance);
        t_deadInstructions.Add(instr);
    }

    private static bool IsMarkedDead(IRInstruction instr)
        => t_deadInstructions?.Contains(instr) == true;

    private static void ClearDeadSet()
    {
        t_deadInstructions?.Clear();
    }
}
