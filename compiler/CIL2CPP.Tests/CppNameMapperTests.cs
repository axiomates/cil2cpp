using Xunit;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class CppNameMapperTests
{
    // ===== IsPrimitive =====

    [Theory]
    [InlineData("System.Int32", true)]
    [InlineData("System.String", true)]
    [InlineData("System.Object", true)]
    [InlineData("System.Void", true)]
    [InlineData("System.Boolean", true)]
    [InlineData("System.Double", true)]
    [InlineData("System.Char", true)]
    [InlineData("System.IntPtr", true)]
    [InlineData("MyNamespace.MyClass", false)]
    [InlineData("System.Collections.Generic.List", false)]
    public void IsPrimitive_ReturnsExpected(string typeName, bool expected)
    {
        Assert.Equal(expected, CppNameMapper.IsPrimitive(typeName));
    }

    // ===== IsValueType =====

    [Theory]
    [InlineData("System.Int32", true)]
    [InlineData("System.Boolean", true)]
    [InlineData("System.Double", true)]
    [InlineData("System.Char", true)]
    [InlineData("System.Single", true)]
    [InlineData("System.Byte", true)]
    [InlineData("System.SByte", true)]
    [InlineData("System.Int16", true)]
    [InlineData("System.UInt16", true)]
    [InlineData("System.UInt32", true)]
    [InlineData("System.Int64", true)]
    [InlineData("System.UInt64", true)]
    [InlineData("System.IntPtr", true)]
    [InlineData("System.UIntPtr", true)]
    [InlineData("System.String", false)]
    [InlineData("System.Object", false)]
    [InlineData("MyClass", false)]
    public void IsValueType_ReturnsExpected(string typeName, bool expected)
    {
        Assert.Equal(expected, CppNameMapper.IsValueType(typeName));
    }

    // ===== GetCppTypeName =====

    [Theory]
    [InlineData("System.Int32", "int32_t")]
    [InlineData("System.Boolean", "bool")]
    [InlineData("System.Void", "void")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Char", "char16_t")]
    [InlineData("System.Byte", "uint8_t")]
    [InlineData("System.SByte", "int8_t")]
    [InlineData("System.Int16", "int16_t")]
    [InlineData("System.UInt16", "uint16_t")]
    [InlineData("System.UInt32", "uint32_t")]
    [InlineData("System.Int64", "int64_t")]
    [InlineData("System.UInt64", "uint64_t")]
    [InlineData("System.IntPtr", "intptr_t")]
    [InlineData("System.UIntPtr", "uintptr_t")]
    public void GetCppTypeName_ValueTypes_NoPointer(string ilName, string expected)
    {
        Assert.Equal(expected, CppNameMapper.GetCppTypeName(ilName));
    }

    [Fact]
    public void GetCppTypeName_String_ReturnsPointer()
    {
        Assert.Equal("cil2cpp::String*", CppNameMapper.GetCppTypeName("System.String"));
    }

    [Fact]
    public void GetCppTypeName_Object_ReturnsPointer()
    {
        Assert.Equal("cil2cpp::Object*", CppNameMapper.GetCppTypeName("System.Object"));
    }

    [Fact]
    public void GetCppTypeName_UserType_ManglesDots()
    {
        Assert.Equal("MyNamespace_MyClass", CppNameMapper.GetCppTypeName("MyNamespace.MyClass"));
    }

    [Fact]
    public void GetCppTypeName_ByRef_AddsPointer()
    {
        Assert.Equal("int32_t*", CppNameMapper.GetCppTypeName("System.Int32&"));
    }

    [Fact]
    public void GetCppTypeName_Pointer_AddsPointer()
    {
        Assert.Equal("int32_t*", CppNameMapper.GetCppTypeName("System.Int32*"));
    }

    [Fact]
    public void GetCppTypeName_Array_ReturnsCilArray()
    {
        Assert.Equal("cil2cpp::Array*", CppNameMapper.GetCppTypeName("System.Int32[]"));
    }

    // ===== GetCppTypeForDecl =====

    [Theory]
    [InlineData("System.Void", "void")]
    [InlineData("System.Int32", "int32_t")]
    [InlineData("System.Boolean", "bool")]
    public void GetCppTypeForDecl_ValueTypes_NoPointer(string ilName, string expected)
    {
        Assert.Equal(expected, CppNameMapper.GetCppTypeForDecl(ilName));
    }

    [Fact]
    public void GetCppTypeForDecl_ReferenceType_AddsPointer()
    {
        // User-defined reference types get * suffix
        var result = CppNameMapper.GetCppTypeForDecl("MyNamespace.MyClass");
        Assert.EndsWith("*", result);
    }

    [Fact]
    public void GetCppTypeForDecl_String_AlreadyHasPointer()
    {
        var result = CppNameMapper.GetCppTypeForDecl("System.String");
        Assert.Equal("cil2cpp::String*", result);
        // Should not double the pointer
        Assert.DoesNotContain("**", result);
    }

    // ===== MangleTypeName =====

    [Theory]
    [InlineData("System.Object", "System_Object")]
    [InlineData("My.Nested/Inner", "My_Nested_Inner")]
    [InlineData("Generic<T>", "Generic_T_")]
    [InlineData("Multi<K, V>", "Multi_K_V_")]
    [InlineData("Outer`1", "Outer_1")]
    [InlineData("Nested+Inner", "Nested_Inner")]
    public void MangleTypeName_ReplacesSpecialChars(string input, string expected)
    {
        Assert.Equal(expected, CppNameMapper.MangleTypeName(input));
    }

    // ===== MangleMethodName =====

    [Fact]
    public void MangleMethodName_CombinesTypeAndMethod()
    {
        Assert.Equal("MyClass_MyMethod", CppNameMapper.MangleMethodName("MyClass", "MyMethod"));
    }

    [Fact]
    public void MangleMethodName_HandlesSpecialChars()
    {
        Assert.Equal("MyClass_get_Value", CppNameMapper.MangleMethodName("MyClass", "get.Value"));
    }

    [Fact]
    public void MangleMethodName_HandlesAngleBrackets()
    {
        Assert.Equal("MyClass__ctor_", CppNameMapper.MangleMethodName("MyClass", "<ctor>"));
    }

    // ===== MangleFieldName =====

    [Theory]
    [InlineData("_myField", "f_myField")]
    [InlineData("publicField", "f_publicField")]
    [InlineData("__x", "f_x")]
    public void MangleFieldName_AddsPrefix(string input, string expected)
    {
        Assert.Equal(expected, CppNameMapper.MangleFieldName(input));
    }

    [Fact]
    public void MangleFieldName_AllUnderscores_KeepsOriginal()
    {
        // Edge case: field name is just underscores
        var result = CppNameMapper.MangleFieldName("___");
        Assert.Equal("f____", result);
    }

    // ===== GetDefaultValue =====

    // ===== IsCompilerGeneratedType =====

    [Theory]
    [InlineData("<PrivateImplementationDetails>", true)]
    [InlineData("<PrivateImplementationDetails>/__StaticArrayInitTypeSize=20", true)]
    [InlineData("MyClass", false)]
    [InlineData("System.Object", false)]
    public void IsCompilerGeneratedType_ReturnsExpected(string typeName, bool expected)
    {
        Assert.Equal(expected, CppNameMapper.IsCompilerGeneratedType(typeName));
    }

    // ===== MangleTypeName edge cases =====

    [Fact]
    public void MangleTypeName_EqualSign_Replaced()
    {
        Assert.Equal("__StaticArrayInitTypeSize_20", CppNameMapper.MangleTypeName("__StaticArrayInitTypeSize=20"));
    }

    [Fact]
    public void MangleTypeName_Dash_Replaced()
    {
        Assert.Equal("My_Type", CppNameMapper.MangleTypeName("My-Type"));
    }

    // ===== GetCppTypeForDecl edge cases =====

    [Fact]
    public void GetCppTypeForDecl_Array_ReturnsArrayPointer()
    {
        Assert.Equal("cil2cpp::Array*", CppNameMapper.GetCppTypeForDecl("System.Int32[]"));
    }

    [Fact]
    public void GetCppTypeForDecl_Object_ReturnsPointer()
    {
        Assert.Equal("cil2cpp::Object*", CppNameMapper.GetCppTypeForDecl("System.Object"));
    }

    // ===== GetDefaultValue =====

    [Theory]
    // IL type names
    [InlineData("System.Boolean", "false")]
    [InlineData("System.Int32", "0")]
    [InlineData("System.Int64", "0")]
    [InlineData("System.Single", "0.0f")]
    [InlineData("System.Double", "0.0")]
    [InlineData("System.Char", "u'\\0'")]
    // C++ type names
    [InlineData("bool", "false")]
    [InlineData("int32_t", "0")]
    [InlineData("uint64_t", "0")]
    [InlineData("float", "0.0f")]
    [InlineData("double", "0.0")]
    [InlineData("char16_t", "u'\\0'")]
    // Reference types
    [InlineData("MyClass*", "nullptr")]
    [InlineData("cil2cpp::String*", "nullptr")]
    [InlineData("SomeUnknownType", "nullptr")]
    public void GetDefaultValue_ReturnsExpected(string typeName, string expected)
    {
        Assert.Equal(expected, CppNameMapper.GetDefaultValue(typeName));
    }
}
