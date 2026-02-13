using Xunit;
using CIL2CPP.Core;

namespace CIL2CPP.Tests;

public class BuildConfigurationTests
{
    [Fact]
    public void Debug_AllFlagsEnabled()
    {
        var config = BuildConfiguration.Debug;

        Assert.True(config.IsDebug);
        Assert.True(config.EmitLineDirectives);
        Assert.True(config.EmitILOffsetComments);
        Assert.True(config.EnableStackTraces);
        Assert.True(config.ReadDebugSymbols);
    }

    [Fact]
    public void Release_AllFlagsDisabled()
    {
        var config = BuildConfiguration.Release;

        Assert.False(config.IsDebug);
        Assert.False(config.EmitLineDirectives);
        Assert.False(config.EmitILOffsetComments);
        Assert.False(config.EnableStackTraces);
        Assert.False(config.ReadDebugSymbols);
    }

    [Fact]
    public void ConfigurationName_Debug_ReturnsDebug()
    {
        Assert.Equal("Debug", BuildConfiguration.Debug.ConfigurationName);
    }

    [Fact]
    public void ConfigurationName_Release_ReturnsRelease()
    {
        Assert.Equal("Release", BuildConfiguration.Release.ConfigurationName);
    }

    [Theory]
    [InlineData("debug")]
    [InlineData("Debug")]
    [InlineData("DEBUG")]
    public void FromName_Debug_CaseInsensitive(string name)
    {
        var config = BuildConfiguration.FromName(name);
        Assert.True(config.IsDebug);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("Release")]
    [InlineData("RELEASE")]
    public void FromName_Release_CaseInsensitive(string name)
    {
        var config = BuildConfiguration.FromName(name);
        Assert.False(config.IsDebug);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("RelWithDebInfo")]
    public void FromName_Invalid_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() => BuildConfiguration.FromName(name));
    }

    [Fact]
    public void Record_Equality_Works()
    {
        var a = BuildConfiguration.Debug;
        var b = BuildConfiguration.Debug;
        Assert.Equal(a, b);
    }

    [Fact]
    public void Record_Inequality_Works()
    {
        Assert.NotEqual(BuildConfiguration.Debug, BuildConfiguration.Release);
    }
}
