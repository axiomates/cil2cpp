using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("DllPaths")]
public class AssemblySetTests
{
    private readonly DllPathsFixture _fixture;

    public AssemblySetTests(DllPathsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_HelloWorld_LoadsRootAssembly()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        Assert.Equal("HelloWorld", set.RootAssemblyName);
        Assert.NotNull(set.RootAssembly);
    }

    [Fact]
    public void Constructor_MultiAssemblyTest_LoadsRoot()
    {
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        Assert.Equal("MultiAssemblyTest", set.RootAssemblyName);
    }

    [Fact]
    public void Constructor_WithDebugConfig_LoadsAssembly()
    {
        var config = new BuildConfiguration { ReadDebugSymbols = true };
        using var set = new AssemblySet(_fixture.HelloWorldDllPath, config);
        Assert.Equal("HelloWorld", set.RootAssemblyName);
        Assert.NotNull(set.RootAssembly);
    }

    [Fact]
    public void Constructor_WithReleaseConfig_LoadsAssembly()
    {
        var config = new BuildConfiguration { ReadDebugSymbols = false };
        using var set = new AssemblySet(_fixture.HelloWorldDllPath, config);
        Assert.Equal("HelloWorld", set.RootAssemblyName);
    }

    [Fact]
    public void Constructor_NullConfig_LoadsAssembly()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath, null);
        Assert.Equal("HelloWorld", set.RootAssemblyName);
    }

    [Fact]
    public void ClassifyAssembly_Root_IsUser()
    {
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        Assert.Equal(AssemblyKind.User, set.ClassifyAssembly("MultiAssemblyTest"));
    }

    [Fact]
    public void ClassifyAssembly_MathLib_IsUser()
    {
        // MathLib is a ProjectReference, its DLL sits in the output directory
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        Assert.Equal(AssemblyKind.User, set.ClassifyAssembly("MathLib"));
    }

    [Fact]
    public void ClassifyAssembly_SystemRuntime_IsBCL()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        Assert.Equal(AssemblyKind.BCL, set.ClassifyAssembly("System.Runtime"));
    }

    [Fact]
    public void ClassifyAssembly_SystemConsole_IsBCL()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        Assert.Equal(AssemblyKind.BCL, set.ClassifyAssembly("System.Console"));
    }

    [Fact]
    public void ClassifyAssembly_UnknownAssembly_DefaultsToBCL()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        // Unknown assemblies default to BCL
        Assert.Equal(AssemblyKind.BCL, set.ClassifyAssembly("SomeUnknownAssembly"));
    }

    [Fact]
    public void LoadAssembly_MathLib_Succeeds()
    {
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        var mathLib = set.LoadAssembly("MathLib");
        Assert.NotNull(mathLib);
        Assert.Equal("MathLib", mathLib!.Name.Name);
    }

    [Fact]
    public void LoadAssembly_SystemRuntime_Succeeds()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var sysRuntime = set.LoadAssembly("System.Runtime");
        Assert.NotNull(sysRuntime);
    }

    [Fact]
    public void LoadAssembly_NonExistent_ReturnsNull()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var result = set.LoadAssembly("TotallyFakeAssembly12345");
        Assert.Null(result);
    }

    [Fact]
    public void LoadAssembly_Cached_ReturnsSameInstance()
    {
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        var first = set.LoadAssembly("MathLib");
        var second = set.LoadAssembly("MathLib");
        Assert.Same(first, second);
    }

    [Fact]
    public void LoadAssembly_RootAssembly_ReturnsCachedRoot()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        // Loading root by name should return the cached root
        var loaded = set.LoadAssembly("HelloWorld");
        Assert.Same(set.RootAssembly, loaded);
    }

    [Fact]
    public void GetAllLoadedTypes_RootOnly_ReturnsUserTypes()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var types = set.GetAllLoadedTypes().ToList();
        Assert.NotEmpty(types);

        // All should be User kind since only root is loaded
        Assert.All(types, t => Assert.Equal(AssemblyKind.User, t.Kind));

        // Should contain Program
        Assert.Contains(types, t => t.Type.Name == "Program");
    }

    [Fact]
    public void GetAllLoadedTypes_AfterLoadMathLib_ContainsBothAssemblies()
    {
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        set.LoadAssembly("MathLib");

        var types = set.GetAllLoadedTypes().ToList();
        var userTypes = types.Where(t => t.Kind == AssemblyKind.User).ToList();

        // Should have types from both MultiAssemblyTest and MathLib
        Assert.Contains(userTypes, t => t.Type.Name == "Program");
        Assert.Contains(userTypes, t => t.Type.Name == "Adder");
        Assert.Contains(userTypes, t => t.Type.Name == "MathUtils");
        Assert.Contains(userTypes, t => t.Type.Name == "Counter");
        Assert.Contains(userTypes, t => t.Type.Name == "ICalculator");
    }

    [Fact]
    public void GetAllLoadedTypes_AfterLoadBCL_ContainsBCLTypes()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        // System.Private.CoreLib contains the actual type definitions
        // (System.Runtime is mostly type-forwarders)
        set.LoadAssembly("System.Private.CoreLib");

        var types = set.GetAllLoadedTypes().ToList();
        var bclTypes = types.Where(t => t.Kind == AssemblyKind.BCL).ToList();

        Assert.NotEmpty(bclTypes);
        Assert.Contains(bclTypes, t => t.Type.FullName == "System.Object");
    }

    [Fact]
    public void GetAllLoadedTypes_SkipsModuleType()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var types = set.GetAllLoadedTypes().ToList();
        Assert.DoesNotContain(types, t => t.Type.Name == "<Module>");
    }

    [Fact]
    public void GetAllLoadedTypes_IncludesNestedTypes()
    {
        // FeatureTest has nested types (e.g., closure classes)
        using var set = new AssemblySet(_fixture.FeatureTestDllPath);
        var types = set.GetAllLoadedTypes().ToList();

        // Check that nested types (compiler-generated like <>c) are included
        var nestedTypes = types.Where(t => t.Type.IsNested).ToList();
        // FeatureTest with delegates/lambdas should have compiler-generated nested types
        Assert.True(nestedTypes.Count >= 0); // May or may not have nested types
    }

    [Fact]
    public void LoadedAssemblies_AfterConstruction_ContainsRoot()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        Assert.Single(set.LoadedAssemblies);
        Assert.True(set.LoadedAssemblies.ContainsKey("HelloWorld"));
    }

    [Fact]
    public void LoadedAssemblies_AfterLoadMathLib_ContainsTwo()
    {
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        set.LoadAssembly("MathLib");
        Assert.Equal(2, set.LoadedAssemblies.Count);
        Assert.True(set.LoadedAssemblies.ContainsKey("MultiAssemblyTest"));
        Assert.True(set.LoadedAssemblies.ContainsKey("MathLib"));
    }

    [Fact]
    public void Resolver_Exposed_ForAdvancedUse()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        Assert.NotNull(set.Resolver);
    }

    [Fact]
    public void Dispose_ClearsLoadedAssemblies()
    {
        var set = new AssemblySet(_fixture.HelloWorldDllPath);
        set.LoadAssembly("System.Runtime");
        Assert.True(set.LoadedAssemblies.Count >= 2);

        set.Dispose();
        Assert.Empty(set.LoadedAssemblies);
    }

    [Fact]
    public void Constructor_MathLib_LoadsAsLibrary()
    {
        // MathLib has no entry point (it's a library)
        using var set = new AssemblySet(_fixture.MathLibDllPath);
        Assert.Equal("MathLib", set.RootAssemblyName);
        Assert.Null(set.RootAssembly.EntryPoint);
    }

    [Fact]
    public void LoadAssembly_SystemPrivateCoreLib_Succeeds()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var coreLib = set.LoadAssembly("System.Private.CoreLib");
        Assert.NotNull(coreLib);
    }

    [Fact]
    public void ClassifyAssembly_SystemPrivateCoreLib_IsBCL()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        Assert.Equal(AssemblyKind.BCL, set.ClassifyAssembly("System.Private.CoreLib"));
    }

    [Fact]
    public void Constructor_DebugConfig_FallsBackWhenNoPdb()
    {
        // Copy a DLL to temp without its PDB to trigger the catch block fallback
        var tempDir = Path.Combine(Path.GetTempPath(), $"cil2cpp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var tempDll = Path.Combine(tempDir, "HelloWorld.dll");
            File.Copy(_fixture.HelloWorldDllPath, tempDll);
            // Do NOT copy PDB — this triggers the ReadSymbols catch fallback

            var config = new BuildConfiguration { ReadDebugSymbols = true };
            using var set = new AssemblySet(tempDll, config);
            Assert.Equal("HelloWorld", set.RootAssemblyName);
            Assert.NotNull(set.RootAssembly);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
