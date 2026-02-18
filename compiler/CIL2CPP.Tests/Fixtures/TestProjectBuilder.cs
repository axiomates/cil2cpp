using System.Diagnostics;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests.Fixtures;

/// <summary>
/// Static, thread-safe builder for integration test projects.
/// Uses Lazy&lt;T&gt; to ensure each test DLL is built at most once per test run,
/// and IRModules/ReachabilityResults are constructed at most once.
/// Module builds REUSE the cached Reachability context (same AssemblySet + ReachabilityResult)
/// to avoid duplicate ReachabilityAnalyzer.Analyze() calls, which are the main bottleneck (~50-70s each).
/// </summary>
public static class TestProjectBuilder
{
    public static string SolutionRoot { get; } = FindSolutionRoot();

    // ===== DLL Paths (lazy-built) =====

    public static Lazy<string> HelloWorldDll { get; } = new(() => BuildAndGetPath("HelloWorld"));
    public static Lazy<string> ArrayTestDll { get; } = new(() => BuildAndGetPath("ArrayTest"));
    public static Lazy<string> FeatureTestDll { get; } = new(() => BuildAndGetPath("FeatureTest"));
    public static Lazy<string> MultiAssemblyTestDll { get; } = new(() => BuildAndGetPath("MultiAssemblyTest"));

    /// <summary>
    /// MathLib is built as a ProjectReference of MultiAssemblyTest.
    /// </summary>
    public static Lazy<string> MathLibDll { get; } = new(() =>
    {
        _ = MultiAssemblyTestDll.Value; // Ensure MultiAssemblyTest is built first
        var path = Path.Combine(SolutionRoot,
            "tests", "MultiAssemblyTest", "bin", "Debug", "net8.0", "MathLib.dll");
        if (!File.Exists(path))
            throw new InvalidOperationException($"MathLib.dll not found at {path}");
        return path;
    });

    // ===== Cached Reachability Contexts (built FIRST — shared with module builds) =====
    // AssemblySet kept alive (not disposed) for the process lifetime.
    // ReachabilityAnalyzer.Analyze() is the main bottleneck (~50-70s).
    // By caching these, module builds skip the expensive Analyze() call.

    // Release contexts (no PDB)
    public static Lazy<(AssemblySet Set, ReachabilityResult Reach)> HelloWorldReachability { get; } = new(() =>
        BuildReachability(HelloWorldDll.Value));

    public static Lazy<(AssemblySet Set, ReachabilityResult Reach)> ArrayTestReachability { get; } = new(() =>
        BuildReachability(ArrayTestDll.Value));

    public static Lazy<(AssemblySet Set, ReachabilityResult Reach)> FeatureTestReachability { get; } = new(() =>
        BuildReachability(FeatureTestDll.Value, forceLibraryMode: true));

    public static Lazy<(AssemblySet Set, ReachabilityResult Reach)> MultiAssemblyReachability { get; } = new(() =>
        BuildReachability(MultiAssemblyTestDll.Value));

    public static Lazy<(AssemblySet Set, ReachabilityResult Reach)> MathLibReachability { get; } = new(() =>
        BuildReachability(MathLibDll.Value));

    // Debug contexts (with PDB — needed so IRBuilder can read sequence points from Cecil types)
    public static Lazy<(AssemblySet Set, ReachabilityResult Reach)> HelloWorldDebugReachability { get; } = new(() =>
        BuildReachability(HelloWorldDll.Value, config: BuildConfiguration.Debug));

    public static Lazy<(AssemblySet Set, ReachabilityResult Reach)> FeatureTestDebugReachability { get; } = new(() =>
        BuildReachability(FeatureTestDll.Value, config: BuildConfiguration.Debug, forceLibraryMode: true));

    // ===== Cached IRModules (reuse Reachability contexts — only IRBuilder.Build is new) =====
    // IRModule is read-only after build — safe for concurrent reads from parallel collections.

    public static Lazy<IRModule> HelloWorldReleaseModule { get; } = new(() =>
        BuildModuleFromContext(HelloWorldReachability.Value, HelloWorldDll.Value));

