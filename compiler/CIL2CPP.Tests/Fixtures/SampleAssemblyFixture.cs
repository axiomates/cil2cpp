using System.Diagnostics;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using Xunit;

namespace CIL2CPP.Tests.Fixtures;

/// <summary>
/// Shared fixture that builds sample assemblies once per test run.
/// Used by tests that need real .NET assemblies (AssemblyReader, IRBuilder, etc.).
/// Caches AssemblySet, ReachabilityResult, and IRModule per sample for performance.
/// </summary>
public class SampleAssemblyFixture : IDisposable
{
    public string HelloWorldDllPath { get; }
    public string ArrayTestDllPath { get; }
    public string FeatureTestDllPath { get; }
    public string MultiAssemblyTestDllPath { get; }
    public string MathLibDllPath { get; }
    public string SolutionRoot { get; }

    // Cached AssemblySet + ReachabilityResult per sample — Release config (lazy-initialized)
    private AssemblySet? _helloWorldReleaseSet;
    private ReachabilityResult? _helloWorldReleaseReach;
    private AssemblySet? _arrayTestReleaseSet;
    private ReachabilityResult? _arrayTestReleaseReach;
    private AssemblySet? _featureTestReleaseSet;
    private ReachabilityResult? _featureTestReleaseReach;
    private AssemblySet? _multiAssemblyTestReleaseSet;
    private ReachabilityResult? _multiAssemblyTestReleaseReach;
    private AssemblySet? _mathLibReleaseSet;
    private ReachabilityResult? _mathLibReleaseReach;

    // Cached IRModule per sample — Release config (lazy-initialized)
    private IRModule? _helloWorldReleaseModule;
    private AssemblyReader? _helloWorldReleaseReader;
    private IRModule? _arrayTestReleaseModule;
    private AssemblyReader? _arrayTestReleaseReader;
    private IRModule? _featureTestReleaseModule;
    private AssemblyReader? _featureTestReleaseReader;
    private IRModule? _multiAssemblyTestReleaseModule;
    private AssemblyReader? _multiAssemblyTestReleaseReader;

    // Cached IRModule per sample — Debug config (lazy-initialized)
    private AssemblySet? _helloWorldDebugSet;
    private ReachabilityResult? _helloWorldDebugReach;
    private IRModule? _helloWorldDebugModule;
    private AssemblyReader? _helloWorldDebugReader;
    private AssemblySet? _featureTestDebugSet;
    private ReachabilityResult? _featureTestDebugReach;
    private IRModule? _featureTestDebugModule;
    private AssemblyReader? _featureTestDebugReader;

    public SampleAssemblyFixture()
    {
        SolutionRoot = FindSolutionRoot();

        var helloWorldProj = Path.Combine(SolutionRoot, "tests", "HelloWorld", "HelloWorld.csproj");
        var arrayTestProj = Path.Combine(SolutionRoot, "tests", "ArrayTest", "ArrayTest.csproj");
        var featureTestProj = Path.Combine(SolutionRoot, "tests", "FeatureTest", "FeatureTest.csproj");
        var multiAssemblyTestProj = Path.Combine(SolutionRoot, "tests", "MultiAssemblyTest", "MultiAssemblyTest.csproj");

        EnsureBuilt(helloWorldProj);
        EnsureBuilt(arrayTestProj);
        EnsureBuilt(featureTestProj);
        EnsureBuilt(multiAssemblyTestProj); // Also builds MathLib as ProjectReference

        HelloWorldDllPath = Path.Combine(SolutionRoot,
            "tests", "HelloWorld", "bin", "Debug", "net8.0", "HelloWorld.dll");
        ArrayTestDllPath = Path.Combine(SolutionRoot,
            "tests", "ArrayTest", "bin", "Debug", "net8.0", "ArrayTest.dll");
        FeatureTestDllPath = Path.Combine(SolutionRoot,
            "tests", "FeatureTest", "bin", "Debug", "net8.0", "FeatureTest.dll");
        MultiAssemblyTestDllPath = Path.Combine(SolutionRoot,
            "tests", "MultiAssemblyTest", "bin", "Debug", "net8.0", "MultiAssemblyTest.dll");
        MathLibDllPath = Path.Combine(SolutionRoot,
            "tests", "MultiAssemblyTest", "bin", "Debug", "net8.0", "MathLib.dll");

        if (!File.Exists(HelloWorldDllPath))
            throw new InvalidOperationException($"HelloWorld.dll not found at {HelloWorldDllPath}");
        if (!File.Exists(ArrayTestDllPath))
            throw new InvalidOperationException($"ArrayTest.dll not found at {ArrayTestDllPath}");
        if (!File.Exists(FeatureTestDllPath))
            throw new InvalidOperationException($"FeatureTest.dll not found at {FeatureTestDllPath}");
        if (!File.Exists(MultiAssemblyTestDllPath))
            throw new InvalidOperationException($"MultiAssemblyTest.dll not found at {MultiAssemblyTestDllPath}");
        if (!File.Exists(MathLibDllPath))
            throw new InvalidOperationException($"MathLib.dll not found at {MathLibDllPath}");
    }

