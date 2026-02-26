using System.Text;
using System.Text.Json;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Core.CodeGen;

/// <summary>
/// Root-cause categories for stubbed methods.
/// </summary>
public enum StubRootCause
{
    /// <summary>Method's declaring type or its locals/params reference a CLR-internal type.</summary>
    ClrInternalType,

    /// <summary>Method uses parameter/return types not in the known type set.</summary>
    UnknownParameterTypes,

    /// <summary>Method body references types not in the known type set.</summary>
    UnknownBodyReferences,

    /// <summary>Method calls a function that was not declared (itself stubbed or missing).</summary>
    UndeclaredFunction,

    /// <summary>Method matches a known broken C++ pattern (Intrinsics, self-recursion, etc.).</summary>
    KnownBrokenPattern,

    /// <summary>Trial-rendered C++ code contains error patterns.</summary>
    RenderedBodyError,

    /// <summary>Method declared in header but no body was emitted (runtime-provided or unreachable).</summary>
    MissingBody,

    /// <summary>Method is stubbed because it calls another stubbed method (cascade).</summary>
    Cascade,
}

/// <summary>
/// Detailed information about why a method was stubbed.
/// </summary>
public record StubInfo(
    /// <summary>IL full name of the declaring type.</summary>
    string TypeName,
    /// <summary>IL method name.</summary>
    string MethodName,
    /// <summary>Mangled C++ function name.</summary>
    string CppName,
    /// <summary>High-level stub reason category.</summary>
    StubRootCause RootCause,
    /// <summary>Detailed description of the root cause.</summary>
    string Detail
);

/// <summary>
/// Analyzes stub root causes, tracks cascade dependencies, and computes
/// unlock potential for each root cause. Used by --analyze-stubs.
/// </summary>
public class StubAnalyzer
{
    private readonly List<StubInfo> _stubs = new();

    /// <summary>
    /// Map: CppName → StubInfo for quick lookup.
    /// </summary>
    private readonly Dictionary<string, StubInfo> _stubByCppName = new();

    /// <summary>
    /// Map: CppName → set of CppNames that this method calls (call graph).
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _callGraph = new();

    /// <summary>
    /// Map: CppName → set of CppNames that call this method (reverse call graph).
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _reverseCallGraph = new();

    /// <summary>All stubs recorded.</summary>
    public IReadOnlyList<StubInfo> Stubs => _stubs;

    /// <summary>
    /// Record a stub with its root cause.
    /// </summary>
    public void AddStub(string typeName, string methodName, string cppName,
        StubRootCause rootCause, string detail)
    {
        var info = new StubInfo(typeName, methodName, cppName, rootCause, detail);
        _stubs.Add(info);
        // Keep first occurrence if duplicate CppName
        _stubByCppName.TryAdd(cppName, info);
    }

    /// <summary>
    /// Record a call edge: callerCppName calls calleeCppName.
    /// Used for cascade analysis.
    /// </summary>
    public void AddCallEdge(string callerCppName, string calleeCppName)
    {
        if (!_callGraph.TryGetValue(callerCppName, out var callees))
        {
            callees = new HashSet<string>();
            _callGraph[callerCppName] = callees;
        }
        callees.Add(calleeCppName);

        if (!_reverseCallGraph.TryGetValue(calleeCppName, out var callers))
        {
            callers = new HashSet<string>();
            _reverseCallGraph[calleeCppName] = callers;
        }
        callers.Add(callerCppName);
    }

    /// <summary>
    /// Check if a method (by CppName) is stubbed.
    /// </summary>
    public bool IsStubbed(string cppName) => _stubByCppName.ContainsKey(cppName);

