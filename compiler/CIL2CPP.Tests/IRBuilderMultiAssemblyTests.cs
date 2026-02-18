using Xunit;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("MultiAssembly")]
public class IRBuilderMultiAssemblyTests
{
    private readonly MultiAssemblyFixture _fixture;

    public IRBuilderMultiAssemblyTests(MultiAssemblyFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Build_MultiAssembly_MultiAssemblyTest_CrossAssemblyTypes()
    {
        var module = _fixture.GetMultiAssemblyReleaseModule();

        // Should have types from both assemblies
        Assert.Contains(module.Types, t => t.Name == "Program");
        Assert.Contains(module.Types, t => t.Name == "MathUtils");
    }
}
