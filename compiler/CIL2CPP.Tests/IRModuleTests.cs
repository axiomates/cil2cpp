using Xunit;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class IRModuleTests
{
    [Fact]
    public void RegisterStringLiteral_FirstCall_ReturnsStr0()
    {
        var module = new IRModule { Name = "Test" };
        var id = module.RegisterStringLiteral("Hello");
        Assert.Equal("__str_0", id);
    }

    [Fact]
    public void RegisterStringLiteral_SameValue_ReturnsSameId()
    {
        var module = new IRModule { Name = "Test" };
        var id1 = module.RegisterStringLiteral("Hello");
        var id2 = module.RegisterStringLiteral("Hello");
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void RegisterStringLiteral_DifferentValues_ReturnsDifferentIds()
    {
        var module = new IRModule { Name = "Test" };
        var id1 = module.RegisterStringLiteral("Hello");
        var id2 = module.RegisterStringLiteral("World");
        Assert.NotEqual(id1, id2);
        Assert.Equal("__str_0", id1);
        Assert.Equal("__str_1", id2);
    }

    [Fact]
    public void RegisterStringLiteral_StoresInDictionary()
    {
        var module = new IRModule { Name = "Test" };
        module.RegisterStringLiteral("Hello");
        Assert.Single(module.StringLiterals);
        Assert.Equal("Hello", module.StringLiterals["Hello"].Value);
    }

    [Fact]
    public void FindType_Exists_ReturnsType()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType { ILFullName = "MyNamespace.MyClass", CppName = "MyNamespace_MyClass" };
        module.Types.Add(type);

        var found = module.FindType("MyNamespace.MyClass");
        Assert.NotNull(found);
        Assert.Same(type, found);
    }

    [Fact]
    public void FindType_NotExists_ReturnsNull()
    {
        var module = new IRModule { Name = "Test" };
        Assert.Null(module.FindType("NonExistent"));
    }

    [Fact]
    public void GetAllMethods_ReturnsMethodsFromAllTypes()
    {
        var module = new IRModule { Name = "Test" };

        var type1 = new IRType { ILFullName = "Type1", CppName = "Type1" };
        type1.Methods.Add(new IRMethod { Name = "Method1", CppName = "Type1_Method1" });
        type1.Methods.Add(new IRMethod { Name = "Method2", CppName = "Type1_Method2" });

        var type2 = new IRType { ILFullName = "Type2", CppName = "Type2" };
        type2.Methods.Add(new IRMethod { Name = "Method3", CppName = "Type2_Method3" });

        module.Types.Add(type1);
        module.Types.Add(type2);

        var allMethods = module.GetAllMethods().ToList();
        Assert.Equal(3, allMethods.Count);
    }

    [Fact]
    public void EntryPoint_DefaultNull()
    {
        var module = new IRModule();
        Assert.Null(module.EntryPoint);
    }
}
