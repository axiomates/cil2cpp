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
            FunctionName = "Animal_Speak",
            IsVirtual = true,
            VTableSlot = 0,
            VTableReturnType = "cil2cpp::String*",
            VTableParamTypes = new List<string> { "Animal*" },
            ResultVar = "__t0"
        };
        instr.Arguments.Add("__this");
        Assert.Equal("cil2cpp::null_check((void*)__this); __t0 = ((cil2cpp::String*(*)(Animal*))(((" +
            "cil2cpp::Object*)__this)->__type_info->vtable->methods[0]))((Animal*)__this);", instr.ToCpp());
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

    // ===== Missing ToCpp() tests =====

    [Fact]
    public void IRSwitch_ToCpp_GeneratesSwitch()
    {
        var sw = new IRSwitch { ValueExpr = "val" };
        sw.CaseLabels.Add("IL_0010");
        sw.CaseLabels.Add("IL_0020");
        sw.CaseLabels.Add("IL_0030");
        var code = sw.ToCpp();
        Assert.Contains("switch (val)", code);
        Assert.Contains("case 0: goto IL_0010;", code);
        Assert.Contains("case 1: goto IL_0020;", code);
        Assert.Contains("case 2: goto IL_0030;", code);
    }

    [Fact]
    public void IRSwitch_Empty_ToCpp()
    {
        var sw = new IRSwitch { ValueExpr = "val" };
        var code = sw.ToCpp();
        Assert.Contains("switch (val)", code);
        Assert.Contains("}", code);
    }

    [Fact]
    public void IRInitObj_ToCpp()
    {
        var instr = new IRInitObj { AddressExpr = "addr", TypeCppName = "MyStruct" };
        Assert.Equal("std::memset(addr, 0, sizeof(MyStruct));", instr.ToCpp());
    }

    [Fact]
    public void IRBox_ToCpp()
    {
        var instr = new IRBox
        {
            ValueExpr = "val",
            ValueTypeCppName = "System_Int32",
            ResultVar = "__t0"
        };
        Assert.Equal("__t0 = cil2cpp::box<System_Int32>(val, &System_Int32_TypeInfo);", instr.ToCpp());
    }

    [Fact]
    public void IRUnbox_UnboxAny_ToCpp()
    {
        var instr = new IRUnbox
        {
            ObjectExpr = "obj",
            ValueTypeCppName = "System_Int32",
            ResultVar = "__t0",
            IsUnboxAny = true
        };
        Assert.Equal("__t0 = cil2cpp::unbox<System_Int32>(reinterpret_cast<cil2cpp::Object*>(obj));", instr.ToCpp());
    }

    [Fact]
    public void IRUnbox_UnboxPtr_ToCpp()
    {
        var instr = new IRUnbox
        {
            ObjectExpr = "obj",
            ValueTypeCppName = "System_Int32",
            ResultVar = "__t0",
            IsUnboxAny = false
        };
        Assert.Equal("__t0 = cil2cpp::unbox_ptr<System_Int32>(reinterpret_cast<cil2cpp::Object*>(obj));", instr.ToCpp());
    }

    [Fact]
    public void IRStaticCtorGuard_ToCpp()
    {
        var instr = new IRStaticCtorGuard { TypeCppName = "MyClass" };
        Assert.Equal("MyClass_ensure_cctor();", instr.ToCpp());
    }

    [Fact]
    public void IRTryBegin_ToCpp()
    {
        var instr = new IRTryBegin();
        Assert.Equal("CIL2CPP_TRY", instr.ToCpp());
    }

    [Fact]
    public void IRCatchBegin_WithType_ToCpp()
    {
        var instr = new IRCatchBegin { ExceptionTypeCppName = "System_Exception" };
        Assert.Equal("CIL2CPP_CATCH(System_Exception)", instr.ToCpp());
    }

    [Fact]
    public void IRCatchBegin_CatchAll_ToCpp()
    {
        var instr = new IRCatchBegin { ExceptionTypeCppName = null };
        Assert.Equal("CIL2CPP_CATCH_ALL", instr.ToCpp());
    }

    [Fact]
    public void IRFinallyBegin_ToCpp()
    {
        var instr = new IRFinallyBegin();
        Assert.Equal("CIL2CPP_FINALLY", instr.ToCpp());
    }

    [Fact]
    public void IRTryEnd_ToCpp()
    {
        var instr = new IRTryEnd();
        Assert.Equal("CIL2CPP_END_TRY", instr.ToCpp());
    }

    [Fact]
    public void IRThrow_ToCpp()
    {
        var instr = new IRThrow { ExceptionExpr = "ex" };
        var code = instr.ToCpp();
        Assert.Contains("throw_exception", code);
        Assert.Contains("(cil2cpp::Exception*)ex", code);
    }

    [Fact]
    public void IRRethrow_ToCpp()
    {
        var instr = new IRRethrow();
        Assert.Equal("CIL2CPP_RETHROW;", instr.ToCpp());
    }

    // ===== Exception Filters =====

    [Fact]
    public void IRFilterBegin_ToCpp()
    {
        var instr = new IRFilterBegin();
        Assert.Equal("CIL2CPP_FILTER_BEGIN", instr.ToCpp());
    }

    [Fact]
    public void IREndFilter_ToCpp()
    {
        var instr = new IREndFilter();
        var code = instr.ToCpp();
        Assert.Contains("__filter_result", code);
        Assert.Contains("__exc_caught", code);
        Assert.Contains("CIL2CPP_RETHROW", code);
    }

    // ===== Phase 2: Interface dispatch ToCpp =====

    [Fact]
    public void IRCall_InterfaceDispatch_ToCpp()
    {
        var instr = new IRCall
        {
            FunctionName = "ISpeak_GetSound",
            IsVirtual = true,
            IsInterfaceCall = true,
            InterfaceTypeCppName = "ISpeak",
            VTableSlot = 0,
            VTableReturnType = "cil2cpp::String*",
            VTableParamTypes = new List<string> { "void*" },
            ResultVar = "__t0"
        };
        instr.Arguments.Add("__this");
        var code = instr.ToCpp();
        Assert.Contains("type_get_interface_vtable_checked", code);
        Assert.Contains("methods[0]", code);
        Assert.Contains("ISpeak_TypeInfo", code);
    }

    [Fact]
    public void IRCall_VirtualDispatch_NoResult_ToCpp()
    {
        var instr = new IRCall
        {
            FunctionName = "MyClass_DoStuff",
            IsVirtual = true,
            VTableSlot = 1,
            VTableReturnType = "void",
            VTableParamTypes = new List<string> { "MyClass*" },
            ResultVar = null
        };
        instr.Arguments.Add("__this");
        var code = instr.ToCpp();
        Assert.DoesNotContain("= (", code);
        Assert.Contains("methods[1]", code);
    }

    [Fact]
    public void IRCall_InterfaceDispatch_MultipleParams_ToCpp()
    {
        var instr = new IRCall
        {
            FunctionName = "ICalc_Add",
            IsVirtual = true,
            IsInterfaceCall = true,
            InterfaceTypeCppName = "ICalc",
            VTableSlot = 0,
            VTableReturnType = "int32_t",
            VTableParamTypes = new List<string> { "void*", "int32_t", "int32_t" },
            ResultVar = "__t0"
        };
        instr.Arguments.Add("obj");
        instr.Arguments.Add("1");
        instr.Arguments.Add("2");
        var code = instr.ToCpp();
        Assert.Contains("void*, int32_t, int32_t", code);
        Assert.Contains("obj, 1, 2", code);
    }

    // ===== Phase 3: Delegate instructions ToCpp =====

    [Fact]
    public void IRLoadFunctionPointer_Static_ToCpp()
    {
        var instr = new IRLoadFunctionPointer
        {
            MethodCppName = "Program_StaticAdd",
            ResultVar = "__t0"
        };
        Assert.Equal("__t0 = (void*)Program_StaticAdd;", instr.ToCpp());
    }

    [Fact]
    public void IRLoadFunctionPointer_Virtual_ToCpp()
    {
        var instr = new IRLoadFunctionPointer
        {
            MethodCppName = "Animal_Speak",
            ResultVar = "__t0",
            IsVirtual = true,
            ObjectExpr = "obj",
            VTableSlot = 3
        };
        var code = instr.ToCpp();
        Assert.Contains("((cil2cpp::Object*)obj)->__type_info->vtable->methods[3]", code);
        Assert.StartsWith("cil2cpp::null_check((void*)obj); __t0 = ", code);
    }

    [Fact]
    public void IRLoadFunctionPointer_VirtualNoSlot_FallsBackToStatic()
    {
        var instr = new IRLoadFunctionPointer
        {
            MethodCppName = "SomeMethod",
            ResultVar = "__t0",
            IsVirtual = true,
            ObjectExpr = null,
            VTableSlot = -1
        };
        Assert.Equal("__t0 = (void*)SomeMethod;", instr.ToCpp());
    }

    [Fact]
    public void IRDelegateCreate_ToCpp()
    {
        var instr = new IRDelegateCreate
        {
            DelegateTypeCppName = "MathOp",
            TargetExpr = "nullptr",
            FunctionPtrExpr = "__fptr",
            ResultVar = "__t0"
        };
        var code = instr.ToCpp();
        Assert.Equal("__t0 = cil2cpp::delegate_create(&MathOp_TypeInfo, (cil2cpp::Object*)nullptr, __fptr);", code);
    }

    [Fact]
    public void IRDelegateInvoke_Static_WithResult_ToCpp()
    {
        var instr = new IRDelegateInvoke
        {
            DelegateExpr = "__del",
            ReturnTypeCpp = "int32_t",
            ResultVar = "__t0"
        };
        instr.ParamTypes.Add("int32_t");
        instr.ParamTypes.Add("int32_t");
        instr.Arguments.Add("a");
        instr.Arguments.Add("b");
        var code = instr.ToCpp();
        // Should have if/else for target check
        Assert.Contains("if (", code);
        Assert.Contains("->target", code);
        Assert.Contains("->method_ptr", code);
        // Static call should have just the param types
        Assert.Contains("int32_t(*)(int32_t, int32_t)", code);
        // Instance call should have Object* + param types
        Assert.Contains("int32_t(*)(cil2cpp::Object*, int32_t, int32_t)", code);
        // Result assignment
        Assert.Contains("__t0 = ", code);
    }

    [Fact]
    public void IRDelegateInvoke_Void_NoResult_ToCpp()
    {
        var instr = new IRDelegateInvoke
        {
            DelegateExpr = "__del",
            ReturnTypeCpp = "void"
        };
        var code = instr.ToCpp();
        Assert.Contains("if (", code);
        Assert.DoesNotContain("__t0", code);
        // Void with no params
        Assert.Contains("void(*)()", code);
    }
}
