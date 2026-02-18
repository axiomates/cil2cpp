using Xunit;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("ArrayTest")]
public class IRBuilderArrayTests
{
    private readonly ArrayTestFixture _fixture;

    public IRBuilderArrayTests(ArrayTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Build_ArrayTest_HasArrayInitData()
    {
        var module = _fixture.GetReleaseModule();
        Assert.True(module.ArrayInitDataBlobs.Count > 0, "ArrayTest should have array init data blobs");
    }

    [Fact]
    public void Build_ArrayTest_HasPrimitiveTypeInfos()
    {
        var module = _fixture.GetReleaseModule();
        Assert.True(module.PrimitiveTypeInfos.ContainsKey("System.Int32"),
            "ArrayTest uses int[] so System.Int32 should be registered");
    }
}
