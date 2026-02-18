using Xunit;
using CIL2CPP.Core.IL;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("DllPaths")]
public class DepsJsonParserTests
{
    private readonly DllPathsFixture _fixture;

    public DepsJsonParserTests(DllPathsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Parse_NonExistentFile_ReturnsEmptyList()
    {
        var result = DepsJsonParser.Parse("nonexistent.deps.json");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_HelloWorldDeps_ReturnsLibraries()
    {
        var depsPath = Path.Combine(
            Path.GetDirectoryName(_fixture.HelloWorldDllPath)!,
            "HelloWorld.deps.json");

        if (!File.Exists(depsPath))
        {
            // deps.json may not be present in all build configs
            return;
        }

        var result = DepsJsonParser.Parse(depsPath);
        Assert.NotNull(result);
        // HelloWorld should at least reference itself
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public void Parse_HelloWorldDeps_ContainsHelloWorld()
    {
        var depsPath = Path.Combine(
            Path.GetDirectoryName(_fixture.HelloWorldDllPath)!,
            "HelloWorld.deps.json");

        if (!File.Exists(depsPath))
            return;

        var result = DepsJsonParser.Parse(depsPath);
        var helloWorld = result.FirstOrDefault(l => l.Name == "HelloWorld");
        Assert.NotNull(helloWorld);
        Assert.Equal("project", helloWorld.Type);
        Assert.NotEmpty(helloWorld.RuntimeDlls);
    }

    [Fact]
    public void Parse_ValidJson_ExtractsVersions()
    {
        // Create a minimal deps.json for testing
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "MyLib/1.0.0": {
                    "runtime": {
                      "MyLib.dll": {}
                    }
                  },
                  "Newtonsoft.Json/13.0.3": {
                    "runtime": {
                      "lib/net6.0/Newtonsoft.Json.dll": {}
                    }
                  }
                }
              },
              "libraries": {
                "MyLib/1.0.0": {
                  "type": "project",
                  "serviceable": false,
                  "sha512": ""
                },
                "Newtonsoft.Json/13.0.3": {
                  "type": "package",
                  "serviceable": true,
                  "sha512": "abc123",
                  "path": "newtonsoft.json/13.0.3"
                }
              }
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Equal(2, result.Count);

            var myLib = result.First(l => l.Name == "MyLib");
            Assert.Equal("1.0.0", myLib.Version);
            Assert.Equal("project", myLib.Type);
            Assert.Null(myLib.Path);
            Assert.Single(myLib.RuntimeDlls);
            Assert.Equal("MyLib.dll", myLib.RuntimeDlls[0]);

            var newtonsoft = result.First(l => l.Name == "Newtonsoft.Json");
            Assert.Equal("13.0.3", newtonsoft.Version);
            Assert.Equal("package", newtonsoft.Type);
            Assert.Equal("newtonsoft.json/13.0.3", newtonsoft.Path);
            Assert.Single(newtonsoft.RuntimeDlls);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_EmptyTargets_ReturnsEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {},
              "libraries": {}
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_NoRuntimeDlls_SkipsLibrary()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "SomeLib/1.0.0": {
                    "dependencies": {
                      "OtherLib": "2.0.0"
                    }
                  }
                }
              },
              "libraries": {
                "SomeLib/1.0.0": {
                  "type": "package"
                }
              }
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Empty(result); // No runtime DLLs → skipped
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_NoLibrariesSection_StillParsesTargets()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "MyLib/2.0.0": {
                    "runtime": {
                      "MyLib.dll": {}
                    }
                  }
                }
              }
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Single(result);
            var lib = result[0];
            Assert.Equal("MyLib", lib.Name);
            Assert.Equal("2.0.0", lib.Version);
            Assert.Equal("", lib.Type); // No library info, defaults to empty
            Assert.Null(lib.Path);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_MultipleTargetFrameworks_OnlyParsesFirst()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "LibA/1.0.0": {
                    "runtime": { "LibA.dll": {} }
                  }
                },
                ".NETCoreApp,Version=v6.0": {
                  "LibB/1.0.0": {
                    "runtime": { "LibB.dll": {} }
                  }
                }
              },
              "libraries": {
                "LibA/1.0.0": { "type": "project" },
                "LibB/1.0.0": { "type": "project" }
              }
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Single(result);
            Assert.Equal("LibA", result[0].Name);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_MultipleRuntimeDlls_AllCaptured()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "BigLib/1.0.0": {
                    "runtime": {
                      "lib/net8.0/BigLib.dll": {},
                      "lib/net8.0/BigLib.Resources.dll": {},
                      "lib/net8.0/BigLib.Core.dll": {}
                    }
                  }
                }
              },
              "libraries": {
                "BigLib/1.0.0": { "type": "package", "path": "biglib/1.0.0" }
              }
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Single(result);
            Assert.Equal(3, result[0].RuntimeDlls.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_LibraryWithoutTypeProperty_DefaultsToEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "NoTypeLib/1.0.0": {
                    "runtime": { "NoTypeLib.dll": {} }
                  }
                }
              },
              "libraries": {
                "NoTypeLib/1.0.0": {
                  "serviceable": false
                }
              }
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Single(result);
            Assert.Equal("", result[0].Type);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_LibraryKeyWithoutVersion_ExtractsEmptyVersion()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "SimpleLib": {
                    "runtime": { "SimpleLib.dll": {} }
                  }
                }
              },
              "libraries": {}
            }
            """);

            var result = DepsJsonParser.Parse(tempFile);
            Assert.Single(result);
            Assert.Equal("SimpleLib", result[0].Name);
            Assert.Equal("", result[0].Version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetNuGetPackagesFolder_ReturnsPathOrNull()
    {
        var folder = DepsJsonParser.GetNuGetPackagesFolder();
        // On a dev machine with .NET SDK, this should return a valid path
        if (folder != null)
        {
            Assert.True(Directory.Exists(folder));
        }
    }

    [Fact]
    public void ResolvePackagePaths_EmptyList_ReturnsEmpty()
    {
        var result = DepsJsonParser.ResolvePackagePaths(new List<DepsJsonParser.DepsLibrary>());
        Assert.Empty(result);
    }

    [Fact]
    public void ResolvePackagePaths_ProjectType_SkipsNuGetResolution()
    {
        var libs = new List<DepsJsonParser.DepsLibrary>
        {
            new("MyProject", "1.0.0", "project", null, new List<string> { "MyProject.dll" })
        };
        var result = DepsJsonParser.ResolvePackagePaths(libs);
        Assert.Empty(result); // project type is skipped
    }

    [Fact]
    public void ResolvePackagePaths_PackageWithNullPath_Skipped()
    {
        var libs = new List<DepsJsonParser.DepsLibrary>
        {
            new("SomePkg", "1.0.0", "package", null, new List<string> { "SomePkg.dll" })
        };
        var result = DepsJsonParser.ResolvePackagePaths(libs);
        Assert.Empty(result); // null path → skipped
    }

    [Fact]
    public void ResolvePackagePaths_PackageWithNonExistentPath_ReturnsEmpty()
    {
        var libs = new List<DepsJsonParser.DepsLibrary>
        {
            new("FakePkg", "99.99.99", "package", "fakepkg/99.99.99",
                new List<string> { "lib/net8.0/FakePkg.dll" })
        };
        var result = DepsJsonParser.ResolvePackagePaths(libs);
        Assert.Empty(result); // file doesn't exist
    }

    [Fact]
    public void Parse_EmptyJsonObject_ReturnsEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{}");
            var result = DepsJsonParser.Parse(tempFile);
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_MultiAssemblyTestDeps_ContainsMathLib()
    {
        var depsPath = Path.Combine(
            Path.GetDirectoryName(_fixture.MultiAssemblyTestDllPath)!,
            "MultiAssemblyTest.deps.json");

        if (!File.Exists(depsPath)) return;

        var result = DepsJsonParser.Parse(depsPath);
        var mathLib = result.FirstOrDefault(l => l.Name == "MathLib");
        Assert.NotNull(mathLib);
        Assert.Equal("project", mathLib.Type);
    }
}
