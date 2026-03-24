using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Per-method mutable state used during IL→IR body compilation.
/// These fields are reset at the start of each ConvertMethodBody call.
/// Extracted from IRBuilder instance fields to enable parallel compilation
/// via ThreadLocal&lt;MethodCompilationContext&gt;.
/// </summary>
internal class MethodCompilationContext
{
    /// <summary>volatile. prefix flag — set by Code.Volatile, consumed by next field access</summary>
    public bool PendingVolatile;

    /// <summary>constrained. prefix type — set by Code.Constrained, consumed by next callvirt</summary>
    public TypeReference? ConstrainedType;

    /// <summary>Fault handler tracking — fault handlers use IRFaultBegin/IRFaultEnd conditional guard</summary>
    public bool InFaultHandler;

    /// <summary>Exception filter tracking — set during filter evaluation region (FilterStart → endfilter)</summary>
    public bool InFilterRegion;
    public int EndfilterOffset = -1;
    public (int TryStart, int TryEnd) CurrentFilterTryKey;

    /// <summary>Filter chain tracking: total filters per try block, and current index for goto labels</summary>
    public Dictionary<(int, int), int> FilterCountPerTry = new();
    public Dictionary<(int, int), int> FilterIndexPerTry = new();

    /// <summary>Multi-filter tracking: try regions that already have a filter/catch handler emitted</summary>
    public HashSet<(int TryStart, int TryEnd)> TrysWithHandlerEmitted = new();

    /// <summary>Regions that have at least one filter (for IRCatchBegin.AfterFilter)</summary>
    public HashSet<(int TryStart, int TryEnd)> TrysWithFilter = new();

    /// <summary>Current filter handler's skip label (for IRFilterHandlerEnd emission)</summary>
    public string? CurrentFilterSkipLabel;

    /// <summary>True when an after-filter catch's if-block is open and needs closing at HandlerEnd</summary>
    public bool AfterFilterCatchOpen;

    /// <summary>
    /// Leave-crossing tracking — per-method data for leave dispatch across protected regions.
    /// Maps leave instruction offset → (targetOffset, innermostCrossedTryStart, innermostCrossedTryEnd, fromHandlerBody)
    /// </summary>
    public Dictionary<int, (int TargetOffset, int InnermostTryStart, int InnermostTryEnd, bool FromHandlerBody)>? LeaveCrossingTargets;

    /// <summary>
    /// Leave dispatch info per region — maps (TryStart,TryEnd) → list of (targetOffset, chainRegion?)
    /// </summary>
    public Dictionary<(int TryStart, int TryEnd),
        List<(int TargetOffset, (int TryStart, int TryEnd)? ChainRegion)>>? RegionLeaveDispatch;

    /// <summary>
    /// Compile-time constant locals: tracks local variables known to hold compile-time constant values.
    /// Used for dead branch elimination: IsSupported=0 → stloc → ldloc → brfalse eliminates dead SIMD paths.
    /// Cleared at method entry and at control flow merge points (branch target labels).
    /// </summary>
    public Dictionary<int, int> CompileTimeConstantLocals = new();

    /// <summary>
    /// Active generic type parameter map (set during ConvertMethodBodyWithGenerics).
    /// Maps generic parameter names (e.g., "T") to concrete type names (e.g., "System.Int32").
    /// </summary>
    public Dictionary<string, string>? ActiveTypeParamMap;

    /// <summary>
    /// Reset all per-method state. Called at the start of each ConvertMethodBody.
    /// </summary>
    public void Reset()
    {
        PendingVolatile = false;
        ConstrainedType = null;
        InFaultHandler = false;
        InFilterRegion = false;
        EndfilterOffset = -1;
        CurrentFilterTryKey = default;
        FilterCountPerTry.Clear();
        FilterIndexPerTry.Clear();
        TrysWithHandlerEmitted.Clear();
        TrysWithFilter.Clear();
        CurrentFilterSkipLabel = null;
        AfterFilterCatchOpen = false;
        LeaveCrossingTargets = null;
        RegionLeaveDispatch = null;
        CompileTimeConstantLocals.Clear();
        // Note: ActiveTypeParamMap is NOT reset here — it is managed externally by
        // ConvertMethodBodyWithGenerics (set before, cleared after ConvertMethodBody).
    }
}
