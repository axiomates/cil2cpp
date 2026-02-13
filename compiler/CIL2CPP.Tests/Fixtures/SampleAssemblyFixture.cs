using System.Diagnostics;
using Xunit;

namespace CIL2CPP.Tests.Fixtures;

/// <summary>
/// Shared fixture that builds sample assemblies once per test run.
/// Used by tests that need real .NET assemblies (AssemblyReader, IRBuilder, etc.).
/// </summary>
public class SampleAssemblyFixture : IDisposable
{
    public string HelloWorldDllPath { get; }
    public string ArrayTestDllPath { get; }
    public string FeatureTestDllPath { get; }
    public string SolutionRoot { get; }

    public SampleAssemblyFixture()
    {
        SolutionRoot = FindSolutionRoot();

        var helloWorldProj = Path.Combine(SolutionRoot, "compiler", "samples", "HelloWorld", "HelloWorld.csproj");
        var arrayTestProj = Path.Combine(SolutionRoot, "compiler", "samples", "ArrayTest", "ArrayTest.csproj");
        var featureTestProj = Path.Combine(SolutionRoot, "compiler", "samples", "FeatureTest", "FeatureTest.csproj");

        EnsureBuilt(helloWorldProj);
        EnsureBuilt(arrayTestProj);
        EnsureBuilt(featureTestProj);

        HelloWorldDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "HelloWorld", "bin", "Debug", "net8.0", "HelloWorld.dll");
        ArrayTestDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "ArrayTest", "bin", "Debug", "net8.0", "ArrayTest.dll");
        FeatureTestDllPath = Path.Combine(SolutionRoot,
            "compiler", "samples", "FeatureTest", "bin", "Debug", "net8.0", "FeatureTest.dll");

        if (!File.Exists(HelloWorldDllPath))
            throw new InvalidOperationException($"HelloWorld.dll not found at {HelloWorldDllPath}");
        if (!File.Exists(ArrayTestDllPath))
            throw new InvalidOperationException($"ArrayTest.dll not found at {ArrayTestDllPath}");
        if (!File.Exists(FeatureTestDllPath))
            throw new InvalidOperationException($"FeatureTest.dll not found at {FeatureTestDllPath}");
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

    public void Dispose() { }
}

[CollectionDefinition("SampleAssembly")]
public class SampleAssemblyCollection : ICollectionFixture<SampleAssemblyFixture> { }