    /// <summary>
    /// Get cached AssemblySet + ReachabilityResult for HelloWorld sample (Release config).
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetHelloWorldReleaseContext()
    {
        if (_helloWorldReleaseSet == null)
        {
            _helloWorldReleaseSet = new AssemblySet(HelloWorldDllPath);
            _helloWorldReleaseReach = new ReachabilityAnalyzer(_helloWorldReleaseSet).Analyze();
        }
        return (_helloWorldReleaseSet, _helloWorldReleaseReach!);
    }

    /// <summary>
    /// Get cached AssemblySet + ReachabilityResult for ArrayTest sample (Release config).
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetArrayTestReleaseContext()
    {
        if (_arrayTestReleaseSet == null)
        {
            _arrayTestReleaseSet = new AssemblySet(ArrayTestDllPath);
            _arrayTestReleaseReach = new ReachabilityAnalyzer(_arrayTestReleaseSet).Analyze();
        }
        return (_arrayTestReleaseSet, _arrayTestReleaseReach!);
    }

    /// <summary>
    /// Get cached AssemblySet + ReachabilityResult for FeatureTest sample (Release config).
    /// Uses library mode (all public types) since tests check individual IL patterns,
    /// not just entry-point-reachable code.
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetFeatureTestReleaseContext()
    {
        if (_featureTestReleaseSet == null)
        {
            _featureTestReleaseSet = new AssemblySet(FeatureTestDllPath);
            _featureTestReleaseReach = new ReachabilityAnalyzer(_featureTestReleaseSet).Analyze(forceLibraryMode: true);
        }
        return (_featureTestReleaseSet, _featureTestReleaseReach!);
    }

    /// <summary>
    /// Get cached AssemblySet + ReachabilityResult for MultiAssemblyTest sample (Release config).
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetMultiAssemblyTestReleaseContext()
    {
        if (_multiAssemblyTestReleaseSet == null)
        {
            _multiAssemblyTestReleaseSet = new AssemblySet(MultiAssemblyTestDllPath);
            _multiAssemblyTestReleaseReach = new ReachabilityAnalyzer(_multiAssemblyTestReleaseSet).Analyze();
        }
        return (_multiAssemblyTestReleaseSet, _multiAssemblyTestReleaseReach!);
    }

    /// <summary>
    /// Get cached AssemblySet + ReachabilityResult for MathLib sample (Release config).
    /// Uses library mode since MathLib has no entry point.
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetMathLibReleaseContext()
    {
        if (_mathLibReleaseSet == null)
        {
            _mathLibReleaseSet = new AssemblySet(MathLibDllPath);
            _mathLibReleaseReach = new ReachabilityAnalyzer(_mathLibReleaseSet).Analyze();
        }
        return (_mathLibReleaseSet, _mathLibReleaseReach!);
    }

    /// <summary>
    /// Get cached IRModule for HelloWorld (Release config). Built once, shared by all tests.
    /// </summary>
    public IRModule GetHelloWorldReleaseModule()
    {
        if (_helloWorldReleaseModule == null)
        {
            var (set, reach) = GetHelloWorldReleaseContext();
            _helloWorldReleaseReader = new AssemblyReader(HelloWorldDllPath);
            var builder = new IRBuilder(_helloWorldReleaseReader);
            _helloWorldReleaseModule = builder.Build(set, reach);
        }
        return _helloWorldReleaseModule;
    }

    /// <summary>
    /// Get cached IRModule for ArrayTest (Release config). Built once, shared by all tests.
    /// </summary>
    public IRModule GetArrayTestReleaseModule()
    {
        if (_arrayTestReleaseModule == null)
        {
            var (set, reach) = GetArrayTestReleaseContext();
            _arrayTestReleaseReader = new AssemblyReader(ArrayTestDllPath);
            var builder = new IRBuilder(_arrayTestReleaseReader);
            _arrayTestReleaseModule = builder.Build(set, reach);
        }
        return _arrayTestReleaseModule;
    }

