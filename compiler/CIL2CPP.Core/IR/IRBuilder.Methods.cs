using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private IRMethod ConvertMethod(IL.MethodInfo methodDef, IRType declaringType)
    {
        var cppName = CppNameMapper.MangleMethodName(declaringType.CppName, methodDef.Name);
        // op_Explicit/op_Implicit: C# allows return-type overloading, C++ doesn't.
        // Append return type to disambiguate.
        if (methodDef.Name is "op_Explicit" or "op_Implicit" or "op_CheckedExplicit" or "op_CheckedImplicit")
        {
            var retMangled = CppNameMapper.MangleTypeName(methodDef.ReturnTypeName);
            cppName = $"{cppName}_{retMangled}";
        }

        var irMethod = new IRMethod
        {
            Name = methodDef.Name,
            CppName = cppName,
            DeclaringType = declaringType,
            ReturnTypeCpp = ResolveTypeForDecl(methodDef.ReturnTypeName),
            IsStatic = methodDef.IsStatic,
            IsVirtual = methodDef.IsVirtual,
            IsAbstract = methodDef.IsAbstract,
            IsConstructor = methodDef.IsConstructor,
            IsStaticConstructor = methodDef.IsConstructor && methodDef.IsStatic,
        };

        // Store raw ECMA-335 MethodAttributes
        var cecilMethod = methodDef.GetCecilMethod();
        irMethod.Attributes = (uint)cecilMethod.Attributes;

        // Detect newslot (C# 'new virtual')
        if (methodDef.IsNewSlot)
            irMethod.IsNewSlot = true;

        // Detect explicit interface overrides (Cecil .override directive)
        if (cecilMethod.HasOverrides)
        {
            foreach (var ovr in cecilMethod.Overrides)
            {
                irMethod.ExplicitOverrides.Add((ovr.DeclaringType.FullName, ovr.Name));
            }
        }

        // Detect finalizer
        if (methodDef.Name == "Finalize" && !methodDef.IsStatic && methodDef.IsVirtual
            && methodDef.Parameters.Count == 0 && methodDef.ReturnTypeName == "System.Void")
            irMethod.IsFinalizer = true;

        // Detect [MethodImpl(MethodImplOptions.InternalCall)]
        if (methodDef.IsInternalCall)
            irMethod.IsInternalCall = true;

        // Check if this method has an icall mapping (even if it has an IL body).
        // This happens for methods like Volatile.Read/Write, Console.WriteLine, Math.*, etc.
        // When an icall mapping exists, callers use the runtime function, making the IL body dead code.
        // Pass firstParamType for type-dispatched overloads (e.g., IntPtr.ctor(Int32) vs ctor(Int64)).
        {
            string? firstParamType = methodDef.Parameters.Count > 0
                ? methodDef.Parameters[0].TypeName : null;
            if (declaringType != null && ICallRegistry.Lookup(
                    declaringType.ILFullName, methodDef.Name, methodDef.Parameters.Count, firstParamType) != null)
                irMethod.HasICallMapping = true;
        }

        // Detect P/Invoke (DllImport) — ECMA-335 II.15.5
        if (methodDef.IsPInvokeImpl && methodDef.PInvokeInfo != null)
        {
            var pinfo = methodDef.PInvokeInfo;
            irMethod.IsPInvoke = true;
            irMethod.PInvokeModule = pinfo.Module?.Name;
            irMethod.PInvokeEntryPoint = pinfo.EntryPoint ?? methodDef.Name;

            // ECMA-335 II.15.5.2: Character set
            irMethod.PInvokeCharSet = pinfo.IsCharSetUnicode ? PInvokeCharSet.Unicode
                : pinfo.IsCharSetAuto ? PInvokeCharSet.Auto
                : PInvokeCharSet.Ansi;

            // ECMA-335 II.15.5.1: Calling convention
            irMethod.PInvokeCallingConvention = pinfo.IsCallConvStdCall ? PInvokeCallingConvention.StdCall
                : pinfo.IsCallConvThiscall ? PInvokeCallingConvention.ThisCall
                : pinfo.IsCallConvFastcall ? PInvokeCallingConvention.FastCall
                : PInvokeCallingConvention.Cdecl;

            irMethod.PInvokeSetLastError = pinfo.SupportsLastError;
        }

        // Detect operator methods
        if (methodDef.Name.StartsWith("op_"))
        {
            irMethod.IsOperator = true;
            irMethod.OperatorName = methodDef.Name;
        }

        // Resolve return type
        if (_typeCache.TryGetValue(methodDef.ReturnTypeName, out var retType))
        {
            irMethod.ReturnType = retType;
        }

        // Parameters
        foreach (var paramDef in methodDef.Parameters)
        {
            var irParam = new IRParameter
            {
                Name = paramDef.Name,
                CppName = paramDef.Name.Length > 0 ? CppNameMapper.MangleIdentifier(paramDef.Name) : $"p{paramDef.Index}",
                CppTypeName = ResolveTypeForDecl(paramDef.TypeName),
                ILTypeName = paramDef.TypeName,
                Index = paramDef.Index,
            };

            if (_typeCache.TryGetValue(paramDef.TypeName, out var paramType))
            {
                irParam.ParameterType = paramType;
            }

            irMethod.Parameters.Add(irParam);
        }

        // Detect varargs calling convention (C# __arglist)
        if (cecilMethod.CallingConvention == MethodCallingConvention.VarArg)
        {
            irMethod.IsVarArg = true;
            irMethod.Parameters.Add(new IRParameter
            {
                Name = "__arglist_handle",
                CppName = "__arglist_handle",
                CppTypeName = "intptr_t",
                ILTypeName = "System.IntPtr",
                Index = irMethod.Parameters.Count,
            });
        }

        // Local variables
        foreach (var localDef in methodDef.GetLocalVariables())
        {
            irMethod.Locals.Add(new IRLocal
            {
                Index = localDef.Index,
                CppName = $"loc_{localDef.Index}",
                CppTypeName = ResolveTypeForDecl(localDef.TypeName),
                IsPinned = localDef.IsPinned,
            });
        }

        // Note: method body is converted in a later pass (after VTables are built)
        return irMethod;
    }

    /// <summary>
    /// Detect overloaded methods whose C++ names collide (e.g. different C# enum types
    /// collapse to the same C++ type via using aliases) and rename them to be unique.
    /// Appends parameter type suffixes to disambiguate.
    /// </summary>
    private void DisambiguateOverloadedMethods()
    {
        foreach (var irType in _module.Types)
        {
            // Skip types already disambiguated — prevents suffix accumulation on re-runs
            if (!_disambiguatedTypes.Add(irType)) continue;
            // Group methods by their C++ mangled name
            var groups = irType.Methods
                .GroupBy(m => m.CppName)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                // Check if the C++ parameter signatures actually collide
                var methods = group.ToList();
                var sigMap = new Dictionary<string, List<IRMethod>>();

                foreach (var m in methods)
                {
                    // Resolve enum type names to their underlying types for collision detection
                    // (e.g., System_Globalization_CultureData_LocaleStringData → int32_t)
                    var sig = string.Join(",", m.Parameters.Select(p =>
                        ResolveEnumToUnderlying(p.CppTypeName, p.ILTypeName)));
                    if (!sigMap.TryGetValue(sig, out var list))
                    {
                        list = new List<IRMethod>();
                        sigMap[sig] = list;
                    }
                    list.Add(m);
                }

                // Always disambiguate when multiple methods share the same C++ name,
                // even if their C++ signatures differ. This prevents silent mismatches
                // when some overload bodies are filtered out by the code generator.
                // C++ overloading works for same-name-different-sig, but our body filters
                // may remove one overload, causing callers to silently bind to the wrong one.

                // Rename all methods in colliding groups by appending IL parameter types
                var originalName = group.Key;
                foreach (var m in methods)
                {
                    var ilSuffix = string.Join("_", m.Parameters.Select(p =>
                        MangleILTypeForDisambiguation(p.ILTypeName)));
                    if (ilSuffix.Length > 0)
                    {
                        m.CppName = $"{m.CppName}__{ilSuffix}";
                        // Register lookup: originalName|ilParam1,ilParam2 → disambiguated name
                        var ilParamKey = string.Join(",", m.Parameters.Select(p => p.ILTypeName));
                        var lookupKey = $"{originalName}|{ilParamKey}";
                        _module.DisambiguatedMethodNames[lookupKey] = m.CppName;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resolve a C++ type name through enum aliases for collision detection.
    /// If the type is an enum (in the module or external), returns the underlying type.
    /// </summary>
    private string ResolveEnumToUnderlying(string cppTypeName, string ilTypeName)
    {
        // Check if it's an enum in the module
        if (_typeCache.TryGetValue(ilTypeName, out var irType) && irType.IsEnum)
        {
            return CppNameMapper.GetCppTypeForDecl(irType.EnumUnderlyingType ?? "System.Int32");
        }
        // Check external enums
        var mangled = cppTypeName.TrimEnd('*').Trim();
        if (_module.ExternalEnumTypes.TryGetValue(mangled, out var underlying))
        {
            return underlying;
        }
        return cppTypeName;
    }

    /// <summary>
    /// Retroactively fix up IRCall.FunctionName in already-compiled method bodies
    /// that reference undisambiguated names. This handles the case where a generic method
    /// specialization (Pass 3.5) calls methods on a type that is only discovered and
    /// disambiguated in the Pass 3.6 re-discovery loop.
    /// Uses the DeferredDisambigKey stored at emit time for precise matching.
    /// </summary>
    private void FixupDisambiguatedCalls()
    {
        if (_module.DisambiguatedMethodNames.Count == 0) return;

        foreach (var irType in _module.Types)
        {
            foreach (var irMethod in irType.Methods)
            {
                foreach (var block in irMethod.BasicBlocks)
                {
                    foreach (var instr in block.Instructions)
                    {
                        if (instr is not IRCall call) continue;
                        if (call.DeferredDisambigKey == null) continue;

                        // Rebuild the lookup key using the stored IL param key
                        var lookupKey = $"{call.FunctionName}|{call.DeferredDisambigKey}";
                        if (_module.DisambiguatedMethodNames.TryGetValue(lookupKey, out var disambiguated))
                        {
                            call.FunctionName = disambiguated;
                            call.DeferredDisambigKey = null; // resolved
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Mangle an IL type name for use in disambiguated method names.
    /// Strips pointer/ref suffixes and applies standard mangling.
    /// </summary>
    private static string MangleILTypeForDisambiguation(string ilTypeName)
    {
        // Preserve pointer/ref suffixes to distinguish e.g. System.Char from System.Char*
        // This prevents name collisions like Append(Char, Int32) vs Append(Char*, Int32)
        var suffix = "";
        var clean = ilTypeName;
        while (clean.EndsWith("*") || clean.EndsWith("&") || clean.EndsWith(" "))
        {
            if (clean.EndsWith("*")) suffix += "Ptr";
            else if (clean.EndsWith("&")) suffix += "Ref";
            clean = clean[..^1];
        }
        return CppNameMapper.MangleTypeName(clean) + suffix;
    }

    /// <summary>
    /// Convert IL method body to IR basic blocks using stack simulation.
    /// </summary>
    private void ConvertMethodBody(IL.MethodInfo methodDef, IRMethod irMethod)
    {
        _pendingVolatile = false; // Reset between methods
        _constrainedType = null;
        _inFilterRegion = false;
        _tempPtrTypes.Clear();
        _endfilterOffset = -1;
        var block = new IRBasicBlock { Id = 0 };
        irMethod.BasicBlocks.Add(block);

        var instructions = methodDef.GetInstructions().ToList();
        if (instructions.Count == 0) return;

        // Build sequence point map for debug info (IL offset -> SourceLocation)
        // Sorted by offset for efficient "most recent" lookup
        List<(int Offset, SourceLocation Location)>? sortedSeqPoints = null;
        if (_config.IsDebug && _reader.HasSymbols)
        {
            var sequencePoints = methodDef.GetSequencePoints();
            if (sequencePoints.Count > 0)
            {
                sortedSeqPoints = sequencePoints
                    .Where(sp => !sp.IsHidden)
                    .OrderBy(sp => sp.ILOffset)
                    .Select(sp => (sp.ILOffset, new SourceLocation
                    {
                        FilePath = sp.SourceFile,
                        Line = sp.StartLine,
                        Column = sp.StartColumn,
                        ILOffset = sp.ILOffset,
                    }))
                    .ToList();
            }
        }

        // Find branch targets (to create labels)
        var branchTargets = new HashSet<int>();
        // Track stack-merge variables at branch targets (for dup+brtrue pattern)
        var branchMergeVars = new Dictionary<int, string>();
        // Save full stack snapshots at conditional branch targets.
        // When a branch target is reached after dead code (e.g., after unconditional br),
        // the linear stack simulation has stale values. We restore the saved snapshot
        // to correctly handle ternary-like IL patterns (e.g., ldarg; brtrue T; ldc X; br M; T: ldc Y; M: stind).
        var branchTargetStacks = new Dictionary<int, StackEntry[]>();
        // Separate dictionary for merge variables created by unconditional br instructions.
        // These carry ternary values across dead/live paths at merge points.
        var brTernaryMerges = new Dictionary<int, StackEntry[]>();
        foreach (var instr in instructions)
        {
            if (ILInstructionCategory.IsBranch(instr.OpCode))
            {
                if (instr.Operand is Instruction target)
                    branchTargets.Add(target.Offset);
                else if (instr.Operand is Instruction[] targets)
                    foreach (var t in targets) branchTargets.Add(t.Offset);
            }
            // Leave instructions also branch
            if ((instr.OpCode == Code.Leave || instr.OpCode == Code.Leave_S) && instr.Operand is Instruction leaveTarget)
                branchTargets.Add(leaveTarget.Offset);
        }

        // Build exception handler event map (IL offset -> list of events)
        var exceptionEvents = new SortedDictionary<int, List<ExceptionEvent>>();
        var openedTryRegions = new HashSet<(int Start, int End)>();
        if (methodDef.HasExceptionHandlers)
        {
            foreach (var handler in methodDef.GetExceptionHandlers())
            {
                AddExceptionEvent(exceptionEvents, handler.TryStart,
                    new ExceptionEvent(ExceptionEventKind.TryBegin, null, handler.TryStart, handler.TryEnd));
                if (handler.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Catch)
                {
                    AddExceptionEvent(exceptionEvents, handler.HandlerStart,
                        new ExceptionEvent(ExceptionEventKind.CatchBegin, handler.CatchTypeName,
                            handler.TryStart, handler.TryEnd));
                }
                else if (handler.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally)
                {
                    AddExceptionEvent(exceptionEvents, handler.HandlerStart,
                        new ExceptionEvent(ExceptionEventKind.FinallyBegin, null,
                            handler.TryStart, handler.TryEnd));
                }
                else if (handler.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Filter
                    && handler.FilterStart.HasValue)
                {
                    // Filter: catch all exceptions at FilterStart, evaluate filter condition,
                    // then accept or reject at endfilter. Handler body follows at HandlerStart.
                    AddExceptionEvent(exceptionEvents, handler.FilterStart.Value,
                        new ExceptionEvent(ExceptionEventKind.FilterBegin, null,
                            handler.TryStart, handler.TryEnd));
                    // Push exception onto stack at handler body start (like CatchBegin)
                    AddExceptionEvent(exceptionEvents, handler.HandlerStart,
                        new ExceptionEvent(ExceptionEventKind.FilterHandlerBegin, null,
                            handler.TryStart, handler.TryEnd));
                }
                AddExceptionEvent(exceptionEvents, handler.HandlerEnd,
                    new ExceptionEvent(ExceptionEventKind.HandlerEnd, null,
                        handler.TryStart, handler.TryEnd));
            }
        }

        // Collect try-finally regions for leave instruction handling.
        // When a 'leave' crosses a try-finally boundary (offset in try region, target >= TryEnd),
        // we suppress the goto and let execution fall through to the finally block naturally.
        var tryFinallyRegions = new List<(int TryStart, int TryEnd)>();
        if (methodDef.HasExceptionHandlers)
        {
            foreach (var handler in methodDef.GetExceptionHandlers())
            {
                if (handler.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally)
                    tryFinallyRegions.Add((handler.TryStart, handler.TryEnd));
            }
        }

        // Stack simulation
        var stack = new Stack<StackEntry>();
        int tempCounter = 0;
        bool skipDeadCode = false; // Skip IL instructions after unconditional branch until next label
        int? lastCondBranchStackDepth = null; // Stack depth after most recent conditional branch (for ternary merge detection)

        foreach (var instr in instructions)
        {
            // Emit exception handler markers at this IL offset
            if (exceptionEvents.TryGetValue(instr.Offset, out var events))
            {
                foreach (var evt in events.OrderBy(e => e.Kind switch
                {
                    ExceptionEventKind.HandlerEnd => 0,
                    ExceptionEventKind.TryBegin => 1,
                    ExceptionEventKind.CatchBegin => 2,
                    ExceptionEventKind.FilterBegin => 2,
                    ExceptionEventKind.FilterHandlerBegin => 2,
                    ExceptionEventKind.FinallyBegin => 3,
                    _ => 4
                }))
                {
                    switch (evt.Kind)
                    {
                        case ExceptionEventKind.TryBegin:
                            var tryKey = (evt.TryStart, evt.TryEnd);
                            if (!openedTryRegions.Contains(tryKey))
                            {
                                openedTryRegions.Add(tryKey);
                                block.Instructions.Add(new IRTryBegin());
                            }
                            break;
                        case ExceptionEventKind.CatchBegin:
                            // Exception handler entry is reachable even after throw/ret —
                            // resume code generation (fixes missing catch body instructions)
                            skipDeadCode = false;
                            stack.Clear();
                            // Use GetCppTypeName (not MangleTypeName) so that runtime exception types
                            // resolve to cil2cpp::Exception etc. — the CIL2CPP_CATCH macro appends
                            // _TypeInfo which must match the runtime-declared TypeInfo names.
                            // System.Object catch is equivalent to catch-all
                            var catchTypeCpp = evt.CatchTypeName is not null and not "System.Object"
                                ? CppNameMapper.GetCppTypeName(evt.CatchTypeName)?.TrimEnd('*').TrimEnd() : null;
                            block.Instructions.Add(new IRCatchBegin { ExceptionTypeCppName = catchTypeCpp });
                            // IL pushes exception onto stack at catch entry
                            stack.Push(new StackEntry("__exc_ctx.current_exception", "cil2cpp::Object*"));
                            break;
                        case ExceptionEventKind.FilterBegin:
                            skipDeadCode = false;
                            stack.Clear();
                            block.Instructions.Add(new IRFilterBegin());
                            // Declare __filter_result in the filter scope (before any labels)
                            block.Instructions.Add(new IRRawCpp { Code = "int32_t __filter_result = 0;" });
                            // IL pushes exception onto stack for filter evaluation
                            stack.Push(new StackEntry("(cil2cpp::Exception*)__exc_ctx.current_exception", "cil2cpp::Exception*"));
                            // Track filter region for endfilter scoping fix
                            _inFilterRegion = true;
                            _endfilterOffset = FindEndfilterOffset(instructions, instr.Offset);
                            break;
                        case ExceptionEventKind.FilterHandlerBegin:
                            skipDeadCode = false;
                            stack.Clear();
                            // Handler body after endfilter — push exception for handler body
                            stack.Push(new StackEntry("(cil2cpp::Exception*)__exc_ctx.current_exception", "cil2cpp::Exception*"));
                            break;
                        case ExceptionEventKind.FinallyBegin:
                            skipDeadCode = false;
                            stack.Clear();
                            block.Instructions.Add(new IRFinallyBegin());
                            break;
                        case ExceptionEventKind.HandlerEnd:
                            // Don't emit END_TRY if another handler (catch/filter) follows
                            // at the same offset for the SAME try block.
                            var hasFollowingHandlerSameTry = events.Any(e =>
                                e != evt
                                && e.TryStart == evt.TryStart && e.TryEnd == evt.TryEnd
                                && (e.Kind == ExceptionEventKind.FilterBegin
                                    || e.Kind == ExceptionEventKind.CatchBegin
                                    || e.Kind == ExceptionEventKind.FinallyBegin));
                            if (!hasFollowingHandlerSameTry)
                                block.Instructions.Add(new IRTryEnd());
                            break;
                    }
                }
            }

            // Save filter result before scope boundary at endfilter label
            if (_inFilterRegion && instr.Offset == _endfilterOffset
                && branchTargets.Contains(instr.Offset) && stack.Count > 0)
            {
                block.Instructions.Add(new IRAssign
                {
                    Target = "__filter_result",
                    Value = stack.Peek().Expr
                });
            }

            // Insert label if this is a branch target
            if (branchTargets.Contains(instr.Offset))
            {
                var wasDeadCode = skipDeadCode;
                skipDeadCode = false; // Resume processing at branch targets

                // Restore stack from saved snapshot when arriving from dead code.
                // After an unconditional branch (br/ret/throw), the linear stack simulation
                // retains stale values from the dead code path. These MUST be cleared to
                // prevent corruption. If a conditional branch saved a stack snapshot for this
                // target, restore it; otherwise clear to empty (the default for most targets).
                if (wasDeadCode)
                {
                    stack.Clear();
                    if (branchTargetStacks.TryGetValue(instr.Offset, out var savedStack))
                    {
                        foreach (var item in savedStack)
                            stack.Push(item);
                    }
                    // If this is also a ternary merge target, push merge variables
                    // (the br handler already assigned the dead-path value)
                    if (brTernaryMerges.TryGetValue(instr.Offset, out var ternaryStack))
                    {
                        foreach (var item in ternaryStack)
                            stack.Push(item);
                    }
                }

                // Ternary merge: if an unconditional br created a merge variable for this target,
                // the fall-through (live) path must assign its current stack top to the
                // merge variable so both paths produce the correct value at the join point.
                // Pattern: brtrue T; push X; br M; T: push Y; M: → at M, assign Y to merge var
                if (!wasDeadCode
                    && brTernaryMerges.TryGetValue(instr.Offset, out var mergeStack)
                    && stack.Count > 0 && mergeStack.Length == 1)
                {
                    var mergeEntry = mergeStack[0];
                    var currentTop = stack.Pop();
                    if (mergeEntry.Expr != currentTop.Expr && IsValidMergeVariable(mergeEntry.Expr))
                    {
                        block.Instructions.Add(new IRAssign { Target = mergeEntry.Expr, Value = currentTop.Expr });
                    }
                    // Phase 2: refine the merge variable's type using the live path.
                    // The br handler set a preliminary type; the live path may be more
                    // specific (e.g., br path has literal 0, live path has void*).
                    string? liveType = !string.IsNullOrEmpty(currentTop.CppType) ? currentTop.CppType : null;
                    if (liveType == null && currentTop.Expr.StartsWith("__t"))
                        liveType = InferTempVarType(currentTop.Expr, block);
                    var mergeType = liveType ?? mergeEntry.CppType;
                    if (!string.IsNullOrEmpty(mergeType))
                        irMethod.TempVarTypes[mergeEntry.Expr] = mergeType;
                    stack.Push(new StackEntry(mergeEntry.Expr, mergeType));
                }
                // Legacy merge: if a conditional branch saved a single merge variable
                // for this offset (dup+brtrue delegate caching pattern), the fall-through
                // path may have a different stack top. Insert an assignment to unify.
                // IMPORTANT: Only do this if we're on a live fall-through path.
                else if (!wasDeadCode
                    && branchMergeVars.TryGetValue(instr.Offset, out var mergeVar)
                    && IsValidMergeVariable(mergeVar)
                    && stack.Count > 0 && stack.Peek().Expr != mergeVar)
                {
                    var currentTop = stack.Pop().Expr;
                    // For pointer-type merge targets, add explicit cast to handle implicit upcasts
                    // (e.g., EqualityComparer<T>* → IEqualityComparer<T>*) since generated C++
                    // structs don't use C++ inheritance. The dup+brtrue pattern can merge values
                    // of different pointer types from branching vs fall-through paths.
                    if (IsPointerTypedOperand(mergeVar, irMethod))
                        currentTop = $"({GetOperandPointerType(mergeVar, irMethod)}){currentTop}";
                    block.Instructions.Add(new IRAssign { Target = mergeVar, Value = currentTop });
                    stack.Push(mergeVar);
                }

                block.Instructions.Add(new IRLabel { LabelName = $"IL_{instr.Offset:X4}" });

                // Reset ternary merge tracking at labels — the conditional branch context
                // is local to the code between a conditional branch and its merge point.
                lastCondBranchStackDepth = null;
            }

            // Skip dead code after unconditional branches (br, ret, throw, rethrow)
            // These instructions have corrupted stack state and produce invalid C++ (e.g., "16 = 0")
            if (skipDeadCode)
                continue;

            int beforeCount = block.Instructions.Count;

            try
            {
                ConvertInstruction(instr, block, stack, irMethod, ref tempCounter, branchMergeVars, branchTargetStacks, branchTargets, tryFinallyRegions, ref lastCondBranchStackDepth, brTernaryMerges);
            }
            catch
            {
                block.Instructions.Add(new IRComment { Text = $"WARNING: Unsupported IL instruction: {instr}" });
                Console.Error.WriteLine($"WARNING: Unsupported IL instruction '{instr.OpCode}' at IL_{instr.Offset:X4} in {irMethod.CppName}");
            }

            // After unconditional terminators, skip remaining dead code until next label
            if (instr.OpCode is Code.Br or Code.Br_S or Code.Ret or Code.Throw or Code.Rethrow
                or Code.Leave or Code.Leave_S or Code.Endfinally)
                skipDeadCode = true;

            // Attach debug info to newly added instructions
            if (_config.IsDebug)
            {
                // Find the most recent sequence point at or before this IL offset
                SourceLocation? currentLoc = null;
                if (sortedSeqPoints != null)
                {
                    // Binary search for most recent sequence point <= instr.Offset
                    int lo = 0, hi = sortedSeqPoints.Count - 1, best = -1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (sortedSeqPoints[mid].Offset <= instr.Offset)
                        {
                            best = mid;
                            lo = mid + 1;
                        }
                        else
                        {
                            hi = mid - 1;
                        }
                    }
                    if (best >= 0)
                    {
                        currentLoc = sortedSeqPoints[best].Location;
                    }
                }

                var debugInfo = currentLoc != null
                    ? currentLoc with { ILOffset = instr.Offset }
                    : new SourceLocation { ILOffset = instr.Offset };

                for (int i = beforeCount; i < block.Instructions.Count; i++)
                {
                    block.Instructions[i].DebugInfo = debugInfo;
                }
            }
        }

        // Emit remaining HandlerEnd events that were never processed.
        // This happens when a handler extends to the end of the method body
        // (Cecil's HandlerEnd is null → offset int.MaxValue, never reached by the loop).
        var lastInstrOffset = instructions[^1].Offset;
        foreach (var (offset, events) in exceptionEvents)
        {
            if (offset > lastInstrOffset)
            {
                foreach (var evt in events)
                {
                    if (evt.Kind != ExceptionEventKind.HandlerEnd) continue;
                    var hasFollowingHandler = events.Any(e =>
                        e != evt && e.TryStart == evt.TryStart && e.TryEnd == evt.TryEnd
                        && e.Kind is ExceptionEventKind.CatchBegin or ExceptionEventKind.FinallyBegin
                            or ExceptionEventKind.FilterBegin);
                    if (!hasFollowingHandler)
                        block.Instructions.Add(new IRTryEnd());
                }
            }
        }
    }

    /// <summary>
    /// Check if a stack value is a valid C++ lvalue for use as a merge variable.
    /// Literals (numeric, nullptr, string) are NOT valid merge targets.
    /// </summary>
    private static bool IsValidMergeVariable(string stackValue)
    {
        if (string.IsNullOrEmpty(stackValue)) return false;
        // Numeric literals (0, 16, -1, 0.5f, etc.)
        if (char.IsDigit(stackValue[0]) || (stackValue[0] == '-' && stackValue.Length > 1 && char.IsDigit(stackValue[1])))
            return false;
        // nullptr
        if (stackValue == "nullptr") return false;
        // String literals
        if (stackValue.StartsWith("\"") || stackValue.StartsWith("u\"") || stackValue.StartsWith("u'")) return false;
        // Cast expressions like "(int32_t)(0)" are not lvalues
        if (stackValue.StartsWith("(") && !stackValue.StartsWith("(&")) return false;
        // Address-of expressions like "&value" or "&loc_N" are rvalues, not valid lvalues
        if (stackValue.StartsWith("&")) return false;
        return true;
    }



    /// <summary>
    /// Infer the C++ type of a temp variable from the block's existing instructions.
    /// Mirrors DetermineTempVarTypes in the code generator but runs at IR build time.
    /// </summary>
    private static string? InferTempVarType(string varName, IRBasicBlock block)
    {
        foreach (var instr in block.Instructions)
        {
            switch (instr)
            {
                case IRCall call when call.ResultVar == varName && call.ResultTypeCpp != null:
                    return call.ResultTypeCpp;
                case IRRawCpp raw when raw.ResultVar == varName && raw.ResultTypeCpp != null:
                    return raw.ResultTypeCpp;
                case IRDeclareLocal decl when decl.VarName == varName:
                    return decl.TypeName;
                case IRBinaryOp binOp when binOp.ResultVar == varName:
                    return binOp.Op is "==" or "!=" or "<" or ">" or "<=" or ">=" ? "int32_t" : "intptr_t";
                case IRConversion conv when conv.ResultVar == varName:
                    return conv.TargetType;
                case IRCast cast when cast.ResultVar == varName:
                    return cast.TargetTypeCpp;
                case IRFieldAccess fa when !fa.IsStore && fa.ResultVar == varName && fa.ResultTypeCpp != null:
                    return fa.ResultTypeCpp;
                case IRStaticFieldAccess sfa when !sfa.IsStore && sfa.ResultVar == varName && sfa.ResultTypeCpp != null:
                    return sfa.ResultTypeCpp;
                case IRBox box when box.ResultVar == varName:
                    return "cil2cpp::Object*";
                case IRUnbox unbox when unbox.ResultVar == varName:
                    return unbox.IsUnboxAny ? unbox.ValueTypeCppName : unbox.ValueTypeCppName + "*";
                case IRNewObj newObj when newObj.ResultVar == varName:
                    return newObj.TypeCppName + "*";
                case IRUnaryOp unOp when unOp.ResultVar == varName && unOp.ResultTypeCpp != null:
                    return unOp.ResultTypeCpp;
                case IRArrayAccess aa when !aa.IsStore && aa.ResultVar == varName:
                    return aa.ElementType;
                case IRLoadFunctionPointer lfp when lfp.ResultVar == varName:
                    return "void*";
                case IRDelegateCreate dc when dc.ResultVar == varName:
                    return "cil2cpp::Delegate*";
                case IRDelegateInvoke di when di.ResultVar == varName:
                    return di.ReturnTypeCpp;
            }
        }
        return null;
    }

    private void ConvertInstruction(ILInstruction instr, IRBasicBlock block, Stack<StackEntry> stack,
        IRMethod method, ref int tempCounter, Dictionary<int, string> branchMergeVars,
        Dictionary<int, StackEntry[]> branchTargetStacks, HashSet<int> branchTargets,
        List<(int TryStart, int TryEnd)> tryFinallyRegions, ref int? lastCondBranchStackDepth,
        Dictionary<int, StackEntry[]> brTernaryMerges)
    {
        switch (instr.OpCode)
        {
            // ===== Load Constants =====
            case Code.Ldc_I4_0: stack.Push("0"); break;
            case Code.Ldc_I4_1: stack.Push("1"); break;
            case Code.Ldc_I4_2: stack.Push("2"); break;
            case Code.Ldc_I4_3: stack.Push("3"); break;
            case Code.Ldc_I4_4: stack.Push("4"); break;
            case Code.Ldc_I4_5: stack.Push("5"); break;
            case Code.Ldc_I4_6: stack.Push("6"); break;
            case Code.Ldc_I4_7: stack.Push("7"); break;
            case Code.Ldc_I4_8: stack.Push("8"); break;
            case Code.Ldc_I4_M1: stack.Push("-1"); break;
            case Code.Ldc_I4_S:
                stack.Push(((sbyte)instr.Operand!).ToString());
                break;
            case Code.Ldc_I4:
            {
                var val = (int)instr.Operand!;
                // INT32_MIN: C++ parses -2147483648 as -(2147483648LL) on MSVC (long is 32-bit),
                // giving type long long instead of int. Use subtraction form to stay int.
                stack.Push(val == int.MinValue ? "(-2147483647 - 1)" : val.ToString());
                break;
            }
            case Code.Ldc_I8:
            {
                var val = (long)instr.Operand!;
                // INT64_MIN: 9223372036854775808LL overflows long long — ill-formed C++20.
                stack.Push(val == long.MinValue ? "(-9223372036854775807LL - 1LL)" : $"{val}LL");
                break;
            }
            case Code.Ldc_R4:
            {
                var val = (float)instr.Operand!;
                if (float.IsNaN(val)) stack.Push("std::numeric_limits<float>::quiet_NaN()");
                else if (float.IsPositiveInfinity(val)) stack.Push("std::numeric_limits<float>::infinity()");
                else if (float.IsNegativeInfinity(val)) stack.Push("(-std::numeric_limits<float>::infinity())");
                else
                {
                    var s = val.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    if (!s.Contains('.') && !s.Contains('E') && !s.Contains('e')) s += ".0";
                    stack.Push(s + "f");
                }
                break;
            }
            case Code.Ldc_R8:
            {
                var val = (double)instr.Operand!;
                if (double.IsNaN(val)) stack.Push("std::numeric_limits<double>::quiet_NaN()");
                else if (double.IsPositiveInfinity(val)) stack.Push("std::numeric_limits<double>::infinity()");
                else if (double.IsNegativeInfinity(val)) stack.Push("(-std::numeric_limits<double>::infinity())");
                else
                {
                    var s = val.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    if (!s.Contains('.') && !s.Contains('E') && !s.Contains('e')) s += ".0";
                    stack.Push(s);
                }
                break;
            }

            // ===== Load String =====
            case Code.Ldstr:
                var strVal = (string)instr.Operand!;
                var strId = _module.RegisterStringLiteral(strVal);
                stack.Push(new StackEntry(strId, "cil2cpp::String*"));
                break;

            case Code.Ldnull:
                stack.Push(new StackEntry("nullptr", "void*"));
                break;

            case Code.Ldtoken:
            {
                if (instr.Operand is FieldReference fieldRef)
                {
                    var fieldDef = fieldRef.Resolve();
                    if (fieldDef?.InitialValue is { Length: > 0 })
                    {
                        var initId = _module.RegisterArrayInitData(fieldDef.InitialValue);
                        stack.Push(initId);
                    }
                    else
                    {
                        stack.Push("0 /* ldtoken field */");
                    }
                }
                else if (instr.Operand is TypeReference typeRef)
                {
                    // ldtoken <type> → push pointer to TypeInfo (RuntimeTypeHandle)
                    // Array types (T[]) all share System.Array's TypeInfo in the runtime.
                    // This also handles generic array types (T[] where T is a type param)
                    // which would otherwise produce unresolvable T___TypeInfo references.
                    string typeInfoName;
                    if (typeRef is Mono.Cecil.ArrayType)
                    {
                        typeInfoName = "System_Array";
                    }
                    else
                    {
                        typeInfoName = GetMangledTypeNameForRef(typeRef);
                        // Ensure TypeInfo exists for primitive types used in typeof()
                        if (CppNameMapper.IsPrimitive(typeRef.FullName))
                        {
                            _module.RegisterPrimitiveTypeInfo(typeRef.FullName);
                        }
                    }
                    stack.Push(new StackEntry($"&{typeInfoName}_TypeInfo", "cil2cpp::TypeInfo*"));
                }
                else if (instr.Operand is MethodReference)
                {
                    stack.Push("0 /* ldtoken method */");
                }
                else
                {
                    stack.Push("0 /* ldtoken */");
                }
                break;
            }

            // ===== Load Arguments =====
            case Code.Ldarg_0:
                stack.Push(new StackEntry(GetArgName(method, 0), GetArgType(method, 0)));
                break;
            case Code.Ldarg_1:
                stack.Push(new StackEntry(GetArgName(method, 1), GetArgType(method, 1)));
                break;
            case Code.Ldarg_2:
                stack.Push(new StackEntry(GetArgName(method, 2), GetArgType(method, 2)));
                break;
            case Code.Ldarg_3:
                stack.Push(new StackEntry(GetArgName(method, 3), GetArgType(method, 3)));
                break;
            case Code.Ldarg_S:
            case Code.Ldarg:
                var paramDef = instr.Operand as ParameterDefinition;
                int argIdx = paramDef?.Index ?? 0;
                if (!method.IsStatic) argIdx++;
                stack.Push(new StackEntry(GetArgName(method, argIdx), GetArgType(method, argIdx)));
                break;

            // ===== Store Arguments =====
            case Code.Starg_S:
            case Code.Starg:
                var stArgDef = instr.Operand as ParameterDefinition;
                int stArgIdx = stArgDef?.Index ?? 0;
                if (!method.IsStatic) stArgIdx++;
                var stArgVal = stack.PopExpr();
                // For pointer-type parameters, add explicit cast to handle implicit upcasts
                // (e.g., EqualityComparer<T>* → IEqualityComparer<T>*) and uintptr_t→void*
                // conversions since generated C++ structs don't use C++ inheritance and
                // .NET's IntPtr/UIntPtr are integers in C++ (not pointers).
                {
                    int paramIdx = stArgDef?.Index ?? 0;
                    if (paramIdx >= 0 && paramIdx < method.Parameters.Count)
                    {
                        var param = method.Parameters[paramIdx];
                        if (param.CppTypeName.EndsWith("*"))
                        {
                            stArgVal = $"({param.CppTypeName}){stArgVal}";
                        }
                        else if (param.CppTypeName is "intptr_t" or "uintptr_t")
                        {
                            stArgVal = $"({param.CppTypeName}){stArgVal}";
                        }
                    }
                }
                block.Instructions.Add(new IRAssign
                {
                    Target = GetArgName(method, stArgIdx),
                    Value = stArgVal
                });
                break;

            // ===== Load Locals =====
            case Code.Ldloc_0: stack.Push(new StackEntry(GetLocalName(method, 0), GetLocalType(method, 0))); break;
            case Code.Ldloc_1: stack.Push(new StackEntry(GetLocalName(method, 1), GetLocalType(method, 1))); break;
            case Code.Ldloc_2: stack.Push(new StackEntry(GetLocalName(method, 2), GetLocalType(method, 2))); break;
            case Code.Ldloc_3: stack.Push(new StackEntry(GetLocalName(method, 3), GetLocalType(method, 3))); break;
            case Code.Ldloc_S:
            case Code.Ldloc:
            {
                var locDef = instr.Operand as VariableDefinition;
                int locIdx = locDef?.Index ?? 0;
                stack.Push(new StackEntry(GetLocalName(method, locIdx), GetLocalType(method, locIdx)));
                break;
            }

            // ===== Load Address of Local/Arg =====
            case Code.Ldloca:
            case Code.Ldloca_S:
            {
                var locaVar = instr.Operand as VariableDefinition;
                int locaIdx = locaVar?.Index ?? 0;
                var locaType = GetLocalType(method, locaIdx);
                stack.Push(new StackEntry($"&{GetLocalName(method, locaIdx)}",
                    locaType != null ? locaType + "*" : null));
                break;
            }

            case Code.Ldarga:
            case Code.Ldarga_S:
            {
                var argaParam = instr.Operand as ParameterDefinition;
                int argaIdx = argaParam?.Index ?? 0;
                if (!method.IsStatic) argaIdx++;
                var argaType = GetArgType(method, argaIdx);
                stack.Push(new StackEntry($"&{GetArgName(method, argaIdx)}",
                    argaType != null ? argaType + "*" : null));
                break;
            }

            // ===== Store Locals =====
            case Code.Stloc_0: EmitStoreLocal(block, stack, method, 0); break;
            case Code.Stloc_1: EmitStoreLocal(block, stack, method, 1); break;
            case Code.Stloc_2: EmitStoreLocal(block, stack, method, 2); break;
            case Code.Stloc_3: EmitStoreLocal(block, stack, method, 3); break;
            case Code.Stloc_S:
            case Code.Stloc:
                var stLocDef = instr.Operand as VariableDefinition;
                EmitStoreLocal(block, stack, method, stLocDef?.Index ?? 0);
                break;

            // ===== Arithmetic =====
            case Code.Add:
            case Code.Sub:
            {
                // IL add/sub on typed pointers uses byte offsets, but C++ pointer
                // arithmetic scales by element size. Detect pointer operands and
                // cast through uint8_t* for correct byte-level arithmetic.
                var op = instr.OpCode == Code.Add ? "+" : "-";
                if (TryEmitPointerArithmetic(block, stack, op, method, ref tempCounter))
                    break;
                EmitBinaryOp(block, stack, op, ref tempCounter);
                break;
            }
            case Code.Mul: EmitBinaryOp(block, stack, "*", ref tempCounter); break;
            case Code.Div: EmitBinaryOp(block, stack, "/", ref tempCounter); break;
            case Code.Div_Un: EmitBinaryOp(block, stack, "/", ref tempCounter, isUnsigned: true); break;
            case Code.Rem: EmitBinaryOp(block, stack, "%", ref tempCounter); break;
            case Code.Rem_Un: EmitBinaryOp(block, stack, "%", ref tempCounter, isUnsigned: true); break;
            case Code.And: EmitBinaryOp(block, stack, "&", ref tempCounter); break;
            case Code.Or: EmitBinaryOp(block, stack, "|", ref tempCounter); break;
            case Code.Xor: EmitBinaryOp(block, stack, "^", ref tempCounter); break;
            case Code.Shl: EmitBinaryOp(block, stack, "<<", ref tempCounter); break;
            case Code.Shr: EmitBinaryOp(block, stack, ">>", ref tempCounter); break;
            case Code.Shr_Un: EmitBinaryOp(block, stack, ">>", ref tempCounter, isUnsigned: true); break; // C++ unsigned >> is logical shift

            // ===== Checked Arithmetic =====
            case Code.Add_Ovf:
            case Code.Add_Ovf_Un:
            case Code.Sub_Ovf:
            case Code.Sub_Ovf_Un:
            case Code.Mul_Ovf:
            case Code.Mul_Ovf_Un:
            {
                EmitCheckedBinaryOp(block, stack, instr.OpCode, ref tempCounter);
                break;
            }

            case Code.Conv_Ovf_I:
            case Code.Conv_Ovf_I1:
            case Code.Conv_Ovf_I2:
            case Code.Conv_Ovf_I4:
            case Code.Conv_Ovf_I8:
            case Code.Conv_Ovf_U:
            case Code.Conv_Ovf_U1:
            case Code.Conv_Ovf_U2:
            case Code.Conv_Ovf_U4:
            case Code.Conv_Ovf_U8:
            case Code.Conv_Ovf_I_Un:
            case Code.Conv_Ovf_I1_Un:
            case Code.Conv_Ovf_I2_Un:
            case Code.Conv_Ovf_I4_Un:
            case Code.Conv_Ovf_I8_Un:
            case Code.Conv_Ovf_U_Un:
            case Code.Conv_Ovf_U1_Un:
            case Code.Conv_Ovf_U2_Un:
            case Code.Conv_Ovf_U4_Un:
            case Code.Conv_Ovf_U8_Un:
            {
                var cppType = GetCheckedConvType(instr.OpCode);
                var isUn = IsUnsignedCheckedConv(instr.OpCode);
                var func = isUn ? "cil2cpp::checked_conv_un" : "cil2cpp::checked_conv";
                var val = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = {func}<{cppType}>({val});",
                    ResultVar = tmp,
                    ResultTypeCpp = cppType,
                });
                stack.Push(tmp);
                break;
            }

            case Code.Neg:
            {
                var entry = stack.PopEntry();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRUnaryOp { Op = "-", Operand = entry.Expr, ResultVar = tmp, ResultTypeCpp = entry.CppType });
                stack.Push(new StackEntry(tmp, entry.CppType));
                break;
            }

            case Code.Not:
            {
                var entry = stack.PopEntry();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRUnaryOp { Op = "~", Operand = entry.Expr, ResultVar = tmp, ResultTypeCpp = entry.CppType });
                stack.Push(new StackEntry(tmp, entry.CppType));
                break;
            }

            // ===== Comparison =====
            case Code.Ceq: EmitBinaryOp(block, stack, "==", ref tempCounter, method: method); break;
            case Code.Cgt: EmitBinaryOp(block, stack, ">", ref tempCounter); break;
            case Code.Cgt_Un: EmitBinaryOp(block, stack, ">", ref tempCounter, isUnsigned: true); break;
            case Code.Clt: EmitBinaryOp(block, stack, "<", ref tempCounter); break;
            case Code.Clt_Un: EmitBinaryOp(block, stack, "<", ref tempCounter, isUnsigned: true); break;

            // ===== Branching =====
            case Code.Br:
            case Code.Br_S:
            {
                var target = (Instruction)instr.Operand!;
                // Save filter result before branching to endfilter offset
                if (_inFilterRegion && target.Offset == _endfilterOffset && stack.Count > 0)
                {
                    block.Instructions.Add(new IRAssign
                    {
                        Target = "__filter_result",
                        Value = stack.Peek().Expr
                    });
                }
                // Ternary merge: when an unconditional br targets a merge point and
                // exactly ONE value was pushed since the last conditional branch,
                // create a merge variable to carry the value across both paths.
                // Pattern 1: brtrue T; ldc X; br M; T: push Y; M: use merged value
                // Pattern 2: brtrue T; ceq/comp; br M; T: ldc 1; M: use merged value
                // Type is deferred to the label handler which sees the live-path type.
                {
                    int mergeCount = lastCondBranchStackDepth.HasValue ? stack.Count - lastCondBranchStackDepth.Value : 0;
                    if (mergeCount == 1 && !brTernaryMerges.ContainsKey(target.Offset))
                    {
                        var entry = stack.Peek();
                        // Create merge variable for single values pushed since the last
                        // conditional branch. Skip complex expressions (casts, member access)
                        // and value-type locals (IRDeclareLocal = SIMD/struct stubs) which
                        // can't be pre-declared as cross-scope pointer variables.
                        if (entry.Expr != null
                            && !entry.Expr.Contains("(") && !entry.Expr.Contains("->"))
                        {
                            // Phase 1: Create the merge variable and assign the br-path value.
                            // The type may be refined at the label handler (phase 2) once
                            // the live-path type is also known.
                            string? brType = !string.IsNullOrEmpty(entry.CppType) ? entry.CppType : null;
                            if (brType == null && entry.Expr.StartsWith("__t"))
                                brType = InferTempVarType(entry.Expr, block);

                            stack.Pop();
                            var mergeVar = $"__t{tempCounter++}";
                            block.Instructions.Add(new IRAssign { Target = mergeVar, Value = entry.Expr });
                            // Set a preliminary type; the label handler may upgrade it.
                            // Use intptr_t as a safe default: it's the same width as pointers
                            // on both 32/64-bit and handles integer/pointer ternaries.
                            method.TempVarTypes[mergeVar] = brType ?? "intptr_t";
                            var mergeEntry = new StackEntry(mergeVar, brType);
                            brTernaryMerges[target.Offset] = new[] { mergeEntry };
                            stack.Push(mergeEntry);
                        }
                    }
                }
                block.Instructions.Add(new IRBranch { TargetLabel = $"IL_{target.Offset:X4}" });
                break;
            }

            case Code.Brtrue:
            case Code.Brtrue_S:
            {
                var cond = stack.PopExpr();
                var target = (Instruction)instr.Operand!;
                // Save stack top as merge variable for branch target (dup+brtrue pattern)
                // Only save if the stack top is a valid C++ lvalue (variable name, not a literal)
                if (stack.Count > 0 && !branchMergeVars.ContainsKey(target.Offset)
                    && IsValidMergeVariable(stack.Peek().Expr))
                    branchMergeVars[target.Offset] = stack.Peek().Expr;
                // Save full stack snapshot for branch target (ternary pattern support).
                // After popping the condition, the remaining stack is what the target sees.
                if (stack.Count > 0 && !branchTargetStacks.ContainsKey(target.Offset))
                    branchTargetStacks[target.Offset] = stack.Reverse().ToArray();
                lastCondBranchStackDepth = stack.Count;
                block.Instructions.Add(new IRConditionalBranch
                {
                    Condition = cond,
                    TrueLabel = $"IL_{target.Offset:X4}"
                });
                break;
            }

            case Code.Brfalse:
            case Code.Brfalse_S:
            {
                var cond = stack.PopExpr();
                var target = (Instruction)instr.Operand!;
                // Save stack top as merge variable for branch target (dup+brfalse pattern)
                // Only save if the stack top is a valid C++ lvalue (variable name, not a literal)
                if (stack.Count > 0 && !branchMergeVars.ContainsKey(target.Offset)
                    && IsValidMergeVariable(stack.Peek().Expr))
                    branchMergeVars[target.Offset] = stack.Peek().Expr;
                // Save full stack snapshot for branch target (ternary pattern support)
                if (stack.Count > 0 && !branchTargetStacks.ContainsKey(target.Offset))
                    branchTargetStacks[target.Offset] = stack.Reverse().ToArray();
                lastCondBranchStackDepth = stack.Count;
                block.Instructions.Add(new IRConditionalBranch
                {
                    Condition = $"!({cond})",
                    TrueLabel = $"IL_{target.Offset:X4}"
                });
                break;
            }

            case Code.Beq:
            case Code.Beq_S:
                EmitComparisonBranch(block, stack, "==", instr, branchTargetStacks: branchTargetStacks, method: method);
                break;
            case Code.Bne_Un:
            case Code.Bne_Un_S:
                EmitComparisonBranch(block, stack, "!=", instr, isUnsigned: true, branchTargetStacks: branchTargetStacks, method: method);
                break;
            case Code.Bge:
            case Code.Bge_S:
                EmitComparisonBranch(block, stack, ">=", instr, branchTargetStacks: branchTargetStacks);
                break;
            case Code.Bgt:
            case Code.Bgt_S:
                EmitComparisonBranch(block, stack, ">", instr, branchTargetStacks: branchTargetStacks);
                break;
            case Code.Ble:
            case Code.Ble_S:
                EmitComparisonBranch(block, stack, "<=", instr, branchTargetStacks: branchTargetStacks);
                break;
            case Code.Blt:
            case Code.Blt_S:
                EmitComparisonBranch(block, stack, "<", instr, branchTargetStacks: branchTargetStacks);
                break;
            // Unsigned branches (ECMA-335 III.3.6-3.12): treat operands as unsigned
            case Code.Bge_Un:
            case Code.Bge_Un_S:
                EmitComparisonBranch(block, stack, ">=", instr, isUnsigned: true, branchTargetStacks: branchTargetStacks);
                break;
            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
                EmitComparisonBranch(block, stack, ">", instr, isUnsigned: true, branchTargetStacks: branchTargetStacks);
                break;
            case Code.Ble_Un:
            case Code.Ble_Un_S:
                EmitComparisonBranch(block, stack, "<=", instr, isUnsigned: true, branchTargetStacks: branchTargetStacks);
                break;
            case Code.Blt_Un:
            case Code.Blt_Un_S:
                EmitComparisonBranch(block, stack, "<", instr, isUnsigned: true, branchTargetStacks: branchTargetStacks);
                break;

            // ===== Switch =====
            case Code.Switch:
            {
                var value = stack.PopExpr();
                var targets = (Instruction[])instr.Operand!;
                var sw = new IRSwitch { ValueExpr = value };
                foreach (var t in targets)
                    sw.CaseLabels.Add($"IL_{t.Offset:X4}");
                block.Instructions.Add(sw);
                break;
            }

            // ===== Field Access =====
            case Code.Ldfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var objEntry = stack.PopEntry();
                var obj = objEntry.Expr.Length > 0 ? objEntry.Expr : "__this";
                // volatile. prefix: fence before load
                if (_pendingVolatile)
                {
                    block.Instructions.Add(new IRRawCpp { Code = "std::atomic_thread_fence(std::memory_order_seq_cst);" });
                    _pendingVolatile = false;
                }

                // Scalar alias interception: types like IntPtr/UIntPtr/Boolean/Char are aliased
                // to C++ scalars (intptr_t, bool, char16_t). IL accesses m_value field but C++
                // scalars have no fields — emit direct value read instead.
                if (fieldRef.Name == "m_value" && CppNameMapper.IsPrimitive(fieldRef.DeclaringType.FullName))
                {
                    var tmp = $"__t{tempCounter++}";
                    var lfFieldTypeName = ResolveFieldTypeRef(fieldRef);
                    var lfFieldTypeCpp = CppNameMapper.GetCppTypeForDecl(lfFieldTypeName);
                    string readExpr;
                    if (obj.StartsWith("&"))
                        readExpr = obj[1..]; // &loc_N → loc_N (value of the local)
                    else if (obj == "__this" || (objEntry.CppType?.EndsWith("*") == true))
                        readExpr = $"*{obj}"; // pointer → dereference
                    else
                        readExpr = obj; // value → use directly
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{tmp} = {readExpr};",
                        ResultVar = tmp,
                        ResultTypeCpp = lfFieldTypeCpp,
                    });
                    stack.Push(new StackEntry(tmp, lfFieldTypeCpp));
                    break;
                }

                // Determine if the object is a value (struct) or pointer:
                // - starts with '&' → pointer to value type → use ->
                // - declaring type is value type and obj is a local/temp → use .
                // Pass stack CppType to detect pointer values not tracked in TempVarTypes
                var isValueAccess = IsValueTypeAccess(fieldRef.DeclaringType, obj, method, objEntry.CppType);
                // Cast to declaring type when the stack value might be a base type (Object*)
                // CIL ldfld specifies the declaring type; C++ needs the correct type for field access
                string? castToType = null;
                if (!isValueAccess && !fieldRef.DeclaringType.IsValueType && obj != "__this")
                {
                    var declTypeName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                    if (declTypeName != "cil2cpp::Object" && declTypeName != "cil2cpp::String")
                        castToType = declTypeName;
                }
                var tmp2 = $"__t{tempCounter++}";
                var lfFieldTypeName2 = ResolveFieldTypeRef(fieldRef);
                var lfFieldTypeCpp2 = CppNameMapper.GetCppTypeForDecl(lfFieldTypeName2);
                block.Instructions.Add(new IRFieldAccess
                {
                    ObjectExpr = obj,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    ResultVar = tmp2,
                    IsValueAccess = isValueAccess,
                    CastToType = castToType,
                    ResultTypeCpp = lfFieldTypeCpp2,
                });
                stack.Push(new StackEntry(tmp2, lfFieldTypeCpp2));
                break;
            }

            case Code.Stfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var val = stack.PopExpr();
                var objEntry = stack.PopEntry();
                var obj = objEntry.Expr.Length > 0 ? objEntry.Expr : "__this";
                bool isVolatileStore = _pendingVolatile;
                _pendingVolatile = false;

                // Scalar alias interception: stfld m_value on primitive alias → direct write
                if (fieldRef.Name == "m_value" && CppNameMapper.IsPrimitive(fieldRef.DeclaringType.FullName))
                {
                    string writeExpr;
                    if (obj.StartsWith("&"))
                        writeExpr = $"{obj[1..]} = {val};"; // &loc_N → loc_N = val
                    else if (obj == "__this" || (objEntry.CppType?.EndsWith("*") == true))
                        writeExpr = $"*{obj} = {val};"; // pointer → *ptr = val
                    else
                        writeExpr = $"{obj} = {val};"; // value → direct assign
                    block.Instructions.Add(new IRRawCpp { Code = writeExpr });
                    if (isVolatileStore)
                        block.Instructions.Add(new IRRawCpp { Code = "std::atomic_thread_fence(std::memory_order_seq_cst);" });
                    break;
                }

                // Cast value for reference type fields to handle derived→base type mismatch
                // (flat struct model: no C++ inheritance, so all pointer casts must be explicit)
                var fieldTypeName = ResolveFieldTypeRef(fieldRef);
                var fieldTypeCpp = CppNameMapper.GetCppTypeForDecl(fieldTypeName);
                if (fieldTypeCpp.EndsWith("*") && !CppNameMapper.IsValueType(fieldTypeName)
                    && fieldTypeCpp != "void*" && val != "nullptr" && val != "0")
                {
                    // Use (void*) intermediate to handle flat struct model (no C++ inheritance)
                    val = $"({fieldTypeCpp})(void*){val}";
                }
                // Pass stack CppType to detect pointer values not tracked in TempVarTypes
                var isValueAccess = IsValueTypeAccess(fieldRef.DeclaringType, obj, method, objEntry.CppType);
                string? stCastToType = null;
                if (!isValueAccess && !fieldRef.DeclaringType.IsValueType && obj != "__this")
                {
                    var stDeclTypeName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                    if (stDeclTypeName != "cil2cpp::Object" && stDeclTypeName != "cil2cpp::String")
                        stCastToType = stDeclTypeName;
                }
                block.Instructions.Add(new IRFieldAccess
                {
                    ObjectExpr = obj,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    IsStore = true,
                    StoreValue = val,
                    IsValueAccess = isValueAccess,
                    CastToType = stCastToType,
                });
                // volatile. prefix: fence after store
                if (isVolatileStore)
                {
                    block.Instructions.Add(new IRRawCpp { Code = "std::atomic_thread_fence(std::memory_order_seq_cst);" });
                }
                break;
            }

            case Code.Ldsfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;

                // Intercept EmptyArray<T>.Value — nested in RuntimeProvidedType Array
                if (fieldRef.Name == "Value" && fieldRef.DeclaringType is GenericInstanceType emptyGit
                    && emptyGit.ElementType.FullName == "System.Array/EmptyArray`1")
                {
                    var elemTypeArg = emptyGit.GenericArguments[0];
                    var resolvedElem = ResolveGenericTypeRef(elemTypeArg, emptyGit);
                    var elemCppType = CppNameMapper.MangleTypeNameClean(resolvedElem);
                    if (CppNameMapper.IsPrimitive(resolvedElem))
                        _module.RegisterPrimitiveTypeInfo(resolvedElem);
                    var tmp2 = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp2} = cil2cpp::array_create(&{elemCppType}_TypeInfo, 0);",
                        ResultVar = tmp2,
                        ResultTypeCpp = "cil2cpp::Array*",
                    });
                    stack.Push(tmp2);
                    break;
                }

                var typeCppName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                var fieldCacheKey = ResolveCacheKey(fieldRef.DeclaringType);
                EmitCctorGuardIfNeeded(block, fieldCacheKey, typeCppName);
                // volatile. prefix: fence before load
                if (_pendingVolatile)
                {
                    block.Instructions.Add(new IRRawCpp { Code = "std::atomic_thread_fence(std::memory_order_seq_cst);" });
                    _pendingVolatile = false;
                }
                var tmp = $"__t{tempCounter++}";
                var sfTypeName = ResolveFieldTypeRef(fieldRef);
                var sfTypeCpp = CppNameMapper.GetCppTypeForDecl(sfTypeName);
                block.Instructions.Add(new IRStaticFieldAccess
                {
                    TypeCppName = typeCppName,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    ResultVar = tmp,
                    ResultTypeCpp = sfTypeCpp,
                });
                stack.Push(new StackEntry(tmp, sfTypeCpp));
                break;
            }

            case Code.Stsfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var typeCppName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                var fieldCacheKey = ResolveCacheKey(fieldRef.DeclaringType);
                EmitCctorGuardIfNeeded(block, fieldCacheKey, typeCppName);
                var val = stack.PopExpr();
                bool isVolatileStore = _pendingVolatile;
                _pendingVolatile = false;
                // Cast value for reference type static fields (derived→base in flat struct model)
                var sfieldTypeName = ResolveFieldTypeRef(fieldRef);
                var sfieldTypeCpp = CppNameMapper.GetCppTypeForDecl(sfieldTypeName);
                if (sfieldTypeCpp.EndsWith("*") && !CppNameMapper.IsValueType(sfieldTypeName)
                    && sfieldTypeCpp != "void*" && val != "nullptr" && val != "0")
                {
                    val = $"({sfieldTypeCpp})(void*){val}";
                }
                block.Instructions.Add(new IRStaticFieldAccess
                {
                    TypeCppName = typeCppName,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    IsStore = true,
                    StoreValue = val,
                });
                // volatile. prefix: fence after store
                if (isVolatileStore)
                {
                    block.Instructions.Add(new IRRawCpp { Code = "std::atomic_thread_fence(std::memory_order_seq_cst);" });
                }
                break;
            }

            case Code.Ldflda:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var obj = stack.PopExprOr("__this");

                // Scalar alias interception: ldflda m_value on primitive alias → address of the value itself
                if (fieldRef.Name == "m_value" && CppNameMapper.IsPrimitive(fieldRef.DeclaringType.FullName))
                {
                    var tmp = $"__t{tempCounter++}";
                    var fldaTypeName = ResolveFieldTypeRef(fieldRef);
                    var fldaTypeCpp = CppNameMapper.GetCppTypeForDecl(fldaTypeName);
                    var fldaPtrType = fldaTypeCpp.EndsWith("*") ? fldaTypeCpp : fldaTypeCpp + "*";
                    string addrExpr;
                    if (obj.StartsWith("&"))
                        addrExpr = obj; // already an address
                    else if (obj == "__this")
                        addrExpr = "__this"; // already a pointer to the scalar
                    else
                        addrExpr = $"&{obj}"; // take address of value
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{tmp} = ({fldaPtrType}){addrExpr};",
                        ResultVar = tmp,
                        ResultTypeCpp = fldaPtrType,
                    });
                    stack.Push(new StackEntry(tmp, fldaPtrType));
                    break;
                }

                var tmp3 = $"__t{tempCounter++}";
                var fieldName = CppNameMapper.MangleFieldName(fieldRef.Name);
                // When obj is an address (e.g. &loc_0 from ldloca), use . accessor
                // to avoid &&loc_0->field which MSVC tokenizes as rvalue-ref &&
                string expr;
                if (obj.StartsWith("&") && !obj.StartsWith("&("))
                    expr = $"&({obj[1..]}.{fieldName})";
                else
                    expr = $"&{obj}->{fieldName}";
                var fldaTypeName2 = ResolveFieldTypeRef(fieldRef);
                var fldaTypeCpp2 = CppNameMapper.GetCppTypeForDecl(fldaTypeName2);
                // ldflda pushes a pointer to the field — always add one level of indirection
                var fldaPtrType2 = fldaTypeCpp2 + "*";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp3} = ({fldaPtrType2}){expr};",
                    ResultVar = tmp3,
                    ResultTypeCpp = fldaPtrType2,
                });
                stack.Push(new StackEntry(tmp3, fldaPtrType2));
                break;
            }

            case Code.Ldsflda:
            {
                var fieldRef = (FieldReference)instr.Operand!;

                // PrivateImplementationDetails fields with RVA data: their InitialValue
                // contains the actual byte blob from the PE file. Use RegisterArrayInitData
                // to create a properly initialized static const array instead of referencing
                // the zero-initialized statics struct.
                if (fieldRef.DeclaringType.Name.Contains("PrivateImplementationDetails"))
                {
                    var fieldDef = fieldRef.Resolve();
                    if (fieldDef?.InitialValue is { Length: > 0 })
                    {
                        var initId = _module.RegisterArrayInitData(fieldDef.InitialValue);
                        stack.Push($"(uint8_t*){initId}");
                        break;
                    }
                }

                var typeCppName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                var fieldCacheKey = ResolveCacheKey(fieldRef.DeclaringType);
                EmitCctorGuardIfNeeded(block, fieldCacheKey, typeCppName);
                var tmp = $"__t{tempCounter++}";
                var sfaTypeName = ResolveFieldTypeRef(fieldRef);
                var sfaTypeCpp = CppNameMapper.GetCppTypeForDecl(sfaTypeName);
                var sfaPtrType = sfaTypeCpp + "*";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = &{typeCppName}_statics.{CppNameMapper.MangleFieldName(fieldRef.Name)};",
                    ResultVar = tmp,
                    ResultTypeCpp = sfaPtrType,
                });
                stack.Push(new StackEntry(tmp, sfaPtrType));
                break;
            }

            // ===== Indirect Load/Store (pointer dereference) =====
            case Code.Ldobj:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var addr = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                if (CppNameMapper.IsValueType(resolvedName))
                {
                    var cppType = CppNameMapper.GetCppTypeName(resolvedName);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = *({cppType}*){addr};",
                        ResultVar = tmp,
                        ResultTypeCpp = cppType,
                    });
                }
                else
                {
                    var cppRefType = CppNameMapper.GetCppTypeForDecl(resolvedName);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = *{addr};",
                        ResultVar = tmp,
                        ResultTypeCpp = cppRefType,
                    });
                }
                stack.Push(tmp);
                break;
            }

            case Code.Stobj:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var val = stack.PopExpr();
                var addr = stack.PopExprOr("nullptr");
                if (CppNameMapper.IsValueType(resolvedName))
                {
                    var cppType = CppNameMapper.GetCppTypeName(resolvedName);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"*({cppType}*){addr} = ({cppType}){val};"
                    });
                }
                else
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"*{addr} = {val};"
                    });
                }
                break;
            }

            case Code.Ldind_I1:
            case Code.Ldind_I2:
            case Code.Ldind_I4:
            case Code.Ldind_I8:
            case Code.Ldind_U1:
            case Code.Ldind_U2:
            case Code.Ldind_U4:
            case Code.Ldind_R4:
            case Code.Ldind_R8:
            case Code.Ldind_I:
            {
                var cppType = GetIndirectType(instr.OpCode);
                var addr = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = *({cppType}*){addr};",
                    ResultVar = tmp,
                    ResultTypeCpp = cppType,
                });
                stack.Push(tmp);
                break;
            }

            case Code.Ldind_Ref:
            {
                var addrEntry = stack.PopEntry();
                var addr = addrEntry.Expr;
                var tmp = $"__t{tempCounter++}";
                // Use StackEntry type to determine the actual element type.
                // For byref params like "out String" (CppType = "cil2cpp::String**"),
                // the dereference should produce "cil2cpp::String*", not "Object*".
                var derefType = "cil2cpp::Object*";
                var castType = "cil2cpp::Object**";
                if (addrEntry.CppType != null && addrEntry.CppType.EndsWith("**"))
                {
                    castType = addrEntry.CppType; // T**
                    derefType = addrEntry.CppType[..^1]; // T** → T*
                }
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = *({castType}){addr};",
                    ResultVar = tmp,
                    ResultTypeCpp = derefType,
                });
                stack.Push(new StackEntry(tmp, derefType));
                break;
            }

            case Code.Stind_I1:
            case Code.Stind_I2:
            case Code.Stind_I4:
            case Code.Stind_I8:
            case Code.Stind_R4:
            case Code.Stind_R8:
            case Code.Stind_I:
            {
                var cppType = GetIndirectType(instr.OpCode);
                var val = stack.PopExpr();
                var addr = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"*({cppType}*){addr} = ({cppType}){val};"
                });
                break;
            }

            case Code.Stind_Ref:
            {
                var val = stack.PopExprOr("nullptr");
                var addr = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"*(cil2cpp::Object**){addr} = (cil2cpp::Object*){val};"
                });
                break;
            }

            // ===== Method Calls =====
            case Code.Call:
            case Code.Callvirt:
            {
                var methodRef = (MethodReference)instr.Operand!;
                var constrainedType = _constrainedType;
                _constrainedType = null; // Consume the constrained prefix
                EmitMethodCall(block, stack, methodRef, instr.OpCode == Code.Callvirt, ref tempCounter, constrainedType);
                break;
            }

            // ===== Object Creation =====
            case Code.Newobj:
            {
                var ctorRef = (MethodReference)instr.Operand!;
                EmitNewObj(block, stack, ctorRef, ref tempCounter);
                break;
            }

            // ===== Return =====
            case Code.Ret:
            {
                if (method.ReturnTypeCpp != "void" && stack.Count > 0)
                {
                    var retVal = stack.Pop().Expr;
                    // Cast return value to method's declared return type for interface/generic returns
                    if (method.ReturnTypeCpp.EndsWith("*"))
                        retVal = $"({method.ReturnTypeCpp}){retVal}";
                    // intptr_t/uintptr_t returns: cast from pointer types
                    // In .NET, IntPtr/UIntPtr and pointers are interchangeable.
                    // In C++, MSVC rejects implicit void*→intptr_t.
                    else if (method.ReturnTypeCpp is "intptr_t" or "uintptr_t"
                             && !retVal.StartsWith($"({method.ReturnTypeCpp})")
                             && retVal != "0" && retVal != "nullptr")
                        retVal = $"({method.ReturnTypeCpp}){retVal}";
                    block.Instructions.Add(new IRReturn { Value = retVal });
                }
                else
                {
                    block.Instructions.Add(new IRReturn());
                }
                break;
            }

            // ===== Conversions =====
            case Code.Conv_I1:  EmitConversion(block, stack, "int8_t", ref tempCounter); break;
            case Code.Conv_I2:  EmitConversion(block, stack, "int16_t", ref tempCounter); break;
            case Code.Conv_I4:  EmitConversion(block, stack, "int32_t", ref tempCounter); break;
            case Code.Conv_I8:  EmitConversion(block, stack, "int64_t", ref tempCounter); break;
            case Code.Conv_I:
            {
                var convIEntry = stack.PeekEntry();
                // Pointer/address-of values are already native-sized — conv.i is a no-op.
                // Preserving the typed pointer avoids void*→T* assignment mismatches
                // in cross-scope pre-declared variables (AddAutoDeclarations).
                if (convIEntry.IsAddressOf || convIEntry.IsPointer) break;
                EmitConversion(block, stack, "intptr_t", ref tempCounter);
                break;
            }
            case Code.Conv_U1:  EmitConversion(block, stack, "uint8_t", ref tempCounter); break;
            case Code.Conv_U2:  EmitConversion(block, stack, "uint16_t", ref tempCounter); break;
            case Code.Conv_U4:  EmitConversion(block, stack, "uint32_t", ref tempCounter); break;
            case Code.Conv_U8:  EmitConversion(block, stack, "uint64_t", ref tempCounter); break;
            case Code.Conv_U:
            {
                var convUEntry = stack.PeekEntry();
                // If converting literal 0 → preserve as 0 (valid null pointer constant in C++)
                if (convUEntry.Expr == "0") break;
                // Pointer/address-of values are already native-sized — conv.u is a no-op.
                // Preserving the typed pointer avoids void*→T* assignment mismatches
                // in cross-scope pre-declared variables (AddAutoDeclarations).
                if (convUEntry.IsAddressOf || convUEntry.IsPointer) break;
                // Local variables (loc_N) that are pointer types — conv.u is a no-op.
                // CppType may be null if not tracked, but we can look up from method locals.
                if (convUEntry.Expr.StartsWith("loc_"))
                {
                    if (int.TryParse(convUEntry.Expr.AsSpan(4), out var locIdx)
                        && locIdx >= 0 && locIdx < method.Locals.Count
                        && method.Locals[locIdx].CppTypeName.EndsWith("*"))
                        break;
                }
                // Temp variables (__tN) that were result of a pointer-returning operation
                if (convUEntry.Expr.StartsWith("__t") && convUEntry.CppType == null)
                {
                    // Look up type from TempVarTypes if available
                    if (method.TempVarTypes.TryGetValue(convUEntry.Expr, out var tempType)
                        && tempType.EndsWith("*"))
                        break;
                }
                EmitConversion(block, stack, "uintptr_t", ref tempCounter);
                break;
            }
            case Code.Conv_R4:  EmitConversion(block, stack, "float", ref tempCounter); break;
            case Code.Conv_R8:  EmitConversion(block, stack, "double", ref tempCounter); break;
            case Code.Conv_R_Un:
            {
                // ECMA-335 III.3.19: convert unsigned integer to float.
                // Treat the value as unsigned at its natural width, then convert to double.
                // Using to_unsigned() preserves the width (int32→uint32, int64→uint64)
                // instead of sign-extending to uint64_t which would give wrong results
                // for 32-bit values (e.g., int32(-1) → uint32(4294967295), not uint64(18446...)).
                var val = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRConversion
                {
                    SourceExpr = $"static_cast<double>(cil2cpp::to_unsigned({val}))",
                    TargetType = "double",
                    ResultVar = tmp
                });
                stack.Push(tmp);
                break;
            }

            // ===== Stack Operations =====
            case Code.Dup:
            {
                if (stack.Count > 0)
                {
                    var entry = stack.PeekEntry();
                    // When dup'ing a local variable reference, save to a temp first.
                    // This decouples the duplicate from the original local so that a
                    // subsequent stloc.N (post-increment pattern like a[i++]) uses the
                    // OLD value, not the updated one. Without this, both copies reference
                    // "loc_N" by name and stloc.N silently invalidates the stack entry.
                    if (entry.Expr.StartsWith("loc_") && !entry.Expr.Contains(' '))
                    {
                        var tmp = $"__t{tempCounter++}";
                        block.Instructions.Add(new IRAssign { Target = tmp, Value = entry.Expr });
                        if (entry.CppType != null)
                            method.TempVarTypes[tmp] = entry.CppType;
                        // Replace the bottom copy (original ldloc) with the temp;
                        // push the original name on top (consumed by subsequent ops).
                        stack.Pop();
                        stack.Push(new StackEntry(tmp, entry.CppType));
                        stack.Push(entry);
                    }
                    else
                    {
                        stack.Push(entry);
                    }
                }
                break;
            }

            case Code.Pop:
            {
                if (stack.Count > 0) stack.Pop();
                break;
            }

            case Code.Nop:
            case Code.Tail:         // tail. prefix: tail call hint, not required for correctness
            case Code.Readonly:     // readonly. prefix: ldelema advisory, no semantic effect
                break;
            case Code.Constrained:  // constrained. prefix: next callvirt uses constrained dispatch
                _constrainedType = (TypeReference)instr.Operand!;
                break;
            case Code.Volatile:     // volatile. prefix: emit memory fence on next field access
                _pendingVolatile = true;
                break;
            case Code.Unaligned:    // unaligned. prefix: alignment hint
                break;

            // ===== Array Operations =====
            case Code.Newarr:
            {
                var elemType = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(elemType);
                var length = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";
                // Use MangleTypeNameClean for TypeInfo references — MangleTypeName adds trailing '_'
                // from '>' for generic instances, but TypeInfo declarations use MangleGenericInstanceTypeName.
                var elemCppType = CppNameMapper.MangleTypeNameClean(resolvedName);
                // Ensure TypeInfo exists for primitive element types
                if (CppNameMapper.IsPrimitive(resolvedName))
                    _module.RegisterPrimitiveTypeInfo(resolvedName);
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::array_create(&{elemCppType}_TypeInfo, {length});",
                    ResultVar = tmp,
                    ResultTypeCpp = "cil2cpp::Array*",
                });
                stack.Push(new StackEntry(tmp, "cil2cpp::Array*"));
                break;
            }

            case Code.Ldlen:
            {
                // TODO: ECMA-335 III.4.18 specifies ldlen pushes native unsigned int (uintptr_t),
                // but .NET arrays are limited to Int32.MaxValue elements. Using int32_t for compatibility.
                var arr = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::array_length({arr});",
                    ResultVar = tmp,
                    ResultTypeCpp = "int32_t",
                });
                stack.Push(tmp);
                break;
            }

            // ===== Array Element Access =====
            case Code.Ldelem_I1: case Code.Ldelem_I2: case Code.Ldelem_I4: case Code.Ldelem_I8:
            case Code.Ldelem_U1: case Code.Ldelem_U2: case Code.Ldelem_U4:
            case Code.Ldelem_R4: case Code.Ldelem_R8: case Code.Ldelem_Ref: case Code.Ldelem_I:
            {
                var index = stack.PopExpr();
                var arr = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRArrayAccess
                {
                    ArrayExpr = arr, IndexExpr = index,
                    ElementType = GetArrayElementType(instr.OpCode), ResultVar = tmp
                });
                stack.Push(tmp);
                break;
            }

            case Code.Ldelem_Any:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var elemType = CppNameMapper.GetCppTypeName(ResolveTypeRefOperand(typeRef));
                // Reference types are stored as Object* pointers in arrays.
                // Use Object* as template arg so array_get returns a pointer, not a struct value.
                if (!IsResolvedValueType(typeRef))
                    elemType = "cil2cpp::Object*";
                var index = stack.PopExpr();
                var arr = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRArrayAccess
                {
                    ArrayExpr = arr, IndexExpr = index,
                    ElementType = elemType, ResultVar = tmp
                });
                stack.Push(new StackEntry(tmp, elemType));
                break;
            }

            case Code.Stelem_I1: case Code.Stelem_I2: case Code.Stelem_I4: case Code.Stelem_I8:
            case Code.Stelem_R4: case Code.Stelem_R8: case Code.Stelem_I:
            {
                var val = stack.PopExpr();
                var index = stack.PopExpr();
                var arr = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRArrayAccess
                {
                    ArrayExpr = arr, IndexExpr = index,
                    ElementType = GetArrayElementType(instr.OpCode),
                    IsStore = true, StoreValue = val
                });
                break;
            }

            case Code.Stelem_Ref:
            {
                // stelem.ref stores a reference in an Object[] array.
                // Cast value to cil2cpp::Object* since flat C++ structs don't inherit from Object.
                var val = stack.PopExpr();
                var index = stack.PopExpr();
                var arr = stack.PopExprOr("nullptr");
                if (val != "nullptr" && val != "0")
                    val = $"(cil2cpp::Object*){val}";
                block.Instructions.Add(new IRArrayAccess
                {
                    ArrayExpr = arr, IndexExpr = index,
                    ElementType = "cil2cpp::Object*",
                    IsStore = true, StoreValue = val
                });
                break;
            }

            case Code.Stelem_Any:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var elemType = CppNameMapper.GetCppTypeName(ResolveTypeRefOperand(typeRef));
                var val = stack.PopExpr();
                var index = stack.PopExpr();
                var arr = stack.PopExprOr("nullptr");
                // Reference types are stored as Object* pointers in arrays.
                // Cast value to Object* since flat C++ structs don't inherit from Object.
                if (!IsResolvedValueType(typeRef))
                {
                    elemType = "cil2cpp::Object*";
                    if (val != "nullptr" && val != "0")
                        val = $"(cil2cpp::Object*)(void*){val}";
                }
                block.Instructions.Add(new IRArrayAccess
                {
                    ArrayExpr = arr, IndexExpr = index,
                    ElementType = elemType,
                    IsStore = true, StoreValue = val
                });
                break;
            }

            case Code.Ldelema:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var elemType = CppNameMapper.GetCppTypeName(ResolveTypeRefOperand(typeRef));
                var index = stack.PopExpr();
                var arr = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                var ptrType = elemType + "*";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({ptrType})cil2cpp::array_get_element_ptr({arr}, {index});",
                    ResultVar = tmp,
                    ResultTypeCpp = ptrType,
                });
                stack.Push(new StackEntry(tmp, ptrType));
                break;
            }

            // ===== Type Operations =====
            case Code.Castclass:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                // Array types always use cil2cpp::Array* regardless of element type
                var castIsArray = typeRef is ArrayType;
                var castTargetType = castIsArray
                    ? "cil2cpp::Array" : GetMangledTypeNameForRef(typeRef);
                var castResultType = castTargetType + "*";
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = castResultType,
                    ResultVar = tmp,
                    IsSafe = false,
                    TypeInfoCppName = castIsArray ? "System_Array" : null
                });
                stack.Push(new StackEntry(tmp, castResultType));
                break;
            }

            case Code.Isinst:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";
                var isArray = typeRef is ArrayType;
                var isinstTargetType = isArray
                    ? "cil2cpp::Array" : GetMangledTypeNameForRef(typeRef);
                var isinstResultType = isinstTargetType + "*";
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = isinstResultType,
                    ResultVar = tmp,
                    IsSafe = true,
                    TypeInfoCppName = isArray ? "System_Array" : null
                });
                stack.Push(new StackEntry(tmp, isinstResultType));
                break;
            }

            // ===== Exception Handling =====
            case Code.Throw:
            {
                var ex = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRThrow { ExceptionExpr = ex });
                break;
            }

            case Code.Rethrow:
            {
                block.Instructions.Add(new IRRethrow());
                break;
            }

            case Code.Leave:
            case Code.Leave_S:
            {
                var target = (Instruction)instr.Operand!;
                stack.Clear(); // leave clears the evaluation stack

                // Check if this leave crosses a try-finally boundary.
                // If so, suppress the goto — execution falls through to the finally block naturally.
                bool crossesFinally = tryFinallyRegions.Any(r =>
                    instr.Offset >= r.TryStart && instr.Offset < r.TryEnd && target.Offset >= r.TryEnd);

                if (!crossesFinally)
                    block.Instructions.Add(new IRBranch { TargetLabel = $"IL_{target.Offset:X4}" });
                break;
            }

            case Code.Endfinally:
                // Handled by macros, no-op in generated code
                break;

            case Code.Endfilter:
            {
                // ECMA-335 III.3.34: pops result (0 = reject, 1 = accept)
                // The result was already saved to __filter_result before scope boundaries.
                if (stack.Count > 0) stack.Pop();
                block.Instructions.Add(new IREndFilter());
                _inFilterRegion = false;
                _endfilterOffset = -1;
                break;
            }

            // ===== Value Type Operations =====
            case Code.Initobj:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var addr = stack.PopExprOr("nullptr");
                // Use GetCppTypeName (not MangleTypeName) so primitives map to C++ types
                // e.g. System.SByte → int8_t (not System_SByte which doesn't exist as a C++ type)
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var typeCpp = CppNameMapper.GetCppTypeName(resolvedName);

                // ECMA-335 III.4.12: initobj behavior depends on type:
                // - Value types: zero the memory at addr (sizeof the struct)
                // - Reference types: set the location to null (it's a pointer)
                // Use Cecil resolution + CppNameMapper as fallback (same pattern as Box handler)
                bool isValueType;
                try
                {
                    var resolved = typeRef.Resolve();
                    if (resolved != null)
                        isValueType = resolved.IsValueType;
                    else if (_typeCache.TryGetValue(resolvedName, out var cachedType))
                        isValueType = cachedType.IsValueType;
                    else
                        isValueType = CppNameMapper.IsValueType(resolvedName);
                }
                catch
                {
                    if (_typeCache.TryGetValue(resolvedName, out var cachedType))
                        isValueType = cachedType.IsValueType;
                    else
                        isValueType = CppNameMapper.IsValueType(resolvedName);
                }

                // Strip pointer suffix — sizeof needs the base type
                if (typeCpp.EndsWith("*")) typeCpp = typeCpp.TrimEnd('*');

                block.Instructions.Add(new IRInitObj
                {
                    AddressExpr = addr,
                    TypeCppName = typeCpp,
                    IsReferenceType = !isValueType
                });
                break;
            }

            case Code.Box:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var val = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";

                if (IsNullableType(typeRef))
                {
                    // ECMA-335 III.4.1: box Nullable<T> →
                    //   if !hasValue → null; else → box the inner T value
                    var git = (GenericInstanceType)typeRef;
                    var innerTypeName = ResolveTypeRefOperand(git.GenericArguments[0]);
                    var innerTypeCppBox = CppNameMapper.IsPrimitive(innerTypeName)
                        ? CppNameMapper.GetCppTypeName(innerTypeName)
                        : CppNameMapper.MangleTypeName(innerTypeName);
                    var innerTypeCppInfo = CppNameMapper.MangleTypeNameClean(innerTypeName);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{tmp} = {val}.f_hasValue"
                            + $" ? (cil2cpp::Object*)cil2cpp::box<{innerTypeCppBox}>({val}.f_value, &{innerTypeCppInfo}_TypeInfo)"
                            + $" : nullptr;",
                        ResultVar = tmp,
                        ResultTypeCpp = "cil2cpp::Object*",
                    });
                }
                else
                {
                    // Check if this is a reference type (class, not value type).
                    // Boxing a reference type is a no-op in the CLR — the value is already an Object*.
                    // Use multiple detection: Cecil Resolve, type cache, CppNameMapper
                    bool isValueType;
                    try
                    {
                        var resolved = typeRef.Resolve();
                        if (resolved != null)
                            isValueType = resolved.IsValueType;
                        else if (_typeCache.TryGetValue(resolvedName, out var cachedType))
                            isValueType = cachedType.IsValueType;
                        else
                            isValueType = CppNameMapper.IsValueType(resolvedName);
                    }
                    catch
                    {
                        // Can't resolve — fall back to CppNameMapper + type cache
                        if (_typeCache.TryGetValue(resolvedName, out var cachedType))
                            isValueType = cachedType.IsValueType;
                        else
                            isValueType = CppNameMapper.IsValueType(resolvedName);
                    }

                    if (!isValueType)
                    {
                        // Reference type: box is just a cast to Object*
                        block.Instructions.Add(new IRRawCpp
                        {
                            Code = $"{tmp} = (cil2cpp::Object*)(void*){val};",
                            ResultVar = tmp,
                            ResultTypeCpp = "cil2cpp::Object*",
                        });
                    }
                    else
                    {
                        // Value type: use box<T> template
                        // For primitives, use the C++ type name (int32_t) for template,
                        // but the mangled IL name (System_Int32) for TypeInfo reference.
                        // Use MangleTypeNameClean to avoid trailing underscore from generic '>' mangling.
                        var typeCpp = CppNameMapper.IsPrimitive(resolvedName)
                            ? CppNameMapper.GetCppTypeName(resolvedName)
                            : CppNameMapper.MangleTypeNameClean(resolvedName);
                        var typeInfoCpp = CppNameMapper.MangleTypeNameClean(resolvedName);
                        block.Instructions.Add(new IRBox
                        {
                            ValueExpr = val,
                            ValueTypeCppName = typeCpp,
                            TypeInfoCppName = typeCpp != typeInfoCpp ? typeInfoCpp : null,
                            ResultVar = tmp
                        });
                    }
                }
                stack.Push(new StackEntry(tmp, "cil2cpp::Object*"));
                break;
            }

            case Code.Unbox_Any:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var obj = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";

                if (IsNullableType(typeRef))
                {
                    // ECMA-335 III.4.33: unbox.any Nullable<T>:
                    //   null → Nullable<T>{ hasValue=false, value=default }
                    //   boxed T → Nullable<T>{ hasValue=true, value=unbox<T>(obj) }
                    var git = (GenericInstanceType)typeRef;
                    var innerTypeName = ResolveTypeRefOperand(git.GenericArguments[0]);
                    var innerTypeCpp = CppNameMapper.IsPrimitive(innerTypeName)
                        ? CppNameMapper.GetCppTypeName(innerTypeName)
                        : CppNameMapper.MangleTypeNameClean(innerTypeName);
                    var nullableCpp = CppNameMapper.GetCppTypeName(resolvedName);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{nullableCpp} {tmp} = {{0}}; " +
                               $"if ({obj}) {{ {tmp}.f_hasValue = true; " +
                               $"{tmp}.f_value = cil2cpp::unbox<{innerTypeCpp}>(reinterpret_cast<cil2cpp::Object*>({obj})); }}",
                        ResultVar = tmp,
                        ResultTypeCpp = nullableCpp,
                    });
                }
                else if (CppNameMapper.IsValueType(resolvedName))
                {
                    // ECMA-335 III.4.33: for value types, extract a copy.
                    // Use MangleTypeNameClean to avoid trailing underscore from generic '>' mangling.
                    var typeCpp = CppNameMapper.IsPrimitive(resolvedName)
                        ? CppNameMapper.GetCppTypeName(resolvedName)
                        : CppNameMapper.MangleTypeNameClean(resolvedName);
                    block.Instructions.Add(new IRUnbox
                    {
                        ObjectExpr = obj,
                        ValueTypeCppName = typeCpp,
                        ResultVar = tmp,
                        IsUnboxAny = true
                    });
                }
                else
                {
                    // ECMA-335 III.4.33: for reference types, equivalent to castclass
                    var unboxIsArray = typeRef is ArrayType;
                    var unboxCastTarget = unboxIsArray
                        ? "cil2cpp::Array" : GetMangledTypeNameForRef(typeRef);
                    block.Instructions.Add(new IRCast
                    {
                        SourceExpr = obj,
                        TargetTypeCpp = unboxCastTarget + "*",
                        ResultVar = tmp,
                        IsSafe = false,
                        TypeInfoCppName = unboxIsArray ? "System_Array" : null
                    });
                }
                stack.Push(tmp);
                break;
            }

            case Code.Unbox:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var obj = stack.PopExprOr("nullptr");
                var typeCpp = CppNameMapper.IsPrimitive(resolvedName)
                    ? CppNameMapper.GetCppTypeName(resolvedName)
                    : CppNameMapper.MangleTypeNameClean(resolvedName);
                var tmp = $"__t{tempCounter++}";
                var unboxPtrType = typeCpp + "*";
                block.Instructions.Add(new IRUnbox
                {
                    ObjectExpr = obj,
                    ValueTypeCppName = typeCpp,
                    ResultVar = tmp,
                    IsUnboxAny = false,
                    ResultTypeCpp = unboxPtrType,
                });
                stack.Push(new StackEntry(tmp, unboxPtrType));
                break;
            }

            // ===== Function pointers (delegates) =====
            case Code.Ldftn:
            {
                var targetMethod = (MethodReference)instr.Operand!;
                var targetTypeCpp = GetMangledTypeNameForRef(targetMethod.DeclaringType);
                string methodCppName;
                if (targetMethod is GenericInstanceMethod ldftnGim)
                {
                    // Generic method: include monomorphized type args in the mangled name
                    var elemMethod = ldftnGim.ElementMethod;
                    var typeArgs = ldftnGim.GenericArguments.Select(a => ResolveTypeRefOperand(a)).ToList();
                    var paramSig = string.Join(",", elemMethod.Parameters.Select(p => p.ParameterType.FullName));
                    var key = MakeGenericMethodKey(elemMethod.DeclaringType.FullName, elemMethod.Name, typeArgs, paramSig);
                    if (_genericMethodInstantiations.TryGetValue(key, out var gmInfo))
                        methodCppName = gmInfo.MangledName;
                    else
                        methodCppName = MangleGenericMethodName(elemMethod.DeclaringType.FullName, elemMethod.Name, typeArgs);
                }
                else
                {
                    methodCppName = CppNameMapper.MangleMethodName(targetTypeCpp, targetMethod.Name);
                }
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRLoadFunctionPointer
                {
                    MethodCppName = methodCppName,
                    ResultVar = tmp,
                    IsVirtual = false
                });
                stack.Push(tmp);
                break;
            }

            case Code.Ldvirtftn:
            {
                var targetMethod = (MethodReference)instr.Operand!;
                var targetTypeCpp = GetMangledTypeNameForRef(targetMethod.DeclaringType);
                var methodCppName = CppNameMapper.MangleMethodName(targetTypeCpp, targetMethod.Name);
                var obj = stack.PopExprOr("nullptr");
                var tmp = $"__t{tempCounter++}";

                // Try to find vtable slot
                int vtableSlot = -1;
                if (_typeCache.TryGetValue(ResolveCacheKey(targetMethod.DeclaringType), out var targetType))
                {
                    var entry = targetType.VTable.FirstOrDefault(e => e.MethodName == targetMethod.Name
                        && (e.Method == null || e.Method.Parameters.Count == targetMethod.Parameters.Count));
                    if (entry != null)
                        vtableSlot = entry.Slot;
                }

                block.Instructions.Add(new IRLoadFunctionPointer
                {
                    MethodCppName = methodCppName,
                    ResultVar = tmp,
                    IsVirtual = true,
                    ObjectExpr = obj,
                    VTableSlot = vtableSlot
                });
                stack.Push(tmp);
                break;
            }

            case Code.Sizeof:
            {
                var typeRef = (TypeReference)instr.Operand!;
                // Use GetCppTypeName so primitives map to C++ types (int32_t, not System_Int32)
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var typeCpp = CppNameMapper.GetCppTypeName(resolvedName);
                // Reference types ARE pointers — sizeof(T*) = pointer size is correct
                if (!CppNameMapper.IsValueType(resolvedName) && !typeCpp.EndsWith("*"))
                    typeCpp += "*";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    // ECMA-335 III.4.25 says uint32, but .NET sizeof() returns int (signed),
                    // and using uint32_t causes template deduction failures in checked arithmetic.
                    Code = $"auto {tmp} = static_cast<int32_t>(sizeof({typeCpp}));",
                    ResultVar = tmp,
                    ResultTypeCpp = "int32_t",
                });
                stack.Push(tmp);
                break;
            }

            case Code.Calli:
            {
                // Indirect function call via function pointer
                var callSite = (CallSite)instr.Operand!;
                var args = new List<string>();
                for (int i = 0; i < callSite.Parameters.Count; i++)
                    args.Add(stack.PopExpr());
                args.Reverse();
                var fptr = stack.PopExprOr("nullptr");

                // Build function pointer type: ReturnType(*)(ParamTypes...)
                var retType = CppNameMapper.GetCppTypeForDecl(callSite.ReturnType.FullName);
                var paramTypes = callSite.Parameters.Select(p =>
                    CppNameMapper.GetCppTypeForDecl(p.ParameterType.FullName)).ToList();
                var paramTypeStr = string.Join(", ", paramTypes);
                var castExpr = $"reinterpret_cast<{retType}(*)({paramTypeStr})>({fptr})";
                var argStr = string.Join(", ", args);

                if (callSite.ReturnType.FullName != "System.Void")
                {
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = {castExpr}({argStr});",
                        ResultVar = tmp,
                        ResultTypeCpp = retType,
                    });
                    stack.Push(tmp);
                }
                else
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{castExpr}({argStr});"
                    });
                }
                break;
            }

            // ===== Stack Allocation =====
            case Code.Localloc:
            {
                var size = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = static_cast<void*>(CIL2CPP_STACKALLOC({size}));",
                    ResultVar = tmp,
                    ResultTypeCpp = "void*",
                });
                stack.Push(new StackEntry(tmp, "void*"));
                break;
            }

            // ===== Newly implemented opcodes (Phase 8.1) =====

            case Code.Ckfinite:
            {
                var val = stack.PopExpr();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::ckfinite({val});",
                    ResultVar = tmp,
                    ResultTypeCpp = "double",
                });
                stack.Push(tmp);
                break;
            }

            case Code.Cpblk:
            {
                var len = stack.PopExpr();
                var src = stack.PopExprOr("nullptr");
                var dest = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"std::memcpy(reinterpret_cast<void*>({dest}), reinterpret_cast<const void*>({src}), static_cast<size_t>({len}));",
                });
                break;
            }

            case Code.Initblk:
            {
                var len = stack.PopExpr();
                var val = stack.PopExpr();
                var addr = stack.PopExprOr("nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"std::memset(reinterpret_cast<void*>({addr}), static_cast<int>({val}), static_cast<size_t>({len}));",
                });
                break;
            }

            case Code.Cpobj:
            {
                var src = stack.PopExprOr("nullptr");
                var dest = stack.PopExprOr("nullptr");
                var typeRef = instr.Operand as TypeReference;
                if (typeRef != null)
                {
                    var resolvedName = ResolveTypeRefOperand(typeRef);
                    var typeCpp = CppNameMapper.GetCppTypeName(resolvedName);

                    // ECMA-335 III.4.4: cpobj behavior depends on type:
                    //   - Value types: memcpy sizeof(T) bytes from src to dest
                    //   - Reference types: copy the object reference (pointer assignment)
                    bool isValueType;
                    try
                    {
                        var resolved = typeRef.Resolve();
                        if (resolved != null)
                            isValueType = resolved.IsValueType;
                        else if (_typeCache.TryGetValue(resolvedName, out var cachedType))
                            isValueType = cachedType.IsValueType;
                        else
                            isValueType = CppNameMapper.IsValueType(resolvedName);
                    }
                    catch
                    {
                        if (_typeCache.TryGetValue(resolvedName, out var cachedType))
                            isValueType = cachedType.IsValueType;
                        else
                            isValueType = CppNameMapper.IsValueType(resolvedName);
                    }

                    if (isValueType)
                    {
                        if (typeCpp.EndsWith("*")) typeCpp = typeCpp.TrimEnd('*');
                        block.Instructions.Add(new IRRawCpp
                        {
                            Code = $"std::memcpy(reinterpret_cast<void*>({dest}), reinterpret_cast<const void*>({src}), sizeof({typeCpp}));",
                        });
                    }
                    else
                    {
                        // Reference type: copy the pointer at src to dest
                        block.Instructions.Add(new IRRawCpp
                        {
                            Code = $"*reinterpret_cast<void**>({dest}) = *reinterpret_cast<void**>({src});",
                        });
                    }
                }
                else
                {
                    block.Instructions.Add(new IRComment { Text = "WARNING: cpobj without type operand" });
                }
                break;
            }

            case Code.Break:
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = "// IL break (debugger breakpoint) — nop in AOT",
                });
                break;
            }

            // ===== Phase 8 remaining: previously KnownUnimplemented =====

            case Code.No: // no. prefix: optimization hint for JIT, safe to ignore in AOT
                break;

            case Code.Jmp:
            {
                // ECMA-335 III.3.37: tail-jump to method with same signature.
                // Eval stack must be empty; current args forwarded to target.
                // Equivalent to: return target(currentArgs...)
                var methodRef = (MethodReference)instr.Operand!;
                var typeCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
                var targetName = CppNameMapper.MangleMethodName(typeCpp, methodRef.Name);

                // Disambiguate overloaded methods (same logic as EmitMethodCall)
                var ilParamKey = string.Join(",", methodRef.Parameters.Select(p =>
                    ResolveGenericTypeRef(p.ParameterType, methodRef.DeclaringType)));
                var lookupKey = $"{targetName}|{ilParamKey}";
                if (_module.DisambiguatedMethodNames.TryGetValue(lookupKey, out var disambiguated))
                    targetName = disambiguated;
                else if (methodRef.Parameters.Count > 0)
                {
                    var declTypeDef = methodRef.DeclaringType.Resolve();
                    if (declTypeDef != null
                        && RuntimeProvidedTypes.Contains(declTypeDef.FullName)
                        && !CoreRuntimeTypes.Contains(declTypeDef.FullName))
                    {
                        var overloadCount = declTypeDef.Methods.Count(m => m.Name == methodRef.Name);
                        if (overloadCount > 1)
                        {
                            var ilSuffix = string.Join("_", methodRef.Parameters.Select(p =>
                            {
                                var resolved = ResolveGenericTypeRef(p.ParameterType, methodRef.DeclaringType);
                                return CppNameMapper.MangleTypeName(resolved.TrimEnd('*', '&', ' '));
                            }));
                            targetName = $"{targetName}__{ilSuffix}";
                        }
                    }
                }

                // Forward current method's arguments to target
                var args = new List<string>();
                if (methodRef.HasThis)
                    args.Add("__this");
                // ECMA-335: jmp requires identical signature — forward current params
                // Skip __arglist_handle if present (not part of the IL-visible signature)
                foreach (var p in method.Parameters.Where(p => p.CppName != "__arglist_handle"))
                    args.Add(p.CppName);
                var callExpr = $"{targetName}({string.Join(", ", args)})";
                if (method.ReturnTypeCpp != "void")
                {
                    block.Instructions.Add(new IRReturn { Value = callExpr });
                }
                else
                {
                    block.Instructions.Add(new IRRawCpp { Code = $"{callExpr};" });
                    block.Instructions.Add(new IRReturn());
                }
                stack.Clear();
                break;
            }

            case Code.Mkrefany:
            {
                // ECMA-335 III.4.14: create TypedReference from managed pointer + type
                var typeRef = (TypeReference)instr.Operand!;
                var ptr = stack.PopExprOr("nullptr");
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var typeCpp = CppNameMapper.MangleTypeName(resolvedName);

                // Ensure TypeInfo symbol exists for primitive types
                _module.RegisterPrimitiveTypeInfo(resolvedName);

                var tmp = $"__t{tempCounter++}";
                // Two separate IRRawCpp: first declares+assigns .value, second assigns .type.
                // The declaration is handled by AddAutoDeclarations or cross-scope pre-declaration.
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = cil2cpp::TypedReference{{static_cast<void*>({ptr}), &{typeCpp}_TypeInfo}};",
                    ResultVar = tmp,
                    ResultTypeCpp = "cil2cpp::TypedReference",
                });
                stack.Push(tmp);
                break;
            }

            case Code.Refanyval:
            {
                // ECMA-335 III.4.22: extract typed pointer from TypedReference
                // Spec requires type check (InvalidCastException on mismatch)
                var typeRef = (TypeReference)instr.Operand!;
                var tr = stack.PopExprOr("{}");
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var typeCpp = CppNameMapper.GetCppTypeName(resolvedName);
                var typeInfoCpp = CppNameMapper.MangleTypeName(resolvedName);

                // Ensure TypeInfo symbol exists for primitive types
                _module.RegisterPrimitiveTypeInfo(resolvedName);

                // Emit runtime type check per ECMA-335 III.4.22
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({tr}.type != &{typeInfoCpp}_TypeInfo) cil2cpp::throw_invalid_cast();"
                });

                // Extract typed pointer (no `auto` — let AddAutoDeclarations handle declaration)
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = static_cast<{typeCpp}*>({tr}.value);",
                    ResultVar = tmp,
                    ResultTypeCpp = $"{typeCpp}*",
                });
                stack.Push(tmp);
                break;
            }

            case Code.Refanytype:
            {
                // ECMA-335 III.4.23: extract RuntimeTypeHandle from TypedReference
                // CIL2CPP represents RuntimeTypeHandle as TypeInfo* directly
                var tr = stack.PopExprOr("{}");
                var tmp = $"__t{tempCounter++}";
                // No `auto` — let AddAutoDeclarations handle declaration
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = {tr}.type;",
                    ResultVar = tmp,
                    ResultTypeCpp = "cil2cpp::TypeInfo*",
                });
                stack.Push(tmp);
                break;
            }

            case Code.Arglist:
            {
                // ECMA-335 III.3.2: push RuntimeArgumentHandle for varargs iteration
                // The __arglist_handle parameter is added by Pass 3 for varargs methods
                stack.Push("__arglist_handle");
                break;
            }

            default:
                block.Instructions.Add(new IRComment { Text = $"WARNING: Unsupported IL instruction: {instr}" });
                Console.Error.WriteLine($"WARNING: Unsupported IL instruction '{instr.OpCode}' at IL_{instr.Offset:X4} in {method.CppName}");
                break;
        }
    }

    internal static string GetIndirectType(Code code) => code switch
    {
        Code.Ldind_I1 or Code.Stind_I1 => "int8_t",
        Code.Ldind_I2 or Code.Stind_I2 => "int16_t",
        Code.Ldind_I4 or Code.Stind_I4 => "int32_t",
        Code.Ldind_I8 or Code.Stind_I8 => "int64_t",
        Code.Ldind_U1 => "uint8_t",
        Code.Ldind_U2 => "uint16_t",
        Code.Ldind_U4 => "uint32_t",
        Code.Ldind_R4 or Code.Stind_R4 => "float",
        Code.Ldind_R8 or Code.Stind_R8 => "double",
        Code.Ldind_I or Code.Stind_I => "intptr_t",
        _ => "int32_t"
    };

    private static void EmitCheckedBinaryOp(IRBasicBlock block, Stack<StackEntry> stack, Code code, ref int tempCounter)
    {
        var b = stack.PopExpr();
        var a = stack.PopExpr();
        var tmp = $"__t{tempCounter++}";

        // ECMA-335 III.3.1-6: checked arithmetic uses the type determined by the CIL stack.
        // Let C++ template argument deduction pick int32_t vs int64_t from the actual operand types.
        // Unsigned variants use to_unsigned() to convert operands before the check.
        bool isUn = code is Code.Add_Ovf_Un or Code.Sub_Ovf_Un or Code.Mul_Ovf_Un;
        var func = code switch
        {
            Code.Add_Ovf or Code.Add_Ovf_Un => isUn ? "cil2cpp::checked_add_un" : "cil2cpp::checked_add",
            Code.Sub_Ovf or Code.Sub_Ovf_Un => isUn ? "cil2cpp::checked_sub_un" : "cil2cpp::checked_sub",
            Code.Mul_Ovf or Code.Mul_Ovf_Un => isUn ? "cil2cpp::checked_mul_un" : "cil2cpp::checked_mul",
            _ => "cil2cpp::checked_add"
        };

        string cppCode;
        if (isUn)
            cppCode = $"auto {tmp} = {func}(cil2cpp::to_unsigned({a}), cil2cpp::to_unsigned({b}));";
        else
            cppCode = $"auto {tmp} = {func}({a}, {b});";

        block.Instructions.Add(new IRRawCpp
        {
            Code = cppCode,
            ResultVar = tmp,
            // ResultTypeCpp left null — auto-deduced from operands
        });
        stack.Push(tmp);
    }

    internal static string GetCheckedConvType(Code code) => code switch
    {
        Code.Conv_Ovf_I or Code.Conv_Ovf_I_Un => "intptr_t",
        Code.Conv_Ovf_I1 or Code.Conv_Ovf_I1_Un => "int8_t",
        Code.Conv_Ovf_I2 or Code.Conv_Ovf_I2_Un => "int16_t",
        Code.Conv_Ovf_I4 or Code.Conv_Ovf_I4_Un => "int32_t",
        Code.Conv_Ovf_I8 or Code.Conv_Ovf_I8_Un => "int64_t",
        Code.Conv_Ovf_U or Code.Conv_Ovf_U_Un => "uintptr_t",
        Code.Conv_Ovf_U1 or Code.Conv_Ovf_U1_Un => "uint8_t",
        Code.Conv_Ovf_U2 or Code.Conv_Ovf_U2_Un => "uint16_t",
        Code.Conv_Ovf_U4 or Code.Conv_Ovf_U4_Un => "uint32_t",
        Code.Conv_Ovf_U8 or Code.Conv_Ovf_U8_Un => "uint64_t",
        _ => "int32_t"
    };

    internal static bool IsUnsignedCheckedConv(Code code) => code switch
    {
        Code.Conv_Ovf_I_Un or Code.Conv_Ovf_I1_Un or Code.Conv_Ovf_I2_Un or
        Code.Conv_Ovf_I4_Un or Code.Conv_Ovf_I8_Un or Code.Conv_Ovf_U_Un or
        Code.Conv_Ovf_U1_Un or Code.Conv_Ovf_U2_Un or Code.Conv_Ovf_U4_Un or
        Code.Conv_Ovf_U8_Un => true,
        _ => false
    };

    /// <summary>
    /// Find the offset of the endfilter instruction following a filter start offset.
    /// </summary>
    private static int FindEndfilterOffset(List<ILInstruction> instructions, int filterStartOffset)
    {
        foreach (var instr in instructions)
        {
            if (instr.Offset >= filterStartOffset && instr.OpCode == Code.Endfilter)
                return instr.Offset;
        }
        return -1;
    }

    // ===== IL Opcode Coverage Sets (for testing) =====

    /// <summary>
    /// All IL opcodes handled by ConvertInstruction.
    /// Any Code value not in this set falls to default and emits a WARNING comment.
    /// Used by ILOpcodeCoverageTests to verify completeness.
    /// </summary>
    internal static readonly HashSet<Code> HandledOpcodes = new()
    {
        // Load Constants
        Code.Ldc_I4_0, Code.Ldc_I4_1, Code.Ldc_I4_2, Code.Ldc_I4_3,
        Code.Ldc_I4_4, Code.Ldc_I4_5, Code.Ldc_I4_6, Code.Ldc_I4_7,
        Code.Ldc_I4_8, Code.Ldc_I4_M1, Code.Ldc_I4_S, Code.Ldc_I4,
        Code.Ldc_I8, Code.Ldc_R4, Code.Ldc_R8,
        // Load String / Null / Token
        Code.Ldstr, Code.Ldnull, Code.Ldtoken,
        // Load/Store Arguments
        Code.Ldarg_0, Code.Ldarg_1, Code.Ldarg_2, Code.Ldarg_3,
        Code.Ldarg_S, Code.Ldarg, Code.Starg_S, Code.Starg,
        Code.Ldarga, Code.Ldarga_S,
        // Load/Store Locals
        Code.Ldloc_0, Code.Ldloc_1, Code.Ldloc_2, Code.Ldloc_3,
        Code.Ldloc_S, Code.Ldloc, Code.Ldloca, Code.Ldloca_S,
        Code.Stloc_0, Code.Stloc_1, Code.Stloc_2, Code.Stloc_3,
        Code.Stloc_S, Code.Stloc,
        // Arithmetic
        Code.Add, Code.Sub, Code.Mul, Code.Div, Code.Div_Un,
        Code.Rem, Code.Rem_Un, Code.Neg, Code.Not,
        // Checked Arithmetic
        Code.Add_Ovf, Code.Add_Ovf_Un, Code.Sub_Ovf, Code.Sub_Ovf_Un,
        Code.Mul_Ovf, Code.Mul_Ovf_Un,
        // Checked Conversions
        Code.Conv_Ovf_I, Code.Conv_Ovf_I1, Code.Conv_Ovf_I2, Code.Conv_Ovf_I4,
        Code.Conv_Ovf_I8, Code.Conv_Ovf_U, Code.Conv_Ovf_U1, Code.Conv_Ovf_U2,
        Code.Conv_Ovf_U4, Code.Conv_Ovf_U8,
        Code.Conv_Ovf_I_Un, Code.Conv_Ovf_I1_Un, Code.Conv_Ovf_I2_Un,
        Code.Conv_Ovf_I4_Un, Code.Conv_Ovf_I8_Un, Code.Conv_Ovf_U_Un,
        Code.Conv_Ovf_U1_Un, Code.Conv_Ovf_U2_Un, Code.Conv_Ovf_U4_Un,
        Code.Conv_Ovf_U8_Un,
        // Bitwise / Comparison
        Code.And, Code.Or, Code.Xor, Code.Shl, Code.Shr, Code.Shr_Un,
        Code.Ceq, Code.Cgt, Code.Cgt_Un, Code.Clt, Code.Clt_Un,
        // Branches
        Code.Br, Code.Br_S, Code.Brtrue, Code.Brtrue_S,
        Code.Brfalse, Code.Brfalse_S,
        Code.Beq, Code.Beq_S, Code.Bne_Un, Code.Bne_Un_S,
        Code.Bge, Code.Bge_S, Code.Bgt, Code.Bgt_S,
        Code.Ble, Code.Ble_S, Code.Blt, Code.Blt_S,
        Code.Bge_Un, Code.Bge_Un_S, Code.Bgt_Un, Code.Bgt_Un_S,
        Code.Ble_Un, Code.Ble_Un_S, Code.Blt_Un, Code.Blt_Un_S,
        Code.Switch,
        // Fields
        Code.Ldfld, Code.Stfld, Code.Ldsfld, Code.Stsfld,
        Code.Ldflda, Code.Ldsflda,
        // Indirect Load/Store
        Code.Ldobj, Code.Stobj,
        Code.Ldind_I1, Code.Ldind_I2, Code.Ldind_I4, Code.Ldind_I8,
        Code.Ldind_U1, Code.Ldind_U2, Code.Ldind_U4,
        Code.Ldind_R4, Code.Ldind_R8, Code.Ldind_I, Code.Ldind_Ref,
        Code.Stind_I1, Code.Stind_I2, Code.Stind_I4, Code.Stind_I8,
        Code.Stind_R4, Code.Stind_R8, Code.Stind_I, Code.Stind_Ref,
        // Method Calls
        Code.Call, Code.Callvirt, Code.Newobj, Code.Ret,
        // Conversions
        Code.Conv_I1, Code.Conv_I2, Code.Conv_I4, Code.Conv_I8, Code.Conv_I,
        Code.Conv_U1, Code.Conv_U2, Code.Conv_U4, Code.Conv_U8, Code.Conv_U,
        Code.Conv_R4, Code.Conv_R8, Code.Conv_R_Un,
        // Stack Operations
        Code.Dup, Code.Pop,
        // Prefixes / No-ops
        Code.Nop, Code.Tail, Code.Readonly, Code.Constrained,
        Code.Volatile, Code.Unaligned,
        // Arrays
        Code.Newarr, Code.Ldlen,
        Code.Ldelem_I1, Code.Ldelem_I2, Code.Ldelem_I4, Code.Ldelem_I8,
        Code.Ldelem_U1, Code.Ldelem_U2, Code.Ldelem_U4,
        Code.Ldelem_R4, Code.Ldelem_R8, Code.Ldelem_Ref, Code.Ldelem_I,
        Code.Ldelem_Any,
        Code.Stelem_I1, Code.Stelem_I2, Code.Stelem_I4, Code.Stelem_I8,
        Code.Stelem_R4, Code.Stelem_R8, Code.Stelem_I, Code.Stelem_Ref,
        Code.Stelem_Any,
        Code.Ldelema,
        // Type Operations
        Code.Castclass, Code.Isinst,
        Code.Initobj, Code.Box, Code.Unbox_Any, Code.Unbox,
        // Exception Handling
        Code.Throw, Code.Rethrow,
        Code.Leave, Code.Leave_S, Code.Endfinally, Code.Endfilter,
        // Function Pointers
        Code.Ldftn, Code.Ldvirtftn, Code.Calli,
        // Misc
        Code.Sizeof, Code.Localloc,
        // Phase 8.1: newly implemented
        Code.Ckfinite, Code.Cpblk, Code.Initblk, Code.Cpobj, Code.Break,
        // Phase 8: previously KnownUnimplemented, now fully handled
        Code.No, Code.Jmp, Code.Mkrefany, Code.Refanyval, Code.Refanytype, Code.Arglist,
    };

}
