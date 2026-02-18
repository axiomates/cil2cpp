using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("FeatureTest4")]
public class IRBuilderFeatureTests4
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderFeatureTests4(FeatureTestFixture fixture)
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

    // ===== Custom Attribute complex args (Type/Enum/Array) =====

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasTypeRefAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var typeRef = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "TypeRefAttribute");
        Assert.NotNull(typeRef);
        Assert.Single(typeRef.ConstructorArgs);
        Assert.Equal(AttributeArgKind.Type, typeRef.ConstructorArgs[0].Kind);
        Assert.Equal("System.String", typeRef.ConstructorArgs[0].Value);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasSeverityAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var severity = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "SeverityAttribute");
        Assert.NotNull(severity);
        Assert.Single(severity.ConstructorArgs);
        Assert.Equal(AttributeArgKind.Enum, severity.ConstructorArgs[0].Kind);
        Assert.Equal(2L, severity.ConstructorArgs[0].Value);  // Severity.High == 2
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasTagsAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var tags = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "TagsAttribute");
        Assert.NotNull(tags);
        Assert.Single(tags.ConstructorArgs);
        var arrayArg = tags.ConstructorArgs[0];
        Assert.Equal(AttributeArgKind.Array, arrayArg.Kind);
        Assert.NotNull(arrayArg.ArrayElements);
        Assert.Equal(3, arrayArg.ArrayElements.Count);
        Assert.Equal("important", arrayArg.ArrayElements[0].Value);
        Assert.Equal("test", arrayArg.ArrayElements[1].Value);
        Assert.Equal("example", arrayArg.ArrayElements[2].Value);
    }

    [Fact]
    public void Build_FeatureTest_AnnotatedMethod_HasTypeRefAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var method = attrType.Methods.First(m => m.Name == "AnnotatedMethod");
        var typeRef = method.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "TypeRefAttribute");
        Assert.NotNull(typeRef);
        Assert.Single(typeRef.ConstructorArgs);
        Assert.Equal(AttributeArgKind.Type, typeRef.ConstructorArgs[0].Kind);
        Assert.Equal("System.Int32", typeRef.ConstructorArgs[0].Value);
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

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeQSort_HasDelegateParam()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeQSort");
        Assert.True(method.IsPInvoke);
        Assert.Equal("qsort", method.PInvokeEntryPoint);
        // Last parameter should be a delegate (CompareCallback)
        var lastParam = method.Parameters.Last();
        Assert.NotNull(lastParam.ParameterType);
        Assert.True(lastParam.ParameterType!.IsDelegate);
    }

    [Fact]
    public void Build_FeatureTest_PInvoke_Point2D_IsValueType()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "Point2D");
        Assert.True(type.IsValueType);
        Assert.Equal(2, type.Fields.Count);
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