    /// <summary>
    /// Analyze all stubs and compute cascade impact.
    /// Call this after all stubs have been recorded.
    /// </summary>
    public StubAnalysisResult Analyze()
    {
        var result = new StubAnalysisResult();

        // 1. Group by root cause category
        foreach (var group in _stubs.GroupBy(s => s.RootCause))
        {
            result.CountByRootCause[group.Key] = group.Count();
        }

        // 2. Compute cascade chains: for each "UndeclaredFunction" stub,
        //    find the root cause of the function it couldn't call
        var cascadeRoots = new Dictionary<string, int>(); // detail → cascade count
        foreach (var stub in _stubs.Where(s => s.RootCause == StubRootCause.UndeclaredFunction))
        {
            // The detail contains the undeclared function name
            var rootDetail = TraceRootCause(stub);
            if (!cascadeRoots.TryGetValue(rootDetail, out _))
                cascadeRoots[rootDetail] = 0;
            cascadeRoots[rootDetail]++;
        }
        result.CascadeRoots = cascadeRoots;

        // 3. Compute CLR-internal type impact: for each CLR-internal type,
        //    count how many methods are directly stubbed because of it,
        //    plus cascaded stubs
        var clrTypeImpact = new Dictionary<string, TypeImpact>();
        foreach (var stub in _stubs.Where(s => s.RootCause == StubRootCause.ClrInternalType))
        {
            var typeName = ExtractClrTypeName(stub.Detail);
            if (!clrTypeImpact.TryGetValue(typeName, out var impact))
            {
                impact = new TypeImpact(typeName);
                clrTypeImpact[typeName] = impact;
            }
            impact.DirectStubs++;
            impact.DirectMethods.Add(stub.CppName);
        }

        // For each CLR-internal type, compute transitive cascade (methods that call
        // the directly-stubbed methods, which themselves become stubs)
        foreach (var (typeName, impact) in clrTypeImpact)
        {
            var cascaded = ComputeTransitiveDependents(impact.DirectMethods);
            impact.CascadeStubs = cascaded.Count;
            impact.TotalImpact = impact.DirectStubs + impact.CascadeStubs;
        }
        result.ClrTypeImpacts = clrTypeImpact.Values
            .OrderByDescending(i => i.TotalImpact)
            .ToList();

        // 4. Compute broken pattern impact
        var patternImpact = new Dictionary<string, int>();
        foreach (var stub in _stubs.Where(s => s.RootCause == StubRootCause.KnownBrokenPattern))
        {
            var pattern = stub.Detail;
            if (!patternImpact.TryGetValue(pattern, out _))
                patternImpact[pattern] = 0;
            patternImpact[pattern]++;
        }
        result.BrokenPatternCounts = patternImpact
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // 5. Compute rendered error impact
        var renderErrorImpact = new Dictionary<string, int>();
        foreach (var stub in _stubs.Where(s => s.RootCause == StubRootCause.RenderedBodyError))
        {
            var detail = stub.Detail;
            if (!renderErrorImpact.TryGetValue(detail, out _))
                renderErrorImpact[detail] = 0;
            renderErrorImpact[detail]++;
        }
        result.RenderedErrorCounts = renderErrorImpact
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // 6. Build "unlock ranking": removing which root cause would unlock the most methods
        result.UnlockRanking = BuildUnlockRanking();

        return result;
    }

    /// <summary>
    /// Trace the root cause of an UndeclaredFunction stub back to the original reason.
    /// </summary>
    private string TraceRootCause(StubInfo stub)
    {
        // Extract the undeclared function name from detail
        var funcName = stub.Detail;
        if (_stubByCppName.TryGetValue(funcName, out var callee))
        {
            if (callee.RootCause == StubRootCause.UndeclaredFunction)
                return TraceRootCause(callee); // Follow the chain
            return $"{callee.RootCause}: {callee.Detail}";
        }
        return $"UndeclaredFunction: {funcName}";
    }

    /// <summary>
    /// Extract the CLR-internal type name from a stub detail string.
    /// Detail format: "declaring type 'X'" or "local type 'X'" or "parameter type 'X'"
    /// </summary>
    private static string ExtractClrTypeName(string detail)
    {
        var start = detail.IndexOf('\'');
        var end = detail.LastIndexOf('\'');
        if (start >= 0 && end > start)
            return detail[(start + 1)..end];
        return detail;
    }

    /// <summary>
    /// Compute all methods that transitively depend on the given set of methods
    /// (i.e., methods that would become stubs because they call these methods).
    /// Uses BFS on the reverse call graph.
    /// </summary>
    private HashSet<string> ComputeTransitiveDependents(HashSet<string> rootMethods)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();

        foreach (var root in rootMethods)
        {
            if (visited.Add(root))
                queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (_reverseCallGraph.TryGetValue(current, out var callers))
            {
                foreach (var caller in callers)
                {
                    if (visited.Add(caller))
                        queue.Enqueue(caller);
                }
            }
        }

