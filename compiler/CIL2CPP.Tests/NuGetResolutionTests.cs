using Xunit;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("NuGet")]
public class NuGetResolutionTests
{
    private readonly NuGetFixture _fixture;

    public NuGetResolutionTests(NuGetFixture fixture)
    {
        _fixture = fixture;
    }

    // ===== JsonSGTest: Assembly Resolution =====

    [Fact]
    public void JsonSGTest_AssemblySet_LoadsRootAssembly()
    {
        var (set, _) = _fixture.GetJsonSGTestReleaseContext();
        Assert.Equal("JsonSGTest", set.RootAssemblyName);
    }

    [Fact]
    public void JsonSGTest_AssemblySet_ResolvesSystemTextJson()
    {
        var (set, _) = _fixture.GetJsonSGTestReleaseContext();

        // System.Text.Json should be auto-loaded during reachability analysis
        Assert.True(set.LoadedAssemblies.ContainsKey("System.Text.Json"),
            "System.Text.Json assembly should be resolved and loaded");
    }

    [Fact]
    public void JsonSGTest_Reachability_FindsProgramType()
    {
        var (_, result) = _fixture.GetJsonSGTestReleaseContext();

        Assert.Contains(result.ReachableTypes, t => t.Name == "Program");
    }

    [Fact]
    public void JsonSGTest_Reachability_FindsPersonType()
    {
        var (_, result) = _fixture.GetJsonSGTestReleaseContext();

        // Person is the user-defined type used with System.Text.Json SG
        Assert.Contains(result.ReachableTypes, t => t.Name == "Person");
    }

    [Fact]
    public void JsonSGTest_Reachability_FindsAppJsonContext()
    {
        var (_, result) = _fixture.GetJsonSGTestReleaseContext();

        // AppJsonContext is the SG-generated JsonSerializerContext subclass
        Assert.Contains(result.ReachableTypes, t => t.Name == "AppJsonContext");
    }

    [Fact]
    public void JsonSGTest_Reachability_HasReasonableTypeCounts()
    {
        var (_, result) = _fixture.GetJsonSGTestReleaseContext();

        // JsonSGTest should have a significant number of reachable types
        // (user types + System.Text.Json + BCL)
        Assert.True(result.ReachableTypes.Count > 100,
            $"Expected >100 reachable types, got {result.ReachableTypes.Count}");
        Assert.True(result.ReachableMethods.Count > 500,
            $"Expected >500 reachable methods, got {result.ReachableMethods.Count}");
    }

    [Fact]
    public void JsonSGTest_Reachability_FindsJsonSerializerOptions()
    {
        var (_, result) = _fixture.GetJsonSGTestReleaseContext();

        // JsonSerializerOptions is a core type used by the SG
        Assert.Contains(result.ReachableTypes,
            t => t.Name == "JsonSerializerOptions");
    }

    [Fact]
    public void JsonSGTest_AssemblySet_LoadsMultipleAssemblies()
    {
        var (set, _) = _fixture.GetJsonSGTestReleaseContext();

        // Should load several assemblies (root + System.Text.Json + BCL refs)
        Assert.True(set.LoadedAssemblies.Count >= 5,
            $"Expected >=5 loaded assemblies, got {set.LoadedAssemblies.Count}");
    }

    // ===== NuGetSimpleTest: Assembly Resolution Only =====
    // NuGetSimpleTest uses Newtonsoft.Json via NuGet PackageReference.
    // Full codegen is too slow (41K+ methods), but assembly resolution should work.

    [Fact]
    public void NuGetSimpleTest_AssemblySet_LoadsRootAssembly()
    {
        using var set = new AssemblySet(_fixture.NuGetSimpleTestDllPath);
        Assert.Equal("NuGetSimpleTest", set.RootAssemblyName);
    }

    [Fact]
    public void NuGetSimpleTest_AssemblySet_ResolvesNewtonsoftJson()
    {
        using var set = new AssemblySet(_fixture.NuGetSimpleTestDllPath);

        // Trigger assembly resolution by examining types
        var mainModule = set.RootAssembly.MainModule;
        var programType = mainModule.Types.FirstOrDefault(t => t.Name == "Program");
        Assert.NotNull(programType);

        // Walk method references to trigger Newtonsoft.Json resolution
        foreach (var method in programType.Methods)
        {
            if (!method.HasBody) continue;
            foreach (var instr in method.Body.Instructions)
            {
                if (instr.Operand is Mono.Cecil.MethodReference methodRef)
                {
                    try { methodRef.Resolve(); } catch { }
                }
            }
        }

        // After resolving, Newtonsoft.Json should be loadable
        set.LoadAssembly("Newtonsoft.Json");
        Assert.True(set.LoadedAssemblies.ContainsKey("Newtonsoft.Json"),
            "Newtonsoft.Json assembly should be resolved via NuGet package path");
    }

    [Fact]
    public void NuGetSimpleTest_DepsJson_Exists()
    {
        var dllPath = _fixture.NuGetSimpleTestDllPath;
        var depsJsonPath = dllPath.Replace(".dll", ".deps.json");
        Assert.True(File.Exists(depsJsonPath),
            $"deps.json should exist at {depsJsonPath}");
    }

    [Fact]
    public void NuGetSimpleTest_HasEntryPoint()
    {
        using var set = new AssemblySet(_fixture.NuGetSimpleTestDllPath);
        Assert.NotNull(set.RootAssembly.EntryPoint);
    }
}
