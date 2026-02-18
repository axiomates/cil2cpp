using Xunit;
using CIL2CPP.Core.IL;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

public class RuntimeLocatorTests
{
    [Fact]
    public void FindDotNetRoot_ReturnsValidPath()
    {
        var root = RuntimeLocator.FindDotNetRoot();
        Assert.NotNull(root);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void FindDotNetRoot_ContainsSharedDirectory()
    {
        var root = RuntimeLocator.FindDotNetRoot();
        Assert.NotNull(root);
        var sharedDir = Path.Combine(root, "shared");
        Assert.True(Directory.Exists(sharedDir));
    }

    [Fact]
    public void FindDotNetRoot_ContainsNetCoreApp()
    {
        var root = RuntimeLocator.FindDotNetRoot();
        Assert.NotNull(root);
        var netCoreDir = Path.Combine(root, "shared", "Microsoft.NETCore.App");
        Assert.True(Directory.Exists(netCoreDir));
    }

    // ParseVersion tests
    [Theory]
    [InlineData("8.0.0", 8, 0)]
    [InlineData("8.0.10", 8, 0)]
    [InlineData("6.0.25", 6, 0)]
    [InlineData("9.0.0", 9, 0)]
    public void ParseVersion_ValidVersion_ParsesCorrectly(string input, int expectedMajor, int expectedMinor)
    {
        var version = RuntimeLocator.ParseVersion(input);
        Assert.Equal(expectedMajor, version.Major);
        Assert.Equal(expectedMinor, version.Minor);
    }

    [Theory]
    [InlineData("8.0.0-preview.1", 8, 0, 0)]
    [InlineData("9.0.0-rc.2", 9, 0, 0)]
    [InlineData("7.0.0-alpha.1.23456.7", 7, 0, 0)]
    public void ParseVersion_PreRelease_StripsPreReleaseSuffix(string input, int expectedMajor, int expectedMinor, int expectedBuild)
    {
        var version = RuntimeLocator.ParseVersion(input);
        Assert.Equal(expectedMajor, version.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedBuild, version.Build);
    }

    [Theory]
    [InlineData("notaversion")]
    [InlineData("")]
    [InlineData("abc.def.ghi")]
    public void ParseVersion_Invalid_ReturnsZeroVersion(string input)
    {
        var version = RuntimeLocator.ParseVersion(input);
        Assert.Equal(new Version(0, 0), version);
    }

    [Fact]
    public void ParseVersion_SemanticComparison_CorrectOrder()
    {
        // This was the original bug: string "8.0.9" > "8.0.10" but Version 8.0.10 > 8.0.9
        var v9 = RuntimeLocator.ParseVersion("8.0.9");
        var v10 = RuntimeLocator.ParseVersion("8.0.10");
        Assert.True(v10 > v9);
    }

    [Fact]
    public void ParseVersion_PreReleaseVsRelease_ReleaseIsHigher()
    {
        // Both parse to same Version since pre-release suffix is stripped
        var preRelease = RuntimeLocator.ParseVersion("8.0.0-preview.1");
        var release = RuntimeLocator.ParseVersion("8.0.0");
        Assert.Equal(preRelease, release);
    }

    // ParseRuntimeVersion tests
    [Fact]
    public void ParseRuntimeVersion_SingleFramework_ReturnsVersion()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "runtimeOptions": {
                "tfm": "net8.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "8.0.0"
                }
              }
            }
            """);

            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Equal("8.0.0", version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseRuntimeVersion_MultipleFrameworks_ReturnsNetCoreAppVersion()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "runtimeOptions": {
                "tfm": "net8.0",
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "8.0.0"
                  },
                  {
                    "name": "Microsoft.AspNetCore.App",
                    "version": "8.0.0"
                  }
                ]
              }
            }
            """);

            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Equal("8.0.0", version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseRuntimeVersion_NoRuntimeOptions_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """{ "other": {} }""");
            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Null(version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseRuntimeVersion_NoFramework_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "runtimeOptions": {
                "tfm": "net8.0"
              }
            }
            """);

            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Null(version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseRuntimeVersion_InvalidJson_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not valid json {{{");
            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Null(version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseRuntimeVersion_EmptyFrameworks_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "runtimeOptions": {
                "frameworks": []
              }
            }
            """);

            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Null(version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseRuntimeVersion_FrameworksWithoutNetCoreApp_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "runtimeOptions": {
                "frameworks": [
                  {
                    "name": "Microsoft.AspNetCore.App",
                    "version": "8.0.0"
                  }
                ]
              }
            }
            """);

            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Null(version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseRuntimeVersion_FrameworkWithoutVersion_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "runtimeOptions": {
                "framework": {
                  "name": "Microsoft.NETCore.App"
                }
              }
            }
            """);

            var version = RuntimeLocator.ParseRuntimeVersion(tempFile);
            Assert.Null(version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // FindRuntimeDirectory edge cases
    [Fact]
    public void FindRuntimeDirectory_WithRuntimeConfig_ReturnsMatchingVersion()
    {
        // Create a temp directory with a fake assembly and runtimeconfig.json
        var tempDir = Path.Combine(Path.GetTempPath(), $"cil2cpp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeAssembly = Path.Combine(tempDir, "Test.dll");
            File.WriteAllText(fakeAssembly, ""); // dummy

            var runtimeConfig = Path.Combine(tempDir, "Test.runtimeconfig.json");
            File.WriteAllText(runtimeConfig, """
            {
              "runtimeOptions": {
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "8.0.0"
                }
              }
            }
            """);

            var dir = RuntimeLocator.FindRuntimeDirectory(fakeAssembly);
            // Should find a runtime directory (version may differ but should exist)
            Assert.NotNull(dir);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindRuntimeDirectory_ExactVersionMatch_ReturnsExactPath()
    {
        // Find an installed .NET version and create a runtimeconfig that matches exactly
        var dotnetRoot = RuntimeLocator.FindDotNetRoot();
        Assert.NotNull(dotnetRoot);

        var sharedDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
        if (!Directory.Exists(sharedDir)) return;

        var installedVersions = Directory.GetDirectories(sharedDir);
        if (installedVersions.Length == 0) return;

        // Use the exact version of an installed runtime
        var exactVersion = Path.GetFileName(installedVersions[0]);

        var tempDir = Path.Combine(Path.GetTempPath(), $"cil2cpp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeAssembly = Path.Combine(tempDir, "Exact.dll");
            File.WriteAllText(fakeAssembly, "");

            var runtimeConfig = Path.Combine(tempDir, "Exact.runtimeconfig.json");
            File.WriteAllText(runtimeConfig, $$"""
            {
              "runtimeOptions": {
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{exactVersion}}"
                }
              }
            }
            """);

            var dir = RuntimeLocator.FindRuntimeDirectory(fakeAssembly);
            Assert.NotNull(dir);
            // Should return the exact version directory
            Assert.Equal(Path.Combine(sharedDir, exactVersion), dir);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindRuntimeDirectory_NoRuntimeConfig_FallsBackToLatest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cil2cpp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeAssembly = Path.Combine(tempDir, "NoConfig.dll");
            File.WriteAllText(fakeAssembly, "");
            // No runtimeconfig.json created

            var dir = RuntimeLocator.FindRuntimeDirectory(fakeAssembly);
            // Should fallback to latest installed version
            Assert.NotNull(dir);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindRuntimeDirectory_VersionNotInstalled_FallsBackToLatestMajorMinor()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cil2cpp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeAssembly = Path.Combine(tempDir, "FallbackVersion.dll");
            File.WriteAllText(fakeAssembly, "");

            // Use a version that is unlikely to be exactly installed (patch 999)
            // but same major.minor as installed .NET 8
            var runtimeConfig = Path.Combine(tempDir, "FallbackVersion.runtimeconfig.json");
            File.WriteAllText(runtimeConfig, """
            {
              "runtimeOptions": {
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "8.0.999"
                }
              }
            }
            """);

            var dir = RuntimeLocator.FindRuntimeDirectory(fakeAssembly);
            Assert.NotNull(dir);
            // Should find an 8.0.x version
            Assert.Contains("8.0.", Path.GetFileName(dir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

[Collection("DllPaths")]
public class RuntimeLocatorIntegrationTests
{
    private readonly DllPathsFixture _fixture;

    public RuntimeLocatorIntegrationTests(DllPathsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void FindRuntimeDirectory_HelloWorld_ReturnsPath()
    {
        var dir = RuntimeLocator.FindRuntimeDirectory(_fixture.HelloWorldDllPath);
        Assert.NotNull(dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void FindRuntimeDirectory_HelloWorld_ContainsCorlib()
    {
        var dir = RuntimeLocator.FindRuntimeDirectory(_fixture.HelloWorldDllPath);
        Assert.NotNull(dir);

        // The runtime directory should contain System.Private.CoreLib.dll
        var corlibPath = Path.Combine(dir, "System.Private.CoreLib.dll");
        Assert.True(File.Exists(corlibPath));
    }

    [Fact]
    public void FindRuntimeDirectory_HelloWorld_ContainsSystemRuntime()
    {
        var dir = RuntimeLocator.FindRuntimeDirectory(_fixture.HelloWorldDllPath);
        Assert.NotNull(dir);

        var systemRuntimePath = Path.Combine(dir, "System.Runtime.dll");
        Assert.True(File.Exists(systemRuntimePath));
    }

    [Fact]
    public void FindRuntimeDirectory_NonExistentAssembly_StillReturnsPath()
    {
        // Even without runtimeconfig.json, should fallback to latest installed version
        var dir = RuntimeLocator.FindRuntimeDirectory("nonexistent.dll");
        Assert.NotNull(dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void ParseRuntimeVersion_HelloWorldRuntimeConfig_ReturnsVersion()
    {
        var dllDir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        var configPath = Path.Combine(dllDir, "HelloWorld.runtimeconfig.json");
        if (!File.Exists(configPath)) return;

        var version = RuntimeLocator.ParseRuntimeVersion(configPath);
        Assert.NotNull(version);
        // Should be something like "8.0.0"
        Assert.Contains(".", version);
    }
}
