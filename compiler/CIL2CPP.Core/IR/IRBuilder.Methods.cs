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
        if (methodDef.Name is "op_Explicit" or "op_Implicit")
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
        if (declaringType != null && ICallRegistry.Lookup(
                declaringType.ILFullName, methodDef.Name, methodDef.Parameters.Count) != null)
            irMethod.HasICallMapping = true;

        // Detect P/Invoke (DllImport)
        if (methodDef.IsPInvokeImpl && methodDef.PInvokeInfo != null)
        {
            irMethod.IsPInvoke = true;
            irMethod.PInvokeModule = methodDef.PInvokeInfo.Module?.Name;
            irMethod.PInvokeEntryPoint = methodDef.PInvokeInfo.EntryPoint ?? methodDef.Name;
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
    /// Mangle an IL type name for use in disambiguated method names.
    /// Strips pointer/ref suffixes and applies standard mangling.
    /// </summary>
    private static string MangleILTypeForDisambiguation(string ilTypeName)
    {
        // Strip pointer/ref suffixes before mangling
        var clean = ilTypeName.TrimEnd('*', '&', ' ');
        return CppNameMapper.MangleTypeName(clean);
    }

    /// <summary>
    /// Convert IL method body to IR basic blocks using stack simulation.
    /// </summary>
    private void ConvertMethodBody(IL.MethodInfo methodDef, IRMethod irMethod)
    {
        _pendingVolatile = false; // Reset between methods
        _constrainedType = null;
        _inFilterRegion = false;
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

        // Stack simulation
        var stack = new Stack<string>();
        int tempCounter = 0;
        bool skipDeadCode = false; // Skip IL instructions after unconditional branch until next label

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
                            // Use GetCppTypeName (not MangleTypeName) so that runtime exception types
                            // resolve to cil2cpp::Exception etc. — the CIL2CPP_CATCH macro appends
                            // _TypeInfo which must match the runtime-declared TypeInfo names.
                            // System.Object catch is equivalent to catch-all
                            var catchTypeCpp = evt.CatchTypeName is not null and not "System.Object"
                                ? CppNameMapper.GetCppTypeName(evt.CatchTypeName)?.TrimEnd('*').TrimEnd() : null;
                            block.Instructions.Add(new IRCatchBegin { ExceptionTypeCppName = catchTypeCpp });
                            // IL pushes exception onto stack at catch entry
                            stack.Push("__exc_ctx.current_exception");
                            break;
                        case ExceptionEventKind.FilterBegin:
                            block.Instructions.Add(new IRFilterBegin());
                            // Declare __filter_result in the filter scope (before any labels)
                            block.Instructions.Add(new IRRawCpp { Code = "int32_t __filter_result = 0;" });
                            // IL pushes exception onto stack for filter evaluation
                            stack.Push("(cil2cpp::Exception*)__exc_ctx.current_exception");
                            // Track filter region for endfilter scoping fix
                            _inFilterRegion = true;
                            _endfilterOffset = FindEndfilterOffset(instructions, instr.Offset);
                            break;
                        case ExceptionEventKind.FilterHandlerBegin:
                            // Handler body after endfilter — push exception for handler body
                            stack.Push("(cil2cpp::Exception*)__exc_ctx.current_exception");
                            break;
                        case ExceptionEventKind.FinallyBegin:
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
                    Value = stack.Peek()
                });
            }

            // Insert label if this is a branch target
            if (branchTargets.Contains(instr.Offset))
            {
                var wasDeadCode = skipDeadCode;
                skipDeadCode = false; // Resume processing at branch targets
                // Merge stack at branch targets: if a conditional branch saved a merge variable
                // for this offset, the fall-through path may have a different stack top.
                // Insert an assignment to unify both paths (fixes dup+brtrue delegate caching pattern).
                // IMPORTANT: Only do this if we're on a live fall-through path. If we were in dead code
                // (after an unconditional br/ret/throw), the stack state is from the dead path and
                // should not be used for merge assignments.
                if (!wasDeadCode
                    && branchMergeVars.TryGetValue(instr.Offset, out var mergeVar)
                    && IsValidMergeVariable(mergeVar)
                    && stack.Count > 0 && stack.Peek() != mergeVar)
                {
                    var currentTop = stack.Pop();
                    block.Instructions.Add(new IRAssign { Target = mergeVar, Value = currentTop });
                    stack.Push(mergeVar);
                }

                block.Instructions.Add(new IRLabel { LabelName = $"IL_{instr.Offset:X4}" });
            }

            // Skip dead code after unconditional branches (br, ret, throw, rethrow)
            // These instructions have corrupted stack state and produce invalid C++ (e.g., "16 = 0")
            if (skipDeadCode)
                continue;

            int beforeCount = block.Instructions.Count;

            try
            {
                ConvertInstruction(instr, block, stack, irMethod, ref tempCounter, branchMergeVars);
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
        return true;
    }

    private void ConvertInstruction(ILInstruction instr, IRBasicBlock block, Stack<string> stack,
        IRMethod method, ref int tempCounter, Dictionary<int, string> branchMergeVars)
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
                stack.Push(((int)instr.Operand!).ToString());
                break;
            case Code.Ldc_I8:
                stack.Push($"{(long)instr.Operand!}LL");
                break;
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
                stack.Push(strId);
                break;

            case Code.Ldnull:
                stack.Push("nullptr");
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
                    var typeCpp = GetMangledTypeNameForRef(typeRef);
                    // Ensure TypeInfo exists for primitive types used in typeof()
                    if (CppNameMapper.IsPrimitive(typeRef.FullName)
                        && typeRef.FullName != "System.Void")
                    {
                        _module.RegisterPrimitiveTypeInfo(typeRef.FullName);
                    }
                    stack.Push($"&{typeCpp}_TypeInfo");
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
                stack.Push(GetArgName(method, 0));
                break;
            case Code.Ldarg_1:
                stack.Push(GetArgName(method, 1));
                break;
            case Code.Ldarg_2:
                stack.Push(GetArgName(method, 2));
                break;
            case Code.Ldarg_3:
                stack.Push(GetArgName(method, 3));
                break;
            case Code.Ldarg_S:
            case Code.Ldarg:
                var paramDef = instr.Operand as ParameterDefinition;
                int argIdx = paramDef?.Index ?? 0;
                if (!method.IsStatic) argIdx++;
                stack.Push(GetArgName(method, argIdx));
                break;

            // ===== Store Arguments =====
            case Code.Starg_S:
            case Code.Starg:
                var stArgDef = instr.Operand as ParameterDefinition;
                int stArgIdx = stArgDef?.Index ?? 0;
                if (!method.IsStatic) stArgIdx++;
                var stArgVal = stack.Count > 0 ? stack.Pop() : "0";
                block.Instructions.Add(new IRAssign
                {
                    Target = GetArgName(method, stArgIdx),
                    Value = stArgVal
                });
                break;

            // ===== Load Locals =====
            case Code.Ldloc_0: stack.Push(GetLocalName(method, 0)); break;
            case Code.Ldloc_1: stack.Push(GetLocalName(method, 1)); break;
            case Code.Ldloc_2: stack.Push(GetLocalName(method, 2)); break;
            case Code.Ldloc_3: stack.Push(GetLocalName(method, 3)); break;
            case Code.Ldloc_S:
            case Code.Ldloc:
                var locDef = instr.Operand as VariableDefinition;
                stack.Push(GetLocalName(method, locDef?.Index ?? 0));
                break;

            // ===== Load Address of Local/Arg =====
            case Code.Ldloca:
            case Code.Ldloca_S:
            {
                var locaVar = instr.Operand as VariableDefinition;
                stack.Push($"&{GetLocalName(method, locaVar?.Index ?? 0)}");
                break;
            }

            case Code.Ldarga:
            case Code.Ldarga_S:
            {
                var argaParam = instr.Operand as ParameterDefinition;
                int argaIdx = argaParam?.Index ?? 0;
                if (!method.IsStatic) argaIdx++;
                stack.Push($"&{GetArgName(method, argaIdx)}");
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
            case Code.Add: EmitBinaryOp(block, stack, "+", ref tempCounter); break;
            case Code.Sub: EmitBinaryOp(block, stack, "-", ref tempCounter); break;
            case Code.Mul: EmitBinaryOp(block, stack, "*", ref tempCounter); break;
            case Code.Div: EmitBinaryOp(block, stack, "/", ref tempCounter); break;
            case Code.Div_Un: EmitBinaryOp(block, stack, "/", ref tempCounter); break;
            case Code.Rem: EmitBinaryOp(block, stack, "%", ref tempCounter); break;
            case Code.Rem_Un: EmitBinaryOp(block, stack, "%", ref tempCounter); break;
            case Code.And: EmitBinaryOp(block, stack, "&", ref tempCounter); break;
            case Code.Or: EmitBinaryOp(block, stack, "|", ref tempCounter); break;
            case Code.Xor: EmitBinaryOp(block, stack, "^", ref tempCounter); break;
            case Code.Shl: EmitBinaryOp(block, stack, "<<", ref tempCounter); break;
            case Code.Shr: EmitBinaryOp(block, stack, ">>", ref tempCounter); break;
            case Code.Shr_Un: EmitBinaryOp(block, stack, ">>", ref tempCounter); break; // C++ unsigned >> is logical shift

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
                var val = stack.Count > 0 ? stack.Pop() : "0";
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
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRUnaryOp { Op = "-", Operand = val, ResultVar = tmp });
                stack.Push(tmp);
                break;
            }

            case Code.Not:
            {
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRUnaryOp { Op = "~", Operand = val, ResultVar = tmp });
                stack.Push(tmp);
                break;
            }

            // ===== Comparison =====
            case Code.Ceq: EmitBinaryOp(block, stack, "==", ref tempCounter); break;
            case Code.Cgt: EmitBinaryOp(block, stack, ">", ref tempCounter); break;
            case Code.Cgt_Un: EmitBinaryOp(block, stack, ">", ref tempCounter); break;
            case Code.Clt: EmitBinaryOp(block, stack, "<", ref tempCounter); break;
            case Code.Clt_Un: EmitBinaryOp(block, stack, "<", ref tempCounter); break;

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
                        Value = stack.Peek()
                    });
                }
                block.Instructions.Add(new IRBranch { TargetLabel = $"IL_{target.Offset:X4}" });
                break;
            }

            case Code.Brtrue:
            case Code.Brtrue_S:
            {
                var cond = stack.Count > 0 ? stack.Pop() : "0";
                var target = (Instruction)instr.Operand!;
                // Save stack top as merge variable for branch target (dup+brtrue pattern)
                // Only save if the stack top is a valid C++ lvalue (variable name, not a literal)
                if (stack.Count > 0 && !branchMergeVars.ContainsKey(target.Offset)
                    && IsValidMergeVariable(stack.Peek()))
                    branchMergeVars[target.Offset] = stack.Peek();
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
                var cond = stack.Count > 0 ? stack.Pop() : "0";
                var target = (Instruction)instr.Operand!;
                // Save stack top as merge variable for branch target (dup+brfalse pattern)
                // Only save if the stack top is a valid C++ lvalue (variable name, not a literal)
                if (stack.Count > 0 && !branchMergeVars.ContainsKey(target.Offset)
                    && IsValidMergeVariable(stack.Peek()))
                    branchMergeVars[target.Offset] = stack.Peek();
                block.Instructions.Add(new IRConditionalBranch
                {
                    Condition = $"!({cond})",
                    TrueLabel = $"IL_{target.Offset:X4}"
                });
                break;
            }

            case Code.Beq:
            case Code.Beq_S:
                EmitComparisonBranch(block, stack, "==", instr);
                break;
            case Code.Bne_Un:
            case Code.Bne_Un_S:
                EmitComparisonBranch(block, stack, "!=", instr);
                break;
            case Code.Bge:
            case Code.Bge_S:
                EmitComparisonBranch(block, stack, ">=", instr);
                break;
            case Code.Bgt:
            case Code.Bgt_S:
                EmitComparisonBranch(block, stack, ">", instr);
                break;
            case Code.Ble:
            case Code.Ble_S:
                EmitComparisonBranch(block, stack, "<=", instr);
                break;
            case Code.Blt:
            case Code.Blt_S:
                EmitComparisonBranch(block, stack, "<", instr);
                break;
            // Unsigned branches (ECMA-335 III.3.6-3.12): treat operands as unsigned
            // C++ types from our codegen are already unsigned for uint/ulong/char,
            // so the standard operators work correctly for the common C# patterns.
            case Code.Bge_Un:
            case Code.Bge_Un_S:
                EmitComparisonBranch(block, stack, ">=", instr);
                break;
            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
                EmitComparisonBranch(block, stack, ">", instr);
                break;
            case Code.Ble_Un:
            case Code.Ble_Un_S:
                EmitComparisonBranch(block, stack, "<=", instr);
                break;
            case Code.Blt_Un:
            case Code.Blt_Un_S:
                EmitComparisonBranch(block, stack, "<", instr);
                break;

            // ===== Switch =====
            case Code.Switch:
            {
                var value = stack.Count > 0 ? stack.Pop() : "0";
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
                var obj = stack.Count > 0 ? stack.Pop() : "__this";
                // volatile. prefix: fence before load
                if (_pendingVolatile)
                {
                    block.Instructions.Add(new IRRawCpp { Code = "std::atomic_thread_fence(std::memory_order_seq_cst);" });
                    _pendingVolatile = false;
                }
                // Determine if the object is a value (struct) or pointer:
                // - starts with '&' → pointer to value type → use ->
                // - declaring type is value type and obj is a local/temp → use .
                var isValueAccess = IsValueTypeAccess(fieldRef.DeclaringType, obj, method);
                // Cast to declaring type when the stack value might be a base type (Object*)
                // CIL ldfld specifies the declaring type; C++ needs the correct type for field access
                string? castToType = null;
                if (!isValueAccess && !fieldRef.DeclaringType.IsValueType && obj != "__this")
                {
                    var declTypeName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                    if (declTypeName != "cil2cpp::Object" && declTypeName != "cil2cpp::String")
                        castToType = declTypeName;
                }
                var tmp = $"__t{tempCounter++}";
                var lfFieldTypeName = ResolveTypeRefOperand(fieldRef.FieldType);
                var lfFieldTypeCpp = CppNameMapper.GetCppTypeForDecl(lfFieldTypeName);
                block.Instructions.Add(new IRFieldAccess
                {
                    ObjectExpr = obj,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    ResultVar = tmp,
                    IsValueAccess = isValueAccess,
                    CastToType = castToType,
                    ResultTypeCpp = lfFieldTypeCpp,
                });
                stack.Push(tmp);
                break;
            }

            case Code.Stfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var obj = stack.Count > 0 ? stack.Pop() : "__this";
                bool isVolatileStore = _pendingVolatile;
                _pendingVolatile = false;
                // Cast value for reference type fields to handle derived→base type mismatch
                // (flat struct model: no C++ inheritance, so all pointer casts must be explicit)
                var fieldTypeName = ResolveTypeRefOperand(fieldRef.FieldType);
                var fieldTypeCpp = CppNameMapper.GetCppTypeForDecl(fieldTypeName);
                if (fieldTypeCpp.EndsWith("*") && !CppNameMapper.IsValueType(fieldTypeName)
                    && fieldTypeCpp != "void*" && val != "nullptr" && val != "0")
                {
                    // Use (void*) intermediate to handle flat struct model (no C++ inheritance)
                    val = $"({fieldTypeCpp})(void*){val}";
                }
                var isValueAccess = IsValueTypeAccess(fieldRef.DeclaringType, obj, method);
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
                    var elemCppType = CppNameMapper.MangleTypeName(resolvedElem);
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
                var sfTypeName = ResolveTypeRefOperand(fieldRef.FieldType);
                var sfTypeCpp = CppNameMapper.GetCppTypeForDecl(sfTypeName);
                block.Instructions.Add(new IRStaticFieldAccess
                {
                    TypeCppName = typeCppName,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    ResultVar = tmp,
                    ResultTypeCpp = sfTypeCpp,
                });
                stack.Push(tmp);
                break;
            }

            case Code.Stsfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var typeCppName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                var fieldCacheKey = ResolveCacheKey(fieldRef.DeclaringType);
                EmitCctorGuardIfNeeded(block, fieldCacheKey, typeCppName);
                var val = stack.Count > 0 ? stack.Pop() : "0";
                bool isVolatileStore = _pendingVolatile;
                _pendingVolatile = false;
                // Cast value for reference type static fields (derived→base in flat struct model)
                var sfieldTypeName = ResolveTypeRefOperand(fieldRef.FieldType);
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
                var obj = stack.Count > 0 ? stack.Pop() : "__this";
                var tmp = $"__t{tempCounter++}";
                var fieldName = CppNameMapper.MangleFieldName(fieldRef.Name);
                // When obj is an address (e.g. &loc_0 from ldloca), use . accessor
                // to avoid &&loc_0->field which MSVC tokenizes as rvalue-ref &&
                string expr;
                if (obj.StartsWith("&") && !obj.StartsWith("&("))
                    expr = $"&({obj[1..]}.{fieldName})";
                else
                    expr = $"&{obj}->{fieldName}";
                var fldaTypeName = ResolveTypeRefOperand(fieldRef.FieldType);
                var fldaTypeCpp = CppNameMapper.GetCppTypeForDecl(fldaTypeName);
                // ldflda pushes a pointer to the field
                var fldaPtrType = fldaTypeCpp.EndsWith("*") ? fldaTypeCpp : fldaTypeCpp + "*";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = {expr};",
                    ResultVar = tmp,
                    ResultTypeCpp = fldaPtrType,
                });
                stack.Push(tmp);
                break;
            }

            case Code.Ldsflda:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var typeCppName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                var fieldCacheKey = ResolveCacheKey(fieldRef.DeclaringType);
                EmitCctorGuardIfNeeded(block, fieldCacheKey, typeCppName);
                var tmp = $"__t{tempCounter++}";
                var sfaTypeName = ResolveTypeRefOperand(fieldRef.FieldType);
                var sfaTypeCpp = CppNameMapper.GetCppTypeForDecl(sfaTypeName);
                var sfaPtrType = sfaTypeCpp.EndsWith("*") ? sfaTypeCpp : sfaTypeCpp + "*";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = &{typeCppName}_statics.{CppNameMapper.MangleFieldName(fieldRef.Name)};",
                    ResultVar = tmp,
                    ResultTypeCpp = sfaPtrType,
                });
                stack.Push(tmp);
                break;
            }

            // ===== Indirect Load/Store (pointer dereference) =====
            case Code.Ldobj:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = *(cil2cpp::Object**){addr};",
                    ResultVar = tmp,
                    ResultTypeCpp = "cil2cpp::Object*",
                });
                stack.Push(tmp);
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
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"*({cppType}*){addr} = ({cppType}){val};"
                });
                break;
            }

            case Code.Stind_Ref:
            {
                var val = stack.Count > 0 ? stack.Pop() : "nullptr";
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                    var retVal = stack.Pop();
                    // Cast return value to method's declared return type for interface/generic returns
                    if (method.ReturnTypeCpp.EndsWith("*"))
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
                // Address-of expressions (&loc_N, &field) are already native pointers
                // — conv.i is a no-op, preserving pointer type avoids intptr_t→T* casts
                var convIVal = stack.Count > 0 ? stack.Peek() : "0";
                if (!convIVal.StartsWith("&"))
                    EmitConversion(block, stack, "intptr_t", ref tempCounter);
                break;
            }
            case Code.Conv_U1:  EmitConversion(block, stack, "uint8_t", ref tempCounter); break;
            case Code.Conv_U2:  EmitConversion(block, stack, "uint16_t", ref tempCounter); break;
            case Code.Conv_U4:  EmitConversion(block, stack, "uint32_t", ref tempCounter); break;
            case Code.Conv_U8:  EmitConversion(block, stack, "uint64_t", ref tempCounter); break;
            case Code.Conv_U:
            {
                var convVal = stack.Count > 0 ? stack.Peek() : "0";
                // If converting literal 0 → preserve as 0 (valid null pointer constant in C++)
                if (convVal == "0") break;
                // Address-of expressions (&loc_N, &field) are already native pointers
                // — conv.u is a no-op, preserving pointer type avoids uintptr_t→T* casts
                if (convVal.StartsWith("&")) break;
                EmitConversion(block, stack, "uintptr_t", ref tempCounter);
                break;
            }
            case Code.Conv_R4:  EmitConversion(block, stack, "float", ref tempCounter); break;
            case Code.Conv_R8:  EmitConversion(block, stack, "double", ref tempCounter); break;
            case Code.Conv_R_Un:
            {
                // ECMA-335 III.3.19: convert unsigned integer to float
                // Must reinterpret as unsigned before converting to double
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRConversion
                {
                    SourceExpr = $"static_cast<uint64_t>({val})",
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
                    var val = stack.Peek();
                    stack.Push(val);
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
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                var elemCppType = CppNameMapper.MangleTypeName(resolvedName);
                // Ensure TypeInfo exists for primitive element types
                if (CppNameMapper.IsPrimitive(resolvedName))
                    _module.RegisterPrimitiveTypeInfo(resolvedName);
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::array_create(&{elemCppType}_TypeInfo, {length});",
                    ResultVar = tmp,
                    ResultTypeCpp = "cil2cpp::Array*",
                });
                stack.Push(tmp);
                break;
            }

            case Code.Ldlen:
            {
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRArrayAccess
                {
                    ArrayExpr = arr, IndexExpr = index,
                    ElementType = elemType, ResultVar = tmp
                });
                stack.Push(tmp);
                break;
            }

            case Code.Stelem_I1: case Code.Stelem_I2: case Code.Stelem_I4: case Code.Stelem_I8:
            case Code.Stelem_R4: case Code.Stelem_R8: case Code.Stelem_I:
            {
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({elemType}*)cil2cpp::array_get_element_ptr({arr}, {index});",
                    ResultVar = tmp,
                    ResultTypeCpp = elemType + "*",
                });
                stack.Push(tmp);
                break;
            }

            // ===== Type Operations =====
            case Code.Castclass:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                // Array types always use cil2cpp::Array* regardless of element type
                var castIsArray = typeRef is ArrayType;
                var castTargetType = castIsArray
                    ? "cil2cpp::Array" : GetMangledTypeNameForRef(typeRef);
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = castTargetType + "*",
                    ResultVar = tmp,
                    IsSafe = false,
                    TypeInfoCppName = castIsArray ? "System_Array" : null
                });
                stack.Push(tmp);
                break;
            }

            case Code.Isinst:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                var isArray = typeRef is ArrayType;
                var isinstTargetType = isArray
                    ? "cil2cpp::Array" : GetMangledTypeNameForRef(typeRef);
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = isinstTargetType + "*",
                    ResultVar = tmp,
                    IsSafe = true,
                    TypeInfoCppName = isArray ? "System_Array" : null
                });
                stack.Push(tmp);
                break;
            }

            // ===== Exception Handling =====
            case Code.Throw:
            {
                var ex = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
                // Use GetCppTypeName (not MangleTypeName) so primitives map to C++ types
                // e.g. System.SByte → int8_t (not System_SByte which doesn't exist as a C++ type)
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var typeCpp = CppNameMapper.GetCppTypeName(resolvedName);
                // Strip pointer suffix — initobj operates on value types, sizeof needs the base type
                if (typeCpp.EndsWith("*")) typeCpp = typeCpp.TrimEnd('*');
                block.Instructions.Add(new IRInitObj
                {
                    AddressExpr = addr,
                    TypeCppName = typeCpp
                });
                break;
            }

            case Code.Box:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var val = stack.Count > 0 ? stack.Pop() : "0";
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
                    var innerTypeCppInfo = CppNameMapper.MangleTypeName(innerTypeName);
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
                        // but the mangled IL name (System_Int32) for TypeInfo reference
                        var typeCpp = CppNameMapper.IsPrimitive(resolvedName)
                            ? CppNameMapper.GetCppTypeName(resolvedName)
                            : CppNameMapper.MangleTypeName(resolvedName);
                        var typeInfoCpp = CppNameMapper.MangleTypeName(resolvedName);
                        block.Instructions.Add(new IRBox
                        {
                            ValueExpr = val,
                            ValueTypeCppName = typeCpp,
                            TypeInfoCppName = typeCpp != typeInfoCpp ? typeInfoCpp : null,
                            ResultVar = tmp
                        });
                    }
                }
                stack.Push(tmp);
                break;
            }

            case Code.Unbox_Any:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var resolvedName = ResolveTypeRefOperand(typeRef);
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";

                if (CppNameMapper.IsValueType(resolvedName))
                {
                    // ECMA-335 III.4.33: for value types, extract a copy
                    var typeCpp = CppNameMapper.IsPrimitive(resolvedName)
                        ? CppNameMapper.GetCppTypeName(resolvedName)
                        : CppNameMapper.MangleTypeName(resolvedName);
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
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var typeCpp = CppNameMapper.IsPrimitive(resolvedName)
                    ? CppNameMapper.GetCppTypeName(resolvedName)
                    : CppNameMapper.MangleTypeName(resolvedName);
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRUnbox
                {
                    ObjectExpr = obj,
                    ValueTypeCppName = typeCpp,
                    ResultVar = tmp,
                    IsUnboxAny = false
                });
                stack.Push(tmp);
                break;
            }

            // ===== Function pointers (delegates) =====
            case Code.Ldftn:
            {
                var targetMethod = (MethodReference)instr.Operand!;
                var targetTypeCpp = GetMangledTypeNameForRef(targetMethod.DeclaringType);
                var methodCppName = CppNameMapper.MangleMethodName(targetTypeCpp, targetMethod.Name);
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
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                if (typeCpp.EndsWith("*")) typeCpp = typeCpp.TrimEnd('*');
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
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
                    args.Add(stack.Count > 0 ? stack.Pop() : "0");
                args.Reverse();
                var fptr = stack.Count > 0 ? stack.Pop() : "nullptr";

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
                var size = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = static_cast<void*>(CIL2CPP_STACKALLOC({size}));",
                    ResultVar = tmp,
                    ResultTypeCpp = "void*",
                });
                stack.Push(tmp);
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

    private static void EmitCheckedBinaryOp(IRBasicBlock block, Stack<string> stack, Code code, ref int tempCounter)
    {
        var b = stack.Count > 0 ? stack.Pop() : "0";
        var a = stack.Count > 0 ? stack.Pop() : "0";
        var tmp = $"__t{tempCounter++}";

        var (func, type) = code switch
        {
            Code.Add_Ovf => ("cil2cpp::checked_add", "int32_t"),
            Code.Add_Ovf_Un => ("cil2cpp::checked_add_un", "uint32_t"),
            Code.Sub_Ovf => ("cil2cpp::checked_sub", "int32_t"),
            Code.Sub_Ovf_Un => ("cil2cpp::checked_sub_un", "uint32_t"),
            Code.Mul_Ovf => ("cil2cpp::checked_mul", "int32_t"),
            Code.Mul_Ovf_Un => ("cil2cpp::checked_mul_un", "uint32_t"),
            _ => ("cil2cpp::checked_add", "int32_t")
        };

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = {func}<{type}>(({type}){a}, ({type}){b});",
            ResultVar = tmp,
            ResultTypeCpp = type,
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
    };

    /// <summary>
    /// IL opcodes intentionally not implemented.
    /// These are either extremely rare, deprecated, or not emitted by Roslyn.
    /// </summary>
    internal static readonly HashSet<Code> KnownUnimplementedOpcodes = new()
    {
        Code.Break,       // Debugger breakpoint (no-op in AOT)
        Code.No,          // no. prefix (optimization hint, no-op)
        Code.Cpobj,       // Copy value type (Roslyn uses ldobj+stobj instead)
        Code.Cpblk,       // Copy block of memory (rare, used by Span internals)
        Code.Initblk,     // Initialize block of memory
        Code.Jmp,         // Jump to another method (deprecated, Roslyn never emits)
        Code.Mkrefany,    // Make TypedReference (__makeref, discouraged)
        Code.Refanyval,   // Extract TypedReference value (__refvalue)
        Code.Refanytype,  // Extract TypedReference type (__reftype)
        Code.Arglist,     // Variable argument list (__arglist, C-style varargs)
        Code.Ckfinite,    // Check for finite float (rarely emitted)
    };
}
