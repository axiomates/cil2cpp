using Xunit;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class ICallRegistryTests
{
    // System.Object
    [Theory]
    [InlineData("System.Object", "MemberwiseClone", 0, "cil2cpp::object_memberwise_clone")]
    [InlineData("System.Object", "GetType", 0, "cil2cpp::object_get_type_managed")]
    public void Lookup_SystemObject_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // System.String
    [Theory]
    [InlineData("System.String", "FastAllocateString", 1, "cil2cpp::string_fast_allocate")]
    [InlineData("System.String", "get_Length", 0, "cil2cpp::string_length")]
    [InlineData("System.String", "get_Chars", 1, "cil2cpp::string_get_chars")]
    public void Lookup_SystemString_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // System.Array
    [Theory]
    [InlineData("System.Array", "get_Length", 0, "cil2cpp::array_get_length")]
    [InlineData("System.Array", "get_Rank", 0, "cil2cpp::array_get_rank")]
    [InlineData("System.Array", "Clear", 3, "cil2cpp::array_clear")]
    [InlineData("System.Array", "GetLength", 1, "cil2cpp::array_get_length_dim")]
    public void Lookup_SystemArray_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // System.Environment
    [Theory]
    [InlineData("System.Environment", "get_NewLine", 0, "cil2cpp::icall::Environment_get_NewLine")]
    [InlineData("System.Environment", "get_TickCount", 0, "cil2cpp::icall::Environment_get_TickCount")]
    [InlineData("System.Environment", "get_TickCount64", 0, "cil2cpp::icall::Environment_get_TickCount64")]
    [InlineData("System.Environment", "get_ProcessorCount", 0, "cil2cpp::icall::Environment_get_ProcessorCount")]
    public void Lookup_SystemEnvironment_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // System.GC
    [Theory]
    [InlineData("System.GC", "Collect", 0, "cil2cpp::gc_collect")]
    [InlineData("System.GC", "SuppressFinalize", 1, "cil2cpp::gc_suppress_finalize")]
    [InlineData("System.GC", "KeepAlive", 1, "cil2cpp::gc_keep_alive")]
    [InlineData("System.GC", "_Collect", 2, "cil2cpp::gc_collect")]
    public void Lookup_SystemGC_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // System.Buffer
    [Theory]
    [InlineData("System.Buffer", "Memmove", 3, "cil2cpp::icall::Buffer_Memmove")]
    [InlineData("System.Buffer", "BlockCopy", 5, "cil2cpp::icall::Buffer_BlockCopy")]
    public void Lookup_SystemBuffer_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // System.Type
    [Fact]
    public void Lookup_SystemType_GetTypeFromHandle()
    {
        var result = ICallRegistry.Lookup("System.Type", "GetTypeFromHandle", 1);
        Assert.Equal("cil2cpp::icall::Type_GetTypeFromHandle", result);
    }

    // System.Threading.Monitor
    [Theory]
    [InlineData("System.Threading.Monitor", "Enter", 1, "cil2cpp::icall::Monitor_Enter")]
    [InlineData("System.Threading.Monitor", "Exit", 1, "cil2cpp::icall::Monitor_Exit")]
    [InlineData("System.Threading.Monitor", "ReliableEnter", 2, "cil2cpp::icall::Monitor_ReliableEnter")]
    public void Lookup_SystemThreadingMonitor_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // RuntimeHelpers
    [Theory]
    [InlineData("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", 2, "cil2cpp::icall::RuntimeHelpers_InitializeArray")]
    [InlineData("System.Runtime.CompilerServices.RuntimeHelpers", "IsReferenceOrContainsReferences", 0, "cil2cpp::icall::RuntimeHelpers_IsReferenceOrContainsReferences")]
    public void Lookup_RuntimeHelpers_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // Negative cases
    [Fact]
    public void Lookup_NonExistentType_ReturnsNull()
    {
        var result = ICallRegistry.Lookup("System.NonExistent", "Method", 0);
        Assert.Null(result);
    }

    [Fact]
    public void Lookup_ExistingTypeWrongMethod_ReturnsNull()
    {
        var result = ICallRegistry.Lookup("System.Object", "NonExistentMethod", 0);
        Assert.Null(result);
    }

    [Fact]
    public void Lookup_ExistingMethodWrongParamCount_ReturnsNull()
    {
        // MemberwiseClone has 0 params, not 1
        var result = ICallRegistry.Lookup("System.Object", "MemberwiseClone", 1);
        Assert.Null(result);
    }

    // RegisterICall (custom)
    [Fact]
    public void RegisterICall_CustomMapping_CanBeLookedUp()
    {
        ICallRegistry.RegisterICall("Test.MyClass", "MyMethod", 2, "cil2cpp::test_my_method");
        var result = ICallRegistry.Lookup("Test.MyClass", "MyMethod", 2);
        Assert.Equal("cil2cpp::test_my_method", result);
    }

    [Fact]
    public void RegisterICall_Overwrite_ReplacesExisting()
    {
        ICallRegistry.RegisterICall("Test.Overwrite", "Method", 0, "old_impl");
        ICallRegistry.RegisterICall("Test.Overwrite", "Method", 0, "new_impl");
        var result = ICallRegistry.Lookup("Test.Overwrite", "Method", 0);
        Assert.Equal("new_impl", result);
    }

    // System.Math — [InternalCall] with no IL body, mapped to <cmath> functions
    [Theory]
    [InlineData("System.Math", "Sqrt", 1, "cil2cpp::icall::Math_Sqrt")]
    [InlineData("System.Math", "Sin", 1, "cil2cpp::icall::Math_Sin")]
    [InlineData("System.Math", "Cos", 1, "cil2cpp::icall::Math_Cos")]
    [InlineData("System.Math", "Pow", 2, "cil2cpp::icall::Math_Pow")]
    [InlineData("System.Math", "Floor", 1, "cil2cpp::icall::Math_Floor")]
    [InlineData("System.Math", "Ceiling", 1, "cil2cpp::icall::Math_Ceiling")]
    public void Lookup_SystemMath_ReturnsICall(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // System.Math typed overloads (Abs, Max, Min) — dispatched by first param type
    [Theory]
    [InlineData("System.Math", "Abs", 1, "System.Double", "cil2cpp::icall::Math_Abs_double")]
    [InlineData("System.Math", "Abs", 1, "System.Single", "cil2cpp::icall::Math_Abs_float")]
    [InlineData("System.Math", "Abs", 1, "System.Int32", "cil2cpp::icall::Math_Abs_int")]
    [InlineData("System.Math", "Max", 2, "System.Double", "cil2cpp::icall::Math_Max_double")]
    [InlineData("System.Math", "Min", 2, "System.Int32", "cil2cpp::icall::Math_Min_int")]
    public void Lookup_SystemMath_TypedOverloads(string type, string method, int paramCount, string firstParam, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount, firstParam);
        Assert.Equal(expected, result);
    }

    // Console methods compile from BCL IL (no icalls)
    [Theory]
    [InlineData("System.Console", "WriteLine", 0)]
    [InlineData("System.Console", "WriteLine", 1)]
    [InlineData("System.Console", "Write", 1)]
    [InlineData("System.Console", "ReadLine", 0)]
    public void Lookup_Console_ReturnsNull_CompiledFromBclIL(string type, string method, int paramCount)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Null(result);
    }

    // Wildcard registrations (Volatile)
    [Theory]
    [InlineData("System.Threading.Volatile", "Read", 1, "cil2cpp::volatile_read")]
    [InlineData("System.Threading.Volatile", "Write", 2, "cil2cpp::volatile_write")]
    public void Lookup_WildcardAndExact_ReturnsCorrectCppName(string type, string method, int paramCount, string expected)
    {
        var result = ICallRegistry.Lookup(type, method, paramCount);
        Assert.Equal(expected, result);
    }

    // String.Concat/IsNullOrEmpty compile from BCL IL now (not icalls)
    [Fact]
    public void Lookup_StringConcat_ReturnsNull_CompiledFromIL()
    {
        var result = ICallRegistry.Lookup("System.String", "Concat", 2);
        Assert.Null(result);
    }

    [Fact]
    public void Lookup_StringIsNullOrEmpty_ReturnsNull_CompiledFromIL()
    {
        var result = ICallRegistry.Lookup("System.String", "IsNullOrEmpty", 1);
        Assert.Null(result);
    }
}
