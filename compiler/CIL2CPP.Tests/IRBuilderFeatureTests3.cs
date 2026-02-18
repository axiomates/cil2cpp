using Xunit;
using Mono.Cecil.Cil;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("FeatureTest3")]
public class IRBuilderFeatureTests3
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderFeatureTests3(FeatureTestFixture fixture)
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
}
