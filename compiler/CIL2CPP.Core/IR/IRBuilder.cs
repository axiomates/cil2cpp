using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;
using CIL2CPP.Core;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Converts IL (from Mono.Cecil) into IR representation.
/// </summary>
public class IRBuilder
{
    private readonly AssemblyReader _reader;
    private readonly IRModule _module;
    private readonly BuildConfiguration _config;
    private readonly Dictionary<string, IRType> _typeCache = new();

    public IRBuilder(AssemblyReader reader, BuildConfiguration? config = null)
    {
        _reader = reader;
        _config = config ?? BuildConfiguration.Release;
        _module = new IRModule { Name = reader.AssemblyName };
    }

    /// <summary>
    /// Build the complete IR module from the assembly.
    /// </summary>
    public IRModule Build()
    {
        // Pass 1: Create all type shells (no fields/methods yet)
        foreach (var typeDef in _reader.GetAllTypes())
        {
            var irType = CreateTypeShell(typeDef);
            _module.Types.Add(irType);
            _typeCache[typeDef.FullName] = irType;
        }

        // Pass 2: Fill in fields, base types, interfaces
        foreach (var typeDef in _reader.GetAllTypes())
        {
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType))
            {
                PopulateTypeDetails(typeDef, irType);
            }
        }

        // Pass 3: Convert methods
        foreach (var typeDef in _reader.GetAllTypes())
        {
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType))
            {
                foreach (var methodDef in typeDef.Methods)
                {
                    var irMethod = ConvertMethod(methodDef, irType);
                    irType.Methods.Add(irMethod);

                    // Detect entry point
                    if (methodDef.Name == "Main" && methodDef.IsStatic)
                    {
                        irMethod.IsEntryPoint = true;
                        _module.EntryPoint = irMethod;
                    }
                }
            }
        }

        // Pass 4: Build vtables
        foreach (var irType in _module.Types)
        {
            BuildVTable(irType);
        }

        return _module;
    }

    private IRType CreateTypeShell(TypeDefinitionInfo typeDef)
    {
        var cppName = CppNameMapper.MangleTypeName(typeDef.FullName);

        return new IRType
        {
            ILFullName = typeDef.FullName,
            CppName = cppName,
            Name = typeDef.Name,
            Namespace = typeDef.Namespace,
            IsValueType = typeDef.IsValueType,
            IsInterface = typeDef.IsInterface,
            IsAbstract = typeDef.IsAbstract,
            IsSealed = typeDef.IsSealed,
            IsEnum = typeDef.IsEnum,
        };
    }

    private void PopulateTypeDetails(TypeDefinitionInfo typeDef, IRType irType)
    {
        // Base type
        if (typeDef.BaseTypeName != null && _typeCache.TryGetValue(typeDef.BaseTypeName, out var baseType))
        {
            irType.BaseType = baseType;
        }

        // Interfaces
        foreach (var ifaceName in typeDef.InterfaceNames)
        {
            if (_typeCache.TryGetValue(ifaceName, out var iface))
            {
                irType.Interfaces.Add(iface);
            }
        }

        // Fields
        foreach (var fieldDef in typeDef.Fields)
        {
            var irField = new IRField
            {
                Name = fieldDef.Name,
                CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                FieldTypeName = fieldDef.TypeName,
                IsStatic = fieldDef.IsStatic,
                IsPublic = fieldDef.IsPublic,
                DeclaringType = irType,
            };

            if (_typeCache.TryGetValue(fieldDef.TypeName, out var fieldType))
            {
                irField.FieldType = fieldType;
            }

            if (fieldDef.IsStatic)
                irType.StaticFields.Add(irField);
            else
                irType.Fields.Add(irField);
        }

        // Calculate instance size
        CalculateInstanceSize(irType);
    }

    private void CalculateInstanceSize(IRType irType)
    {
        // Start with object header (vtable pointer + GC mark + sync block)
        int size = irType.IsValueType ? 0 : 16; // sizeof(Object)

        // Add base type fields
        if (irType.BaseType != null)
        {
            size = irType.BaseType.InstanceSize;
        }

        // Add own fields
        foreach (var field in irType.Fields)
        {
            int fieldSize = GetFieldSize(field.FieldTypeName);
            int alignment = GetFieldAlignment(field.FieldTypeName);

            // Align
            size = (size + alignment - 1) & ~(alignment - 1);
            field.Offset = size;
            size += fieldSize;
        }

        // Align to pointer size
        size = (size + 7) & ~7;
        irType.InstanceSize = size;
    }

    private int GetFieldSize(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Int16" or "System.UInt16" or "System.Char" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            _ => 8 // Pointer size (reference types)
        };
    }

    private int GetFieldAlignment(string typeName)
    {
        return Math.Min(GetFieldSize(typeName), 8);
    }

    private IRMethod ConvertMethod(IL.MethodInfo methodDef, IRType declaringType)
    {
        var cppName = CppNameMapper.MangleMethodName(declaringType.CppName, methodDef.Name);

        var irMethod = new IRMethod
        {
            Name = methodDef.Name,
            CppName = cppName,
            DeclaringType = declaringType,
            ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(methodDef.ReturnTypeName),
            IsStatic = methodDef.IsStatic,
            IsVirtual = methodDef.IsVirtual,
            IsAbstract = methodDef.IsAbstract,
            IsConstructor = methodDef.IsConstructor,
        };

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
                CppTypeName = CppNameMapper.GetCppTypeForDecl(paramDef.TypeName),
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
                CppTypeName = CppNameMapper.GetCppTypeForDecl(localDef.TypeName),
            });
        }

        // Convert method body to IR instructions
        if (methodDef.HasBody && !methodDef.IsAbstract)
        {
            ConvertMethodBody(methodDef, irMethod);
        }

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
            if (ILInstructionCategory.IsBranch(instr.OpCode) && instr.Operand is Instruction target)
            {
                branchTargets.Add(target.Offset);
            }
        }

        // Stack simulation
        var stack = new Stack<string>();
        int tempCounter = 0;

        foreach (var instr in instructions)
        {
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
                // For unsupported instructions, add a comment
                block.Instructions.Add(new IRComment { Text = $"TODO: {instr}" });
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
                stack.Push($"{(float)instr.Operand!}f");
                break;
            case Code.Ldc_R8:
                stack.Push($"{(double)instr.Operand!}");
                break;

            // ===== Load String =====
            case Code.Ldstr:
                var strVal = (string)instr.Operand!;
                var strId = _module.RegisterStringLiteral(strVal);
                stack.Push(strId);
                break;

            case Code.Ldnull:
                stack.Push("nullptr");
                break;

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
            case Code.Clt: EmitBinaryOp(block, stack, "<", ref tempCounter); break;

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
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRStaticFieldAccess
                {
                    TypeCppName = CppNameMapper.MangleTypeName(fieldRef.DeclaringType.FullName),
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    ResultVar = tmp,
                });
                stack.Push(tmp);
                break;
            }

            case Code.Stsfld:
            {
                var fieldRef = (FieldReference)instr.Operand!;
                var val = stack.Count > 0 ? stack.Pop() : "0";
                block.Instructions.Add(new IRStaticFieldAccess
                {
                    TypeCppName = CppNameMapper.MangleTypeName(fieldRef.DeclaringType.FullName),
                    FieldCppName = CppNameMapper.MangleFieldName(fieldRef.Name),
                    IsStore = true,
                    StoreValue = val,
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
            case Code.Conv_I4:
            {
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRConversion { SourceExpr = val, TargetType = "int32_t", ResultVar = tmp });
                stack.Push(tmp);
                break;
            }
            case Code.Conv_I8:
            {
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRConversion { SourceExpr = val, TargetType = "int64_t", ResultVar = tmp });
                stack.Push(tmp);
                break;
            }
            case Code.Conv_R4:
            {
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRConversion { SourceExpr = val, TargetType = "float", ResultVar = tmp });
                stack.Push(tmp);
                break;
            }
            case Code.Conv_R8:
            {
                var val = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRConversion { SourceExpr = val, TargetType = "double", ResultVar = tmp });
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
                break;

            // ===== Array Operations =====
            case Code.Newarr:
            {
                var elemType = (TypeReference)instr.Operand!;
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                var elemCppType = CppNameMapper.MangleTypeName(elemType.FullName);
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

            // ===== Type Operations =====
            case Code.Castclass:
            {
                var typeRef = (TypeReference)instr.Operand!;
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                var targetType = CppNameMapper.MangleTypeName(typeRef.FullName);
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = targetType + "*",
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
                var targetType = CppNameMapper.MangleTypeName(typeRef.FullName);
                block.Instructions.Add(new IRCast
                {
                    SourceExpr = obj,
                    TargetTypeCpp = targetType + "*",
                    ResultVar = tmp,
                    IsSafe = true
                });
                stack.Push(tmp);
                break;
            }

            default:
                block.Instructions.Add(new IRComment { Text = $"TODO: {instr}" });
                break;
        }
    }

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
        var irCall = new IRCall();

        // Map known BCL methods
        var mappedName = MapBclMethod(methodRef);
        if (mappedName != null)
        {
            irCall.FunctionName = mappedName;
        }
        else
        {
            var typeCpp = CppNameMapper.MangleTypeName(methodRef.DeclaringType.FullName);
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

        // Return value
        if (methodRef.ReturnType.FullName != "System.Void")
        {
            var tmp = $"__t{tempCounter++}";
            irCall.ResultVar = tmp;
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {irCall.FunctionName}({string.Join(", ", irCall.Arguments)});"
            });
            stack.Push(tmp);
        }
        else
        {
            block.Instructions.Add(irCall);
        }
    }

    private void EmitNewObj(IRBasicBlock block, Stack<string> stack, MethodReference ctorRef,
        ref int tempCounter)
    {
        var typeCpp = CppNameMapper.MangleTypeName(ctorRef.DeclaringType.FullName);
        var ctorName = CppNameMapper.MangleMethodName(typeCpp, ".ctor");
        var tmp = $"__t{tempCounter++}";

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
                if (methodRef.Parameters.Count == 0)
                    return "cil2cpp::System::Console_WriteLine";
                var paramType = methodRef.Parameters[0].ParameterType.FullName;
                return paramType switch
                {
                    "System.String" => "cil2cpp::System::Console_WriteLine",
                    "System.Int32" => "cil2cpp::System::Console_WriteLine",
                    "System.Int64" => "cil2cpp::System::Console_WriteLine",
                    "System.Single" => "cil2cpp::System::Console_WriteLine",
                    "System.Double" => "cil2cpp::System::Console_WriteLine",
                    "System.Boolean" => "cil2cpp::System::Console_WriteLine",
                    "System.Object" => "cil2cpp::System::Console_WriteLine",
                    _ => "cil2cpp::System::Console_WriteLine"
                };
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

        return null;
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

        // Override or add virtual methods
        foreach (var method in irType.Methods.Where(m => m.IsVirtual))
        {
            var existing = irType.VTable.FirstOrDefault(e => e.MethodName == method.Name);
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
}
