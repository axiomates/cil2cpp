using Xunit;
using CIL2CPP.Core.CodeGen;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

/// <summary>
/// Tests for SIMD dead-code handling: opaque struct generation, render-time replacement,
/// and correct method emission for methods with SIMD locals in dead branches.
/// </summary>
public class SimdHandlingTests
{
    private static IRModule CreateModuleWithSimdLocal(string localTypeName)
    {
        var module = new IRModule { Name = "TestApp" };

        var type = new IRType
        {
            ILFullName = "TestClass",
            CppName = "TestClass",
            Name = "TestClass",
            Namespace = "",
            IsValueType = false,
        };

        var method = new IRMethod
        {
            Name = "TestMethod",
            CppName = "TestClass_TestMethod",
            DeclaringType = type,
            IsStatic = true,
            ReturnTypeCpp = "void",
        };

        // Add a SIMD local (from dead branch — Cecil always includes all locals)
        method.Locals.Add(new IRLocal
        {
            Index = 0,
            CppName = "loc_0",
            CppTypeName = localTypeName,
        });

        // Add a normal local
        method.Locals.Add(new IRLocal
        {
            Index = 1,
            CppName = "loc_1",
            CppTypeName = "int32_t",
        });

        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRReturn());
        method.BasicBlocks.Add(bb);
        type.Methods.Add(method);

        // Entry point
        var mainType = new IRType
        {
            ILFullName = "Program",
            CppName = "Program",
            Name = "Program",
            Namespace = "",
            IsValueType = false,
        };
        var mainMethod = new IRMethod
        {
            Name = "Main",
            CppName = "Program_Main",
            DeclaringType = mainType,
            IsStatic = true,
            ReturnTypeCpp = "void",
        };
        var mainBb = new IRBasicBlock { Id = 0 };
        mainBb.Instructions.Add(new IRReturn());
        mainMethod.BasicBlocks.Add(mainBb);
        mainType.Methods.Add(mainMethod);

        module.Types.Add(type);
        module.Types.Add(mainType);
        module.EntryPoint = mainMethod;
        return module;
    }

    [Theory]
    [InlineData("System_Runtime_Intrinsics_Vector128_1_System_Byte", 16)]
    [InlineData("System_Runtime_Intrinsics_Vector256_1_System_Int32", 32)]
    [InlineData("System_Runtime_Intrinsics_Vector512_1_System_Double", 64)]
    [InlineData("System_Runtime_Intrinsics_Vector64_1_System_UInt16", 8)]
    [InlineData("System_Numerics_Vector_1_System_Single", 16)]
    public void SimdOpaqueStruct_HasCorrectSize(string typeName, int expectedSize)
    {
        var module = CreateModuleWithSimdLocal(typeName);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains($"struct {typeName} {{ uint8_t __opaque[{expectedSize}]; }};", output.HeaderFile.Content);
    }

    [Theory]
    [InlineData("System_Runtime_Intrinsics_Vector128_1_System_Byte")]
    [InlineData("System_Runtime_Intrinsics_Vector256_1_System_Int32")]
    [InlineData("System_Runtime_Intrinsics_Vector512_1_System_Double")]
    [InlineData("System_Runtime_Intrinsics_Vector64_1_System_UInt16")]
    [InlineData("System_Numerics_Vector_1_System_Single")]
    public void MethodWithSimdLocal_IsNotBlocked(string localTypeName)
    {
        var module = CreateModuleWithSimdLocal(localTypeName);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        // Method should be declared in header
        Assert.Contains("void TestClass_TestMethod()", output.HeaderFile.Content);

        // Method should have body in source
        var source = output.MethodFiles.FirstOrDefault();
        Assert.NotNull(source);
        Assert.Contains("void TestClass_TestMethod()", source.Content);
        // Local should be declared with default init
        Assert.Contains($"{localTypeName} loc_0 = {{}};", source.Content);
    }

    [Theory]
    [InlineData("System_Runtime_Intrinsics_X86_Sse2_Add")]
    [InlineData("System_Runtime_Intrinsics_Arm_AdvSimd_Add")]
    [InlineData("System_Runtime_Intrinsics_Wasm_PackedSimd_Add")]
    [InlineData("System_Runtime_Intrinsics_Vector256_ShuffleUnsafe")]
    [InlineData("System_Runtime_Intrinsics_Vector512_Create")]
    public void SimdIntrinsicCall_ReplacedWithDeadCode(string functionName)
    {
        // X86/Arm/Wasm intrinsic calls in dead branches get render-time replacement
        var module = CreateModuleWithSimdCall(functionName);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        var source = output.MethodFiles.FirstOrDefault();
        Assert.NotNull(source);
        Assert.Contains("SIMD dead-code", source.Content);
    }

    [Theory]
    [InlineData("System_Runtime_Intrinsics_Vector128_1_System_Byte_op_Addition")]
    [InlineData("System_Numerics_Vector_1_System_Single_op_Multiply")]
    public void SimdContainerCall_InLiveCode_MethodBlockedAsStub(string functionName)
    {
        // SIMD generic type instance methods (Vector128<T>.op_*, Vector<T>.op_*)
        // access opaque struct fields — block the method and emit a stub instead
        var module = CreateModuleWithSimdCall(functionName);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        // Method should NOT appear in method files (it's blocked by CallsUndeclaredFunction)
        var source = output.MethodFiles.FirstOrDefault();
        if (source != null)
        {
            Assert.DoesNotContain("TestClass_Test()", source.Content);
        }
        // Method should appear in stub file instead
        Assert.Contains("TestClass_Test", output.StubFile.Content);
    }

    private static IRModule CreateModuleWithSimdCall(string functionName)
    {
        var module = new IRModule { Name = "TestApp" };
        var type = new IRType
        {
            ILFullName = "TestClass",
            CppName = "TestClass",
            Name = "TestClass",
            Namespace = "",
            IsValueType = false,
        };

        var method = new IRMethod
        {
            Name = "Test",
            CppName = "TestClass_Test",
            DeclaringType = type,
            IsStatic = true,
            ReturnTypeCpp = "int32_t",
        };

        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRCall
        {
            FunctionName = functionName,
            ResultVar = "__t0",
            ResultTypeCpp = "int32_t",
        });
        bb.Instructions.Add(new IRReturn { Value = "__t0" });
        method.BasicBlocks.Add(bb);
        type.Methods.Add(method);

        var mainType = new IRType
        {
            ILFullName = "Program",
            CppName = "Program",
            Name = "Program",
            Namespace = "",
            IsValueType = false,
        };
        var mainMethod = new IRMethod
        {
            Name = "Main",
            CppName = "Program_Main",
            DeclaringType = mainType,
            IsStatic = true,
            ReturnTypeCpp = "void",
        };
        var mainBb = new IRBasicBlock { Id = 0 };
        mainBb.Instructions.Add(new IRReturn());
        mainMethod.BasicBlocks.Add(mainBb);
        mainType.Methods.Add(mainMethod);

        module.Types.Add(type);
        module.Types.Add(mainType);
        module.EntryPoint = mainMethod;
        return module;
    }
}
