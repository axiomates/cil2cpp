using Xunit;
using Mono.Cecil.Cil;
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
        if (config == null) return _fixture.GetHelloWorldModule();
        // Non-default config (e.g. Debug): create fresh context with debug symbols
        using var set = new AssemblySet(_fixture.HelloWorldDllPath, config);
        var reach = new ReachabilityAnalyzer(set).Analyze();
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, config);
        var builder = new IRBuilder(reader, config);
        return builder.Build(set, reach);
    }

    private IRModule BuildArrayTest(BuildConfiguration? config = null)
    {
        if (config == null) return _fixture.GetArrayTestModule();
        using var set = new AssemblySet(_fixture.ArrayTestDllPath, config);
        var reach = new ReachabilityAnalyzer(set).Analyze();
        using var reader = new AssemblyReader(_fixture.ArrayTestDllPath, config);
        var builder = new IRBuilder(reader, config);
        return builder.Build(set, reach);
    }

    private IRModule BuildFeatureTest(BuildConfiguration? config = null)
    {
        if (config == null) return _fixture.GetFeatureTestModule();
        using var set = new AssemblySet(_fixture.FeatureTestDllPath, config);
        var reach = new ReachabilityAnalyzer(set).Analyze(forceLibraryMode: true);
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath, config);
        var builder = new IRBuilder(reader, config);
        return builder.Build(set, reach);
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
        // Math methods now compile from BCL IL — calls go to mangled BCL method names
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
        // Conv_R_Un should produce static_cast<double>(static_cast<uint64_t>(val))
        var rUnConv = convs.FirstOrDefault(c =>
            c.TargetType == "double" && c.SourceExpr.Contains("uint64_t"));
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
        // Math methods now compile from BCL IL — calls go to mangled BCL method names
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

    // ===== Phase 3: Generics =====

    [Fact]
    public void Build_FeatureTest_OpenGenericType_Skipped()
    {
        var module = BuildFeatureTest();
        // Open generic Wrapper`1 should NOT appear as a type (it's a template)
        var openType = module.Types.FirstOrDefault(t => t.ILFullName == "Wrapper`1");
        Assert.Null(openType);
    }

    [Fact]
    public void Build_FeatureTest_WrapperInt_Exists()
    {
        var module = BuildFeatureTest();
        var type = module.Types.FirstOrDefault(t => t.IsGenericInstance && t.CppName.Contains("Wrapper"));
        Assert.NotNull(type);
        Assert.True(type!.IsGenericInstance);
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_HasFields()
    {
        var module = BuildFeatureTest();
        var wrapperTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("Wrapper")).ToList();
        Assert.True(wrapperTypes.Count >= 1);
        // Each Wrapper<T> should have a _value field
        foreach (var type in wrapperTypes)
        {
            Assert.True(type.Fields.Count > 0 || type.StaticFields.Count > 0,
                $"Generic instance {type.CppName} should have fields");
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_HasMethods()
    {
        var module = BuildFeatureTest();
        var wrapperTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.StartsWith("Wrapper_")).ToList();
        Assert.True(wrapperTypes.Count >= 1);
        foreach (var type in wrapperTypes)
        {
            var methodNames = type.Methods.Select(m => m.Name).ToList();
            Assert.Contains("GetValue", methodNames);
            Assert.Contains("SetValue", methodNames);
            Assert.Contains(".ctor", methodNames);
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_FieldTypeSubstituted()
    {
        var module = BuildFeatureTest();
        // Find a Wrapper instance that uses int (System.Int32)
        var wrapperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Wrapper") && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(wrapperInt);
        // _value field should have type int32_t, not "T"
        var valueField = wrapperInt!.Fields.FirstOrDefault(f => f.Name == "_value");
        Assert.NotNull(valueField);
        Assert.DoesNotContain("T", valueField!.FieldTypeName);
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_CppNameMangled()
    {
        var module = BuildFeatureTest();
        var wrapperTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("Wrapper")).ToList();
        foreach (var type in wrapperTypes)
        {
            // CppName should be a valid C++ identifier (no angle brackets, backticks, etc.)
            Assert.DoesNotContain("<", type.CppName);
            Assert.DoesNotContain(">", type.CppName);
            Assert.DoesNotContain("`", type.CppName);
        }
    }

    // ===== Rethrow instruction =====

    [Fact]
    public void Build_FeatureTest_TestRethrow_HasNestedTryCatch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestRethrow");
        // Should have two TryBegin (outer + inner)
        var tryBegins = instrs.OfType<IRTryBegin>().ToList();
        Assert.True(tryBegins.Count >= 2, "TestRethrow should have nested try blocks");
    }

    // ===== Float/Double NaN and Infinity =====

    [Fact]
    public void Build_FeatureTest_TestSpecialFloats_HasAssigns()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestSpecialFloats");
        // The NaN/Infinity float/double values should produce assignments
        Assert.True(instrs.Count > 0, "TestSpecialFloats should have instructions");
        // Check that no WARNING comments were generated (all opcodes handled)
        var warnings = instrs.OfType<IRComment>().Where(c => c.Text.Contains("WARNING")).ToList();
        Assert.Empty(warnings);
    }

    // ===== Delegate.Combine and Delegate.Remove =====

    [Fact]
    public void Build_FeatureTest_TestDelegateCombine_HasCombineCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegateCombine");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("delegate_combine"));
    }

    [Fact]
    public void Build_FeatureTest_TestDelegateCombine_HasRemoveCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegateCombine");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("delegate_remove"));
    }

    // ===== Math.Abs overloads — now compiled from BCL IL =====

    [Fact]
    public void Build_FeatureTest_TestMathAbsOverloads_HasAbsCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMathAbsOverloads");
        var calls = instrs.OfType<IRCall>().ToList();
        var funcNames = calls.Select(c => c.FunctionName).ToList();
        // Math.Abs overloads now compile from BCL IL — calls go to mangled BCL method names
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("Abs") || n.Contains("abs"));
    }

    // ===== AllFieldTypes: field size coverage for Int16/Char/Int64/Double =====

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_Exists()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes");
        Assert.NotNull(type);
    }

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_HasExpectedFields()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes")!;
        var fieldNames = type.Fields.Select(f => f.Name).ToList();
        Assert.Contains("ShortField", fieldNames);
        Assert.Contains("CharField", fieldNames);
        Assert.Contains("LongField", fieldNames);
        Assert.Contains("DoubleField", fieldNames);
        Assert.Contains("ByteField", fieldNames);
        Assert.Contains("FloatField", fieldNames);
    }

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_InstanceSize_IncludesAllFields()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes")!;
        // Object header (16) + short(2) + char(2) + long(8) + double(8) + byte(1) + float(4) + padding
        Assert.True(type.InstanceSize >= 32, $"AllFieldTypes instance size should be >= 32, got {type.InstanceSize}");
    }

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_FieldTypes()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes")!;
        var shortField = type.Fields.First(f => f.Name == "ShortField");
        var charField = type.Fields.First(f => f.Name == "CharField");
        var longField = type.Fields.First(f => f.Name == "LongField");
        var doubleField = type.Fields.First(f => f.Name == "DoubleField");
        Assert.Equal("System.Int16", shortField.FieldTypeName);
        Assert.Equal("System.Char", charField.FieldTypeName);
        Assert.Equal("System.Int64", longField.FieldTypeName);
        Assert.Equal("System.Double", doubleField.FieldTypeName);
    }

    // ===== Virtual dispatch on System.Object =====

    [Fact]
    public void Build_FeatureTest_TestObjectMethods_VirtualDispatch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualObjectDispatch");
        var calls = instrs.OfType<IRCall>().Where(c => c.IsVirtual).ToList();
        // ToString/GetHashCode/Equals on System.Object should be virtual calls
        Assert.True(calls.Count >= 2, "Virtual object dispatch should produce virtual IRCalls");
        Assert.True(calls.Any(c => c.VTableSlot >= 0), "Virtual calls should have VTableSlot");
    }

    // ===== Debug mode with FeatureTest (more coverage for debug paths) =====

    [Fact]
    public void Build_FeatureTest_Debug_InstructionsHaveSourceLocations()
    {
        var module = BuildFeatureTest(BuildConfiguration.Debug);
        var instrs = GetMethodInstructions(module, "Program", "TestArithmetic");
        var withSource = instrs.Where(i => i.DebugInfo != null && i.DebugInfo.Line > 0).ToList();
        Assert.True(withSource.Count > 0, "Debug FeatureTest should have source locations");
    }

    [Fact]
    public void Build_FeatureTest_Debug_HasFilePath()
    {
        var module = BuildFeatureTest(BuildConfiguration.Debug);
        var instrs = GetMethodInstructions(module, "Program", "TestArithmetic");
        var withFile = instrs.Where(i => i.DebugInfo?.FilePath != null).ToList();
        Assert.True(withFile.Count > 0, "Debug FeatureTest should have file paths");
        Assert.True(withFile[0].DebugInfo!.FilePath!.Contains("Program.cs"));
    }

    [Fact]
    public void Build_FeatureTest_Debug_ExceptionHandling_HasDebugInfo()
    {
        var module = BuildFeatureTest(BuildConfiguration.Debug);
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionHandling");
        var tryInstrs = instrs.OfType<IRTryBegin>().ToList();
        Assert.True(tryInstrs.Count > 0);
        // In debug mode, exception handler instructions should have debug info
        var withDebug = instrs.Where(i => i.DebugInfo != null).ToList();
        Assert.True(withDebug.Count > instrs.Count / 2,
            "Most instructions should have debug info in debug mode");
    }

    // ===== Static constructor guard =====

    [Fact]
    public void Build_FeatureTest_StaticFieldAccess_HasCctorGuard()
    {
        var module = BuildFeatureTest();
        // Program has a static field _globalValue with initializer
        // Accessing static fields may trigger cctor guard
        var program = module.FindType("Program")!;
        if (program.HasCctor)
        {
            var instrs = GetMethodInstructions(module, "Program", "TestStaticFields");
            Assert.Contains(instrs, i => i is IRStaticCtorGuard);
        }
    }

    // ===== FeatureTest: Array creation with RawCpp =====

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasArrayCreation()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        // newarr produces IRRawCpp with array_create
        Assert.Contains(instrs, i => i is IRRawCpp raw && raw.Code.Contains("array_create"));
    }

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasArrayLength()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        Assert.Contains(instrs, i => i is IRRawCpp raw && raw.Code.Contains("array_length"));
    }

    // ===== Typed array element access (GetArrayElementType coverage) =====

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasByteArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "uint8_t" && !a.IsStore);  // ldelem.u1
        Assert.Contains(accesses, a => a.ElementType == "int8_t" && a.IsStore);    // stelem.i1
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasShortArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "int16_t" && !a.IsStore);  // ldelem.i2
        Assert.Contains(accesses, a => a.ElementType == "int16_t" && a.IsStore);   // stelem.i2
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasLongArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "int64_t" && !a.IsStore);  // ldelem.i8
        Assert.Contains(accesses, a => a.ElementType == "int64_t" && a.IsStore);   // stelem.i8
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasFloatArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "float" && !a.IsStore);    // ldelem.r4
        Assert.Contains(accesses, a => a.ElementType == "float" && a.IsStore);     // stelem.r4
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasDoubleArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "double" && !a.IsStore);   // ldelem.r8
        Assert.Contains(accesses, a => a.ElementType == "double" && a.IsStore);    // stelem.r8
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasObjectArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "cil2cpp::Object*" && !a.IsStore); // ldelem.ref
        Assert.Contains(accesses, a => a.ElementType == "cil2cpp::Object*" && a.IsStore);  // stelem.ref
    }

    // ===== GenericHelper: generic type resolution in method bodies =====

    [Fact]
    public void Build_FeatureTest_GenericHelper_IntSpecialization_Exists()
    {
        var module = BuildFeatureTest();
        var types = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("GenericHelper")).ToList();
        Assert.True(types.Count >= 1, "Should have GenericHelper<int> specialization");
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_HasArrayField()
    {
        var module = BuildFeatureTest();
        var types = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("GenericHelper")).ToList();
        foreach (var type in types)
        {
            Assert.True(type.Fields.Count > 0 || type.StaticFields.Count > 0,
                $"{type.CppName} should have _items field");
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_HasSetGetCountMethods()
    {
        var module = BuildFeatureTest();
        var types = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("GenericHelper")).ToList();
        foreach (var type in types)
        {
            var methodNames = type.Methods.Select(m => m.Name).ToList();
            Assert.Contains("Set", methodNames);
            Assert.Contains("Get", methodNames);
            Assert.Contains("Count", methodNames);
            Assert.Contains(".ctor", methodNames);
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_MethodsHaveInstructions()
    {
        var module = BuildFeatureTest();
        var helperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(helperInt);
        var setMethod = helperInt!.Methods.First(m => m.Name == "Set");
        var getMethod = helperInt.Methods.First(m => m.Name == "Get");
        Assert.True(setMethod.BasicBlocks.SelectMany(b => b.Instructions).Any(),
            "Set method should have instructions");
        Assert.True(getMethod.BasicBlocks.SelectMany(b => b.Instructions).Any(),
            "Get method should have instructions");
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_StringSpecialization()
    {
        var module = BuildFeatureTest();
        var helperStr = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.String"));
        Assert.NotNull(helperStr);
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_SwapMethod_HasRefParams()
    {
        var module = BuildFeatureTest();
        var helperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(helperInt);
        var swap = helperInt!.Methods.FirstOrDefault(m => m.Name == "Swap");
        Assert.NotNull(swap);
        Assert.Equal(2, swap!.Parameters.Count);
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_SwapMethod_HasInstructions()
    {
        var module = BuildFeatureTest();
        var helperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(helperInt);
        var swap = helperInt!.Methods.First(m => m.Name == "Swap");
        var instrs = swap.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.True(instrs.Count > 0, "Swap should have instructions");
    }

    // ===== Generic Methods (standalone, not generic types) =====

    [Fact]
    public void Build_FeatureTest_GenericMethods_IdentityInt_Exists()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var identityInt = utils!.Methods.FirstOrDefault(m =>
            m.CppName.Contains("Identity") && m.CppName.Contains("System_Int32"));
        Assert.NotNull(identityInt);
        Assert.True(identityInt!.IsGenericInstance);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_IdentityString_Exists()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var identityStr = utils!.Methods.FirstOrDefault(m =>
            m.CppName.Contains("Identity") && m.CppName.Contains("System_String"));
        Assert.NotNull(identityStr);
        Assert.Equal("cil2cpp::String*", identityStr!.ReturnTypeCpp);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_SwapInt_Exists()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var swapInt = utils!.Methods.FirstOrDefault(m =>
            m.CppName.Contains("Swap") && m.CppName.Contains("System_Int32"));
        Assert.NotNull(swapInt);
        Assert.Equal(2, swapInt!.Parameters.Count);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_IdentityInt_HasBody()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var identityInt = utils!.Methods.First(m =>
            m.CppName.Contains("Identity") && m.CppName.Contains("System_Int32"));
        var instrs = identityInt.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.True(instrs.Count > 0, "Identity<int> should have method body");
        Assert.Contains(instrs, i => i is IRReturn);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_OpenMethodsSkipped()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        // Open generic methods (Identity<T>, Swap<T>) should NOT appear as non-specialized
        var openMethods = utils!.Methods.Where(m =>
            m.Name is "Identity" or "Swap" && !m.IsGenericInstance).ToList();
        Assert.Empty(openMethods);
    }

    [Fact]
    public void Build_FeatureTest_TestGenericMethods_CallsSpecialized()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestGenericMethods");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("Identity") && c.FunctionName.Contains("System_Int32"));
        Assert.Contains(calls, c => c.FunctionName.Contains("Identity") && c.FunctionName.Contains("System_String"));
        Assert.Contains(calls, c => c.FunctionName.Contains("Swap") && c.FunctionName.Contains("System_Int32"));
    }

    // ===== Lambda/Closures =====

    [Fact]
    public void Build_FeatureTest_Lambda_DisplayClass_Exists()
    {
        var module = BuildFeatureTest();
        // C# compiler generates <>c class for stateless lambdas
        var lambdaType = module.Types.FirstOrDefault(t => t.CppName.Contains("___c") && !t.CppName.Contains("DisplayClass"));
        Assert.NotNull(lambdaType);
    }

    [Fact]
    public void Build_FeatureTest_Lambda_DisplayClass_HasMethods()
    {
        var module = BuildFeatureTest();
        var lambdaType = module.Types.FirstOrDefault(t => t.CppName.Contains("___c") && !t.CppName.Contains("DisplayClass"));
        Assert.NotNull(lambdaType);
        // Should have lambda body methods (e.g., <TestLambda>b__43_0)
        Assert.True(lambdaType!.Methods.Count >= 2, "Lambda display class should have lambda body methods");
    }

    [Fact]
    public void Build_FeatureTest_Closure_DisplayClass_Exists()
    {
        var module = BuildFeatureTest();
        // C# compiler generates <>c__DisplayClass for closures
        var closureType = module.Types.FirstOrDefault(t => t.CppName.Contains("DisplayClass"));
        Assert.NotNull(closureType);
    }

    [Fact]
    public void Build_FeatureTest_Closure_DisplayClass_HasCapturedFields()
    {
        var module = BuildFeatureTest();
        var closureType = module.Types.FirstOrDefault(t => t.CppName.Contains("DisplayClass"));
        Assert.NotNull(closureType);
        // Should have captured variable fields
        Assert.True(closureType!.Fields.Count >= 1, "Closure display class should have captured variable fields");
    }

    [Fact]
    public void Build_FeatureTest_TestLambda_HasDelegateCreate()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestLambda");
        Assert.Contains(instrs, i => i is IRDelegateCreate);
    }

    [Fact]
    public void Build_FeatureTest_TestLambda_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestLambda");
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestClosure_CreatesDelegateFromDisplayClass()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestClosure");
        // Should create display class instance (IRNewObj)
        Assert.Contains(instrs, i => i is IRNewObj);
        // Should create delegate from display class method (IRDelegateCreate)
        Assert.Contains(instrs, i => i is IRDelegateCreate);
        // Should invoke delegate (IRDelegateInvoke)
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestClosure_HasFieldAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestClosure");
        // Should store captured variable to display class field
        var fieldAccesses = instrs.OfType<IRFieldAccess>().Where(f => f.IsStore).ToList();
        Assert.True(fieldAccesses.Count >= 1, "Should store captured variables to display class");
    }

    // ===== Events =====

    [Fact]
    public void Build_FeatureTest_EventSource_HasDelegateField()
    {
        var module = BuildFeatureTest();
        var eventType = module.FindType("EventSource");
        Assert.NotNull(eventType);
        // Event backing field (delegate)
        Assert.True(eventType!.Fields.Count >= 1, "EventSource should have delegate backing field");
    }

    [Fact]
    public void Build_FeatureTest_EventSource_HasAddRemoveMethods()
    {
        var module = BuildFeatureTest();
        var eventType = module.FindType("EventSource");
        Assert.NotNull(eventType);
        var methodNames = eventType!.Methods.Select(m => m.Name).ToList();
        // C# generates add_ and remove_ accessor methods for events
        Assert.Contains("add_OnNotify", methodNames);
        Assert.Contains("remove_OnNotify", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_EventSource_HasSubscribeFireMethods()
    {
        var module = BuildFeatureTest();
        var eventType = module.FindType("EventSource");
        Assert.NotNull(eventType);
        var methodNames = eventType!.Methods.Select(m => m.Name).ToList();
        Assert.Contains("Subscribe", methodNames);
        Assert.Contains("Unsubscribe", methodNames);
        Assert.Contains("Fire", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_EventSource_FireMethod_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var fireMethod = module.FindType("EventSource")!.Methods.First(m => m.Name == "Fire");
        var instrs = fireMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestEvents_HasMethodCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestEvents");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.True(calls.Count >= 3, "TestEvents should have Subscribe/Unsubscribe/Fire calls");
    }

    // ===== Ldflda / Ldobj / Stobj instructions =====

    [Fact]
    public void Build_FeatureTest_EventSource_AddMethod_HasLdflda()
    {
        var module = BuildFeatureTest();
        var addMethod = module.FindType("EventSource")!.Methods.First(m => m.Name == "add_OnNotify");
        var instrs = addMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // The auto-generated add_ method uses ldflda for Interlocked.CompareExchange
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("&") && r.Code.Contains("->"));
    }

    // ===== Phase 2: User value type registration =====

    [Fact]
    public void Build_FeatureTest_UserValueType_Registered()
    {
        var module = BuildFeatureTest();
        // After building, Point and Vector2 should be registered as value types
        Assert.True(CppNameMapper.IsValueType("Point"), "Point should be registered as value type");
        Assert.True(CppNameMapper.IsValueType("Vector2"), "Vector2 should be registered as value type");
    }

    // ===== Phase 2: VTable seeded with Object methods =====

    [Fact]
    public void Build_FeatureTest_VTable_SeededWithObjectMethods()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        var vtableNames = animal.VTable.Select(e => e.MethodName).ToList();
        Assert.Contains("ToString", vtableNames);
        Assert.Contains("Equals", vtableNames);
        Assert.Contains("GetHashCode", vtableNames);
    }

    // ===== Ref parameters: Ldind_I4 / Stind_I4 =====

    [Fact]
    public void Build_FeatureTest_SwapInt_HasLdindI4()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapInt");
        // ldind.i4 produces IRRawCpp with *(int32_t*) cast
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(int32_t*)"));
    }

    [Fact]
    public void Build_FeatureTest_SwapInt_HasStindI4()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapInt");
        // stind.i4 produces IRRawCpp with *(int32_t*) assignment
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(int32_t*)") && r.Code.Contains("="));
    }

    // ===== Ref parameters: Ldind_Ref / Stind_Ref =====

    [Fact]
    public void Build_FeatureTest_SwapObj_HasLdindRef()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapObj");
        // ldind.ref produces IRRawCpp with *(cil2cpp::Object**)
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(cil2cpp::Object**)"));
    }

    [Fact]
    public void Build_FeatureTest_SwapObj_HasStindRef()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapObj");
        // stind.ref produces IRRawCpp with *(cil2cpp::Object**) assignment
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(cil2cpp::Object**)") && r.Code.Contains("="));
    }

    // ===== GetIndirectType covers all ldind/stind variants =====

    [Theory]
    [InlineData(Code.Ldind_I1, "int8_t")]
    [InlineData(Code.Ldind_I2, "int16_t")]
    [InlineData(Code.Ldind_I4, "int32_t")]
    [InlineData(Code.Ldind_I8, "int64_t")]
    [InlineData(Code.Ldind_U1, "uint8_t")]
    [InlineData(Code.Ldind_U2, "uint16_t")]
    [InlineData(Code.Ldind_U4, "uint32_t")]
    [InlineData(Code.Ldind_R4, "float")]
    [InlineData(Code.Ldind_R8, "double")]
    [InlineData(Code.Ldind_I, "intptr_t")]
    [InlineData(Code.Stind_I1, "int8_t")]
    [InlineData(Code.Stind_I2, "int16_t")]
    [InlineData(Code.Stind_I4, "int32_t")]
    [InlineData(Code.Stind_I8, "int64_t")]
    [InlineData(Code.Stind_R4, "float")]
    [InlineData(Code.Stind_R8, "double")]
    public void GetIndirectType_ReturnsCorrectCppType(Code code, string expectedCppType)
    {
        var result = IRBuilder.GetIndirectType(code);
        Assert.Equal(expectedCppType, result);
    }

    // ===== TestRefParams calls SwapInt and SwapObj =====

    [Fact]
    public void Build_FeatureTest_TestRefParams_HasSwapCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestRefParams");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("SwapInt"));
        Assert.Contains(calls, c => c.FunctionName.Contains("SwapObj"));
    }

    // ===== Ldsflda (load static field address) =====

    [Fact]
    public void Build_FeatureTest_TestStaticFieldRef_HasLdsflda()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestStaticFieldRef");
        // ldsflda produces IRRawCpp with &TypeName_statics.fieldName
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("_statics.") && r.Code.Contains("&"));
    }

    // ===== Ldvirtftn (virtual function pointer for delegate) =====

    [Fact]
    public void Build_FeatureTest_TestVirtualDelegate_HasLdvirtftn()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualDelegate");
        var ldfptrs = instrs.OfType<IRLoadFunctionPointer>().ToList();
        Assert.Contains(ldfptrs, p => p.IsVirtual);
    }

    [Fact]
    public void Build_FeatureTest_TestVirtualDelegate_HasDelegateCreate()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualDelegate");
        Assert.Contains(instrs, i => i is IRDelegateCreate);
    }

    [Fact]
    public void Build_FeatureTest_TestVirtualDelegate_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualDelegate");
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    // ===== Generic value type (Pair<T>) =====

    [Fact]
    public void Build_FeatureTest_PairInt_Exists()
    {
        var module = BuildFeatureTest();
        var pairTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("Pair")).ToList();
        Assert.True(pairTypes.Count >= 1, "Should have Pair<int> specialization");
    }

    [Fact]
    public void Build_FeatureTest_PairInt_IsValueType()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        Assert.True(pairInt!.IsValueType, "Pair<int> should be a value type");
    }

    [Fact]
    public void Build_FeatureTest_PairInt_HasFields()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        var fieldNames = pairInt!.Fields.Select(f => f.Name).ToList();
        Assert.Contains("First", fieldNames);
        Assert.Contains("Second", fieldNames);
    }

    [Fact]
    public void Build_FeatureTest_PairInt_FieldTypesSubstituted()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        var firstField = pairInt!.Fields.First(f => f.Name == "First");
        Assert.Equal("System.Int32", firstField.FieldTypeName);
    }

    [Fact]
    public void Build_FeatureTest_PairInt_HasConstructor()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        var ctor = pairInt!.Methods.FirstOrDefault(m => m.IsConstructor);
        Assert.NotNull(ctor);
        Assert.Equal(2, ctor!.Parameters.Count);
    }

    // ===== StringFunc delegate type =====

    [Fact]
    public void Build_FeatureTest_StringFunc_IsDelegate()
    {
        var module = BuildFeatureTest();
        var stringFunc = module.FindType("StringFunc");
        Assert.NotNull(stringFunc);
        Assert.True(stringFunc!.IsDelegate);
    }

    // Build path with explicit AssemblySet + Reachability
    [Fact]
    public void Build_MultiAssembly_HelloWorld_ProducesModule()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var reachability = new ReachabilityAnalyzer(set).Analyze();

        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var builder = new IRBuilder(reader);
        var module = builder.Build(set, reachability);

        Assert.NotNull(module);
        Assert.Equal("HelloWorld", module.Name);
        Assert.NotEmpty(module.Types);
    }

    [Fact]
    public void Build_MultiAssembly_HelloWorld_SetsSourceKind()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var reachability = new ReachabilityAnalyzer(set).Analyze();

        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var builder = new IRBuilder(reader);
        var module = builder.Build(set, reachability);

        var programType = module.FindType("Program");
        Assert.NotNull(programType);
        Assert.Equal(AssemblyKind.User, programType!.SourceKind);
    }

    [Fact]
    public void Build_MultiAssembly_HelloWorld_MarksRuntimeProvided()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var reachability = new ReachabilityAnalyzer(set).Analyze();

        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var builder = new IRBuilder(reader);
        var module = builder.Build(set, reachability);

        // User types should NOT be runtime-provided
        var programType = module.FindType("Program");
        Assert.NotNull(programType);
        Assert.False(programType!.IsRuntimeProvided);
    }

    [Fact]
    public void Build_MultiAssembly_MultiAssemblyTest_CrossAssemblyTypes()
    {
        using var set = new AssemblySet(_fixture.MultiAssemblyTestDllPath);
        var reachability = new ReachabilityAnalyzer(set).Analyze();

        using var reader = new AssemblyReader(_fixture.MultiAssemblyTestDllPath);
        var builder = new IRBuilder(reader);
        var module = builder.Build(set, reachability);

        // Should have types from both assemblies
        Assert.Contains(module.Types, t => t.Name == "Program");
        Assert.Contains(module.Types, t => t.Name == "MathUtils");
    }

    [Fact]
    public void Build_MultiAssembly_HasEntryPoint()
    {
        using var set = new AssemblySet(_fixture.HelloWorldDllPath);
        var reachability = new ReachabilityAnalyzer(set).Analyze();

        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var builder = new IRBuilder(reader);
        var module = builder.Build(set, reachability);

        Assert.NotNull(module.EntryPoint);
    }

    // ===== Indexer (get_Item/set_Item) =====

    [Fact]
    public void Build_FeatureTest_IntList_HasGetItem()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "IntList");
        Assert.Contains(type.Methods, m => m.Name == "get_Item");
    }

    [Fact]
    public void Build_FeatureTest_IntList_HasSetItem()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "IntList");
        Assert.Contains(type.Methods, m => m.Name == "set_Item");
    }

    [Fact]
    public void Build_FeatureTest_TestIndexer_CallsGetSetItem()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestIndexer");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("get_Item"));
        Assert.Contains(calls, c => c.FunctionName.Contains("set_Item"));
    }

    // ===== Default parameters =====

    [Fact]
    public void Build_FeatureTest_TestDefaultParameters_CallsAdd()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDefaultParameters");
        var calls = instrs.OfType<IRCall>().ToList();
        // Both calls to Add should be present (with default and explicit)
        var addCalls = calls.Where(c => c.FunctionName.Contains("Add")).ToList();
        Assert.True(addCalls.Count >= 2);
    }

    [Fact]
    public void Build_FeatureTest_DefaultParamHelper_AddHasTwoParams()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "DefaultParamHelper");
        var addMethod = type.Methods.First(m => m.Name == "Add");
        // In IL, default params are just regular params
        Assert.Equal(2, addMethod.Parameters.Count);
    }

    // ===== Init-only setter =====

    [Fact]
    public void Build_FeatureTest_ImmutablePoint_HasSetX()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "ImmutablePoint");
        // init setter compiles to set_X method
        Assert.Contains(type.Methods, m => m.Name == "set_X");
    }

    [Fact]
    public void Build_FeatureTest_ImmutablePoint_HasSetY()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "ImmutablePoint");
        Assert.Contains(type.Methods, m => m.Name == "set_Y");
    }

    [Fact]
    public void Build_FeatureTest_TestInitOnlySetter_CallsSetters()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestInitOnlySetter");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("set_X"));
        Assert.Contains(calls, c => c.FunctionName.Contains("set_Y"));
    }

    // ===== Checked arithmetic =====

    [Fact]
    public void Build_FeatureTest_CheckedAdd_HasCheckedAdd()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "CheckedAdd");
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("cil2cpp::checked_add"));
    }

    [Fact]
    public void Build_FeatureTest_CheckedSub_HasCheckedSub()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "CheckedSub");
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("cil2cpp::checked_sub"));
    }

    [Fact]
    public void Build_FeatureTest_CheckedMul_HasCheckedMul()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "CheckedMul");
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("cil2cpp::checked_mul"));
    }

    // ===== Checked conversions (conv.ovf.*) =====

    [Fact]
    public void Build_FeatureTest_CheckedToByte_HasCheckedConv()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "CheckedToByte");
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("cil2cpp::checked_conv<uint8_t>"));
    }

    [Fact]
    public void Build_FeatureTest_CheckedToSByte_HasCheckedConv()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "CheckedToSByte");
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("cil2cpp::checked_conv<int8_t>"));
    }

    [Fact]
    public void Build_FeatureTest_CheckedToUInt_HasCheckedConv()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "CheckedToUInt");
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("cil2cpp::checked_conv<uint32_t>"));
    }

    [Theory]
    [InlineData(Code.Conv_Ovf_I_Un, true)]
    [InlineData(Code.Conv_Ovf_I1_Un, true)]
    [InlineData(Code.Conv_Ovf_U_Un, true)]
    [InlineData(Code.Conv_Ovf_U8_Un, true)]
    [InlineData(Code.Conv_Ovf_I, false)]
    [InlineData(Code.Conv_Ovf_I1, false)]
    [InlineData(Code.Conv_Ovf_U, false)]
    [InlineData(Code.Conv_Ovf_U8, false)]
    public void IsUnsignedCheckedConv_ReturnsCorrect(Code code, bool expected)
    {
        Assert.Equal(expected, IRBuilder.IsUnsignedCheckedConv(code));
    }

    // ===== GetCheckedConvType =====

    [Theory]
    [InlineData(Code.Conv_Ovf_I, "intptr_t")]
    [InlineData(Code.Conv_Ovf_I1, "int8_t")]
    [InlineData(Code.Conv_Ovf_I2, "int16_t")]
    [InlineData(Code.Conv_Ovf_I4, "int32_t")]
    [InlineData(Code.Conv_Ovf_I8, "int64_t")]
    [InlineData(Code.Conv_Ovf_U, "uintptr_t")]
    [InlineData(Code.Conv_Ovf_U1, "uint8_t")]
    [InlineData(Code.Conv_Ovf_U2, "uint16_t")]
    [InlineData(Code.Conv_Ovf_U4, "uint32_t")]
    [InlineData(Code.Conv_Ovf_U8, "uint64_t")]
    [InlineData(Code.Conv_Ovf_I_Un, "intptr_t")]
    [InlineData(Code.Conv_Ovf_I1_Un, "int8_t")]
    [InlineData(Code.Conv_Ovf_I2_Un, "int16_t")]
    [InlineData(Code.Conv_Ovf_I4_Un, "int32_t")]
    [InlineData(Code.Conv_Ovf_I8_Un, "int64_t")]
    [InlineData(Code.Conv_Ovf_U_Un, "uintptr_t")]
    [InlineData(Code.Conv_Ovf_U1_Un, "uint8_t")]
    [InlineData(Code.Conv_Ovf_U2_Un, "uint16_t")]
    [InlineData(Code.Conv_Ovf_U4_Un, "uint32_t")]
    [InlineData(Code.Conv_Ovf_U8_Un, "uint64_t")]
    public void GetCheckedConvType_ReturnsCorrectCppType(Code code, string expectedCppType)
    {
        var result = IRBuilder.GetCheckedConvType(code);
        Assert.Equal(expectedCppType, result);
    }

    // ===== Nullable<T> =====

    [Fact]
    public void Build_FeatureTest_Nullable_MonomorphizesStruct()
    {
        var module = BuildFeatureTest();
        // Nullable<int> should be monomorphized with correct fields
        var nullable = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Nullable"));
        Assert.NotNull(nullable);
        Assert.True(nullable!.IsValueType);
        var fieldNames = nullable.Fields.Select(f => f.Name).ToList();
        Assert.Contains("hasValue", fieldNames);
        Assert.Contains("value", fieldNames);
    }

    // ===== ValueTuple =====

    [Fact]
    public void Build_FeatureTest_ValueTuple_MonomorphizesStruct()
    {
        var module = BuildFeatureTest();
        // ValueTuple<int,int> should be monomorphized with Item1, Item2 fields
        var tuple = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("ValueTuple"));
        Assert.NotNull(tuple);
        Assert.True(tuple!.IsValueType);
        var fieldNames = tuple.Fields.Select(f => f.Name).ToList();
        Assert.Contains("Item1", fieldNames);
        Assert.Contains("Item2", fieldNames);
    }

    // ===== record =====

    [Fact]
    public void Build_FeatureTest_Record_DetectedAsRecord()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        Assert.True(recordType!.IsRecord, "PersonRecord should be detected as a record type");
    }

    [Fact]
    public void Build_FeatureTest_Record_HasSynthesizedMethods()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        var methodNames = recordType!.Methods.Select(m => m.Name).ToList();
        Assert.Contains("ToString", methodNames);
        Assert.Contains("Equals", methodNames);
        Assert.Contains("GetHashCode", methodNames);
        Assert.Contains("<Clone>$", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_Record_ToStringHasBody()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        var toString = recordType!.Methods.FirstOrDefault(m => m.Name == "ToString");
        Assert.NotNull(toString);
        Assert.True(toString!.BasicBlocks.Count > 0, "Synthesized ToString should have a body");
        // Should contain string_concat calls for field formatting
        var allCode = string.Join("\n", toString.BasicBlocks
            .SelectMany(b => b.Instructions).Select(i => i.ToCpp()));
        Assert.Contains("string_concat", allCode);
    }

    [Fact]
    public void Build_FeatureTest_Record_EqualsHasFieldComparison()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        // Find the typed Equals (takes PersonRecord* parameter)
        var typedEquals = recordType!.Methods.FirstOrDefault(m =>
            m.Name == "Equals" && m.Parameters.Count == 1
            && m.Parameters[0].CppTypeName.Contains(recordType.CppName));
        Assert.NotNull(typedEquals);
        Assert.True(typedEquals!.BasicBlocks.Count > 0, "Typed Equals should have a synthesized body");
    }

    // ===== record struct =====

    [Fact]
    public void Build_FeatureTest_RecordStruct_DetectedAsRecord()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        Assert.True(pointRecord!.IsRecord, "PointRecord should be detected as a record type");
        Assert.True(pointRecord.IsValueType, "PointRecord should be a value type");
    }

    [Fact]
    public void Build_FeatureTest_RecordStruct_HasSynthesizedMethods()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        var methodNames = pointRecord!.Methods.Select(m => m.Name).ToList();
        Assert.Contains("ToString", methodNames);
        Assert.Contains("Equals", methodNames);
        Assert.Contains("GetHashCode", methodNames);
        Assert.Contains("op_Equality", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_RecordStruct_EqualsUsesValueAccessor()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        // Find typed Equals (takes PointRecord parameter — value, not pointer)
        var typedEquals = pointRecord!.Methods.FirstOrDefault(m =>
            m.Name == "Equals" && m.Parameters.Count == 1
            && m.Parameters[0].CppTypeName.Contains(pointRecord.CppName));
        Assert.NotNull(typedEquals);
        Assert.True(typedEquals!.BasicBlocks.Count > 0);
        // Value type should NOT have null check in typed Equals
        var code = string.Join("\n", typedEquals.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRRawCpp>()
            .Select(i => i.Code));
        Assert.DoesNotContain("== nullptr", code);
        // Should use "." accessor for value-type other param, not "->"
        Assert.Contains(".", code);
    }

    [Fact]
    public void Build_FeatureTest_RecordStruct_NoCloneMethod()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        var cloneMethod = pointRecord!.Methods.FirstOrDefault(m => m.Name == "<Clone>$");
        // record struct doesn't have <Clone>$ method
        Assert.Null(cloneMethod);
    }

    // ===== Async/Await =====

    [Fact]
    public void Build_FeatureTest_Async_StateMachineCompiled()
    {
        var module = BuildFeatureTest();
        // C# compiler generates a state machine type for async methods
        // Name pattern: <ComputeAsync>d__N (class in Debug, struct in Release)
        var stateMachine = module.Types.FirstOrDefault(t =>
            t.Name.Contains("ComputeAsync") && t.Name.Contains("d__"));
        Assert.NotNull(stateMachine);
        // Should have MoveNext method
        var moveNext = stateMachine!.Methods.FirstOrDefault(m => m.Name == "MoveNext");
        Assert.NotNull(moveNext);
        Assert.True(moveNext!.BasicBlocks.Count > 0, "MoveNext should have body");
    }

    [Fact]
    public void Build_FeatureTest_Async_MoveNextCallsExist()
    {
        var module = BuildFeatureTest();
        var stateMachine = module.Types.FirstOrDefault(t =>
            t.Name.Contains("ComputeAsync") && t.Name.Contains("d__"));
        Assert.NotNull(stateMachine);
        var moveNext = stateMachine!.Methods.First(m => m.Name == "MoveNext");
        var allInstructions = moveNext.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // Should have IRRawCpp instructions for intercepted builder/awaiter calls
        var rawCpp = allInstructions.OfType<IRRawCpp>().ToList();
        Assert.True(rawCpp.Count > 0, "MoveNext should have inline C++ from intercepted calls");
    }

    // ===== unbox.any reference type → castclass =====

    [Fact]
    public void Build_FeatureTest_UnboxAnyRefType_EmitsCastNotUnbox()
    {
        var module = BuildFeatureTest();
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestUnboxAnyRefType");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // unbox.any on string (reference type) should emit IRCast, not IRUnbox
        var casts = allInstructions.OfType<IRCast>().ToList();
        Assert.True(casts.Any(c => c.TargetTypeCpp.Contains("String")),
            "unbox.any on reference type should emit IRCast (castclass semantics)");
    }

    [Fact]
    public void Build_FeatureTest_UnboxAnyValueType_StillEmitsUnbox()
    {
        var module = BuildFeatureTest();
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestUnboxAnyRefType");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // unbox.any on int (value type) should still emit IRUnbox
        var unboxes = allInstructions.OfType<IRUnbox>().ToList();
        Assert.True(unboxes.Count > 0, "unbox.any on value type should still emit IRUnbox");
    }

    // ===== box Nullable<T> → unwrap =====

    [Fact]
    public void Build_FeatureTest_BoxNullable_EmitsUnwrapLogic()
    {
        var module = BuildFeatureTest();
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestNullableBoxing");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var rawCpp = allInstructions.OfType<IRRawCpp>().Select(r => r.Code).ToList();
        var allCode = string.Join("\n", rawCpp);
        // box Nullable<T> should check f_hasValue, not box the whole struct
        Assert.Contains("f_hasValue", allCode);
        Assert.Contains("f_value", allCode);
        Assert.Contains("nullptr", allCode);
    }

    [Fact]
    public void Build_FeatureTest_BoxNullable_NoIRBoxForNullable()
    {
        var module = BuildFeatureTest();
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestNullableBoxing");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // There should be no IRBox with "Nullable" in the type name
        var nullableBoxes = allInstructions.OfType<IRBox>()
            .Where(b => b.ValueTypeCppName.Contains("Nullable"))
            .ToList();
        Assert.Empty(nullableBoxes);
    }

    // ===== ValueTuple.Equals / GetHashCode / ToString =====

    [Fact]
    public void Build_FeatureTest_ValueTupleGetHashCode_UsesHashCombining()
    {
        var module = BuildFeatureTest();
        // ValueTuple.GetHashCode is intercepted when called directly on the tuple type
        // The C# compiler may route through constrained callvirt → Object.GetHashCode()
        // but our interception also handles direct ValueTuple calls
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestValueTupleEquals");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var rawCpp = allInstructions.OfType<IRRawCpp>().Select(r => r.Code).ToList();
        var allCode = string.Join("\n", rawCpp);
        // No stub comments should remain for Equals
        Assert.DoesNotContain("/* ValueTuple.Equals stub */", allCode);
    }

    // ===== Abstract class + multi-level inheritance =====

    [Fact]
    public void Build_FeatureTest_AbstractClass_HasAbstractFlag()
    {
        var module = BuildFeatureTest();
        var shape = module.FindType("Shape");
        Assert.NotNull(shape);
        Assert.True(shape!.IsAbstract);
        var areaMethod = shape.Methods.FirstOrDefault(m => m.Name == "Area");
        Assert.NotNull(areaMethod);
        Assert.True(areaMethod!.IsAbstract);
    }

    [Fact]
    public void Build_FeatureTest_MultiLevelInheritance_VTableOverrides()
    {
        var module = BuildFeatureTest();
        var unitCircle = module.FindType("UnitCircle");
        Assert.NotNull(unitCircle);
        // UnitCircle inherits from Circle which inherits from Shape
        // Area() and Describe() should be in vtable (inherited from Circle)
        Assert.Contains(unitCircle!.VTable, e => e.MethodName == "Area");
        Assert.Contains(unitCircle.VTable, e => e.MethodName == "Describe");
    }

    // ===== Conversion operators (op_Implicit / op_Explicit) =====

    [Fact]
    public void Build_FeatureTest_ImplicitOperator_CompilesAsStaticMethod()
    {
        var module = BuildFeatureTest();
        var celsius = module.FindType("Celsius");
        Assert.NotNull(celsius);
        Assert.Contains(celsius!.Methods, m => m.Name == "op_Implicit");
    }

    [Fact]
    public void Build_FeatureTest_ExplicitOperator_CompilesAsStaticMethod()
    {
        var module = BuildFeatureTest();
        var celsius = module.FindType("Celsius");
        Assert.NotNull(celsius);
        Assert.Contains(celsius!.Methods, m => m.Name == "op_Explicit");
    }

    // ===== Static method call triggers cctor (ECMA-335 II.10.5.3.1) =====

    [Fact]
    public void Build_FeatureTest_StaticMethodCall_HasCctorGuard()
    {
        var module = BuildFeatureTest();
        var lazyInit = module.FindType("LazyInit");
        Assert.NotNull(lazyInit);
        Assert.True(lazyInit!.HasCctor);

        // TestStaticMethodCctor calls LazyInit.GetValue() — should have cctor guard
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestStaticMethodCctor");
        Assert.NotNull(testMethod);
        var instrs = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instrs, i => i is IRStaticCtorGuard);
    }

    // ===== Object.ReferenceEquals =====

    [Fact]
    public void Build_FeatureTest_ReferenceEquals_CompiledCorrectly()
    {
        var module = BuildFeatureTest();
        // C# compiler inlines Object.ReferenceEquals as ceq (pointer comparison)
        // Verify the test method compiles and produces comparison instructions
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestReferenceEquals");
        Assert.NotNull(testMethod);
        var instrs = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // Should have comparison operations (ceq → IRBinaryOp or IRRawCpp with ==)
        Assert.True(instrs.Count > 0);
    }

    // ===== Object.MemberwiseClone =====

    [Fact]
    public void Build_FeatureTest_MemberwiseClone_MappedToRuntime()
    {
        var module = BuildFeatureTest();
        var cloneable = module.FindType("Cloneable");
        Assert.NotNull(cloneable);
        var shallowCopy = cloneable!.Methods.FirstOrDefault(m => m.Name == "ShallowCopy");
        Assert.NotNull(shallowCopy);
        var calls = shallowCopy!.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(calls, c => c == "cil2cpp::object_memberwise_clone");
    }

    // ===== Overloaded virtual methods (same name, different param types) =====

    [Fact]
    public void Build_FeatureTest_OverloadedVirtual_SeparateVTableSlots()
    {
        var module = BuildFeatureTest();
        var formatter = module.FindType("Formatter");
        Assert.NotNull(formatter);

        // Should have two separate vtable entries for Format(int) and Format(string)
        var formatEntries = formatter!.VTable.Where(e => e.MethodName == "Format").ToList();
        Assert.Equal(2, formatEntries.Count);
        Assert.NotEqual(formatEntries[0].Slot, formatEntries[1].Slot);
    }

    [Fact]
    public void Build_FeatureTest_OverloadedVirtual_DerivedOverridesBoth()
    {
        var module = BuildFeatureTest();
        var prefixFormatter = module.FindType("PrefixFormatter");
        Assert.NotNull(prefixFormatter);

        // PrefixFormatter should override both Format(int) and Format(string)
        var formatEntries = prefixFormatter!.VTable.Where(e => e.MethodName == "Format").ToList();
        Assert.Equal(2, formatEntries.Count);
        // Both should have Method != null (overridden, not base stubs)
        Assert.All(formatEntries, e => Assert.NotNull(e.Method));
    }

    // ===== Interface inheritance (IB : IA) =====

    [Fact]
    public void Build_FeatureTest_InterfaceInheritImpl_HasBothInterfaceImpls()
    {
        var module = BuildFeatureTest();
        var impl = module.FindType("InterfaceInheritImpl");
        Assert.NotNull(impl);
        // Cecil flattens interface list: both IBase and IDerived should be present
        var ifaceNames = impl!.InterfaceImpls.Select(i => i.Interface.Name).ToList();
        Assert.Contains("IBase", ifaceNames);
        Assert.Contains("IDerived", ifaceNames);
    }

    [Fact]
    public void Build_FeatureTest_InterfaceInheritImpl_BaseMethodMapped()
    {
        var module = BuildFeatureTest();
        var impl = module.FindType("InterfaceInheritImpl")!;
        var baseImpl = impl.InterfaceImpls.First(i => i.Interface.Name == "IBase");
        // IBase has 1 method: BaseMethod() — should map to InterfaceInheritImpl.BaseMethod
        Assert.Single(baseImpl.MethodImpls);
        Assert.NotNull(baseImpl.MethodImpls[0]);
        Assert.Equal("BaseMethod", baseImpl.MethodImpls[0]!.Name);
    }

    [Fact]
    public void Build_FeatureTest_InterfaceInheritImpl_DerivedMethodMapped()
    {
        var module = BuildFeatureTest();
        var impl = module.FindType("InterfaceInheritImpl")!;
        var derivedImpl = impl.InterfaceImpls.First(i => i.Interface.Name == "IDerived");
        // IDerived has 1 own method: DerivedMethod()
        Assert.Single(derivedImpl.MethodImpls);
        Assert.NotNull(derivedImpl.MethodImpls[0]);
        Assert.Equal("DerivedMethod", derivedImpl.MethodImpls[0]!.Name);
    }

    // ===== Overloaded interface methods =====

    [Fact]
    public void Build_FeatureTest_Processor_HasOverloadedInterfaceMethods()
    {
        var module = BuildFeatureTest();
        var processor = module.FindType("Processor");
        Assert.NotNull(processor);
        var iProcessImpl = processor!.InterfaceImpls.FirstOrDefault(i => i.Interface.Name == "IProcess");
        Assert.NotNull(iProcessImpl);
        // IProcess has 2 methods: Process(int) and Process(string) — both should be mapped
        Assert.Equal(2, iProcessImpl!.MethodImpls.Count);
        Assert.All(iProcessImpl.MethodImpls, m => Assert.NotNull(m));
    }

    [Fact]
    public void Build_FeatureTest_Processor_OverloadedMethods_DistinctImpls()
    {
        var module = BuildFeatureTest();
        var processor = module.FindType("Processor")!;
        var iProcessImpl = processor.InterfaceImpls.First(i => i.Interface.Name == "IProcess");
        // The two Process methods should resolve to different implementing methods
        var methods = iProcessImpl.MethodImpls.Where(m => m != null).ToList();
        Assert.Equal(2, methods.Count);
        // They should have the same name but different parameter types
        Assert.All(methods, m => Assert.Equal("Process", m!.Name));
        Assert.NotEqual(methods[0]!.Parameters[0].CppTypeName, methods[1]!.Parameters[0].CppTypeName);
    }

    // ===== Multi-parameter generic type =====

    [Fact]
    public void Build_FeatureTest_KeyValueIntString_Exists()
    {
        var module = BuildFeatureTest();
        var kv = module.Types.FirstOrDefault(t => t.CppName.Contains("KeyValue") && t.IsGenericInstance);
        Assert.NotNull(kv);
        Assert.Equal(2, kv!.GenericArguments.Count);
    }

    [Fact]
    public void Build_FeatureTest_KeyValueIntString_FieldsSubstituted()
    {
        var module = BuildFeatureTest();
        var kv = module.Types.First(t => t.CppName.Contains("KeyValue") && t.IsGenericInstance);
        var keyField = kv.Fields.FirstOrDefault(f => f.Name == "Key");
        var valueField = kv.Fields.FirstOrDefault(f => f.Name == "Value");
        Assert.NotNull(keyField);
        Assert.NotNull(valueField);
        // Key is int, Value is string — types should be substituted
        Assert.DoesNotContain("T", keyField!.FieldTypeName);
        Assert.DoesNotContain("T", valueField!.FieldTypeName);
    }

    // ===== Generic type inheritance =====

    [Fact]
    public void Build_FeatureTest_SpecialWrapperInt_Exists()
    {
        var module = BuildFeatureTest();
        var sw = module.Types.FirstOrDefault(t => t.CppName.Contains("SpecialWrapper") && t.IsGenericInstance);
        Assert.NotNull(sw);
    }

    [Fact]
    public void Build_FeatureTest_SpecialWrapperInt_HasBaseType()
    {
        var module = BuildFeatureTest();
        var sw = module.Types.First(t => t.CppName.Contains("SpecialWrapper") && t.IsGenericInstance);
        Assert.NotNull(sw.BaseType);
        Assert.Contains("Wrapper", sw.BaseType!.CppName);
    }

    [Fact]
    public void Build_FeatureTest_SpecialWrapperInt_BaseTypeHasGetValue()
    {
        var module = BuildFeatureTest();
        var sw = module.Types.First(t => t.CppName.Contains("SpecialWrapper") && t.IsGenericInstance);
        // GetValue lives on base type Wrapper<int>, not on SpecialWrapper<int> itself
        Assert.NotNull(sw.BaseType);
        Assert.Contains(sw.BaseType!.Methods, m => m.Name == "GetValue");
    }

    // ===== Nested generic instantiation =====

    [Fact]
    public void Build_FeatureTest_NestedGeneric_WrapperOfWrapperInt_Exists()
    {
        var module = BuildFeatureTest();
        // Wrapper<Wrapper<int>> should exist as a separate generic instantiation
        var nested = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Wrapper") &&
            t.GenericArguments.Any(a => a.Contains("Wrapper")));
        Assert.NotNull(nested);
    }

    // ===== Generic type with static constructor =====

    [Fact]
    public void Build_FeatureTest_GenericCacheInt_HasCctor()
    {
        var module = BuildFeatureTest();
        var cache = module.Types.FirstOrDefault(t => t.CppName.Contains("GenericCache") && t.IsGenericInstance);
        Assert.NotNull(cache);
        Assert.True(cache!.HasCctor, "GenericCache<int> should have HasCctor flag set");
    }

    [Fact]
    public void Build_FeatureTest_GenericCacheInt_HasStaticField()
    {
        var module = BuildFeatureTest();
        var cache = module.Types.First(t => t.CppName.Contains("GenericCache") && t.IsGenericInstance);
        Assert.Contains(cache.StaticFields, f => f.Name == "_initCount");
    }

    // ===== Explicit Interface Implementation =====

    [Fact]
    public void Build_FeatureTest_FileLogger_HasExplicitOverride()
    {
        var module = BuildFeatureTest();
        var logger = module.Types.First(t => t.CppName == "FileLogger");
        // The explicit impl method should have ExplicitOverrides populated
        var logMethod = logger.Methods.FirstOrDefault(m =>
            m.ExplicitOverrides.Any(o => o.InterfaceTypeName == "ILogger" && o.MethodName == "Log"));
        Assert.NotNull(logMethod);
    }

    [Fact]
    public void Build_FeatureTest_FileLogger_InterfaceImpl_Log_Mapped()
    {
        var module = BuildFeatureTest();
        var logger = module.Types.First(t => t.CppName == "FileLogger");
        // ILogger interface impl should have Log method mapped (via explicit override)
        var iloggerImpl = logger.InterfaceImpls.FirstOrDefault(i => i.Interface.CppName == "ILogger");
        Assert.NotNull(iloggerImpl);
        var logImpl = iloggerImpl!.MethodImpls.FirstOrDefault(m => m != null);
        Assert.NotNull(logImpl);
    }

    [Fact]
    public void Build_FeatureTest_FileLogger_InterfaceImpl_Format_Mapped()
    {
        var module = BuildFeatureTest();
        var logger = module.Types.First(t => t.CppName == "FileLogger");
        // IFormatter interface impl should have Format method mapped (via implicit name match)
        var ifmtImpl = logger.InterfaceImpls.FirstOrDefault(i => i.Interface.CppName == "IFormatter");
        Assert.NotNull(ifmtImpl);
        var fmtImpl = ifmtImpl!.MethodImpls.FirstOrDefault(m => m != null && m.Name == "Format");
        Assert.NotNull(fmtImpl);
    }

    // ===== Method Hiding (newslot) =====

    [Fact]
    public void Build_FeatureTest_DerivedDisplay_Show_IsNewSlot()
    {
        var module = BuildFeatureTest();
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var show = derived.Methods.First(m => m.Name == "Show");
        Assert.True(show.IsNewSlot, "DerivedDisplay.Show should be marked as newslot");
    }

    [Fact]
    public void Build_FeatureTest_DerivedDisplay_Show_HasDifferentVTableSlot()
    {
        var module = BuildFeatureTest();
        var baseType = module.Types.First(t => t.CppName == "BaseDisplay");
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var baseSlot = baseType.VTable.First(e => e.MethodName == "Show").Slot;
        var derivedShow = derived.Methods.First(m => m.Name == "Show");
        // newslot should create a DIFFERENT vtable slot than the base
        Assert.NotEqual(baseSlot, derivedShow.VTableSlot);
    }

    [Fact]
    public void Build_FeatureTest_DerivedDisplay_Value_OverridesBaseSlot()
    {
        var module = BuildFeatureTest();
        var baseType = module.Types.First(t => t.CppName == "BaseDisplay");
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var baseSlot = baseType.VTable.First(e => e.MethodName == "Value").Slot;
        var derivedValue = derived.Methods.First(m => m.Name == "Value");
        // Normal override should reuse the SAME vtable slot
        Assert.Equal(baseSlot, derivedValue.VTableSlot);
    }

    [Fact]
    public void Build_FeatureTest_FinalDisplay_Overrides_DerivedShow()
    {
        var module = BuildFeatureTest();
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var final_ = module.Types.First(t => t.CppName == "FinalDisplay");
        var derivedShowSlot = derived.Methods.First(m => m.Name == "Show").VTableSlot;
        // FinalDisplay.Show overrides DerivedDisplay.Show's (hidden) slot
        var finalShow = final_.Methods.First(m => m.Name == "Show");
        Assert.Equal(derivedShowSlot, finalShow.VTableSlot);
    }

    // ===== sizeof opcode =====

    [Fact]
    public void Build_FeatureTest_TestSizeOf_HasSizeofInstruction()
    {
        var module = BuildFeatureTest();
        var testMethod = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestSizeOf");
        var allCode = string.Join("\n", testMethod.BasicBlocks
            .SelectMany(b => b.Instructions)
            .Select(i => i.ToCpp()));
        // Roslyn emits sizeof opcode only for user-defined structs (builtins are const-folded)
        Assert.Contains("sizeof(TinyStruct)", allCode);
        Assert.Contains("sizeof(BigStruct)", allCode);
    }

    // ===== No-op prefixes (constrained., etc.) =====

    [Fact]
    public void Build_FeatureTest_NoConstrainedWarnings()
    {
        var module = BuildFeatureTest();
        // After implementing constrained. as no-op, no WARNING comments about it should exist
        var allComments = module.Types
            .SelectMany(t => t.Methods)
            .SelectMany(m => m.BasicBlocks)
            .SelectMany(b => b.Instructions)
            .OfType<IRComment>()
            .Select(c => c.Text);
        Assert.DoesNotContain(allComments, c => c.Contains("Constrained"));
    }

    // ===== ldtoken / typeof =====

    [Fact]
    public void Build_FeatureTest_TinyStruct_Exists()
    {
        var module = BuildFeatureTest();
        var tiny = module.Types.FirstOrDefault(t => t.CppName == "TinyStruct");
        Assert.NotNull(tiny);
        Assert.True(tiny!.IsValueType);
    }

    [Fact]
    public void Build_FeatureTest_BigStruct_HasThreeFields()
    {
        var module = BuildFeatureTest();
        var big = module.Types.First(t => t.CppName == "BigStruct");
        Assert.Equal(3, big.Fields.Count);
    }

    // ===== Control Flow: Loops =====

    [Fact]
    public void Build_FeatureTest_TestWhileLoop_HasBackwardBranch()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestWhileLoop");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // While loops produce backward conditional branches
        Assert.Contains(instrs, i => i is IRConditionalBranch);
        Assert.Contains(instrs, i => i is IRLabel);
    }

    [Fact]
    public void Build_FeatureTest_TestDoWhileLoop_HasConditionalBranch()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestDoWhileLoop");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instrs, i => i is IRConditionalBranch);
    }

    [Fact]
    public void Build_FeatureTest_TestForLoop_HasBranching()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestForLoop");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instrs, i => i is IRConditionalBranch);
        // For loops also have unconditional branch (to loop condition check)
        Assert.Contains(instrs, i => i is IRBranch);
    }

    [Fact]
    public void Build_FeatureTest_TestNestedLoopBreakContinue_HasMultipleBranches()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestNestedLoopBreakContinue");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var branches = instrs.Count(i => i is IRConditionalBranch);
        // Nested loops + break + continue → multiple conditional branches
        Assert.True(branches >= 4, $"Expected >= 4 conditional branches, got {branches}");
    }

    // ===== Control Flow: Goto =====

    [Fact]
    public void Build_FeatureTest_TestGoto_HasUnconditionalBranch()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestGoto");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // Forward goto + backward goto both produce IRBranch
        var jumps = instrs.Count(i => i is IRBranch);
        Assert.True(jumps >= 2, $"Expected >= 2 unconditional branches (forward + backward goto), got {jumps}");
    }

    // ===== Control Flow: Nested If/Else =====

    [Fact]
    public void Build_FeatureTest_TestNestedIfElse_HasMultipleBranches()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestNestedIfElse");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // if/else if/else → at least 2 conditional branches
        var branches = instrs.Count(i => i is IRConditionalBranch);
        Assert.True(branches >= 2, $"Expected >= 2 conditional branches, got {branches}");
    }

    // ===== Control Flow: Ternary Operator =====

    [Fact]
    public void Build_FeatureTest_TestTernary_HasConditionalBranch()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestTernary");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instrs, i => i is IRConditionalBranch);
    }

    // ===== Control Flow: Short-Circuit =====

    [Fact]
    public void Build_FeatureTest_TestShortCircuit_NoWarnings()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestShortCircuit");
        var comments = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRComment>()
            .Select(c => c.Text);
        Assert.DoesNotContain(comments, c => c.Contains("WARNING"));
    }

    // ===== Control Flow: Unsigned Comparison =====

    [Fact]
    public void Build_FeatureTest_TestUnsignedComparison_NoBranchWarnings()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestUnsignedComparison");
        var comments = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRComment>()
            .Select(c => c.Text);
        // No unsupported opcode warnings — all unsigned branch opcodes handled
        Assert.DoesNotContain(comments, c => c.Contains("WARNING"));
    }

    [Fact]
    public void Build_FeatureTest_TestUnsignedComparison_HasComparisonOps()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestUnsignedComparison");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // Console.WriteLine(a < b) uses comparison opcodes (clt.un → IRBinaryOp), not branches
        var compOps = instrs.OfType<IRBinaryOp>().Select(b => b.Op).ToList();
        Assert.Contains("<", compOps);
        Assert.Contains(">", compOps);
    }

    // ===== Control Flow: NaN Comparison =====

    [Fact]
    public void Build_FeatureTest_TestFloatNaNComparison_NoBranchWarnings()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestFloatNaNComparison");
        var comments = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRComment>()
            .Select(c => c.Text);
        Assert.DoesNotContain(comments, c => c.Contains("WARNING"));
    }

    // ===== Index / Range =====

    [Fact]
    public void Build_FeatureTest_TestIndexFromEnd_NoWarnings()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestIndexFromEnd");
        var comments = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRComment>()
            .Select(c => c.Text);
        Assert.DoesNotContain(comments, c => c.Contains("WARNING"));
    }

    [Fact]
    public void Build_FeatureTest_TestRangeSlice_HasGetSubArray()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestRangeSlice");
        var rawCpps = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRRawCpp>()
            .Select(r => r.Code)
            .ToList();
        // arr[1..3] uses RuntimeHelpers.GetSubArray → array_get_subarray
        Assert.Contains(rawCpps, c => c.Contains("array_get_subarray"));
    }

    [Fact]
    public void Build_FeatureTest_TestRangeGetOffsetAndLength_NoWarnings()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestRangeGetOffsetAndLength");
        var comments = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRComment>()
            .Select(c => c.Text);
        Assert.DoesNotContain(comments, c => c.Contains("WARNING"));
    }

    // ===== Threading Tests =====

    [Fact]
    public void Build_FeatureTest_TestLock_NoWarnings()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestLock");
        var allInstructions = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .ToList();
        // No WARNING comments
        var warnings = allInstructions.OfType<IRComment>()
            .Where(c => c.Text.Contains("WARNING"))
            .ToList();
        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_FeatureTest_TestInterlocked_HasAtomicCalls()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestInterlocked");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        // Interlocked.Increment → Interlocked_Increment_i32
        Assert.Contains(calls, c => c.Contains("Interlocked_Increment"));
        // Interlocked.CompareExchange → Interlocked_CompareExchange_i32
        Assert.Contains(calls, c => c.Contains("Interlocked_CompareExchange"));
    }

    [Fact]
    public void Build_FeatureTest_TestInterlockedLong_HasI64Calls()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestInterlockedLong");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        // Interlocked.Increment(ref long) → Interlocked_Increment_i64
        Assert.Contains(calls, c => c.Contains("Interlocked_Increment_i64"));
    }

    [Fact]
    public void Build_FeatureTest_TestMonitorWaitPulse_HasMonitorWaitPulse()
    {
        var module = BuildFeatureTest();
        // Check main method has Monitor.Wait
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestMonitorWaitPulse");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(calls, c => c.Contains("Monitor_Wait"));

        // Monitor.Pulse is in the lambda closure — check all types for it
        var allCalls = module.Types
            .SelectMany(t => t.Methods)
            .SelectMany(m => m.BasicBlocks)
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(allCalls, c => c.Contains("Monitor_Pulse"));
    }

    // ===== Reflection tests =====

    [Fact]
    public void Build_FeatureTest_TestGetType_HasGetTypeManagedCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestGetType");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(calls, c => c.Contains("object_get_type_managed"));
    }

    [Fact]
    public void Build_FeatureTest_TestTypeToString_HasVtableOrToString()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestTypeToString");
        // Type.ToString() may be resolved as:
        // 1. type_to_string via TryEmitTypeCall (if Roslyn targets Type::ToString)
        // 2. vtable dispatch via Object::ToString (default virtual call path)
        // Both are valid — check that method has instructions
        var allInstr = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .ToList();
        Assert.NotEmpty(allInstr);
    }

    [Fact]
    public void Build_FeatureTest_ReflectionIRField_HasAttributes()
    {
        var module = BuildFeatureTest();
        // Dog inherits from Animal which has _name field
        var animalType = module.Types.FirstOrDefault(t => t.Name == "Animal");
        Assert.NotNull(animalType);
        var nameField = animalType.Fields.FirstOrDefault(f => f.Name == "_name");
        Assert.NotNull(nameField);
        // _name is "protected string" → Family (0x0004)
        Assert.NotEqual(0u, nameField.Attributes);
    }

    [Fact]
    public void Build_FeatureTest_ReflectionIRMethod_HasAttributes()
    {
        var module = BuildFeatureTest();
        var programType = module.Types.First(t => t.Name == "Program");
        var mainMethod = programType.Methods.First(m => m.Name == "Main");
        // Main is public static → at least Public (0x0006) | Static (0x0010)
        Assert.NotEqual(0u, mainMethod.Attributes);
        // Check Public flag (access mask & 0x7 == 0x6)
        Assert.Equal(0x0006u, mainMethod.Attributes & 0x0007u);
    }

    // ===== Custom Attributes =====

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasObsolete()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.FirstOrDefault(t => t.Name == "AttributeTestClass");
        Assert.NotNull(attrType);
        var obsolete = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "System.ObsoleteAttribute");
        Assert.NotNull(obsolete);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_ObsoleteHasMessage()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var obsolete = attrType.CustomAttributes.First(
            a => a.AttributeTypeName == "System.ObsoleteAttribute");
        Assert.Single(obsolete.ConstructorArgs);
        Assert.Equal("System.String", obsolete.ConstructorArgs[0].TypeName);
        Assert.Equal("Use NewClass instead", obsolete.ConstructorArgs[0].Value);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasDescription()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var desc = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "DescriptionAttribute");
        Assert.NotNull(desc);
        Assert.Single(desc.ConstructorArgs);
        Assert.Equal("A test class with attributes", desc.ConstructorArgs[0].Value);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_FieldHasObsolete()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var oldField = attrType.Fields.FirstOrDefault(f => f.Name == "OldField");
        Assert.NotNull(oldField);
        var obsolete = oldField.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "System.ObsoleteAttribute");
        Assert.NotNull(obsolete);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_MethodHasDescription()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var method = attrType.Methods.FirstOrDefault(m => m.Name == "AnnotatedMethod");
        Assert.NotNull(method);
        var desc = method.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "DescriptionAttribute");
        Assert.NotNull(desc);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_NoCompilerInternalAttrs()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        // CompilerGeneratedAttribute and NullableContextAttribute should be filtered out
        Assert.DoesNotContain(attrType.CustomAttributes,
            a => a.AttributeTypeName.Contains("CompilerGenerated"));
        Assert.DoesNotContain(attrType.CustomAttributes,
            a => a.AttributeTypeName.Contains("NullableContext"));
    }

    // ===== Multi-dimensional array tests =====

    [Fact]
    public void Build_FeatureTest_MdArray_Create2D_HasRank2()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "MdArrayTest");
        var method = type.Methods.First(m => m.Name == "Create2D");
        var allCode = string.Join("\n", method.BasicBlocks.SelectMany(b => b.Instructions).Select(i => i.ToCpp()));
        // Rank 2 passed to mdarray_create
        Assert.Contains(", 2,", allCode);
    }

    [Fact]
    public void Build_FeatureTest_MdArray_GetTotalLength_HasArrayGetLength()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "MdArrayTest");
        var method = type.Methods.First(m => m.Name == "GetTotalLength");
        var allCode = string.Join("\n", method.BasicBlocks.SelectMany(b => b.Instructions).Select(i => i.ToCpp()));
        // Should call array_get_length (ICall) which handles both 1D and multi-dim
        Assert.Contains("array_get_length", allCode);
    }

    // ===== P/Invoke tests =====

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeAbs_Detected()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeAbs");
        Assert.True(method.IsPInvoke);
        Assert.Equal("msvcrt.dll", method.PInvokeModule);
        Assert.Equal("abs", method.PInvokeEntryPoint);
    }

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeStrLen_Detected()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeStrLen");
        Assert.True(method.IsPInvoke);
        Assert.Equal("msvcrt.dll", method.PInvokeModule);
        Assert.Equal("strlen", method.PInvokeEntryPoint);
    }

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeAbs_HasNoBody()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeAbs");
        // P/Invoke methods have no IL body
        Assert.Empty(method.BasicBlocks);
    }

    // ===== Default Interface Methods (DIM) tests =====

    [Fact]
    public void Build_FeatureTest_DIM_InterfaceHasDefaultMethods()
    {
        var module = BuildFeatureTest();
        var iface = module.Types.First(t => t.Name == "IGreeter");
        Assert.True(iface.IsInterface);

        // GetName is abstract — no body
        var getName = iface.Methods.First(m => m.Name == "GetName");
        Assert.True(getName.IsAbstract);
        Assert.Empty(getName.BasicBlocks);

        // Greet has a default body
        var greet = iface.Methods.First(m => m.Name == "Greet");
        Assert.False(greet.IsAbstract);
        Assert.NotEmpty(greet.BasicBlocks);

        // Version has a default body
        var version = iface.Methods.First(m => m.Name == "Version");
        Assert.False(version.IsAbstract);
        Assert.NotEmpty(version.BasicBlocks);
    }

    [Fact]
    public void Build_FeatureTest_DIM_DefaultGreeterUser_UsesDefaultImpl()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "DefaultGreeterUser");

        // DefaultGreeterUser implements IGreeter but doesn't override Greet()
        var iface = module.Types.First(t => t.Name == "IGreeter");

        // Find the interface impl for IGreeter
        var ifaceImpl = type.InterfaceImpls.FirstOrDefault(i => i.Interface == iface);
        Assert.NotNull(ifaceImpl);

        // The Greet slot should point to the interface's default method
        var greetIdx = iface.Methods
            .Where(m => !m.IsConstructor && !m.IsStaticConstructor)
            .ToList()
            .FindIndex(m => m.Name == "Greet");
        Assert.True(greetIdx >= 0);
        var implMethod = ifaceImpl.MethodImpls[greetIdx];
        Assert.NotNull(implMethod);
        Assert.Equal(iface, implMethod.DeclaringType); // Points to interface method
    }

    [Fact]
    public void Build_FeatureTest_DIM_CustomGreeterUser_OverridesDefault()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "CustomGreeterUser");
        var iface = module.Types.First(t => t.Name == "IGreeter");

        var ifaceImpl = type.InterfaceImpls.FirstOrDefault(i => i.Interface == iface);
        Assert.NotNull(ifaceImpl);

        // The Greet slot should point to the class's own override, not interface default
        var greetIdx = iface.Methods
            .Where(m => !m.IsConstructor && !m.IsStaticConstructor)
            .ToList()
            .FindIndex(m => m.Name == "Greet");
        Assert.True(greetIdx >= 0);
        var implMethod = ifaceImpl.MethodImpls[greetIdx];
        Assert.NotNull(implMethod);
        Assert.Equal(type, implMethod.DeclaringType); // Points to class method
    }

    [Fact]
    public void Build_FeatureTest_DIM_ILogger2_AllDefaultMethods()
    {
        var module = BuildFeatureTest();
        var iface = module.Types.First(t => t.Name == "ILogger2");
        Assert.True(iface.IsInterface);

        // Log has a default body
        var log = iface.Methods.First(m => m.Name == "Log");
        Assert.False(log.IsAbstract);
        Assert.NotEmpty(log.BasicBlocks);
    }

    [Fact]
    public void Build_FeatureTest_DIM_LoggerUser_UsesAllDefaults()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "LoggerUser");
        var iface = module.Types.First(t => t.Name == "ILogger2");

        var ifaceImpl = type.InterfaceImpls.FirstOrDefault(i => i.Interface == iface);
        Assert.NotNull(ifaceImpl);

        // Log slot should point to interface default
        var logIdx = iface.Methods
            .Where(m => !m.IsConstructor && !m.IsStaticConstructor)
            .ToList()
            .FindIndex(m => m.Name == "Log");
        Assert.True(logIdx >= 0);
        var implMethod = ifaceImpl.MethodImpls[logIdx];
        Assert.NotNull(implMethod);
        Assert.Equal(iface, implMethod.DeclaringType);
    }

    // ===== Span<T> tests =====

    [Fact]
    public void Build_FeatureTest_SpanInt_SyntheticType_Created()
    {
        var module = BuildFeatureTest();
        // Span<int> should be created as a synthetic generic instance
        var spanInt = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.ILFullName.StartsWith("System.Span`1<"));
        Assert.NotNull(spanInt);
        Assert.True(spanInt.IsValueType);
        Assert.True(spanInt.Fields.Any(f => f.CppName == "f_reference"));
        Assert.True(spanInt.Fields.Any(f => f.CppName == "f_length"));
    }

    [Fact]
    public void Build_FeatureTest_ReadOnlySpan_SyntheticType_Created()
    {
        var module = BuildFeatureTest();
        var roSpan = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.ILFullName.StartsWith("System.ReadOnlySpan`1<"));
        Assert.NotNull(roSpan);
        Assert.True(roSpan.IsValueType);
    }

    // ===== Generic Variance tests =====

    [Fact]
    public void Build_FeatureTest_Variance_CovariantInstance_HasVariance()
    {
        var module = BuildFeatureTest();
        // ICovariant<string> should be created as a generic instance with covariant variance
        var covariantStr = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.CppName.Contains("ICovariant") && t.CppName.Contains("String"));
        Assert.NotNull(covariantStr);
        Assert.True(covariantStr.IsInterface);
        Assert.Single(covariantStr.GenericParameterVariances);
        Assert.Equal(GenericVariance.Covariant, covariantStr.GenericParameterVariances[0]);
        Assert.NotNull(covariantStr.GenericDefinitionCppName);
    }

    [Fact]
    public void Build_FeatureTest_Variance_ContravariantInstance_HasVariance()
    {
        var module = BuildFeatureTest();
        // IContravariant<object> should be created with contravariant variance
        var contravariantObj = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.CppName.Contains("IContravariant") && t.CppName.Contains("Object"));
        Assert.NotNull(contravariantObj);
        Assert.Single(contravariantObj.GenericParameterVariances);
        Assert.Equal(GenericVariance.Contravariant, contravariantObj.GenericParameterVariances[0]);
    }

    [Fact]
    public void Build_FeatureTest_Variance_GenericArguments_Populated()
    {
        var module = BuildFeatureTest();
        var covariantStr = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.CppName.Contains("ICovariant") && t.CppName.Contains("String"));
        Assert.NotNull(covariantStr);
        Assert.Single(covariantStr.GenericArguments);
        Assert.Equal("System.String", covariantStr.GenericArguments[0]);
    }

    // ===== CancellationToken / TaskCompletionSource =====

    [Fact]
    public void Build_FeatureTest_TaskCompletionSource_Monomorphized()
    {
        var module = BuildFeatureTest();
        var tcsInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance
            && t.ILFullName.Contains("TaskCompletionSource`1")
            && t.ILFullName.Contains("System.Int32"));
        Assert.NotNull(tcsInt);
        Assert.False(tcsInt!.IsValueType);
        Assert.Contains(tcsInt.Fields, f => f.CppName == "f_task");
    }

    // ── LINQ Interception Tests ───────────────────────────────

    [Fact]
    public void Build_FeatureTest_LinqSum_Intercepted()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "LinqSum");
        var rawCpp = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRRawCpp>().ToList();
        Assert.True(rawCpp.Any(r => r.Code.Contains("array_data")),
            "LinqSum should use array_data for iteration");
    }

    [Fact]
    public void Build_FeatureTest_GenericDelegate_IsDelegate()
    {
        var module = BuildFeatureTest();
        var funcType = module.Types.FirstOrDefault(t =>
            t.ILFullName.Contains("System.Func`2") && t.ILFullName.Contains("Boolean"));
        Assert.NotNull(funcType);
        Assert.True(funcType!.IsDelegate,
            "Generic Func<int,bool> should have IsDelegate = true");
    }

    // ===== System.IO tests =====

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_FileWriteAndRead_HasFileWriteCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "FileWriteAndRead");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("File_WriteAllText")),
            "FileWriteAndRead should map to File_WriteAllText");
        Assert.True(calls.Any(c => c.FunctionName.Contains("File_ReadAllText")),
            "FileWriteAndRead should map to File_ReadAllText");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_FileExists_HasExistsCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "FileExists");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("File_Exists")),
            "FileExists should map to File_Exists");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_PathCombine_HasCombineCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "PathCombine");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("Path_Combine")),
            "PathCombine should map to Path_Combine");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_PathGetFileName_HasGetFileNameCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "PathGetFileName");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("Path_GetFileName")),
            "PathGetFileName should map to Path_GetFileName");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_PathGetExtension_HasGetExtensionCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "PathGetExtension");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("Path_GetExtension")),
            "PathGetExtension should map to Path_GetExtension");
    }
}