        // Subtract the root methods themselves — we only want cascaded
        visited.ExceptWith(rootMethods);
        return visited;
    }

    /// <summary>
    /// Build ranking of "which root cause, if resolved, would unlock the most methods".
    /// Groups by actionable items (specific CLR types, specific patterns) and computes
    /// total impact including cascades.
    /// </summary>
    private List<UnlockEntry> BuildUnlockRanking()
    {
        var entries = new List<UnlockEntry>();

        // Group CLR-internal type stubs by the specific CLR type that caused them
        var clrTypeGroups = _stubs
            .Where(s => s.RootCause == StubRootCause.ClrInternalType)
            .GroupBy(s => ExtractClrTypeName(s.Detail));

        foreach (var group in clrTypeGroups)
        {
            var directMethods = group.Select(s => s.CppName).ToHashSet();
            var cascaded = ComputeTransitiveDependents(directMethods);
            entries.Add(new UnlockEntry(
                $"Remove '{group.Key}' from ClrInternalTypeNames",
                group.Count(),
                cascaded.Count,
                group.Count() + cascaded.Count
            ));
        }

        // Group broken pattern stubs
        var patternGroups = _stubs
            .Where(s => s.RootCause == StubRootCause.KnownBrokenPattern)
            .GroupBy(s => s.Detail);

        foreach (var group in patternGroups)
        {
            var directMethods = group.Select(s => s.CppName).ToHashSet();
            var cascaded = ComputeTransitiveDependents(directMethods);
            entries.Add(new UnlockEntry(
                $"Fix broken pattern: {group.Key}",
                group.Count(),
                cascaded.Count,
                group.Count() + cascaded.Count
            ));
        }

        // Group rendered error stubs
        var renderGroups = _stubs
            .Where(s => s.RootCause == StubRootCause.RenderedBodyError)
            .GroupBy(s => s.Detail);

        foreach (var group in renderGroups)
        {
            var directMethods = group.Select(s => s.CppName).ToHashSet();
            var cascaded = ComputeTransitiveDependents(directMethods);
            entries.Add(new UnlockEntry(
                $"Fix rendered error: {group.Key}",
                group.Count(),
                cascaded.Count,
                group.Count() + cascaded.Count
            ));
        }

        // Unknown parameter/body types
        var unknownTypeGroups = _stubs
            .Where(s => s.RootCause == StubRootCause.UnknownParameterTypes ||
                        s.RootCause == StubRootCause.UnknownBodyReferences)
            .GroupBy(s => s.Detail);

        foreach (var group in unknownTypeGroups)
        {
            var directMethods = group.Select(s => s.CppName).ToHashSet();
            var cascaded = ComputeTransitiveDependents(directMethods);
            entries.Add(new UnlockEntry(
                $"Resolve unknown type: {group.Key}",
                group.Count(),
                cascaded.Count,
                group.Count() + cascaded.Count
            ));
        }

        return entries.OrderByDescending(e => e.TotalUnlocked).ToList();
    }

    /// <summary>
    /// Generate analysis report as formatted text.
    /// </summary>
    public string GenerateReport(StubAnalysisResult result, string assemblyName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║           CIL2CPP Stub Root-Cause Analysis Report           ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"Assembly: {assemblyName}");
        sb.AppendLine($"Total stubs: {_stubs.Count}");
        sb.AppendLine();

        // Section 1: Summary by root cause
        sb.AppendLine("━━━ Summary by Root Cause ━━━");
        sb.AppendLine();
        foreach (var (cause, count) in result.CountByRootCause.OrderByDescending(kv => kv.Value))
        {
            var pct = _stubs.Count > 0 ? (count * 100.0 / _stubs.Count) : 0;
            sb.AppendLine($"  {cause,-30} {count,6}  ({pct:F1}%)");
        }
        sb.AppendLine();

        // Section 2: CLR-internal type impact ranking
        if (result.ClrTypeImpacts.Count > 0)
        {
            sb.AppendLine("━━━ CLR-Internal Type Impact ━━━");
            sb.AppendLine();
            sb.AppendLine($"  {"Type",-55} {"Direct",7} {"Cascade",8} {"Total",6}");
            sb.AppendLine($"  {new string('─', 55)} {new string('─', 7)} {new string('─', 8)} {new string('─', 6)}");
            foreach (var impact in result.ClrTypeImpacts)
            {
                sb.AppendLine($"  {impact.TypeName,-55} {impact.DirectStubs,7} {impact.CascadeStubs,8} {impact.TotalImpact,6}");
            }
            sb.AppendLine();
        }

        // Section 3: Broken pattern impact
        if (result.BrokenPatternCounts.Count > 0)
        {
            sb.AppendLine("━━━ Broken Pattern Impact ━━━");
            sb.AppendLine();
            foreach (var (pattern, count) in result.BrokenPatternCounts)
            {
                sb.AppendLine($"  {count,6}  {pattern}");
            }
            sb.AppendLine();
        }

        // Section 4: Rendered error impact
        if (result.RenderedErrorCounts.Count > 0)
        {
            sb.AppendLine("━━━ Rendered Error Impact ━━━");
            sb.AppendLine();
            // Group RE stubs by detail to show affected method names
            var reStubsByDetail = _stubs
                .Where(s => s.RootCause == StubRootCause.RenderedBodyError)
                .GroupBy(s => s.Detail)
                .OrderByDescending(g => g.Count())
                .ToList();
            foreach (var group in reStubsByDetail)
            {
                sb.AppendLine($"  {group.Count(),6}  {group.Key}");
                foreach (var stub in group)
                    sb.AppendLine($"           → {stub.TypeName}::{stub.MethodName}");
            }
            sb.AppendLine();
        }

        // Section 4b: Self-recursive method detail
        var selfRecursive = _stubs
            .Where(s => s.RootCause == StubRootCause.KnownBrokenPattern && s.Detail == "self-recursive call")
            .ToList();
        if (selfRecursive.Count > 0)
        {
            sb.AppendLine("━━━ Self-Recursive Methods ━━━");
            sb.AppendLine();
            foreach (var stub in selfRecursive.OrderBy(s => s.TypeName).ThenBy(s => s.MethodName))
                sb.AppendLine($"  {stub.TypeName}::{stub.MethodName}  ({stub.CppName})");
            sb.AppendLine();
        }

        // Section 4c: Undeclared function detail
        var undeclaredDetails = _stubs
            .Where(s => s.RootCause == StubRootCause.UndeclaredFunction)
            .GroupBy(s => s.Detail)
            .OrderByDescending(g => g.Count())
            .ToList();
        if (undeclaredDetails.Count > 0)
        {
            sb.AppendLine("━━━ Undeclared Function Detail ━━━");
            sb.AppendLine();
            foreach (var group in undeclaredDetails)
            {
                sb.AppendLine($"  {group.Count(),6}  {group.Key}");
            }
            sb.AppendLine();
        }

        // Section 5: Unlock ranking (the most actionable section)
        if (result.UnlockRanking.Count > 0)
        {
            sb.AppendLine("━━━ Unlock Ranking (what to fix first) ━━━");
            sb.AppendLine();
            sb.AppendLine($"  {"#",3} {"Action",-60} {"Direct",7} {"Cascade",8} {"Total",6}");
            sb.AppendLine($"  {new string('─', 3)} {new string('─', 60)} {new string('─', 7)} {new string('─', 8)} {new string('─', 6)}");
            int rank = 1;
            foreach (var entry in result.UnlockRanking.Take(30))
            {
                sb.AppendLine($"  {rank,3} {entry.Action,-60} {entry.DirectUnlocked,7} {entry.CascadeUnlocked,8} {entry.TotalUnlocked,6}");
                rank++;
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check stub counts against a budget file. Returns true if within budget.
    /// Ratchets down (auto-updates) the budget file when actual counts are lower.
    /// </summary>
    public static bool CheckBudget(string budgetPath, string assemblyName,
        StubAnalysisResult result, out string message)
    {
        if (!File.Exists(budgetPath))
        {
            message = $"[STUB BUDGET] No budget file found at {budgetPath}";
            return true; // No budget file = no check
        }

        var json = File.ReadAllText(budgetPath);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty(assemblyName, out var budget))
        {
            message = $"[STUB BUDGET] No budget entry for '{assemblyName}'";
            return true;
        }

        var sb = new StringBuilder();
        bool exceeded = false;
        bool ratcheted = false;

        var categories = new (string Name, StubRootCause Cause)[]
        {
            ("MissingBody", StubRootCause.MissingBody),
            ("KnownBrokenPattern", StubRootCause.KnownBrokenPattern),
            ("UndeclaredFunction", StubRootCause.UndeclaredFunction),
            ("ClrInternalType", StubRootCause.ClrInternalType),
            ("UnknownBodyReferences", StubRootCause.UnknownBodyReferences),
            ("UnknownParameterTypes", StubRootCause.UnknownParameterTypes),
            ("RenderedBodyError", StubRootCause.RenderedBodyError),
        };

        int actualTotal = result.CountByRootCause.Values.Sum();
        int budgetTotal = budget.TryGetProperty("total", out var t) ? t.GetInt32() : int.MaxValue;

        if (actualTotal > budgetTotal)
        {
            sb.AppendLine($"  EXCEEDED total: {actualTotal} > {budgetTotal} (budget)");
            exceeded = true;
        }

        foreach (var (name, cause) in categories)
        {
            int actual = result.CountByRootCause.GetValueOrDefault(cause);
            int budgetVal = budget.TryGetProperty(name, out var bv) ? bv.GetInt32() : int.MaxValue;
            if (actual > budgetVal)
            {
                sb.AppendLine($"  EXCEEDED {name}: {actual} > {budgetVal} (budget)");
                exceeded = true;
            }
        }

        if (exceeded)
        {
            message = $"[STUB BUDGET EXCEEDED]\n{sb}";
            return false;
        }

        // Ratchet: auto-tighten budget if actual < budget
        if (actualTotal < budgetTotal)
        {
            ratcheted = true;
            var newBudget = new Dictionary<string, object>
            {
                ["total"] = actualTotal,
            };
            foreach (var (name, cause) in categories)
            {
                newBudget[name] = result.CountByRootCause.GetValueOrDefault(cause);
            }

            // Read entire JSON, replace this assembly's entry, write back
            var root = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json) ?? new();
            var entry = new Dictionary<string, int> { ["total"] = actualTotal };
            foreach (var (name, cause) in categories)
                entry[name] = result.CountByRootCause.GetValueOrDefault(cause);
            root[assemblyName] = entry;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(budgetPath, JsonSerializer.Serialize(root, options) + "\n");
        }

        if (ratcheted)
            message = $"[STUB BUDGET RATCHETED: {budgetTotal} → {actualTotal}]";
        else
            message = $"[STUB BUDGET OK: {actualTotal} <= {budgetTotal}]";
        return true;
    }
}