    /// <summary>
    /// Get cached IRModule for FeatureTest (Release config). Built once, shared by all tests.
    /// </summary>
    public IRModule GetFeatureTestReleaseModule()
    {
        if (_featureTestReleaseModule == null)
        {
            var (set, reach) = GetFeatureTestReleaseContext();
            _featureTestReleaseReader = new AssemblyReader(FeatureTestDllPath);
            var builder = new IRBuilder(_featureTestReleaseReader);
            _featureTestReleaseModule = builder.Build(set, reach);
        }
        return _featureTestReleaseModule;
    }

    /// <summary>
    /// Get cached IRModule for MultiAssemblyTest (Release config). Built once, shared by all tests.
    /// </summary>
    public IRModule GetMultiAssemblyTestReleaseModule()
    {
        if (_multiAssemblyTestReleaseModule == null)
        {
            var (set, reach) = GetMultiAssemblyTestReleaseContext();
            _multiAssemblyTestReleaseReader = new AssemblyReader(MultiAssemblyTestDllPath);
            var builder = new IRBuilder(_multiAssemblyTestReleaseReader);
            _multiAssemblyTestReleaseModule = builder.Build(set, reach);
        }
        return _multiAssemblyTestReleaseModule;
    }

    /// <summary>
    /// Get cached IRModule for HelloWorld (Debug config with PDB). Built once, shared by Debug tests.
    /// </summary>
    public IRModule GetHelloWorldDebugModule()
    {
        if (_helloWorldDebugModule == null)
        {
            _helloWorldDebugSet = new AssemblySet(HelloWorldDllPath, BuildConfiguration.Debug);
            _helloWorldDebugReach = new ReachabilityAnalyzer(_helloWorldDebugSet).Analyze();
            _helloWorldDebugReader = new AssemblyReader(HelloWorldDllPath, BuildConfiguration.Debug);
            var builder = new IRBuilder(_helloWorldDebugReader, BuildConfiguration.Debug);
            _helloWorldDebugModule = builder.Build(_helloWorldDebugSet, _helloWorldDebugReach);
        }
        return _helloWorldDebugModule;
    }

    /// <summary>
    /// Get cached IRModule for FeatureTest (Debug config with PDB). Built once, shared by Debug tests.
    /// </summary>
    public IRModule GetFeatureTestDebugModule()
    {
        if (_featureTestDebugModule == null)
        {
            _featureTestDebugSet = new AssemblySet(FeatureTestDllPath, BuildConfiguration.Debug);
            _featureTestDebugReach = new ReachabilityAnalyzer(_featureTestDebugSet).Analyze(forceLibraryMode: true);
            _featureTestDebugReader = new AssemblyReader(FeatureTestDllPath, BuildConfiguration.Debug);
            var builder = new IRBuilder(_featureTestDebugReader, BuildConfiguration.Debug);
            _featureTestDebugModule = builder.Build(_featureTestDebugSet, _featureTestDebugReach);
        }
        return _featureTestDebugModule;
    }

    private static void EnsureBuilt(string csprojPath)
    {
        var dir = Path.GetDirectoryName(csprojPath)!;
        var dllName = Path.GetFileNameWithoutExtension(csprojPath) + ".dll";
        var dllPath = Path.Combine(dir, "bin", "Debug", "net8.0", dllName);

        if (File.Exists(dllPath)) return;

        var psi = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c Debug --nologo -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = dir
        };
        var proc = Process.Start(psi)!;
        proc.WaitForExit(60_000);
        if (proc.ExitCode != 0)
        {
            var stderr = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to build {csprojPath}: {stderr}");
        }
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

    public void Dispose()
    {
        _helloWorldReleaseReader?.Dispose();
        _arrayTestReleaseReader?.Dispose();
        _featureTestReleaseReader?.Dispose();
        _multiAssemblyTestReleaseReader?.Dispose();
        _helloWorldDebugReader?.Dispose();
        _featureTestDebugReader?.Dispose();
        _helloWorldReleaseSet?.Dispose();
        _arrayTestReleaseSet?.Dispose();
        _featureTestReleaseSet?.Dispose();
        _multiAssemblyTestReleaseSet?.Dispose();
        _mathLibReleaseSet?.Dispose();
        _helloWorldDebugSet?.Dispose();
        _featureTestDebugSet?.Dispose();
    }
}

[CollectionDefinition("SampleAssembly")]
public class SampleAssemblyCollection : ICollectionFixture<SampleAssemblyFixture> { }
