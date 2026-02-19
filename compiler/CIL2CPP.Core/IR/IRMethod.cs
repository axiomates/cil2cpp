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
    public bool IsFinalizer { get; set; }
    public bool IsOperator { get; set; }
    public bool IsGenericInstance { get; set; }
    public bool IsInternalCall { get; set; }
    /// <summary>
    /// True if this method has an icall mapping in ICallRegistry.
    /// When set, the method body is dead code (callers use the icall instead).
    /// </summary>
    public bool HasICallMapping { get; set; }
    public bool IsNewSlot { get; set; }
    public bool IsVarArg { get; set; }
    public bool IsPInvoke { get; set; }
    public string? PInvokeModule { get; set; }
    public string? PInvokeEntryPoint { get; set; }

    /// <summary>ECMA-335 II.15.5.2: Character set marshaling for P/Invoke</summary>
    public PInvokeCharSet PInvokeCharSet { get; set; } = PInvokeCharSet.Ansi;

    /// <summary>ECMA-335 II.15.5.1: Calling convention for P/Invoke</summary>
    public PInvokeCallingConvention PInvokeCallingConvention { get; set; } = PInvokeCallingConvention.Cdecl;

    /// <summary>Whether the runtime should capture the native error code after the P/Invoke call</summary>
    public bool PInvokeSetLastError { get; set; }
    public string? OperatorName { get; set; }
    public int VTableSlot { get; set; } = -1;

    /// <summary>Raw ECMA-335 MethodAttributes value (II.23.1.10)</summary>
    public uint Attributes { get; set; }

    /// <summary>Custom attributes applied to this method</summary>
    public List<IRCustomAttribute> CustomAttributes { get; } = new();

    /// <summary>
    /// Explicit interface overrides (from Cecil's .override directive).
    /// Each entry is (InterfaceTypeName, MethodName) — e.g. ("IFoo", "Method").
    /// </summary>
    public List<(string InterfaceTypeName, string MethodName)> ExplicitOverrides { get; } = new();

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
    public string ILTypeName { get; set; } = "";
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
    /// <summary>
    /// Pinned local (fixed statement). BoehmGC is conservative so pinning is a no-op.
    /// </summary>
    public bool IsPinned { get; set; }
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
