using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("DllPaths")]
public class AssemblyReaderTests
{
    private readonly DllPathsFixture _fixture;

    public AssemblyReaderTests(DllPathsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_ValidAssembly_Succeeds()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        Assert.NotNull(reader);
    }

    [Fact]
    public void Constructor_InvalidPath_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new AssemblyReader("nonexistent_assembly.dll"));
    }

    [Fact]
    public void AssemblyName_HelloWorld_ReturnsHelloWorld()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        Assert.Equal("HelloWorld", reader.AssemblyName);
    }

    [Fact]
    public void HasSymbols_WithDebugConfig_True()
    {
        // Debug build should have PDB available
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Debug);
        Assert.True(reader.HasSymbols);
    }

    [Fact]
    public void HasSymbols_WithReleaseConfig_False()
    {
        // Release config doesn't request symbols
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Release);
        Assert.False(reader.HasSymbols);
    }

    [Fact]
    public void HasSymbols_DefaultConfig_False()
    {
        // null config defaults to Release (ReadDebugSymbols = false)
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        Assert.False(reader.HasSymbols);
    }

    [Fact]
    public void GetAllTypes_HelloWorld_ContainsExpectedTypes()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var types = reader.GetAllTypes().ToList();
        var typeNames = types.Select(t => t.Name).ToList();

        Assert.Contains("Calculator", typeNames);
        Assert.Contains("Program", typeNames);
    }

    [Fact]
    public void GetAllTypes_HelloWorld_ExcludesModuleType()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var types = reader.GetAllTypes().ToList();
        Assert.DoesNotContain(types, t => t.Name == "<Module>");
    }

    [Fact]
    public void GetType_ExistingType_ReturnsType()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var type = reader.GetType("Calculator");
        Assert.NotNull(type);
        Assert.Equal("Calculator", type!.Name);
    }

    [Fact]
    public void GetType_NonExistingType_ReturnsNull()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        Assert.Null(reader.GetType("NonExistentType"));
    }

    [Fact]
    public void GetReferencedAssemblies_HelloWorld_ContainsSystemRuntime()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var refs = reader.GetReferencedAssemblies().ToList();
        // .NET 8 assemblies reference System.Runtime or mscorlib
        Assert.True(refs.Any(r => r.Contains("System.Runtime") || r.Contains("mscorlib")),
            $"Expected System.Runtime in references, got: {string.Join(", ", refs)}");
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        reader.Dispose();
        // Second dispose should not throw
        var ex = Record.Exception(() => reader.Dispose());
        Assert.Null(ex);
    }
}
