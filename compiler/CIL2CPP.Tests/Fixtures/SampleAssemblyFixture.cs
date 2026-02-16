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

    // Cached AssemblySet + ReachabilityResult per sample (lazy-initialized)
    private AssemblySet? _helloWorldSet;
    private ReachabilityResult? _helloWorldReach;
    private AssemblySet? _arrayTestSet;
    private ReachabilityResult? _arrayTestReach;
    private AssemblySet? _featureTestSet;
    private ReachabilityResult? _featureTestReach;

    // Cached IRModule per sample (default config only, lazy-initialized)
    private IRModule? _helloWorldModule;
    private AssemblyReader? _helloWorldReader;
    private IRModule? _arrayTestModule;
    private AssemblyReader? _arrayTestReader;
    private IRModule? _featureTestModule;
    private AssemblyReader? _featureTestReader;

    public SampleAssemblyFixture()
    {
        SolutionRoot = FindSolutionRoot();

        var helloWorldProj = Path.Combine(SolutionRoot, "compiler", "samples", "HelloWorld", "HelloWorld.csproj");
        var arrayTestProj = Path.Combine(SolutionRoot, "compiler", "samples", "ArrayTest", "ArrayTest.csproj");
        var featureTestProj = Path.Combine(SolutionRoot, "compiler", "samples", "FeatureTest", "FeatureTest.csproj");
        var multiAssemblyTestProj = Path.Combine(SolutionRoot, "compiler", "samples", "MultiAssemblyTest", "MultiAssemblyTest.csproj");

        EnsureBuilt(helloWorldProj);
        EnsureBuilt(arrayTestProj);
        EnsureBuilt(featureTestProj);
        EnsureBuilt(multiAssemblyTestProj); // Also builds MathLib as ProjectReference

        HelloWorldDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "HelloWorld", "bin", "Debug", "net8.0", "HelloWorld.dll");
        ArrayTestDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "ArrayTest", "bin", "Debug", "net8.0", "ArrayTest.dll");
        FeatureTestDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "FeatureTest", "bin", "Debug", "net8.0", "FeatureTest.dll");
        MultiAssemblyTestDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "MultiAssemblyTest", "bin", "Debug", "net8.0", "MultiAssemblyTest.dll");
        MathLibDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "MultiAssemblyTest", "bin", "Debug", "net8.0", "MathLib.dll");

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
    /// Get cached AssemblySet + ReachabilityResult for HelloWorld sample.
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetHelloWorldContext()
    {
        if (_helloWorldSet == null)
        {
            _helloWorldSet = new AssemblySet(HelloWorldDllPath);
            _helloWorldReach = new ReachabilityAnalyzer(_helloWorldSet).Analyze();
        }
        return (_helloWorldSet, _helloWorldReach!);
    }

    /// <summary>
    /// Get cached AssemblySet + ReachabilityResult for ArrayTest sample.
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetArrayTestContext()
    {
        if (_arrayTestSet == null)
        {
            _arrayTestSet = new AssemblySet(ArrayTestDllPath);
            _arrayTestReach = new ReachabilityAnalyzer(_arrayTestSet).Analyze();
        }
        return (_arrayTestSet, _arrayTestReach!);
    }

    /// <summary>
    /// Get cached AssemblySet + ReachabilityResult for FeatureTest sample.
    /// Uses library mode (all public types) since tests check individual IL patterns,
    /// not just entry-point-reachable code.
    /// </summary>
    public (AssemblySet Set, ReachabilityResult Reach) GetFeatureTestContext()
    {
        if (_featureTestSet == null)
        {
            _featureTestSet = new AssemblySet(FeatureTestDllPath);
            _featureTestReach = new ReachabilityAnalyzer(_featureTestSet).Analyze(forceLibraryMode: true);
        }
        return (_featureTestSet, _featureTestReach!);
    }

    /// <summary>
    /// Get cached IRModule for HelloWorld (default config). Built once, shared by all tests.
    /// </summary>
    public IRModule GetHelloWorldModule()
    {
        if (_helloWorldModule == null)
        {
            var (set, reach) = GetHelloWorldContext();
            _helloWorldReader = new AssemblyReader(HelloWorldDllPath);
            var builder = new IRBuilder(_helloWorldReader);
            _helloWorldModule = builder.Build(set, reach);
        }
        return _helloWorldModule;
    }

    /// <summary>
    /// Get cached IRModule for ArrayTest (default config). Built once, shared by all tests.
    /// </summary>
    public IRModule GetArrayTestModule()
    {
        if (_arrayTestModule == null)
        {
            var (set, reach) = GetArrayTestContext();
            _arrayTestReader = new AssemblyReader(ArrayTestDllPath);
            var builder = new IRBuilder(_arrayTestReader);
            _arrayTestModule = builder.Build(set, reach);
        }
        return _arrayTestModule;
    }

    /// <summary>
    /// Get cached IRModule for FeatureTest (default config). Built once, shared by all tests.
    /// </summary>
    public IRModule GetFeatureTestModule()
    {
        if (_featureTestModule == null)
        {
            var (set, reach) = GetFeatureTestContext();
            _featureTestReader = new AssemblyReader(FeatureTestDllPath);
            var builder = new IRBuilder(_featureTestReader);
            _featureTestModule = builder.Build(set, reach);
        }
        return _featureTestModule;
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
        _helloWorldReader?.Dispose();
        _arrayTestReader?.Dispose();
        _featureTestReader?.Dispose();
        _helloWorldSet?.Dispose();
        _arrayTestSet?.Dispose();
        _featureTestSet?.Dispose();
    }
}

[CollectionDefinition("SampleAssembly")]
public class SampleAssemblyCollection : ICollectionFixture<SampleAssemblyFixture> { }
