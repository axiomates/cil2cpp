namespace CIL2CPP.Core.IR;

/// <summary>
/// Represents a method in the IR.
/// </summary>
public class IRMethod
{
    /// <summary>Original .NET name</summary>
    public string Name { get; set; } = "";

    /// <summary>C++ function name (fully qualified)</summary>
    public string CppName { get; set; } = "";

    /// <summary>Declaring type</summary>
    public IRType? DeclaringType { get; set; }

    /// <summary>Return type</summary>
    public IRType? ReturnType { get; set; }

    /// <summary>Return type name (for primitives)</summary>
    public string ReturnTypeCpp { get; set; } = "void";

    /// <summary>Parameters</summary>
    public List<IRParameter> Parameters { get; } = new();

    /// <summary>Local variables</summary>
    public List<IRLocal> Locals { get; } = new();

    /// <summary>Basic blocks (control flow graph)</summary>
    public List<IRBasicBlock> BasicBlocks { get; } = new();

    // Method flags
    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsConstructor { get; set; }
    public bool IsStaticConstructor { get; set; }
    public bool IsEntryPoint { get; set; }
    public int VTableSlot { get; set; } = -1;

    /// <summary>
    /// Generate the C++ function signature.
    /// </summary>
    public string GetCppSignature()
    {
        var parts = new List<string>();

        // 'this' pointer for instance methods
        if (!IsStatic && DeclaringType != null)
        {
            parts.Add($"{DeclaringType.CppName}* __this");
        }

        foreach (var param in Parameters)
        {
            parts.Add($"{param.CppTypeName} {param.CppName}");
        }

        return $"{ReturnTypeCpp} {CppName}({string.Join(", ", parts)})";
    }
}

/// <summary>
/// Method parameter.
/// </summary>
public class IRParameter
{
    public string Name { get; set; } = "";
    public string CppName { get; set; } = "";
    public IRType? ParameterType { get; set; }
    public string CppTypeName { get; set; } = "";
    public int Index { get; set; }
}

/// <summary>
/// Local variable.
/// </summary>
public class IRLocal
{
    public int Index { get; set; }
    public string CppName { get; set; } = "";
    public IRType? LocalType { get; set; }
    public string CppTypeName { get; set; } = "";
}

/// <summary>
/// A basic block in the control flow graph.
/// </summary>
public class IRBasicBlock
{
    public int Id { get; set; }
    public string Label => $"BB_{Id}";
    public List<IRInstruction> Instructions { get; } = new();
}

/// <summary>
/// Represents a source code location for debug mapping.
/// </summary>
public record SourceLocation
{
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public int ILOffset { get; init; } = -1;
}

/// <summary>
/// Base class for all IR instructions.
/// </summary>
public abstract class IRInstruction
{
    /// <summary>Generate C++ code for this instruction.</summary>
    public abstract string ToCpp();

    /// <summary>Source location for debug mapping. Null in Release mode.</summary>
    public SourceLocation? DebugInfo { get; set; }
}

// ============ Concrete IR Instructions ============

public class IRComment : IRInstruction
{
    public string Text { get; set; } = "";
    public override string ToCpp() => $"// {Text}";
}

public class IRAssign : IRInstruction
{
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
    public override string ToCpp() => $"{Target} = {Value};";
}

public class IRDeclareLocal : IRInstruction
{
    public string TypeName { get; set; } = "";
    public string VarName { get; set; } = "";
    public string? InitValue { get; set; }
    public override string ToCpp() =>
        InitValue != null
            ? $"{TypeName} {VarName} = {InitValue};"
            : $"{TypeName} {VarName} = {{0}};";
}

public class IRReturn : IRInstruction
{
    public string? Value { get; set; }
    public override string ToCpp() =>
        Value != null ? $"return {Value};" : "return;";
}

public class IRCall : IRInstruction
{
    public string FunctionName { get; set; } = "";
    public List<string> Arguments { get; } = new();
    public string? ResultVar { get; set; }
    public bool IsVirtual { get; set; }
    public string? VTableAccess { get; set; }

    public override string ToCpp()
    {
        var args = string.Join(", ", Arguments);
        string call;

        if (IsVirtual && VTableAccess != null)
        {
            call = $"{VTableAccess}({args})";
        }
        else
        {
            call = $"{FunctionName}({args})";
        }

        return ResultVar != null ? $"{ResultVar} = {call};" : $"{call};";
    }
}

public class IRNewObj : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public string CtorName { get; set; } = "";
    public List<string> CtorArgs { get; } = new();
    public string ResultVar { get; set; } = "";

    public override string ToCpp()
    {
        var lines = new List<string>
        {
            $"{ResultVar} = ({TypeCppName}*)cil2cpp::gc::alloc(sizeof({TypeCppName}), &{TypeCppName}_TypeInfo);",
        };

        var allArgs = new List<string> { ResultVar };
        allArgs.AddRange(CtorArgs);
        lines.Add($"{CtorName}({string.Join(", ", allArgs)});");

        return string.Join("\n    ", lines);
    }
}

