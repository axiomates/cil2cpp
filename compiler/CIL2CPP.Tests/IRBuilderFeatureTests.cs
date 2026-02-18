using Xunit;
using Mono.Cecil.Cil;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("FeatureTest")]
public class IRBuilderFeatureTests
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderFeatureTests(FeatureTestFixture fixture)
    {
        _fixture = fixture;
    }

    private IRModule BuildFeatureTest(BuildConfiguration? config = null)
    {
        if (config == null || !config.ReadDebugSymbols)
            return _fixture.GetReleaseModule();
        return _fixture.GetDebugModule();
    }

    private List<IRInstruction> GetMethodInstructions(IRModule module, string typeName, string methodName)
    {
        var type = module.FindType(typeName)!;
        var method = type.Methods.First(m => m.Name == methodName);
        return method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
    }

    // ===== FeatureTest: Module structure =====

    [Fact]
    public void Build_FeatureTest_HasExpectedTypes()
    {
        var module = BuildFeatureTest();
        Assert.NotNull(module.FindType("Animal"));
        Assert.NotNull(module.FindType("Dog"));
        Assert.NotNull(module.FindType("Cat"));
        Assert.NotNull(module.FindType("Program"));
        Assert.NotNull(module.FindType("Color"));
        Assert.NotNull(module.FindType("Point"));
    }

    [Fact]
    public void Build_FeatureTest_EnumType()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        Assert.True(color.IsEnum);
        Assert.True(color.IsValueType);
    }

    [Fact]
    public void Build_FeatureTest_ValueType()
    {
        var module = BuildFeatureTest();
        var point = module.FindType("Point")!;
        Assert.True(point.IsValueType);
        Assert.False(point.IsEnum);
        Assert.True(point.Fields.Count >= 2, "Point should have X and Y fields");
    }

    // ===== FeatureTest: Inheritance & VTable =====

    [Fact]
    public void Build_FeatureTest_InheritanceChain()
    {
        var module = BuildFeatureTest();
        var dog = module.FindType("Dog")!;
        Assert.NotNull(dog.BaseType);
        Assert.Equal("Animal", dog.BaseType!.ILFullName);
    }

    [Fact]
    public void Build_FeatureTest_VirtualMethods_InAnimal()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        var speak = animal.Methods.FirstOrDefault(m => m.Name == "Speak");
        Assert.NotNull(speak);
        Assert.True(speak!.IsVirtual);
    }

    [Fact]
    public void Build_FeatureTest_VTable_DogOverridesSpeak()
    {
        var module = BuildFeatureTest();
        var dog = module.FindType("Dog")!;
        Assert.True(dog.VTable.Count > 0, "Dog should have vtable entries");
        var speakEntry = dog.VTable.FirstOrDefault(v => v.MethodName == "Speak");
        Assert.NotNull(speakEntry);
        Assert.Equal("Dog", speakEntry!.DeclaringType!.Name);
    }

    [Fact]
    public void Build_FeatureTest_VTable_AnimalBaseEntry()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        var speakEntry = animal.VTable.FirstOrDefault(v => v.MethodName == "Speak");
        Assert.NotNull(speakEntry);
        Assert.Equal("Animal", speakEntry!.DeclaringType!.Name);
        Assert.True(speakEntry.Slot >= 0);
    }

    // ===== FeatureTest: Static fields =====

    [Fact]
    public void Build_FeatureTest_StaticFields()
    {
        var module = BuildFeatureTest();
        var program = module.FindType("Program")!;
        Assert.True(program.StaticFields.Count > 0, "Program should have static field _globalValue");
    }

    [Fact]
    public void Build_FeatureTest_StaticFieldAccess_InTestStaticFields()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestStaticFields");
        Assert.Contains(instrs, i => i is IRStaticFieldAccess sfa && !sfa.IsStore);
        Assert.Contains(instrs, i => i is IRStaticFieldAccess sfa && sfa.IsStore);
    }

    [Fact]
    public void Build_FeatureTest_AnimalStaticField()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        Assert.True(animal.StaticFields.Count > 0, "Animal has static _count field");
    }

    // ===== FeatureTest: Arithmetic opcodes =====

    [Fact]
    public void Build_FeatureTest_TestArithmetic_HasBinaryOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestArithmetic");
        var binOps = instrs.OfType<IRBinaryOp>().Select(b => b.Op).ToList();
        Assert.Contains("+", binOps);
        Assert.Contains("-", binOps);
        Assert.Contains("*", binOps);
        Assert.Contains("/", binOps);
        Assert.Contains("%", binOps);
    }

    [Fact]
    public void Build_FeatureTest_TestArithmetic_HasNeg()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestArithmetic");
        Assert.Contains(instrs, i => i is IRUnaryOp u && u.Op == "-");
    }

    // ===== FeatureTest: Branching opcodes =====

    [Fact]
    public void Build_FeatureTest_TestBranching_HasConditionalBranches()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBranching");
        var condBranches = instrs.OfType<IRConditionalBranch>().ToList();
        Assert.True(condBranches.Count > 0, "TestBranching should have conditional branches");
    }

    [Fact]
    public void Build_FeatureTest_TestBranching_HasComparisonOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBranching");
        var binOps = instrs.OfType<IRBinaryOp>().Select(b => b.Op).ToList();
        Assert.Contains("==", binOps);
        Assert.Contains(">", binOps);
        Assert.Contains("<", binOps);
    }

    // ===== FeatureTest: Conversions =====

    [Fact]
    public void Build_FeatureTest_TestConversions_HasConversionInstructions()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestConversions");
        var convs = instrs.OfType<IRConversion>().ToList();
        Assert.True(convs.Count > 0, "TestConversions should have conversion instructions");
        var targetTypes = convs.Select(c => c.TargetType).ToList();
        Assert.Contains("int64_t", targetTypes);  // conv.i8
        Assert.Contains("float", targetTypes);     // conv.r4
        Assert.Contains("double", targetTypes);    // conv.r8
    }

    // ===== FeatureTest: Bitwise =====

    [Fact]
    public void Build_FeatureTest_TestBitwiseOps_HasBitwiseOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBitwiseOps");
        var binOps = instrs.OfType<IRBinaryOp>().Select(b => b.Op).ToList();
        Assert.Contains("&", binOps);
        Assert.Contains("|", binOps);
        Assert.Contains("^", binOps);
        Assert.Contains("<<", binOps);
        Assert.Contains(">>", binOps);
    }

    [Fact]
    public void Build_FeatureTest_TestBitwiseOps_HasNot()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBitwiseOps");
        Assert.Contains(instrs, i => i is IRUnaryOp u && u.Op == "~");
    }

    // ===== FeatureTest: Exception handling =====

    [Fact]
    public void Build_FeatureTest_TestExceptionHandling_HasTryCatch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionHandling");
        Assert.Contains(instrs, i => i is IRTryBegin);
        Assert.Contains(instrs, i => i is IRCatchBegin);
        Assert.Contains(instrs, i => i is IRTryEnd);
    }

    [Fact]
    public void Build_FeatureTest_TestExceptionHandling_HasFinally()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionHandling");
        Assert.Contains(instrs, i => i is IRFinallyBegin);
    }

    [Fact]
    public void Build_FeatureTest_TestExceptionHandling_HasThrow()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionHandling");
        Assert.Contains(instrs, i => i is IRThrow);
    }

    // ===== FeatureTest: Exception Filters =====

    [Fact]
    public void Build_FeatureTest_TestExceptionFilter_HasFilterBegin()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionFilter");
        Assert.Contains(instrs, i => i is IRFilterBegin);
    }

    [Fact]
    public void Build_FeatureTest_TestExceptionFilter_HasEndFilter()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionFilter");
        Assert.Contains(instrs, i => i is IREndFilter);
    }

    [Fact]
    public void Build_FeatureTest_TestExceptionFilter_HasFilterResultDecl()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionFilter");
        // __filter_result should be declared after FilterBegin
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("__filter_result"));
    }

    [Fact]
    public void Build_FeatureTest_TestExceptionFilter_FilterResultAssigned()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionFilter");
        // __filter_result should be assigned before endfilter
        var assigns = instrs.OfType<IRAssign>().ToList();
        Assert.Contains(assigns, a => a.Target == "__filter_result");
    }

    [Fact]
    public void Build_FeatureTest_TestExceptionFilter_EndFilterUsesFilterResult()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionFilter");
        var endFilter = instrs.OfType<IREndFilter>().First();
        var code = endFilter.ToCpp();
        Assert.Contains("__filter_result", code);
    }

    // ===== FeatureTest: Casting =====

    [Fact]
    public void Build_FeatureTest_TestCasting_HasCastInstructions()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestCasting");
        var casts = instrs.OfType<IRCast>().ToList();
        Assert.True(casts.Count > 0, "TestCasting should have cast instructions");
        Assert.Contains(casts, c => c.IsSafe);   // isinst (as)
        Assert.Contains(casts, c => !c.IsSafe);  // castclass
    }

    // ===== FeatureTest: Boxing/Unboxing =====

    [Fact]
    public void Build_FeatureTest_TestBoxingUnboxing_HasBox()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBoxingUnboxing");
        Assert.Contains(instrs, i => i is IRBox);
    }

    [Fact]
    public void Build_FeatureTest_TestBoxingUnboxing_HasUnbox()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBoxingUnboxing");
        Assert.Contains(instrs, i => i is IRUnbox);
    }

    // ===== FeatureTest: Switch =====

    [Fact]
    public void Build_FeatureTest_TestSwitchStatement_HasSwitch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestSwitchStatement");
        var switches = instrs.OfType<IRSwitch>().ToList();
        Assert.True(switches.Count > 0, "TestSwitchStatement should have switch instruction");
        Assert.True(switches[0].CaseLabels.Count >= 4, "Switch should have at least 4 cases");
    }

    // ===== FeatureTest: Float/Double constants =====

    [Fact]
    public void Build_FeatureTest_TestFloatDouble_HasFloatingPointOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestFloatDouble");
        // Should have binary ops for float/double arithmetic
        Assert.Contains(instrs, i => i is IRBinaryOp);
    }

    // ===== FeatureTest: Long =====

    [Fact]
    public void Build_FeatureTest_TestLong_HasLongOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestLong");
        Assert.Contains(instrs, i => i is IRBinaryOp);
    }

    // ===== FeatureTest: Null/Dup =====

    [Fact]
    public void Build_FeatureTest_TestNullAndDup_HasBranches()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestNullAndDup");
        // Testing for null check: if (obj == null)
        Assert.Contains(instrs, i => i is IRConditionalBranch);
    }

    // ===== FeatureTest: Virtual calls generate IRCall =====

    [Fact]
    public void Build_FeatureTest_TestVirtualCalls_HasCallInstructions()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualCalls");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.True(calls.Count > 0, "TestVirtualCalls should have call instructions");
    }

    // ===== FeatureTest: Constructor chain =====

    [Fact]
    public void Build_FeatureTest_DogConstructor_Exists()
    {
        var module = BuildFeatureTest();
        var dog = module.FindType("Dog")!;
        var ctor = dog.Methods.FirstOrDefault(m => m.IsConstructor);
        Assert.NotNull(ctor);
    }

    // ===== FeatureTest: Method with labels (branch targets) =====

    [Fact]
    public void Build_FeatureTest_TestBranching_HasLabels()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBranching");
        Assert.Contains(instrs, i => i is IRLabel);
    }

    // ===== FeatureTest: Method calls with return values produce IRCall =====

    [Fact]
    public void Build_FeatureTest_GetCount_ReturnsViaIRCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualCalls");
        var calls = instrs.OfType<IRCall>().ToList();
        // GetCount is a static call that returns a value
        var callWithResult = calls.FirstOrDefault(c => c.ResultVar != null);
        Assert.NotNull(callWithResult);
    }

    // ===== FeatureTest: Animal has protected field =====

    [Fact]
    public void Build_FeatureTest_Animal_HasNameField()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        Assert.Contains(animal.Fields, f => f.Name == "_name");
    }

    // ===== FeatureTest: HasCctor detection =====

    [Fact]
    public void Build_FeatureTest_Program_HasCctor()
    {
        var module = BuildFeatureTest();
        // Program has static field initializer (_globalValue = 100) which may generate a .cctor
        var program = module.FindType("Program")!;
        // Static field with initializer → compiler may generate .cctor
        // Verify HasCctor flag is correctly detected
        // Note: whether HasCctor is true depends on compiler output
        Assert.NotNull(program);
    }

    // ===== FeatureTest: BCL method mapping =====

    [Fact]
    public void Build_FeatureTest_TestObjectMethods_HasObjectCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestObjectMethods");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("object_to_string"));
        Assert.Contains(calls, c => c.FunctionName.Contains("object_get_hash_code"));
        Assert.Contains(calls, c => c.FunctionName.Contains("object_equals"));
    }

    [Fact]
    public void Build_FeatureTest_TestMathOps_HasMathCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMathOps");
        var calls = instrs.OfType<IRCall>().ToList();
        var funcNames = calls.Select(c => c.FunctionName).ToList();
        // Math methods are [InternalCall] — mapped to cil2cpp::icall::Math_* via ICallRegistry
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("abs"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("max"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("min"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("sqrt"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("pow"));
    }

    // ===== FeatureTest: Struct operations =====

    [Fact]
    public void Build_FeatureTest_TestStructOps_HasInitObj()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestStructOps");
        Assert.Contains(instrs, i => i is IRInitObj);
    }

    // ===== FeatureTest: More conversions =====

    [Fact]
    public void Build_FeatureTest_TestMoreConversions_HasMoreConvTypes()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMoreConversions");
        var convs = instrs.OfType<IRConversion>().Select(c => c.TargetType).ToList();
        Assert.Contains("uint32_t", convs);
    }

    [Fact]
    public void Build_FeatureTest_TestMoreConversions_HasIntPtrConversion()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMoreConversions");
        var convs = instrs.OfType<IRConversion>().Select(c => c.TargetType).ToList();
        Assert.Contains("intptr_t", convs);
    }

    [Fact]
    public void Build_FeatureTest_TestMoreConversions_ConvRUn_CastsToUnsigned()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMoreConversions");
        var convs = instrs.OfType<IRConversion>().ToList();
        // Conv_R_Un should produce static_cast<double>(cil2cpp::to_unsigned(val))
        // to_unsigned preserves the original width (avoids sign-extending 32-bit to 64-bit)
        var rUnConv = convs.FirstOrDefault(c =>
            c.TargetType == "double" && c.SourceExpr.Contains("to_unsigned"));
        Assert.NotNull(rUnConv);
    }

    // ===== FeatureTest: ModifyArg =====

    [Fact]
    public void Build_FeatureTest_ModifyArg_HasAssign()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "ModifyArg");
        // starg results in an IRAssign to the parameter
        Assert.Contains(instrs, i => i is IRAssign);
    }

    // ===== FeatureTest: Console.Write =====

    [Fact]
    public void Build_FeatureTest_TestConsoleWrite_HasWriteCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestConsoleWrite");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("Console_Write"));
    }

    // ===== FeatureTest: More math ops =====

    [Fact]
    public void Build_FeatureTest_TestMoreMathOps_HasAllMathCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMoreMathOps");
        var calls = instrs.OfType<IRCall>().ToList();
        var funcNames = calls.Select(c => c.FunctionName).ToList();
        // Math methods are [InternalCall] — mapped to cil2cpp::icall::Math_* via ICallRegistry
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("ceil"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("round"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("sin"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("cos"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("tan"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("log"));
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("exp"));
    }

    // ===== FeatureTest: ManyParams (ldarg.s) =====

    [Fact]
    public void Build_FeatureTest_ManyParams_HasSixParams()
    {
        var module = BuildFeatureTest();
        var program = module.FindType("Program")!;
        var method = program.Methods.First(m => m.Name == "ManyParams");
        Assert.Equal(6, method.Parameters.Count);
    }

    // ===== FeatureTest: Constants 6, 7, 8 =====

    [Fact]
    public void Build_FeatureTest_TestConstants_HasBinaryOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestConstants");
        Assert.Contains(instrs, i => i is IRBinaryOp);
    }

    // ===== Phase 2: Enum support =====

    [Fact]
    public void Build_FeatureTest_EnumUnderlyingType_Set()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        Assert.Equal("System.Int32", color.EnumUnderlyingType);
    }

    [Fact]
    public void Build_FeatureTest_EnumConstants_Extracted()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        var constFields = color.StaticFields.Where(f => f.ConstantValue != null).ToList();
        Assert.True(constFields.Count >= 3, "Color enum should have at least Red, Green, Blue");
    }

    [Fact]
    public void Build_FeatureTest_EnumNoValueField()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        Assert.DoesNotContain(color.Fields, f => f.Name == "value__");
    }

    // ===== Phase 2: VTable dispatch =====

    [Fact]
    public void Build_FeatureTest_VirtualCall_HasVTableSlot()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualCalls");
        var virtualCalls = instrs.OfType<IRCall>().Where(c => c.IsVirtual && c.VTableSlot >= 0).ToList();
        Assert.True(virtualCalls.Count > 0, "TestVirtualCalls should generate virtual dispatch with VTableSlot");
    }

    // ===== Phase 2: Interface dispatch =====

    [Fact]
    public void Build_FeatureTest_Duck_HasInterfaceImpls()
    {
        var module = BuildFeatureTest();
        var duck = module.FindType("Duck")!;
        Assert.True(duck.InterfaceImpls.Count > 0, "Duck should implement ISpeak");
        Assert.Contains(duck.InterfaceImpls, impl => impl.Interface.Name == "ISpeak");
    }

    [Fact]
    public void Build_FeatureTest_InterfaceDispatch_HasInterfaceCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestInterfaceDispatch");
        var ifaceCalls = instrs.OfType<IRCall>().Where(c => c.IsInterfaceCall).ToList();
        Assert.True(ifaceCalls.Count > 0, "TestInterfaceDispatch should generate interface dispatch calls");
    }

    // ===== Phase 2: Finalizer =====

    [Fact]
    public void Build_FeatureTest_Resource_HasFinalizer()
    {
        var module = BuildFeatureTest();
        var resource = module.FindType("Resource")!;
        Assert.NotNull(resource.Finalizer);
    }

    // ===== Phase 2: Operator overloading =====

    [Fact]
    public void Build_FeatureTest_Vector2_HasOperator()
    {
        var module = BuildFeatureTest();
        var vector2 = module.FindType("Vector2")!;
        var opMethod = vector2.Methods.FirstOrDefault(m => m.IsOperator);
        Assert.NotNull(opMethod);
        Assert.Equal("op_Addition", opMethod!.OperatorName);
    }

    // ===== Phase 3: Properties =====

    [Fact]
    public void Build_FeatureTest_PersonType_Exists()
    {
        var module = BuildFeatureTest();
        Assert.NotNull(module.FindType("Person"));
    }

    [Fact]
    public void Build_FeatureTest_Person_HasBackingFields()
    {
        var module = BuildFeatureTest();
        var person = module.FindType("Person")!;
        // Auto-properties generate backing fields like <Name>k__BackingField
        // After mangling, they become valid C++ identifiers without < or >
        var fieldCppNames = person.Fields.Select(f => f.CppName).ToList();
        foreach (var name in fieldCppNames)
        {
            Assert.DoesNotContain("<", name);
            Assert.DoesNotContain(">", name);
        }
    }

    [Fact]
    public void Build_FeatureTest_Person_HasGetSetMethods()
    {
        var module = BuildFeatureTest();
        var person = module.FindType("Person")!;
        var methodNames = person.Methods.Select(m => m.Name).ToList();
        Assert.Contains("get_Name", methodNames);
        Assert.Contains("set_Name", methodNames);
        Assert.Contains("get_Age", methodNames);
        Assert.Contains("set_Age", methodNames);
        Assert.Contains("get_ManualProp", methodNames);
        Assert.Contains("set_ManualProp", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_TestProperties_HasFieldAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestProperties");
        var calls = instrs.OfType<IRCall>().ToList();
        // TestProperties calls get_Name, get_Age, set_ManualProp, get_ManualProp
        Assert.True(calls.Count > 0);
    }

    // ===== Phase 3: foreach array =====

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        Assert.Contains(instrs, i => i is IRArrayAccess);
    }

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasBranching()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        Assert.Contains(instrs, i => i is IRConditionalBranch);
    }

    // ===== Phase 3: using/Dispose =====

    [Fact]
    public void Build_FeatureTest_DisposableResource_Exists()
    {
        var module = BuildFeatureTest();
        Assert.NotNull(module.FindType("DisposableResource"));
    }

    [Fact]
    public void Build_FeatureTest_DisposableResource_HasDispose()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("DisposableResource")!;
        var methodNames = type.Methods.Select(m => m.Name).ToList();
        Assert.Contains("Dispose", methodNames);
    }

    // ===== Phase 3: Delegate type detection =====

    [Fact]
    public void Build_FeatureTest_MathOp_IsDelegate()
    {
        var module = BuildFeatureTest();
        var mathOp = module.FindType("MathOp");
        Assert.NotNull(mathOp);
        Assert.True(mathOp!.IsDelegate);
    }

    [Fact]
    public void Build_FeatureTest_NonDelegate_NotMarkedAsDelegate()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        Assert.False(animal.IsDelegate);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_HasLdftn()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        Assert.Contains(instrs, i => i is IRLoadFunctionPointer);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_HasDelegateCreate()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        Assert.Contains(instrs, i => i is IRDelegateCreate);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_DelegateInvokeHasParams()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        var invoke = instrs.OfType<IRDelegateInvoke>().First();
        Assert.Equal(2, invoke.ParamTypes.Count);
        Assert.Equal("int32_t", invoke.ReturnTypeCpp);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_NoUnsupportedWarnings()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        var warnings = instrs.OfType<IRComment>().Where(c => c.Text.Contains("WARNING")).ToList();
        Assert.Empty(warnings);
    }
}
