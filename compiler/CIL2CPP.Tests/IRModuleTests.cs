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

    // ===== RegisterArrayInitData =====

    [Fact]
    public void RegisterArrayInitData_ReturnsUniqueId()
    {
        var module = new IRModule { Name = "Test" };
        var id = module.RegisterArrayInitData(new byte[] { 1, 2, 3 });
        Assert.Equal("__arr_init_0", id);
    }

    [Fact]
    public void RegisterArrayInitData_MultipleCalls_IncrementId()
    {
        var module = new IRModule { Name = "Test" };
        var id1 = module.RegisterArrayInitData(new byte[] { 1 });
        var id2 = module.RegisterArrayInitData(new byte[] { 2 });
        Assert.Equal("__arr_init_0", id1);
        Assert.Equal("__arr_init_1", id2);
    }

    [Fact]
    public void RegisterArrayInitData_StoresDataCorrectly()
    {
        var module = new IRModule { Name = "Test" };
        var data = new byte[] { 0xAA, 0xBB, 0xCC };
        module.RegisterArrayInitData(data);
        Assert.Single(module.ArrayInitDataBlobs);
        Assert.Equal(data, module.ArrayInitDataBlobs[0].Data);
        Assert.Equal("__arr_init_0", module.ArrayInitDataBlobs[0].Id);
    }

    // ===== RegisterPrimitiveTypeInfo =====

    [Fact]
    public void RegisterPrimitiveTypeInfo_Int32_RegistersCorrectly()
    {
        var module = new IRModule { Name = "Test" };
        module.RegisterPrimitiveTypeInfo("System.Int32");

        Assert.Single(module.PrimitiveTypeInfos);
        var entry = module.PrimitiveTypeInfos["System.Int32"];
        Assert.Equal("System.Int32", entry.ILFullName);
        Assert.Equal("System_Int32", entry.CppMangledName);
        Assert.Equal("int32_t", entry.CppTypeName);
    }

    [Fact]
    public void RegisterPrimitiveTypeInfo_Duplicate_NoOp()
    {
        var module = new IRModule { Name = "Test" };
        module.RegisterPrimitiveTypeInfo("System.Int32");
        module.RegisterPrimitiveTypeInfo("System.Int32");
        Assert.Single(module.PrimitiveTypeInfos);
    }

    [Fact]
    public void RegisterPrimitiveTypeInfo_Boolean_RegistersCorrectly()
    {
        var module = new IRModule { Name = "Test" };
        module.RegisterPrimitiveTypeInfo("System.Boolean");
        var entry = module.PrimitiveTypeInfos["System.Boolean"];
        Assert.Equal("bool", entry.CppTypeName);
        Assert.Equal("System_Boolean", entry.CppMangledName);
    }

    // ===== Additional String Literal Tests =====

    [Fact]
    public void RegisterStringLiteral_EmptyString()
    {
        var module = new IRModule { Name = "Test" };
        var id = module.RegisterStringLiteral("");
        Assert.Equal("__str_0", id);
        Assert.Equal("", module.StringLiterals[""].Value);
    }

    [Fact]
    public void RegisterStringLiteral_ManyUnique_CorrectOrdering()
    {
        var module = new IRModule { Name = "Test" };
        for (int i = 0; i < 10; i++)
        {
            var id = module.RegisterStringLiteral($"str_{i}");
            Assert.Equal($"__str_{i}", id);
        }
        Assert.Equal(10, module.StringLiterals.Count);
    }

    [Fact]
    public void RegisterStringLiteral_SpecialChars()
    {
        var module = new IRModule { Name = "Test" };
        var id = module.RegisterStringLiteral("Hello\nWorld\t!");
        Assert.Equal("__str_0", id);
        Assert.Equal("Hello\nWorld\t!", module.StringLiterals["Hello\nWorld\t!"].Value);
    }

    [Fact]
    public void RegisterStringLiteral_Unicode()
    {
        var module = new IRModule { Name = "Test" };
        module.RegisterStringLiteral("\u4F60\u597D");
        Assert.Single(module.StringLiterals);
    }

    // ===== Additional FindType Tests =====

    [Fact]
    public void FindType_MultipleTypes_FindsCorrect()
    {
        var module = new IRModule { Name = "Test" };
        module.Types.Add(new IRType { ILFullName = "A.B", CppName = "A_B" });
        module.Types.Add(new IRType { ILFullName = "C.D", CppName = "C_D" });
        module.Types.Add(new IRType { ILFullName = "E.F", CppName = "E_F" });

        var found = module.FindType("C.D");
        Assert.NotNull(found);
        Assert.Equal("C_D", found!.CppName);
    }

    [Fact]
    public void FindType_EmptyModule_ReturnsNull()
    {
        var module = new IRModule { Name = "Test" };
        Assert.Null(module.FindType("Anything"));
    }

    [Fact]
    public void FindType_EmptyString_ReturnsNull()
    {
        var module = new IRModule { Name = "Test" };
        module.Types.Add(new IRType { ILFullName = "MyType", CppName = "MyType" });
        Assert.Null(module.FindType(""));
    }

    // ===== Additional GetAllMethods Tests =====

    [Fact]
    public void GetAllMethods_EmptyModule_ReturnsEmpty()
    {
        var module = new IRModule { Name = "Test" };
        Assert.Empty(module.GetAllMethods());
    }

    [Fact]
    public void GetAllMethods_TypesWithNoMethods_ReturnsEmpty()
    {
        var module = new IRModule { Name = "Test" };
        module.Types.Add(new IRType { ILFullName = "EmptyType" });
        Assert.Empty(module.GetAllMethods());
    }

    [Fact]
    public void GetAllMethods_ManyTypes_ReturnsAll()
    {
        var module = new IRModule { Name = "Test" };

        var t1 = new IRType { ILFullName = "T1" };
        t1.Methods.Add(new IRMethod { CppName = "T1_A" });
        t1.Methods.Add(new IRMethod { CppName = "T1_B" });

        var t2 = new IRType { ILFullName = "T2" };
        t2.Methods.Add(new IRMethod { CppName = "T2_C" });

        var t3 = new IRType { ILFullName = "T3" };
        t3.Methods.Add(new IRMethod { CppName = "T3_D" });
        t3.Methods.Add(new IRMethod { CppName = "T3_E" });
        t3.Methods.Add(new IRMethod { CppName = "T3_F" });

        module.Types.Add(t1);
        module.Types.Add(t2);
        module.Types.Add(t3);

        var all = module.GetAllMethods().ToList();
        Assert.Equal(6, all.Count);
    }

    // ===== EntryPoint =====

    [Fact]
    public void EntryPoint_SetAndRetrieve()
    {
        var module = new IRModule { Name = "Test" };
        var method = new IRMethod { CppName = "Program_Main", IsEntryPoint = true };
        module.EntryPoint = method;
        Assert.Same(method, module.EntryPoint);
    }

    // ===== Additional ArrayInitData Tests =====

    [Fact]
    public void RegisterArrayInitData_EmptyData()
    {
        var module = new IRModule { Name = "Test" };
        var id = module.RegisterArrayInitData(Array.Empty<byte>());
        Assert.Equal("__arr_init_0", id);
        Assert.Empty(module.ArrayInitDataBlobs[0].Data);
    }

    [Fact]
    public void RegisterArrayInitData_LargeData()
    {
        var module = new IRModule { Name = "Test" };
        var data = new byte[1024];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i & 0xFF);
        var id = module.RegisterArrayInitData(data);
        Assert.Equal("__arr_init_0", id);
        Assert.Equal(1024, module.ArrayInitDataBlobs[0].Data.Length);
    }

    // ===== Additional PrimitiveTypeInfo Tests =====

    [Theory]
    [InlineData("System.Int32", "int32_t", "System_Int32")]
    [InlineData("System.Int64", "int64_t", "System_Int64")]
    [InlineData("System.Single", "float", "System_Single")]
    [InlineData("System.Double", "double", "System_Double")]
    [InlineData("System.Boolean", "bool", "System_Boolean")]
    [InlineData("System.Byte", "uint8_t", "System_Byte")]
    [InlineData("System.Char", "char16_t", "System_Char")]
    [InlineData("System.Int16", "int16_t", "System_Int16")]
    [InlineData("System.UInt16", "uint16_t", "System_UInt16")]
    [InlineData("System.UInt32", "uint32_t", "System_UInt32")]
    [InlineData("System.UInt64", "uint64_t", "System_UInt64")]
    [InlineData("System.SByte", "int8_t", "System_SByte")]
    public void RegisterPrimitiveTypeInfo_AllPrimitives(string ilName, string expectedCppType, string expectedMangled)
    {
        var module = new IRModule { Name = "Test" };
        module.RegisterPrimitiveTypeInfo(ilName);
        var entry = module.PrimitiveTypeInfos[ilName];
        Assert.Equal(expectedCppType, entry.CppTypeName);
        Assert.Equal(expectedMangled, entry.CppMangledName);
    }

    [Fact]
    public void RegisterPrimitiveTypeInfo_MultipleDifferent()
    {
        var module = new IRModule { Name = "Test" };
        module.RegisterPrimitiveTypeInfo("System.Int32");
        module.RegisterPrimitiveTypeInfo("System.Double");
        module.RegisterPrimitiveTypeInfo("System.Boolean");
        Assert.Equal(3, module.PrimitiveTypeInfos.Count);
    }

    // ===== Module Name =====

    [Fact]
    public void Module_DefaultName_Empty()
    {
        var module = new IRModule();
        Assert.Equal("", module.Name);
    }

    // ===== Data class defaults =====

    [Fact]
    public void IRStringLiteral_Defaults()
    {
        var lit = new IRStringLiteral();
        Assert.Equal("", lit.Id);
        Assert.Equal("", lit.Value);
    }

    [Fact]
    public void IRArrayInitData_Defaults()
    {
        var data = new IRArrayInitData();
        Assert.Equal("", data.Id);
        Assert.Empty(data.Data);
    }

    [Fact]
    public void PrimitiveTypeInfoEntry_Defaults()
    {
        var entry = new PrimitiveTypeInfoEntry();
        Assert.Equal("", entry.ILFullName);
        Assert.Equal("", entry.CppMangledName);
        Assert.Equal("", entry.CppTypeName);
    }
}
