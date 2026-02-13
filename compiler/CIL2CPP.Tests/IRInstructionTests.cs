using Xunit;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class IRInstructionTests
{
    [Fact]
    public void IRComment_ToCpp()
    {
        var instr = new IRComment { Text = "This is a comment" };
        Assert.Equal("// This is a comment", instr.ToCpp());
    }

    [Fact]
    public void IRAssign_ToCpp()
    {
        var instr = new IRAssign { Target = "__t0", Value = "42" };
        Assert.Equal("__t0 = 42;", instr.ToCpp());
    }

    [Fact]
    public void IRDeclareLocal_WithInit_ToCpp()
    {
        var instr = new IRDeclareLocal { TypeName = "int32_t", VarName = "x", InitValue = "0" };
        Assert.Equal("int32_t x = 0;", instr.ToCpp());
    }

    [Fact]
    public void IRDeclareLocal_NoInit_ToCpp()
    {
        var instr = new IRDeclareLocal { TypeName = "int32_t", VarName = "x" };
        Assert.Equal("int32_t x = {0};", instr.ToCpp());
    }

    [Fact]
    public void IRReturn_Void_ToCpp()
    {
        var instr = new IRReturn();
        Assert.Equal("return;", instr.ToCpp());
    }

    [Fact]
    public void IRReturn_WithValue_ToCpp()
    {
        var instr = new IRReturn { Value = "__t0" };
        Assert.Equal("return __t0;", instr.ToCpp());
    }

    [Fact]
    public void IRCall_NoResult_ToCpp()
    {
        var instr = new IRCall { FunctionName = "Console_WriteLine" };
        instr.Arguments.Add("str");
        Assert.Equal("Console_WriteLine(str);", instr.ToCpp());
    }

    [Fact]
    public void IRCall_WithResult_ToCpp()
    {
        var instr = new IRCall { FunctionName = "Calculator_Add", ResultVar = "__t0" };
        instr.Arguments.Add("a");
        instr.Arguments.Add("b");
        Assert.Equal("__t0 = Calculator_Add(a, b);", instr.ToCpp());
    }

    [Fact]
    public void IRCall_Virtual_ToCpp()
    {
        var instr = new IRCall
        {
            FunctionName = "Method",
            IsVirtual = true,
            VTableAccess = "vtable->Method",
            ResultVar = "__t0"
        };
        instr.Arguments.Add("__this");
        Assert.Equal("__t0 = vtable->Method(__this);", instr.ToCpp());
    }

    [Fact]
    public void IRNewObj_ToCpp()
    {
        var instr = new IRNewObj
        {
            TypeCppName = "Calculator",
            CtorName = "Calculator__ctor",
            ResultVar = "__t0"
        };
        var code = instr.ToCpp();
        Assert.Contains("cil2cpp::gc::alloc(sizeof(Calculator), &Calculator_TypeInfo)", code);
        Assert.Contains("Calculator__ctor(__t0)", code);
    }

    [Fact]
    public void IRNewObj_WithCtorArgs_ToCpp()
    {
        var instr = new IRNewObj
        {
            TypeCppName = "MyClass",
            CtorName = "MyClass__ctor",
            ResultVar = "__t0"
        };
        instr.CtorArgs.Add("42");
        var code = instr.ToCpp();
        Assert.Contains("MyClass__ctor(__t0, 42)", code);
    }

    [Fact]
    public void IRBinaryOp_ToCpp()
    {
        var instr = new IRBinaryOp { Left = "a", Right = "b", Op = "+", ResultVar = "__t0" };
        Assert.Equal("__t0 = a + b;", instr.ToCpp());
    }

    [Fact]
    public void IRUnaryOp_ToCpp()
    {
        var instr = new IRUnaryOp { Operand = "x", Op = "-", ResultVar = "__t0" };
        Assert.Equal("__t0 = -x;", instr.ToCpp());
    }

    [Fact]
    public void IRBranch_ToCpp()
    {
        var instr = new IRBranch { TargetLabel = "BB_1" };
        Assert.Equal("goto BB_1;", instr.ToCpp());
    }

    [Fact]
    public void IRConditionalBranch_TrueOnly_ToCpp()
    {
        var instr = new IRConditionalBranch { Condition = "__t0", TrueLabel = "BB_1" };
        Assert.Equal("if (__t0) goto BB_1;", instr.ToCpp());
    }

    [Fact]
    public void IRConditionalBranch_TrueAndFalse_ToCpp()
    {
        var instr = new IRConditionalBranch { Condition = "__t0", TrueLabel = "BB_1", FalseLabel = "BB_2" };
        Assert.Equal("if (__t0) goto BB_1; else goto BB_2;", instr.ToCpp());
    }

    [Fact]
    public void IRLabel_ToCpp()
    {
        var instr = new IRLabel { LabelName = "BB_3" };
        Assert.Equal("BB_3:", instr.ToCpp());
    }

    [Fact]
    public void IRFieldAccess_Load_ToCpp()
    {
        var instr = new IRFieldAccess
        {
            ObjectExpr = "__this",
            FieldCppName = "f_result",
            ResultVar = "__t0",
            IsStore = false
        };
        Assert.Equal("__t0 = __this->f_result;", instr.ToCpp());
    }

    [Fact]
    public void IRFieldAccess_Store_ToCpp()
    {
        var instr = new IRFieldAccess
        {
            ObjectExpr = "__this",
            FieldCppName = "f_result",
            IsStore = true,
            StoreValue = "42"
        };
        Assert.Equal("__this->f_result = 42;", instr.ToCpp());
    }

    [Fact]
    public void IRStaticFieldAccess_Load_ToCpp()
    {
        var instr = new IRStaticFieldAccess
        {
            TypeCppName = "MyClass",
            FieldCppName = "f_counter",
            ResultVar = "__t0",
            IsStore = false
        };
        Assert.Equal("__t0 = MyClass_statics.f_counter;", instr.ToCpp());
    }

    [Fact]
    public void IRStaticFieldAccess_Store_ToCpp()
    {
        var instr = new IRStaticFieldAccess
        {
            TypeCppName = "MyClass",
            FieldCppName = "f_counter",
            IsStore = true,
            StoreValue = "0"
        };
        Assert.Equal("MyClass_statics.f_counter = 0;", instr.ToCpp());
    }

    [Fact]
    public void IRArrayAccess_Load_ToCpp()
    {
        var instr = new IRArrayAccess
        {
            ArrayExpr = "arr",
            IndexExpr = "i",
            ElementType = "int32_t",
            ResultVar = "__t0",
            IsStore = false
        };
        Assert.Equal("__t0 = cil2cpp::array_get<int32_t>(arr, i);", instr.ToCpp());
    }

    [Fact]
    public void IRArrayAccess_Store_ToCpp()
    {
        var instr = new IRArrayAccess
        {
            ArrayExpr = "arr",
            IndexExpr = "i",
            ElementType = "int32_t",
            IsStore = true,
            StoreValue = "42"
        };
        Assert.Equal("cil2cpp::array_set<int32_t>(arr, i, 42);", instr.ToCpp());
    }

    [Fact]
    public void IRCast_Safe_ToCpp()
    {
        var instr = new IRCast
        {
            SourceExpr = "obj",
            TargetTypeCpp = "MyClass*",
            ResultVar = "__t0",
            IsSafe = true
        };
        var code = instr.ToCpp();
        Assert.Contains("object_as", code);
        Assert.Contains("MyClass_TypeInfo", code);
    }

    [Fact]
    public void IRCast_Unsafe_ToCpp()
    {
        var instr = new IRCast
        {
            SourceExpr = "obj",
            TargetTypeCpp = "MyClass*",
            ResultVar = "__t0",
            IsSafe = false
        };
        var code = instr.ToCpp();
        Assert.Contains("object_cast", code);
    }

    [Fact]
    public void IRConversion_ToCpp()
    {
        var instr = new IRConversion
        {
            SourceExpr = "x",
            TargetType = "int64_t",
            ResultVar = "__t0"
        };
        Assert.Equal("__t0 = static_cast<int64_t>(x);", instr.ToCpp());
    }

    [Fact]
    public void IRNullCheck_ToCpp()
    {
        var instr = new IRNullCheck { Expr = "obj" };
        Assert.Equal("cil2cpp::null_check(obj);", instr.ToCpp());
    }

    [Fact]
    public void IRRawCpp_ToCpp()
    {
        var instr = new IRRawCpp { Code = "printf(\"hello\\n\");" };
        Assert.Equal("printf(\"hello\\n\");", instr.ToCpp());
    }

    [Fact]
    public void SourceLocation_Properties()
    {
        var loc = new SourceLocation
        {
            FilePath = "test.cs",
            Line = 42,
            Column = 5,
            ILOffset = 0x10
        };
        Assert.Equal("test.cs", loc.FilePath);
        Assert.Equal(42, loc.Line);
        Assert.Equal(5, loc.Column);
        Assert.Equal(0x10, loc.ILOffset);
    }

    [Fact]
    public void IRInstruction_DebugInfo_DefaultNull()
    {
        var instr = new IRComment { Text = "test" };
        Assert.Null(instr.DebugInfo);
    }

    [Fact]
    public void IRBasicBlock_Label_Format()
    {
        var bb = new IRBasicBlock { Id = 3 };
        Assert.Equal("BB_3", bb.Label);
    }
}
