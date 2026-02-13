using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("SampleAssembly")]
public class SequencePointInfoTests
{
    private readonly SampleAssemblyFixture _fixture;

    public SequencePointInfoTests(SampleAssemblyFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SequencePoint_HasValidSourceFile()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Debug);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var seqPoints = main.GetSequencePoints();
        var visible = seqPoints.Where(sp => !sp.IsHidden).ToList();
        Assert.True(visible.Count > 0, "Should have non-hidden sequence points");
        Assert.All(visible, sp => Assert.False(string.IsNullOrEmpty(sp.SourceFile)));
    }

    [Fact]
    public void SequencePoint_HasValidLineNumbers()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Debug);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var seqPoints = main.GetSequencePoints();
        var visible = seqPoints.Where(sp => !sp.IsHidden).ToList();
        Assert.All(visible, sp =>
        {
            Assert.True(sp.StartLine > 0, "StartLine should be positive");
            Assert.True(sp.EndLine >= sp.StartLine, "EndLine >= StartLine");
            Assert.True(sp.StartColumn > 0, "StartColumn should be positive");
        });
    }

    [Fact]
    public void SequencePoint_ILOffsetIsNonNegative()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Debug);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var seqPoints = main.GetSequencePoints();
        Assert.All(seqPoints, sp => Assert.True(sp.ILOffset >= 0));
    }

    [Fact]
    public void SequencePoint_HiddenHasSpecialLine()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Debug);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var seqPoints = main.GetSequencePoints();
        var hidden = seqPoints.Where(sp => sp.IsHidden).ToList();
        // Hidden sequence points should have the 0xFEEFEE marker line
        Assert.All(hidden, sp => Assert.Equal(0xFEEFEE, sp.StartLine));
    }

    [Fact]
    public void SequencePoint_SourceFileContainsProgram()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Debug);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var seqPoints = main.GetSequencePoints();
        var visible = seqPoints.Where(sp => !sp.IsHidden).ToList();
        // The source file should reference Program.cs
        Assert.True(visible.Any(sp => sp.SourceFile.Contains("Program.cs")),
            "At least one sequence point should reference Program.cs");
    }
}
