using Xunit;
using Mono.Cecil.Cil;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("ControlFlow")]
public class IRBuilderControlFlowTests
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderControlFlowTests(FeatureTestFixture fixture)
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

    // ===== Arithmetic =====

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

    // ===== Bitwise =====

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

    // ===== Branching =====

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

    [Fact]
    public void Build_FeatureTest_TestBranching_HasLabels()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestBranching");
        Assert.Contains(instrs, i => i is IRLabel);
    }

    // ===== Conversions =====

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

    // ===== Checked Arithmetic =====

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

    // ===== Checked Conversions =====

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

    // ===== Exception Handling =====

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

    [Fact]
    public void Build_FeatureTest_TestRethrow_HasNestedTryCatch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestRethrow");
        // Should have two TryBegin (outer + inner)
        var tryBegins = instrs.OfType<IRTryBegin>().ToList();
        Assert.True(tryBegins.Count >= 2, "TestRethrow should have nested try blocks");
    }

    // ===== Exception Filters =====

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

    // ===== Casting =====

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

    // ===== Boxing / Unboxing =====

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

    // ===== Switch =====

    [Fact]
    public void Build_FeatureTest_TestSwitchStatement_HasSwitch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestSwitchStatement");
        var switches = instrs.OfType<IRSwitch>().ToList();
        Assert.True(switches.Count > 0, "TestSwitchStatement should have switch instruction");
        Assert.True(switches[0].CaseLabels.Count >= 4, "Switch should have at least 4 cases");
    }

    // ===== Float / Double / Long =====

    [Fact]
    public void Build_FeatureTest_TestFloatDouble_HasFloatingPointOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestFloatDouble");
        // Should have binary ops for float/double arithmetic
        Assert.Contains(instrs, i => i is IRBinaryOp);
    }

    [Fact]
    public void Build_FeatureTest_TestLong_HasLongOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestLong");
        Assert.Contains(instrs, i => i is IRBinaryOp);
    }

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

    // ===== Null / Dup =====

    [Fact]
    public void Build_FeatureTest_TestNullAndDup_HasBranches()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestNullAndDup");
        // Testing for null check: if (obj == null)
        Assert.Contains(instrs, i => i is IRConditionalBranch);
    }

    // ===== Math Operations =====

    [Fact]
    public void Build_FeatureTest_TestConstants_HasBinaryOps()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestConstants");
        Assert.Contains(instrs, i => i is IRBinaryOp);
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

    [Fact]
    public void Build_FeatureTest_TestMathAbsOverloads_HasAbsCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMathAbsOverloads");
        var calls = instrs.OfType<IRCall>().ToList();
        var funcNames = calls.Select(c => c.FunctionName).ToList();
        // Math.Abs overloads are [InternalCall] — mapped to cil2cpp::icall::Math_Abs_* via ICallRegistry
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("Abs") || n.Contains("abs"));
    }

    // ===== Struct Operations =====

    [Fact]
    public void Build_FeatureTest_TestStructOps_HasInitObj()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestStructOps");
        Assert.Contains(instrs, i => i is IRInitObj);
    }

    // ===== Loops =====

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

    // ===== Goto =====

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

    // ===== Nested If/Else =====

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

    // ===== Ternary / Short-Circuit =====

    [Fact]
    public void Build_FeatureTest_TestTernary_HasConditionalBranch()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.CppName == "Program")
            .Methods.First(m => m.Name == "TestTernary");
        var instrs = method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instrs, i => i is IRConditionalBranch);
    }

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

    // ===== Unsigned & NaN Comparison =====

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

    // ===== sizeof =====

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

    // ===== Ldind / Stind =====

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

    // ===== Method Parameters =====

    [Fact]
    public void Build_FeatureTest_ManyParams_HasSixParams()
    {
        var module = BuildFeatureTest();
        var program = module.FindType("Program")!;
        var method = program.Methods.First(m => m.Name == "ManyParams");
        Assert.Equal(6, method.Parameters.Count);
    }

    [Fact]
    public void Build_FeatureTest_ModifyArg_HasAssign()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "ModifyArg");
        // starg results in an IRAssign to the parameter
        Assert.Contains(instrs, i => i is IRAssign);
    }

    // ===== BCL Method Mapping =====

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

    // ===== Static Method Cctor Guard =====

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

    // ===== No-op Prefixes =====

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
}
