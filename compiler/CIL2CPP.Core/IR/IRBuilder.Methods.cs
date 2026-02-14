using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private IRMethod ConvertMethod(IL.MethodInfo methodDef, IRType declaringType)
    {
        var cppName = CppNameMapper.MangleMethodName(declaringType.CppName, methodDef.Name);

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

        // Detect newslot (C# 'new virtual')
        if (methodDef.IsNewSlot)
            irMethod.IsNewSlot = true;

        // Detect explicit interface overrides (Cecil .override directive)
        var cecilMethod = methodDef.GetCecilMethod();
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
                CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
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
            });
        }

        // Note: method body is converted in a later pass (after VTables are built)
        return irMethod;
    }

    /// <summary>
    /// Convert IL method body to IR basic blocks using stack simulation.
    /// </summary>
    private void ConvertMethodBody(IL.MethodInfo methodDef, IRMethod irMethod)
    {
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
                        new ExceptionEvent(ExceptionEventKind.CatchBegin, handler.CatchTypeName));
                }
                else if (handler.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally)
                {
                    AddExceptionEvent(exceptionEvents, handler.HandlerStart,
                        new ExceptionEvent(ExceptionEventKind.FinallyBegin));
                }
                AddExceptionEvent(exceptionEvents, handler.HandlerEnd,
                    new ExceptionEvent(ExceptionEventKind.HandlerEnd));
            }
        }

        // Stack simulation
        var stack = new Stack<string>();
        int tempCounter = 0;

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
                            var catchTypeCpp = evt.CatchTypeName != null
                                ? CppNameMapper.MangleTypeName(evt.CatchTypeName) : null;
                            block.Instructions.Add(new IRCatchBegin { ExceptionTypeCppName = catchTypeCpp });
                            // IL pushes exception onto stack at catch entry
                            stack.Push("__exc_ctx.current_exception");
                            break;
                        case ExceptionEventKind.FinallyBegin:
                            block.Instructions.Add(new IRFinallyBegin());
                            break;
                        case ExceptionEventKind.HandlerEnd:
                            block.Instructions.Add(new IRTryEnd());
                            break;
                    }
                }
            }

            // Insert label if this is a branch target
            if (branchTargets.Contains(instr.Offset))
            {
                block.Instructions.Add(new IRLabel { LabelName = $"IL_{instr.Offset:X4}" });
            }

            int beforeCount = block.Instructions.Count;

            try
            {
                ConvertInstruction(instr, block, stack, irMethod, ref tempCounter);
            }
            catch
            {
                block.Instructions.Add(new IRComment { Text = $"WARNING: Unsupported IL instruction: {instr}" });
                Console.Error.WriteLine($"WARNING: Unsupported IL instruction '{instr.OpCode}' at IL_{instr.Offset:X4} in {irMethod.CppName}");
            }

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

    private void ConvertInstruction(ILInstruction instr, IRBasicBlock block, Stack<string> stack,
        IRMethod method, ref int tempCounter)
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
            case Code.Rem: EmitBinaryOp(block, stack, "%", ref tempCounter); break;
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
                    Code = $"{tmp} = {func}<{cppType}>({val});"
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
                block.Instructions.Add(new IRBranch { TargetLabel = $"IL_{target.Offset:X4}" });
                break;
            }

            case Code.Brtrue:
            case Code.Brtrue_S:
            {
                var cond = stack.Count > 0 ? stack.Pop() : "0";
                var target = (Instruction)instr.Operand!;
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
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRFieldAccess
                {
                    ObjectExpr = obj,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    ResultVar = tmp,
                });
                stack.Push(tmp);
                break;
            }

            case Code.Stfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var obj = stack.Count > 0 ? stack.Pop() : "__this";
                block.Instructions.Add(new IRFieldAccess
                {
                    ObjectExpr = obj,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    IsStore = true,
                    StoreValue = val,
                });
                break;
            }

            case Code.Ldsfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var typeCppName = GetMangledTypeNameForRef(fieldRef.DeclaringType);
                var fieldCacheKey = ResolveCacheKey(fieldRef.DeclaringType);
                EmitCctorGuardIfNeeded(block, fieldCacheKey, typeCppName);
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRStaticFieldAccess
                {
                    TypeCppName = typeCppName,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    ResultVar = tmp,
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
                block.Instructions.Add(new IRStaticFieldAccess
                {
                    TypeCppName = typeCppName,
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    IsStore = true,
                    StoreValue = val,
                });
                break;
            }

            case Code.Ldflda:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var obj = stack.Count > 0 ? stack.Pop() : "__this";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = &{obj}->{CppNameMapper.MangleFieldName(fieldRef.Name)};"
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
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = &{typeCppName}_statics.{CppNameMapper.MangleFieldName(fieldRef.Name)};"
                });
                stack.Push(tmp);
                break;
            }

            // ===== Indirect Load/Store (pointer dereference) =====
            case Code.Ldobj:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                if (CppNameMapper.IsValueType(typeRef.FullName))
                {
                    var cppType = CppNameMapper.GetCppTypeName(typeRef.FullName);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = *({cppType}*){addr};"
                    });
                }
                else
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = *{addr};"
                    });
                }
                stack.Push(tmp);
                break;
            }

            case Code.Stobj:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
                if (CppNameMapper.IsValueType(typeRef.FullName))
                {
                    var cppType = CppNameMapper.GetCppTypeName(typeRef.FullName);
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
                    Code = $"auto {tmp} = *({cppType}*){addr};"
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
                    Code = $"auto {tmp} = *(cil2cpp::Object**){addr};"
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
                EmitMethodCall(block, stack, methodRef, instr.OpCode == Code.Callvirt, ref tempCounter);
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
                    block.Instructions.Add(new IRReturn { Value = stack.Pop() });
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
            case Code.Conv_I:   EmitConversion(block, stack, "intptr_t", ref tempCounter); break;
            case Code.Conv_U1:  EmitConversion(block, stack, "uint8_t", ref tempCounter); break;
            case Code.Conv_U2:  EmitConversion(block, stack, "uint16_t", ref tempCounter); break;
            case Code.Conv_U4:  EmitConversion(block, stack, "uint32_t", ref tempCounter); break;
            case Code.Conv_U8:  EmitConversion(block, stack, "uint64_t", ref tempCounter); break;
            case Code.Conv_U:   EmitConversion(block, stack, "uintptr_t", ref tempCounter); break;
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
            // IL prefix opcodes — safe to ignore in AOT monomorphized code
            case Code.Constrained:  // constrained. prefix: monomorphization resolves concrete types
            case Code.Tail:         // tail. prefix: tail call hint, not required for correctness
            case Code.Readonly:     // readonly. prefix: ldelema advisory, no semantic effect
            case Code.Volatile:     // volatile. prefix: memory ordering hint
            case Code.Unaligned:    // unaligned. prefix: alignment hint
                break;

            // ===== Array Operations =====
            case Code.Newarr:
            {
                var elemType = (TypeReference)instr.Operand!;
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                var elemCppType = CppNameMapper.MangleTypeName(elemType.FullName);
                // Ensure TypeInfo exists for primitive element types
                if (CppNameMapper.IsPrimitive(elemType.FullName))
                    _module.RegisterPrimitiveTypeInfo(elemType.FullName);
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::array_create(&{elemCppType}_TypeInfo, {length});"
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
                    Code = $"auto {tmp} = cil2cpp::array_length({arr});"
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
                var elemType = CppNameMapper.GetCppTypeName(typeRef.FullName);
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
            case Code.Stelem_R4: case Code.Stelem_R8: case Code.Stelem_Ref: case Code.Stelem_I:
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

            case Code.Stelem_Any:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var elemType = CppNameMapper.GetCppTypeName(typeRef.FullName);
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
                var elemType = CppNameMapper.GetCppTypeName(typeRef.FullName);
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var arr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({elemType}*)cil2cpp::array_get_element_ptr({arr}, {index});"
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
                var castTargetType = GetMangledTypeNameForRef(typeRef);
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = castTargetType + "*",
                    ResultVar = tmp,
                    IsSafe = false
                });
                stack.Push(tmp);
                break;
            }

            case Code.Isinst:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                var isinstTargetType = GetMangledTypeNameForRef(typeRef);
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = isinstTargetType + "*",
                    ResultVar = tmp,
                    IsSafe = true
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
            case Code.Endfilter:
                // Handled by macros, no-op in generated code
                break;

            // ===== Value Type Operations =====
            case Code.Initobj:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var addr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var typeCpp = GetMangledTypeNameForRef(typeRef);
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
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";

                if (IsNullableType(typeRef))
                {
                    // ECMA-335 III.4.1: box Nullable<T> →
                    //   if !hasValue → null; else → box the inner T value
                    var git = (GenericInstanceType)typeRef;
                    var innerTypeName = git.GenericArguments[0].FullName;
                    var innerTypeCpp = CppNameMapper.MangleTypeName(innerTypeName);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{tmp} = {val}.f_hasValue"
                            + $" ? (cil2cpp::Object*)cil2cpp::box<{innerTypeCpp}>({val}.f_value, &{innerTypeCpp}_TypeInfo)"
                            + $" : nullptr;"
                    });
                }
                else
                {
                    var typeCpp = CppNameMapper.MangleTypeName(typeRef.FullName);
                    block.Instructions.Add(new IRBox
                    {
                        ValueExpr = val,
                        ValueTypeCppName = typeCpp,
                        ResultVar = tmp
                    });
                }
                stack.Push(tmp);
                break;
            }

            case Code.Unbox_Any:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";

                if (CppNameMapper.IsValueType(typeRef.FullName))
                {
                    // ECMA-335 III.4.33: for value types, extract a copy
                    var typeCpp = CppNameMapper.MangleTypeName(typeRef.FullName);
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
                    var castTargetType = GetMangledTypeNameForRef(typeRef);
                    block.Instructions.Add(new IRCast
                    {
                        SourceExpr = obj,
                        TargetTypeCpp = castTargetType + "*",
                        ResultVar = tmp,
                        IsSafe = false
                    });
                }
                stack.Push(tmp);
                break;
            }

            case Code.Unbox:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var typeCpp = CppNameMapper.MangleTypeName(typeRef.FullName);
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
                var typeCpp = CppNameMapper.MangleTypeName(typeRef.FullName);
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = static_cast<int32_t>(sizeof({typeCpp}));"
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
                        Code = $"auto {tmp} = {castExpr}({argStr});"
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
        Code.Ldind_I => "intptr_t",
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
            Code = $"auto {tmp} = {func}<{type}>(({type}){a}, ({type}){b});"
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
}
