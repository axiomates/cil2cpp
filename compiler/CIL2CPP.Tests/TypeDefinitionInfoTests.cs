using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("SampleAssembly")]
public class TypeDefinitionInfoTests
{
    private readonly SampleAssemblyFixture _fixture;

    public TypeDefinitionInfoTests(SampleAssemblyFixture fixture)
    {
        _fixture = fixture;
    }

    // ===== TypeDefinitionInfo properties for Calculator =====

    [Fact]
    public void Name_Calculator_ReturnsCalculator()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.Equal("Calculator", calc.Name);
    }

    [Fact]
    public void Namespace_Calculator_ReturnsEmpty()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.Equal("", calc.Namespace);
    }

    [Fact]
    public void FullName_Calculator_ReturnsCalculator()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.Equal("Calculator", calc.FullName);
    }

    [Fact]
    public void BaseTypeName_Calculator_ReturnsSystemObject()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.Equal("System.Object", calc.BaseTypeName);
    }

    [Fact]
    public void IsClass_Calculator_True()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.True(calc.IsClass);
    }

    [Fact]
    public void IsInterface_Calculator_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.False(calc.IsInterface);
    }

    [Fact]
    public void IsValueType_Calculator_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.False(calc.IsValueType);
    }

    [Fact]
    public void IsEnum_Calculator_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.False(calc.IsEnum);
    }

    [Fact]
    public void IsAbstract_Calculator_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.False(calc.IsAbstract);
    }

    [Fact]
    public void IsSealed_Calculator_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.False(calc.IsSealed);
    }

    [Fact]
    public void IsPublic_Calculator_True()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.True(calc.IsPublic);
    }

    [Fact]
    public void IsNested_Calculator_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.False(calc.IsNested);
    }

    [Fact]
    public void HasGenericParameters_Calculator_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.False(calc.HasGenericParameters);
    }

    [Fact]
    public void InterfaceNames_Calculator_Empty()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.Empty(calc.InterfaceNames);
    }

    // ===== Fields =====

    [Fact]
    public void Fields_Calculator_HasResultField()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var resultField = calc.Fields.FirstOrDefault(f => f.Name == "_result");
        Assert.NotNull(resultField);
        Assert.Equal("System.Int32", resultField!.TypeName);
    }

    [Fact]
    public void FieldInfo_Result_IsPrivate()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var resultField = calc.Fields.First(f => f.Name == "_result");
        Assert.True(resultField.IsPrivate);
    }

    [Fact]
    public void FieldInfo_Result_IsNotStatic()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var resultField = calc.Fields.First(f => f.Name == "_result");
        Assert.False(resultField.IsStatic);
    }

    // ===== Methods =====

    [Fact]
    public void Methods_Calculator_HasAddMethod()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var addMethod = calc.Methods.FirstOrDefault(m => m.Name == "Add");
        Assert.NotNull(addMethod);
        Assert.Equal("System.Int32", addMethod!.ReturnTypeName);
        Assert.Equal(2, addMethod.Parameters.Count);
    }

    [Fact]
    public void Methods_Calculator_HasConstructor()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var ctor = calc.Methods.FirstOrDefault(m => m.IsConstructor);
        Assert.NotNull(ctor);
    }

    [Fact]
    public void Methods_Calculator_HasSetResult()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var method = calc.Methods.FirstOrDefault(m => m.Name == "SetResult");
        Assert.NotNull(method);
        Assert.Equal("System.Void", method!.ReturnTypeName);
        Assert.Single(method.Parameters);
    }

    [Fact]
    public void Methods_Calculator_HasGetResult()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var method = calc.Methods.FirstOrDefault(m => m.Name == "GetResult");
        Assert.NotNull(method);
        Assert.Equal("System.Int32", method!.ReturnTypeName);
        Assert.Empty(method.Parameters);
    }

    // ===== Program type =====

    [Fact]
    public void Methods_Program_HasMain()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.FirstOrDefault(m => m.Name == "Main");
        Assert.NotNull(main);
        Assert.True(main!.IsStatic);
    }

    [Fact]
    public void Methods_Program_Main_HasBody()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        Assert.True(main.HasBody);
    }

    // ===== MethodInfo details =====

    [Fact]
    public void GetInstructions_AddMethod_ReturnsInstructions()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        var instructions = add.GetInstructions().ToList();
        Assert.True(instructions.Count > 0, "Add method should have IL instructions");
    }

    [Fact]
    public void Parameters_AddMethod_HasTwoParams()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.Equal(2, add.Parameters.Count);
        Assert.Equal("a", add.Parameters[0].Name);
        Assert.Equal("System.Int32", add.Parameters[0].TypeName);
        Assert.Equal("b", add.Parameters[1].Name);
        Assert.Equal("System.Int32", add.Parameters[1].TypeName);
    }

    [Fact]
    public void Parameters_AddMethod_IndicesCorrect()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.Equal(0, add.Parameters[0].Index);
        Assert.Equal(1, add.Parameters[1].Index);
    }

    [Fact]
    public void ReturnTypeName_AddMethod_ReturnsSystemInt32()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.Equal("System.Int32", add.ReturnTypeName);
    }

    [Fact]
    public void MethodInfo_Add_IsNotStatic()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.False(add.IsStatic);
    }

    [Fact]
    public void MethodInfo_Add_IsNotVirtual()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.False(add.IsVirtual);
    }

    [Fact]
    public void MethodInfo_Add_IsNotAbstract()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.False(add.IsAbstract);
    }

    [Fact]
    public void MethodInfo_Add_IsNotConstructor()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.False(add.IsConstructor);
    }

    [Fact]
    public void HasGenericParameters_AddMethod_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.False(add.HasGenericParameters);
    }

    // ===== Sequence Points =====

    [Fact]
    public void GetSequencePoints_DebugWithSymbols_ReturnsPoints()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Debug);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var seqPoints = main.GetSequencePoints();
        Assert.True(seqPoints.Count > 0, "Debug build with PDB should have sequence points");
    }

    [Fact]
    public void GetSequencePoints_NoDebugSymbols_ReturnsEmpty()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath, BuildConfiguration.Release);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var seqPoints = main.GetSequencePoints();
        Assert.Empty(seqPoints);
    }

    // ===== GetLocalVariables =====

    [Fact]
    public void GetLocalVariables_MainMethod_HasLocals()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var locals = main.GetLocalVariables().ToList();
        Assert.True(locals.Count > 0, "Main method should have local variables");
    }

    [Fact]
    public void GetLocalVariables_AddMethod_DoesNotThrow()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        var locals = add.GetLocalVariables().ToList();
        Assert.NotNull(locals);
    }

    // ===== Exception Handlers =====

    [Fact]
    public void HasExceptionHandlers_MainMethod_False()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        Assert.False(main.HasExceptionHandlers);
    }

    [Fact]
    public void GetExceptionHandlers_MainMethod_ReturnsEmpty()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        Assert.Empty(main.GetExceptionHandlers());
    }

    // ===== Exception handlers from FeatureTest =====

    [Fact]
    public void HasExceptionHandlers_FeatureTest_TestExceptionHandling_True()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var method = prog.Methods.First(m => m.Name == "TestExceptionHandling");
        Assert.True(method.HasExceptionHandlers);
    }

    [Fact]
    public void GetExceptionHandlers_FeatureTest_ReturnsHandlers()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var method = prog.Methods.First(m => m.Name == "TestExceptionHandling");
        var handlers = method.GetExceptionHandlers();
        Assert.True(handlers.Count > 0, "TestExceptionHandling should have exception handlers");
    }

    [Fact]
    public void ExceptionHandler_Catch_HasCatchType()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var method = prog.Methods.First(m => m.Name == "TestExceptionHandling");
        var handlers = method.GetExceptionHandlers();
        var catchHandler = handlers.FirstOrDefault(h =>
            h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Catch);
        Assert.NotNull(catchHandler);
        Assert.NotNull(catchHandler!.CatchTypeName);
        Assert.Contains("Exception", catchHandler.CatchTypeName!);
    }

    [Fact]
    public void ExceptionHandler_Catch_HasValidOffsets()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var method = prog.Methods.First(m => m.Name == "TestExceptionHandling");
        var handlers = method.GetExceptionHandlers();
        var catchHandler = handlers.First(h =>
            h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Catch);
        Assert.True(catchHandler.TryStart >= 0);
        Assert.True(catchHandler.TryEnd > catchHandler.TryStart);
        Assert.True(catchHandler.HandlerStart >= 0);
        Assert.True(catchHandler.HandlerEnd > catchHandler.HandlerStart);
    }

    [Fact]
    public void ExceptionHandler_Finally_Exists()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var method = prog.Methods.First(m => m.Name == "TestExceptionHandling");
        var handlers = method.GetExceptionHandlers();
        var finallyHandler = handlers.FirstOrDefault(h =>
            h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally);
        Assert.NotNull(finallyHandler);
        Assert.Null(finallyHandler!.CatchTypeName); // finally has no catch type
    }

    // ===== FeatureTest: Virtual method properties =====

    [Fact]
    public void MethodInfo_VirtualMethod_IsVirtual()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var animal = reader.GetAllTypes().First(t => t.Name == "Animal");
        var speak = animal.Methods.First(m => m.Name == "Speak");
        Assert.True(speak.IsVirtual);
    }

    // ===== FeatureTest: Enum type properties =====

    [Fact]
    public void TypeInfo_Color_IsEnum()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var color = reader.GetAllTypes().First(t => t.Name == "Color");
        Assert.True(color.IsEnum);
        Assert.True(color.IsValueType);
        Assert.True(color.IsSealed);
    }

    // ===== FeatureTest: Struct properties =====

    [Fact]
    public void TypeInfo_Point_IsValueType()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var point = reader.GetAllTypes().First(t => t.Name == "Point");
        Assert.True(point.IsValueType);
        Assert.False(point.IsEnum);
        // In Cecil, IsClass is true for structs too (CLR metadata quirk)
        Assert.Equal("System.ValueType", point.BaseTypeName);
    }

    // ===== ILInstruction properties (via FeatureTest) =====

    [Fact]
    public void ILInstruction_HasValidOffset()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var instrs = main.GetInstructions().ToList();
        Assert.True(instrs.Count > 0);
        Assert.Equal(0, instrs[0].Offset);
    }

    [Fact]
    public void ILInstruction_OpCodeName_IsNotEmpty()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var instrs = main.GetInstructions().ToList();
        Assert.All(instrs, i => Assert.False(string.IsNullOrEmpty(i.OpCodeName)));
    }

    [Fact]
    public void ILInstruction_ToString_ContainsOffset()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var instr = main.GetInstructions().First();
        var str = instr.ToString();
        Assert.Contains("IL_0000", str);
    }

    [Fact]
    public void ILInstruction_FlowControl_IsValid()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var instrs = main.GetInstructions().ToList();
        // All instructions should have a defined FlowControl
        Assert.All(instrs, i => Assert.True(Enum.IsDefined(typeof(Mono.Cecil.Cil.FlowControl), i.FlowControl)));
    }

    [Fact]
    public void ILInstruction_OperandString_ForCallInstruction()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var callInstr = main.GetInstructions().FirstOrDefault(i =>
            i.OpCode == Mono.Cecil.Cil.Code.Call);
        Assert.NotNull(callInstr);
        // Call instructions have method reference as operand string
        Assert.False(string.IsNullOrEmpty(callInstr!.OperandString));
    }

    [Fact]
    public void ILInstruction_OperandString_BranchTarget()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var branching = prog.Methods.First(m => m.Name == "TestBranching");
        var brInstr = branching.GetInstructions().FirstOrDefault(i =>
            CIL2CPP.Core.IL.ILInstructionCategory.IsBranch(i.OpCode));
        Assert.NotNull(brInstr);
        Assert.Contains("IL_", brInstr!.OperandString);
    }

    [Fact]
    public void ILInstruction_OperandString_StringLiteral()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var method = prog.Methods.First(m => m.Name == "TestBranching");
        var ldstr = method.GetInstructions().FirstOrDefault(i =>
            i.OpCode == Mono.Cecil.Cil.Code.Ldstr);
        Assert.NotNull(ldstr);
        Assert.Contains("\"", ldstr!.OperandString);
    }

    [Fact]
    public void ILInstruction_StackBehavior_IsValid()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        var instrs = main.GetInstructions().ToList();
        Assert.All(instrs, i =>
        {
            Assert.True(Enum.IsDefined(typeof(Mono.Cecil.Cil.StackBehaviour), i.PopBehavior));
            Assert.True(Enum.IsDefined(typeof(Mono.Cecil.Cil.StackBehaviour), i.PushBehavior));
        });
    }

    // ===== FieldInfo additional properties =====

    [Fact]
    public void FieldInfo_PublicField_IsPublic()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var point = reader.GetAllTypes().First(t => t.Name == "Point");
        var xField = point.Fields.First(f => f.Name == "X");
        Assert.True(xField.IsPublic);
        Assert.False(xField.IsPrivate);
        Assert.False(xField.IsStatic);
    }

    [Fact]
    public void FieldInfo_StaticField_IsStatic()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var animal = reader.GetAllTypes().First(t => t.Name == "Animal");
        var countField = animal.Fields.FirstOrDefault(f => f.Name == "_count");
        Assert.NotNull(countField);
        Assert.True(countField!.IsStatic);
    }

    // ===== FieldInfo readonly (IsInitOnly) =====

    [Fact]
    public void FieldInfo_ReadonlyField_IsInitOnly()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var config = reader.GetAllTypes().First(t => t.Name == "Config");
        var maxRetries = config.Fields.First(f => f.Name == "MaxRetries");
        Assert.True(maxRetries.IsInitOnly);
    }

    [Fact]
    public void FieldInfo_MutableField_IsNotInitOnly()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var point = reader.GetAllTypes().First(t => t.Name == "Point");
        var xField = point.Fields.First(f => f.Name == "X");
        Assert.False(xField.IsInitOnly);
    }

    // ===== GenericParameterNames =====

    [Fact]
    public void GenericParameterNames_Calculator_Empty()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        Assert.Empty(calc.GenericParameterNames);
    }

    [Fact]
    public void GenericParameterNames_AddMethod_Empty()
    {
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var calc = reader.GetAllTypes().First(t => t.Name == "Calculator");
        var add = calc.Methods.First(m => m.Name == "Add");
        Assert.Empty(add.GenericParameterNames);
    }

    // ===== MethodInfo: IsPrivate / IsPublic =====

    [Fact]
    public void MethodInfo_PrivateMethod_IsPrivate()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var testArithmetic = prog.Methods.First(m => m.Name == "TestArithmetic");
        Assert.True(testArithmetic.IsPrivate);
        Assert.False(testArithmetic.IsPublic);
    }

    [Fact]
    public void MethodInfo_PublicMethod_IsPublic()
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath);
        var prog = reader.GetAllTypes().First(t => t.Name == "Program");
        var main = prog.Methods.First(m => m.Name == "Main");
        Assert.True(main.IsPublic);
        Assert.False(main.IsPrivate);
    }
}