public class IRBinaryOp : IRInstruction
{
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
    public string Op { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() => $"{ResultVar} = {Left} {Op} {Right};";
}

public class IRUnaryOp : IRInstruction
{
    public string Operand { get; set; } = "";
    public string Op { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() => $"{ResultVar} = {Op}{Operand};";
}

public class IRBranch : IRInstruction
{
    public string TargetLabel { get; set; } = "";
    public override string ToCpp() => $"goto {TargetLabel};";
}

public class IRConditionalBranch : IRInstruction
{
    public string Condition { get; set; } = "";
    public string TrueLabel { get; set; } = "";
    public string? FalseLabel { get; set; }

    public override string ToCpp()
    {
        if (FalseLabel != null)
            return $"if ({Condition}) goto {TrueLabel}; else goto {FalseLabel};";
        return $"if ({Condition}) goto {TrueLabel};";
    }
}

public class IRLabel : IRInstruction
{
    public string LabelName { get; set; } = "";
    public override string ToCpp() => $"{LabelName}:";
}

public class IRSwitch : IRInstruction
{
    public string ValueExpr { get; set; } = "";
    public List<string> CaseLabels { get; } = new();

    public override string ToCpp()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"switch ({ValueExpr}) {{");
        for (int i = 0; i < CaseLabels.Count; i++)
            sb.AppendLine($"        case {i}: goto {CaseLabels[i]};");
        sb.Append("    }");
        return sb.ToString();
    }
}

public class IRFieldAccess : IRInstruction
{
    public string ObjectExpr { get; set; } = "";
    public string FieldCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }

    public override string ToCpp()
    {
        if (IsStore)
            return $"{ObjectExpr}->{FieldCppName} = {StoreValue};";
        return $"{ResultVar} = {ObjectExpr}->{FieldCppName};";
    }
}

public class IRStaticFieldAccess : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public string FieldCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }

    public override string ToCpp()
    {
        var fullName = $"{TypeCppName}_statics.{FieldCppName}";
        if (IsStore)
            return $"{fullName} = {StoreValue};";
        return $"{ResultVar} = {fullName};";
    }
}

public class IRArrayAccess : IRInstruction
{
    public string ArrayExpr { get; set; } = "";
    public string IndexExpr { get; set; } = "";
    public string ElementType { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }

    public override string ToCpp()
    {
        if (IsStore)
            return $"cil2cpp::array_set<{ElementType}>({ArrayExpr}, {IndexExpr}, {StoreValue});";
        return $"{ResultVar} = cil2cpp::array_get<{ElementType}>({ArrayExpr}, {IndexExpr});";
    }
}

public class IRCast : IRInstruction
{
    public string SourceExpr { get; set; } = "";
    public string TargetTypeCpp { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsSafe { get; set; } // 'as' vs cast

    public override string ToCpp()
    {
        if (IsSafe)
            return $"{ResultVar} = ({TargetTypeCpp})cil2cpp::object_as((cil2cpp::Object*){SourceExpr}, &{TargetTypeCpp.TrimEnd('*')}_TypeInfo);";
        return $"{ResultVar} = ({TargetTypeCpp})cil2cpp::object_cast((cil2cpp::Object*){SourceExpr}, &{TargetTypeCpp.TrimEnd('*')}_TypeInfo);";
    }
}

public class IRConversion : IRInstruction
{
    public string SourceExpr { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() => $"{ResultVar} = static_cast<{TargetType}>({SourceExpr});";
}

public class IRNullCheck : IRInstruction
{
    public string Expr { get; set; } = "";
    public override string ToCpp() => $"cil2cpp::null_check({Expr});";
}

public class IRInitObj : IRInstruction
{
    public string AddressExpr { get; set; } = "";
    public string TypeCppName { get; set; } = "";
    public override string ToCpp() =>
        $"std::memset({AddressExpr}, 0, sizeof({TypeCppName}));";
}

public class IRBox : IRInstruction
{
    public string ValueExpr { get; set; } = "";
    public string ValueTypeCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() =>
        $"{ResultVar} = cil2cpp::box<{ValueTypeCppName}>({ValueExpr}, &{ValueTypeCppName}_TypeInfo);";
}

public class IRUnbox : IRInstruction
{
    public string ObjectExpr { get; set; } = "";
    public string ValueTypeCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsUnboxAny { get; set; }
    public override string ToCpp() => IsUnboxAny
        ? $"{ResultVar} = cil2cpp::unbox<{ValueTypeCppName}>({ObjectExpr});"
        : $"{ResultVar} = cil2cpp::unbox_ptr<{ValueTypeCppName}>({ObjectExpr});";
}

public class IRStaticCtorGuard : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public override string ToCpp() => $"{TypeCppName}_ensure_cctor();";
}

// ============ Exception Handling Instructions ============

public class IRTryBegin : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_TRY";
}

public class IRCatchBegin : IRInstruction
{
    public string? ExceptionTypeCppName { get; set; }
    public override string ToCpp() => ExceptionTypeCppName != null
        ? $"CIL2CPP_CATCH({ExceptionTypeCppName})" : "CIL2CPP_CATCH_ALL";
}

public class IRFinallyBegin : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_FINALLY";
}

public class IRTryEnd : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_END_TRY";
}

public class IRThrow : IRInstruction
{
    public string ExceptionExpr { get; set; } = "";
    public override string ToCpp() =>
        $"cil2cpp::throw_exception(static_cast<cil2cpp::Exception*>({ExceptionExpr}));";
}

public class IRRethrow : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_RETHROW;";
}

public class IRRawCpp : IRInstruction
{
    public string Code { get; set; } = "";
    public override string ToCpp() => Code;
}
