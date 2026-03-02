using Xunit;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class RdXmlParserTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string WriteTempXml(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void Parse_ValidRdXml_ReturnsRules()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="MyLib">
                  <Type Name="MyLib.Foo" Dynamic="Required All" />
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Single(rules);
        Assert.Equal("MyLib", rules[0].AssemblyName);
        Assert.Equal("MyLib.Foo", rules[0].TypeName);
        Assert.Null(rules[0].MethodName);
    }

    [Fact]
    public void Parse_RequiredAll_FlagsAreAllBits()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="A">
                  <Type Name="T" Dynamic="Required All" />
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Equal(-1, rules[0].MemberTypes); // All = -1
    }

    [Fact]
    public void Parse_RequiredPublic_FlagsArePublicOnly()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="A">
                  <Type Name="T" Dynamic="Required Public" />
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Equal(0x2FE3, rules[0].MemberTypes); // All public members
    }

    [Fact]
    public void Parse_TypeWithNoDirective_DefaultsToAll()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="A">
                  <Type Name="T" />
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Equal(-1, rules[0].MemberTypes); // Default: preserve all
    }

    [Fact]
    public void Parse_MethodLevelRules()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="A">
                  <Type Name="T" Dynamic="Required All">
                    <Method Name="DoStuff" />
                  </Type>
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Equal(2, rules.Count);
        // First rule is the type-level rule
        Assert.Null(rules[0].MethodName);
        // Second rule is the method-level rule
        Assert.Equal("DoStuff", rules[1].MethodName);
        Assert.Equal(-1, rules[1].MemberTypes);
    }

    [Fact]
    public void Parse_PropertyGeneratesGetSetRules()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="A">
                  <Type Name="T" Dynamic="Required All">
                    <Property Name="Name" />
                  </Type>
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Equal(3, rules.Count); // 1 type + get_Name + set_Name
        Assert.Equal("get_Name", rules[1].MethodName);
        Assert.Equal("set_Name", rules[2].MethodName);
    }

    [Fact]
    public void Parse_AssemblyLevelDynamic()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="MyLib" Dynamic="Required All" />
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Single(rules);
        Assert.Equal("MyLib", rules[0].AssemblyName);
        Assert.Null(rules[0].TypeName);
        Assert.Equal(-1, rules[0].MemberTypes);
    }

    [Fact]
    public void Parse_MultipleTypesAndAssemblies()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="Lib1">
                  <Type Name="Lib1.Foo" Dynamic="Required All" />
                  <Type Name="Lib1.Bar" Dynamic="Required Public" />
                </Assembly>
                <Assembly Name="Lib2">
                  <Type Name="Lib2.Baz" />
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Equal(3, rules.Count);
        Assert.Equal("Lib1", rules[0].AssemblyName);
        Assert.Equal("Lib2", rules[2].AssemblyName);
    }

    [Fact]
    public void Parse_MissingFile_ReturnsEmpty()
    {
        var rules = RdXmlParser.Parse("/nonexistent/path/rd.xml");
        Assert.Empty(rules);
    }

    [Fact]
    public void Parse_EmptyXml_ReturnsEmpty()
    {
        var xml = """<Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata"></Directives>""";
        var rules = RdXmlParser.Parse(WriteTempXml(xml));
        Assert.Empty(rules);
    }

    [Fact]
    public void Parse_NoNamespace_StillWorks()
    {
        var xml = """
            <Directives>
              <Application>
                <Assembly Name="A">
                  <Type Name="T" Dynamic="Required All" />
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Single(rules);
        Assert.Equal("T", rules[0].TypeName);
    }

    [Fact]
    public void Parse_BrowseAttribute_MapsSameAsDynamic()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="A">
                  <Type Name="T" Browse="Required All" />
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        Assert.Equal(-1, rules[0].MemberTypes);
    }

    [Fact]
    public void Parse_TypeWithMethodAndProperty_CorrectRuleCount()
    {
        var xml = """
            <Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
              <Application>
                <Assembly Name="A">
                  <Type Name="T" Dynamic="Required All">
                    <Method Name="Execute" />
                    <Property Name="Value" />
                  </Type>
                </Assembly>
              </Application>
            </Directives>
            """;
        var rules = RdXmlParser.Parse(WriteTempXml(xml));

        // 1 type + 1 method + 2 property accessors = 4
        Assert.Equal(4, rules.Count);
        Assert.Null(rules[0].MethodName);         // type-level
        Assert.Equal("Execute", rules[1].MethodName);
        Assert.Equal("get_Value", rules[2].MethodName);
        Assert.Equal("set_Value", rules[3].MethodName);
    }

    [Fact]
    public void PreservationRule_RecordEquality()
    {
        var a = new RdXmlParser.PreservationRule("Asm", "Type", null, -1);
        var b = new RdXmlParser.PreservationRule("Asm", "Type", null, -1);
        Assert.Equal(a, b);
    }
}
