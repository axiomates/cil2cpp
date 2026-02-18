using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("IRBuilderCore")]
public class IRBuilderCoreTests
{
    private readonly HelloWorldFixture _fixture;

    public IRBuilderCoreTests(HelloWorldFixture fixture)
    {
        _fixture = fixture;
    }

    private IRModule BuildHelloWorld(BuildConfiguration? config = null)
    {
        if (config == null || !config.ReadDebugSymbols)
            return _fixture.GetReleaseModule();
        return _fixture.GetDebugModule();
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

    // ===== Build path with explicit AssemblySet + Reachability =====

    [Fact]
    public void Build_MultiAssembly_HelloWorld_ProducesModule()
    {
        var module = _fixture.GetReleaseModule();

        Assert.NotNull(module);
        Assert.Equal("HelloWorld", module.Name);
        Assert.NotEmpty(module.Types);
    }

    [Fact]
    public void Build_MultiAssembly_HelloWorld_SetsSourceKind()
    {
        var module = _fixture.GetReleaseModule();

        var programType = module.FindType("Program");
        Assert.NotNull(programType);
        Assert.Equal(AssemblyKind.User, programType!.SourceKind);
    }

    [Fact]
    public void Build_MultiAssembly_HelloWorld_MarksRuntimeProvided()
    {
        var module = _fixture.GetReleaseModule();

        // User types should NOT be runtime-provided
        var programType = module.FindType("Program");
        Assert.NotNull(programType);
        Assert.False(programType!.IsRuntimeProvided);
    }

    [Fact]
    public void Build_MultiAssembly_HasEntryPoint()
    {
        var module = _fixture.GetReleaseModule();

        Assert.NotNull(module.EntryPoint);
    }
}