/// <summary>
/// Results of stub analysis.
/// </summary>
public class StubAnalysisResult
{
    /// <summary>Number of stubs per root cause category.</summary>
    public Dictionary<StubRootCause, int> CountByRootCause { get; set; } = new();

    /// <summary>CLR-internal type impact rankings (sorted by TotalImpact descending).</summary>
    public List<TypeImpact> ClrTypeImpacts { get; set; } = new();

    /// <summary>Cascade root causes and counts.</summary>
    public Dictionary<string, int> CascadeRoots { get; set; } = new();

    /// <summary>Broken pattern → count.</summary>
    public Dictionary<string, int> BrokenPatternCounts { get; set; } = new();

    /// <summary>Rendered error → count.</summary>
    public Dictionary<string, int> RenderedErrorCounts { get; set; } = new();

    /// <summary>Ranked list of "fix this to unlock the most methods".</summary>
    public List<UnlockEntry> UnlockRanking { get; set; } = new();
}

/// <summary>
/// Impact of a single CLR-internal type on stub count.
/// </summary>
public class TypeImpact
{
    public string TypeName { get; }
    public int DirectStubs { get; set; }
    public int CascadeStubs { get; set; }
    public int TotalImpact { get; set; }

    /// <summary>Set of CppNames directly stubbed due to this type.</summary>
    public HashSet<string> DirectMethods { get; } = new();

    public TypeImpact(string typeName)
    {
        TypeName = typeName;
    }
}

/// <summary>
/// A ranked entry: "if you fix X, you unlock Y methods".
/// </summary>
public record UnlockEntry(
    string Action,
    int DirectUnlocked,
    int CascadeUnlocked,
    int TotalUnlocked
);
