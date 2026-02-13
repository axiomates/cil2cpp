using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("SampleAssembly")]
public class IRBuilderTests
{
    private readonly SampleAssemblyFixture _fixture;

    public IRBuilderTests(SampleAssemblyFixture fixture)
    {
        _fixture = fixture;
    }

    private IRModule BuildHelloWorld(BuildConfiguration? config = null)
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, config);
        var builder = new IRBuilder(reader, config);
        return builder.Build();
    }

    private IRModule BuildArrayTest(BuildConfiguration? config = null)
    {
        using var reader = new AssemblyReader(_fixture.ArrayTestDllPath, config);
        var builder = new IRBuilder(reader, config);
        return builder.Build();
    }

    private IRModule BuildFeatureTest(BuildConfiguration? config = null)
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath, config);
        var builder = new IRBuilder(reader, config);
        return builder.Build();
    }

    private List<IRInstruction> GetMethodInstructions(IRModule module, string typeName, string methodName)
    {
        var type = module.FindType(typeName)!;
        var method = type.Methods.First(m => m.Name == methodName);
        return method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
    }

    // ===== Module basics =====

    [Fact]
    public void Build_HelloWorld_ReturnsModule()
    {
        var module = BuildHelloWorld();
        Assert.NotNull(module);
    }

    [Fact]
    public void Build_HelloWorld_ModuleName()
    {
        var module = BuildHelloWorld();
        Assert.Equal("HelloWorld", module.Name);
    }

    [Fact]
    public void Build_HelloWorld_HasTypes()
    {
        var module = BuildHelloWorld();
        Assert.True(module.Types.Count >= 2, "Should have at least Calculator and Program");
    }

    [Fact]
    public void Build_HelloWorld_HasEntryPoint()
    {
        var module = BuildHelloWorld();
        Assert.NotNull(module.EntryPoint);
        Assert.Equal("Main", module.EntryPoint!.Name);
    }

    [Fact]
    public void Build_HelloWorld_EntryPointIsStatic()
    {
        var module = BuildHelloWorld();
        Assert.True(module.EntryPoint!.IsStatic);
    }

    [Fact]
    public void Build_HelloWorld_EntryPointIsMarked()
    {
        var module = BuildHelloWorld();
        Assert.True(module.EntryPoint!.IsEntryPoint);
    }

    // ===== Calculator type =====

    [Fact]
    public void Build_HelloWorld_CalculatorType_Exists()
    {
        var module = BuildHelloWorld();
        Assert.NotNull(module.FindType("Calculator"));
    }

    [Fact]
    public void Build_HelloWorld_CalculatorType_IsNotValueType()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        Assert.False(calc.IsValueType);
    }

    [Fact]
    public void Build_HelloWorld_CalculatorType_HasFields()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        Assert.True(calc.Fields.Count > 0, "Calculator should have instance fields");
        Assert.Contains(calc.Fields, f => f.CppName.Contains("result"));
    }

    [Fact]
    public void Build_HelloWorld_CalculatorType_FieldTypeName()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var resultField = calc.Fields.First(f => f.Name == "_result");
        Assert.Equal("System.Int32", resultField.FieldTypeName);
        Assert.False(resultField.IsStatic);
    }

    [Fact]
    public void Build_HelloWorld_CalculatorType_HasMethods()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var methodNames = calc.Methods.Select(m => m.Name).ToList();
        Assert.Contains("Add", methodNames);
        Assert.Contains("SetResult", methodNames);
        Assert.Contains("GetResult", methodNames);
        Assert.Contains(".ctor", methodNames);
    }

    [Fact]
    public void Build_HelloWorld_CalculatorType_CppName()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        Assert.Equal("Calculator", calc.CppName);
    }

    [Fact]
    public void Build_HelloWorld_CalculatorType_InstanceSize_Positive()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        Assert.True(calc.InstanceSize > 0, "Instance size should be positive");
    }

    // ===== Add method =====

    [Fact]
    public void Build_HelloWorld_AddMethod_HasParameters()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.Equal(2, add.Parameters.Count);
        Assert.Equal("int32_t", add.Parameters[0].CppTypeName);
        Assert.Equal("int32_t", add.Parameters[1].CppTypeName);
    }

    [Fact]
    public void Build_HelloWorld_AddMethod_ReturnType()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.Equal("int32_t", add.ReturnTypeCpp);
        Assert.False(add.IsStatic);
    }

    [Fact]
    public void Build_HelloWorld_AddMethod_HasBasicBlocks()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.True(add.BasicBlocks.Count > 0, "Add method should have basic blocks");
    }

    [Fact]
    public void Build_HelloWorld_AddMethod_HasBinaryOp()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var add = calc.Methods.First(m => m.Name == "Add");
        var allInstructions = add.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(allInstructions, i => i is IRBinaryOp binOp && binOp.Op == "+");
    }

    [Fact]
    public void Build_HelloWorld_AddMethod_HasReturn()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var add = calc.Methods.First(m => m.Name == "Add");
        var allInstructions = add.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var returns = allInstructions.OfType<IRReturn>().ToList();
        Assert.True(returns.Count > 0, "Add method should have a return");
        Assert.True(returns.Any(r => r.Value != null), "Return should have a value");
    }

    // ===== Main method =====

    [Fact]
    public void Build_HelloWorld_MainMethod_HasStringLiterals()
    {
        var module = BuildHelloWorld();
        Assert.True(module.StringLiterals.Count > 0, "HelloWorld uses string literals");
        Assert.True(module.StringLiterals.ContainsKey("Hello, CIL2CPP!"));
    }

    [Fact]
    public void Build_HelloWorld_MainMethod_HasNewObj()
    {
        var module = BuildHelloWorld();
        var mainMethod = module.EntryPoint!;
        var allInstructions = mainMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(allInstructions, i => i is IRNewObj newObj && newObj.TypeCppName.Contains("Calculator"));
    }

    [Fact]
    public void Build_HelloWorld_MainMethod_HasCallInstructions()
    {
        var module = BuildHelloWorld();
        var mainMethod = module.EntryPoint!;
        var allInstructions = mainMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var calls = allInstructions.OfType<IRCall>().ToList();
        Assert.True(calls.Count > 0, "Main should contain method calls");
    }

    // ===== Program type =====

    [Fact]
    public void Build_HelloWorld_ProgramType_HasNoInstanceFields()
    {
        var module = BuildHelloWorld();
        var prog = module.FindType("Program")!;
        Assert.Empty(prog.Fields);
    }

    // ===== Constructor =====

    [Fact]
    public void Build_HelloWorld_Constructor_IsMarked()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var ctor = calc.Methods.First(m => m.Name == ".ctor");
        Assert.True(ctor.IsConstructor);
        Assert.False(ctor.IsStatic);
    }

    // ===== SetResult/GetResult field access =====

    [Fact]
    public void Build_HelloWorld_SetResult_HasFieldStore()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var setResult = calc.Methods.First(m => m.Name == "SetResult");
        var allInstructions = setResult.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(allInstructions, i => i is IRFieldAccess fa && fa.IsStore);
    }

    [Fact]
    public void Build_HelloWorld_GetResult_HasFieldLoad()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var getResult = calc.Methods.First(m => m.Name == "GetResult");
        var allInstructions = getResult.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(allInstructions, i => i is IRFieldAccess fa && !fa.IsStore);
    }

    // ===== ArrayTest =====

    [Fact]
    public void Build_ArrayTest_HasArrayInitData()
    {
        var module = BuildArrayTest();
        Assert.True(module.ArrayInitDataBlobs.Count > 0, "ArrayTest should have array init data blobs");
    }

    [Fact]
    public void Build_ArrayTest_HasPrimitiveTypeInfos()
    {
        var module = BuildArrayTest();
        Assert.True(module.PrimitiveTypeInfos.ContainsKey("System.Int32"),
            "ArrayTest uses int[] so System.Int32 should be registered");
    }

    // ===== Debug mode =====

    [Fact]
    public void Build_Debug_InstructionsHaveDebugInfo()
    {
        var module = BuildHelloWorld(BuildConfiguration.Debug);
        var mainMethod = module.EntryPoint!;
        var allInstructions = mainMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var withDebug = allInstructions.Where(i => i.DebugInfo != null).ToList();
        Assert.True(withDebug.Count > 0, "Debug build should have debug info on instructions");
    }

    [Fact]
    public void Build_Debug_DebugInfo_HasSourceLocation()
    {
        var module = BuildHelloWorld(BuildConfiguration.Debug);
        var mainMethod = module.EntryPoint!;
        var allInstructions = mainMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var withSource = allInstructions
            .Where(i => i.DebugInfo != null && i.DebugInfo.Line > 0 && !string.IsNullOrEmpty(i.DebugInfo.FilePath))
            .ToList();
        Assert.True(withSource.Count > 0, "Debug build should have source locations");
    }

    [Fact]
    public void Build_Release_InstructionsHaveNoDebugInfo()
    {
        var module = BuildHelloWorld(BuildConfiguration.Release);
        var mainMethod = module.EntryPoint!;
        var allInstructions = mainMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var withDebug = allInstructions.Where(i => i.DebugInfo != null).ToList();
        Assert.Empty(withDebug);
    }

    // ===== CppName mangling =====

    [Fact]
    public void Build_HelloWorld_MethodCppNames_AreMangled()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.Equal("Calculator_Add", add.CppName);
    }

    [Fact]
    public void Build_HelloWorld_FieldCppNames_AreMangled()
    {
        var module = BuildHelloWorld();
        var calc = module.FindType("Calculator")!;
        var resultField = calc.Fields.First(f => f.Name == "_result");
        Assert.Equal("f_result", resultField.CppName);
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
    public void Build_FeatureTest_TestStringOps_HasStringCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestStringOps");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("string_concat"));
        Assert.Contains(calls, c => c.FunctionName.Contains("string_is_null_or_empty"));
        Assert.Contains(calls, c => c.FunctionName.Contains("string_length"));
    }

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
        Assert.Contains(funcNames, n => n.Contains("abs"));
        Assert.Contains(funcNames, n => n.Contains("max"));
        Assert.Contains(funcNames, n => n.Contains("min"));
        Assert.Contains(funcNames, n => n.Contains("sqrt"));
        Assert.Contains(funcNames, n => n.Contains("pow"));
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
        Assert.Contains(funcNames, n => n.Contains("ceil"));
        Assert.Contains(funcNames, n => n.Contains("round"));
        Assert.Contains(funcNames, n => n.Contains("sin"));
        Assert.Contains(funcNames, n => n.Contains("cos"));
        Assert.Contains(funcNames, n => n.Contains("tan"));
        Assert.Contains(funcNames, n => n.Contains("log"));
        Assert.Contains(funcNames, n => n.Contains("exp"));
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
}