    public static Lazy<IRModule> HelloWorldDebugModule { get; } = new(() =>
        BuildModuleFromContext(HelloWorldDebugReachability.Value, HelloWorldDll.Value, BuildConfiguration.Debug));

    public static Lazy<IRModule> FeatureTestReleaseModule { get; } = new(() =>
        BuildModuleFromContext(FeatureTestReachability.Value, FeatureTestDll.Value));

    public static Lazy<IRModule> FeatureTestDebugModule { get; } = new(() =>
        BuildModuleFromContext(FeatureTestDebugReachability.Value, FeatureTestDll.Value, BuildConfiguration.Debug));

    public static Lazy<IRModule> ArrayTestReleaseModule { get; } = new(() =>
        BuildModuleFromContext(ArrayTestReachability.Value, ArrayTestDll.Value));

    public static Lazy<IRModule> MultiAssemblyReleaseModule { get; } = new(() =>
        BuildModuleFromContext(MultiAssemblyReachability.Value, MultiAssemblyTestDll.Value));

    // ===== Builders =====

    private static (AssemblySet Set, ReachabilityResult Reach) BuildReachability(
        string dllPath, BuildConfiguration? config = null, bool forceLibraryMode = false)
    {
        var set = new AssemblySet(dllPath, config); // intentionally NOT disposed — lives for process lifetime
        var reach = new ReachabilityAnalyzer(set).Analyze(forceLibraryMode: forceLibraryMode);
        return (set, reach);
    }

    // IRBuilder.Build() uses static mutable state (CppNameMapper._userValueTypes)
    // that is not thread-safe. Serialize all module builds with a lock.
    // This is fine because Build() is fast (~3-5s) — the bottleneck is Analyze() which runs unlocked.
    // Internal: tests that mutate CppNameMapper._userValueTypes must also acquire this lock.
    internal static readonly object ModuleBuildLock = new();

    /// <summary>
    /// Build an IRModule reusing a pre-computed (AssemblySet, ReachabilityResult) context.
    /// Only AssemblyReader + IRBuilder.Build run here (~3-5s), skipping the expensive Analyze() (~50-70s).
    /// Serialized via lock because IRBuilder uses static mutable state.
    /// </summary>
    private static IRModule BuildModuleFromContext(
        (AssemblySet Set, ReachabilityResult Reach) context,
        string dllPath,
        BuildConfiguration? config = null)
    {
        lock (ModuleBuildLock)
        {
            using var reader = new AssemblyReader(dllPath, config);
            var builder = new IRBuilder(reader, config);
            return builder.Build(context.Set, context.Reach);
        }
    }

    // ===== Build Helpers =====

    private static string BuildAndGetPath(string projectName)
    {
        var csprojPath = Path.Combine(SolutionRoot, "tests", projectName, $"{projectName}.csproj");
        var dllPath = Path.Combine(SolutionRoot,
            "tests", projectName, "bin", "Debug", "net8.0", $"{projectName}.dll");

        // Always run dotnet build to pick up source changes.
        // dotnet build has incremental compilation — it's free (~1s) when nothing changed.
        var dir = Path.GetDirectoryName(csprojPath)!;
        var psi = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c Debug --nologo -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = dir
        };
        var proc = Process.Start(psi)!;

        // Read stdout/stderr asynchronously to avoid deadlock.
        // If the child process fills the OS pipe buffer (~4KB) before we read,
        // it blocks waiting for the parent to drain — but WaitForExit blocks too → deadlock.
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();

        if (!proc.WaitForExit(60_000))
        {
            proc.Kill();
            throw new InvalidOperationException(
                $"Timed out (60s) building {csprojPath}. stderr: {stderrTask.Result}");
        }

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"Failed to build {csprojPath}: {stderrTask.Result}");

        if (!File.Exists(dllPath))
            throw new InvalidOperationException($"{projectName}.dll not found at {dllPath}");

        return dllPath;
    }

    private static string FindSolutionRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "compiler")) &&
                Directory.Exists(Path.Combine(dir, "runtime")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException(
            "Cannot find solution root (directory with compiler/ and runtime/)");
    }
}
