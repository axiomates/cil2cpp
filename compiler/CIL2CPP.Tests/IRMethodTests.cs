using Xunit;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class IRMethodTests
{
    [Fact]
    public void GetCppSignature_StaticVoidNoParams()
    {
        var method = new IRMethod
        {
            Name = "Main",
            CppName = "Program_Main",
            IsStatic = true,
            ReturnTypeCpp = "void"
        };

        Assert.Equal("void Program_Main()", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_StaticWithParams()
    {
        var method = new IRMethod
        {
            Name = "Add",
            CppName = "Calculator_Add",
            IsStatic = true,
            ReturnTypeCpp = "int32_t"
        };
        method.Parameters.Add(new IRParameter { CppName = "a", CppTypeName = "int32_t" });
        method.Parameters.Add(new IRParameter { CppName = "b", CppTypeName = "int32_t" });

        Assert.Equal("int32_t Calculator_Add(int32_t a, int32_t b)", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_InstanceMethod_IncludesThisPointer()
    {
        var type = new IRType { CppName = "Calculator" };
        var method = new IRMethod
        {
            Name = "GetResult",
            CppName = "Calculator_GetResult",
            IsStatic = false,
            DeclaringType = type,
            ReturnTypeCpp = "int32_t"
        };

        Assert.Equal("int32_t Calculator_GetResult(Calculator* __this)", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_InstanceWithParams()
    {
        var type = new IRType { CppName = "Calculator" };
        var method = new IRMethod
        {
            Name = "SetResult",
            CppName = "Calculator_SetResult",
            IsStatic = false,
            DeclaringType = type,
            ReturnTypeCpp = "void"
        };
        method.Parameters.Add(new IRParameter { CppName = "value", CppTypeName = "int32_t" });

        Assert.Equal("void Calculator_SetResult(Calculator* __this, int32_t value)", method.GetCppSignature());
    }
}

public class IRTypeTests
{
    [Fact]
    public void GetCppTypeName_ValueType_NoBarePointer()
    {
        var type = new IRType { CppName = "MyStruct", IsValueType = true };
        Assert.Equal("MyStruct", type.GetCppTypeName());
    }

    [Fact]
    public void GetCppTypeName_ValueType_AsPointer()
    {
        var type = new IRType { CppName = "MyStruct", IsValueType = true };
        Assert.Equal("MyStruct*", type.GetCppTypeName(asPointer: true));
    }

    [Fact]
    public void GetCppTypeName_ReferenceType_AlwaysPointer()
    {
        var type = new IRType { CppName = "MyClass", IsValueType = false };
        Assert.Equal("MyClass*", type.GetCppTypeName());
    }
}
