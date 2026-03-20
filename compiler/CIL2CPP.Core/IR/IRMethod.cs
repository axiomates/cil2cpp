namespace CIL2CPP.Core.IR;

/// <summary>
/// Classification of methods as known dead code at the IR level.
/// Tagged during method shell creation based on Cecil metadata (namespace, declaring type),
/// NOT on rendered C++ function names.
/// </summary>
public enum DeadCodeCategory : byte
{
    None = 0,
    /// <summary>SIMD intrinsics + vector methods + helpers — guarded by IsSupported/IsHardwareAccelerated = false</summary>
    Simd = 1,
    /// <summary>EventSource diagnostics — guarded by IsEnabled() = false in AOT</summary>
    EventSource = 2,
    /// <summary>AOT-incompatible operations (AssemblyLoadContext) — no runtime equivalent</summary>
    AotIncompatible = 3,
}

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

    /// <summary>[MarshalAs] on the return type for P/Invoke methods</summary>
    public MarshalAsType? ReturnMarshalAs { get; set; }
    public string? OperatorName { get; set; }

    /// <summary>
    /// If this method was stubbed at IR level (e.g., CLR-internal type dependency),
    /// this contains the detailed reason. Null if not stubbed at IR level.
    /// </summary>
    public string? IrStubReason { get; set; }

    /// <summary>
    /// Dead code classification based on Cecil metadata (namespace/type).
    /// Set during method shell creation (Pass 3) and generic specialization.
    /// </summary>
    public DeadCodeCategory DeadCodeCategory { get; set; } = DeadCodeCategory.None;

    /// <summary>TypeInfo names referenced by this method's body (e.g., for casts, boxing, interface dispatch).</summary>
    public HashSet<string> ReferencedTypeInfoNames { get; } = new();

    /// <summary>Pointer type names referenced by this method's body (need forward declarations).</summary>
    public HashSet<string> ReferencedPointerTypeNames { get; } = new();

    /// <summary>Whether ComputeTypeReferences() has been called.</summary>
    private bool _typeRefsComputed;

    /// <summary>
    /// Collect type references from all instructions into ReferencedTypeInfoNames and ReferencedPointerTypeNames.
    /// Called after body compilation. Replaces post-render string scanning (CollectTypeInfoRefs/CollectBodyPointerTypeRefs).
    /// </summary>
    public void ComputeTypeReferences()
    {
        ReferencedTypeInfoNames.Clear();
        ReferencedPointerTypeNames.Clear();
        foreach (var block in BasicBlocks)
            foreach (var instr in block.Instructions)
                instr.CollectTypeReferences(ReferencedTypeInfoNames, ReferencedPointerTypeNames);
        _typeRefsComputed = true;
    }

    /// <summary>
    /// Ensure type references are computed. Lazy — computes on first access if not already done.
    /// Called by code generation to handle methods compiled via paths that don't call ConvertMethodBody.
    /// </summary>
    public void EnsureTypeReferencesComputed()
    {
        if (!_typeRefsComputed && BasicBlocks.Count > 0)
            ComputeTypeReferences();
    }

    public int VTableSlot { get; set; } = -1;

    /// <summary>Raw ECMA-335 MethodAttributes value (II.23.1.10)</summary>
    public uint Attributes { get; set; }

    /// <summary>Custom attributes applied to this method</summary>
    public List<IRCustomAttribute> CustomAttributes { get; } = new();

    /// <summary>
    /// Authoritative C++ types for temporary variables (__tN), recorded during IL→IR conversion.
    /// DetermineTempVarTypes in codegen prefers these over regex-based inference.
    /// </summary>
    public Dictionary<string, string> TempVarTypes { get; } = new();

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

    /// <summary>
    /// [MarshalAs] unmanaged type override for P/Invoke marshaling (ECMA-335 II.15.5.4).
    /// When set, overrides the default marshaling for this parameter.
    /// </summary>
    public MarshalAsType? MarshalAs { get; set; }

    /// <summary>
    /// For MarshalAs.LPArray: index of the parameter that specifies the array size.
    /// -1 if not specified.
    /// </summary>
    public int MarshalAsSizeParamIndex { get; set; } = -1;

    /// <summary>
    /// C.7.2: P/Invoke parameter direction ([In]/[Out] attributes).
    /// Controls copy-back semantics after native call.
    /// </summary>
    public PInvokeParameterDirection PInvokeDirection { get; set; } = PInvokeParameterDirection.In;
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
