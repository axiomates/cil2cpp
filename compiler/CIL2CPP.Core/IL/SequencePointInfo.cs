namespace CIL2CPP.Core.IL;

/// <summary>
/// Represents a mapping from an IL offset to a source code location.
/// Wraps Mono.Cecil.Cil.SequencePoint.
/// </summary>
public class SequencePointInfo
{
    public int ILOffset { get; }
    public string SourceFile { get; }
    public int StartLine { get; }
    public int StartColumn { get; }
    public int EndLine { get; }
    public int EndColumn { get; }

    /// <summary>Whether this is a "hidden" sequence point (line = 0xFEEFEE).</summary>
    public bool IsHidden { get; }

    public SequencePointInfo(Mono.Cecil.Cil.SequencePoint sp)
    {
        ILOffset = sp.Offset;
        SourceFile = sp.Document?.Url ?? "";
        StartLine = sp.StartLine;
        StartColumn = sp.StartColumn;
        EndLine = sp.EndLine;
        EndColumn = sp.EndColumn;
        // 0xFEEFEE (16707566) is the "hidden" marker in PDB files
        IsHidden = sp.StartLine == 0xFEEFEE;
    }
}
