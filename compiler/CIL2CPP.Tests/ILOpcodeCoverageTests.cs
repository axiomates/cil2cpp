using Xunit;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

/// <summary>
/// Tests that verify IL opcode coverage completeness.
/// All ECMA-335 opcodes are handled — 100% coverage.
/// </summary>
public class ILOpcodeCoverageTests
{
    // ===== Structural Tests (no fixture needed, millisecond-fast) =====

    [Fact]
    public void HandledOpcodes_CoverAllOpcodes()
    {
        var allCodes = Enum.GetValues<Code>().ToHashSet();
        var unaccounted = allCodes
            .Where(c => !IRBuilder.HandledOpcodes.Contains(c))
            .OrderBy(c => c.ToString())
            .ToList();

        Assert.True(unaccounted.Count == 0,
            $"Unaccounted opcodes (not in HandledOpcodes):\n"
            + string.Join("\n", unaccounted.Select(c => $"  Code.{c}")));
    }

    [Fact]
    public void HandledOpcodes_AtLeast190()
    {
        Assert.True(IRBuilder.HandledOpcodes.Count >= 190,
            $"Expected at least 190 handled opcodes, got {IRBuilder.HandledOpcodes.Count}. "
            + "This guard prevents large regressions in opcode coverage.");
    }

    // ===== Per-category coverage tests =====

    [Theory]
    [InlineData(Code.Ldarg_0)]
    [InlineData(Code.Ldarg_1)]
    [InlineData(Code.Ldarg_2)]
    [InlineData(Code.Ldarg_3)]
    [InlineData(Code.Ldarg_S)]
    [InlineData(Code.Ldarg)]
    [InlineData(Code.Starg_S)]
    [InlineData(Code.Starg)]
    [InlineData(Code.Ldarga)]
    [InlineData(Code.Ldarga_S)]
    public void HandledOpcodes_AllArgumentOpcodes(Code code)
    {
        Assert.Contains(code, IRBuilder.HandledOpcodes);
    }

    [Theory]
    [InlineData(Code.Ldloc_0)]
    [InlineData(Code.Ldloc_1)]
    [InlineData(Code.Ldloc_2)]
    [InlineData(Code.Ldloc_3)]
    [InlineData(Code.Ldloc_S)]
    [InlineData(Code.Ldloc)]
    [InlineData(Code.Ldloca)]
    [InlineData(Code.Ldloca_S)]
    [InlineData(Code.Stloc_0)]
    [InlineData(Code.Stloc_1)]
    [InlineData(Code.Stloc_2)]
    [InlineData(Code.Stloc_3)]
    [InlineData(Code.Stloc_S)]
    [InlineData(Code.Stloc)]
    public void HandledOpcodes_AllLocalOpcodes(Code code)
    {
        Assert.Contains(code, IRBuilder.HandledOpcodes);
    }

    [Theory]
    [InlineData(Code.Conv_I1)]
    [InlineData(Code.Conv_I2)]
    [InlineData(Code.Conv_I4)]
    [InlineData(Code.Conv_I8)]
    [InlineData(Code.Conv_I)]
    [InlineData(Code.Conv_U1)]
    [InlineData(Code.Conv_U2)]
    [InlineData(Code.Conv_U4)]
    [InlineData(Code.Conv_U8)]
    [InlineData(Code.Conv_U)]
    [InlineData(Code.Conv_R4)]
    [InlineData(Code.Conv_R8)]
    [InlineData(Code.Conv_R_Un)]
    public void HandledOpcodes_AllConversionOpcodes(Code code)
    {
        Assert.Contains(code, IRBuilder.HandledOpcodes);
    }

    [Theory]
    [InlineData(Code.Br)]
    [InlineData(Code.Br_S)]
    [InlineData(Code.Brtrue)]
    [InlineData(Code.Brtrue_S)]
    [InlineData(Code.Brfalse)]
    [InlineData(Code.Brfalse_S)]
    [InlineData(Code.Beq)]
    [InlineData(Code.Beq_S)]
    [InlineData(Code.Bne_Un)]
    [InlineData(Code.Bne_Un_S)]
    [InlineData(Code.Bge)]
    [InlineData(Code.Bge_S)]
    [InlineData(Code.Bgt)]
    [InlineData(Code.Bgt_S)]
    [InlineData(Code.Ble)]
    [InlineData(Code.Ble_S)]
    [InlineData(Code.Blt)]
    [InlineData(Code.Blt_S)]
    [InlineData(Code.Bge_Un)]
    [InlineData(Code.Bge_Un_S)]
    [InlineData(Code.Bgt_Un)]
    [InlineData(Code.Bgt_Un_S)]
    [InlineData(Code.Ble_Un)]
    [InlineData(Code.Ble_Un_S)]
    [InlineData(Code.Blt_Un)]
    [InlineData(Code.Blt_Un_S)]
    [InlineData(Code.Switch)]
    public void HandledOpcodes_AllBranchOpcodes(Code code)
    {
        Assert.Contains(code, IRBuilder.HandledOpcodes);
    }

    [Theory]
    [InlineData(Code.Ldelem_I1)]
    [InlineData(Code.Ldelem_I2)]
    [InlineData(Code.Ldelem_I4)]
    [InlineData(Code.Ldelem_I8)]
    [InlineData(Code.Ldelem_U1)]
    [InlineData(Code.Ldelem_U2)]
    [InlineData(Code.Ldelem_U4)]
    [InlineData(Code.Ldelem_R4)]
    [InlineData(Code.Ldelem_R8)]
    [InlineData(Code.Ldelem_Ref)]
    [InlineData(Code.Ldelem_I)]
    [InlineData(Code.Ldelem_Any)]
    [InlineData(Code.Stelem_I1)]
    [InlineData(Code.Stelem_I2)]
    [InlineData(Code.Stelem_I4)]
    [InlineData(Code.Stelem_I8)]
    [InlineData(Code.Stelem_R4)]
    [InlineData(Code.Stelem_R8)]
    [InlineData(Code.Stelem_I)]
    [InlineData(Code.Stelem_Ref)]
    [InlineData(Code.Stelem_Any)]
    [InlineData(Code.Ldelema)]
    [InlineData(Code.Newarr)]
    [InlineData(Code.Ldlen)]
    public void HandledOpcodes_AllArrayOpcodes(Code code)
    {
        Assert.Contains(code, IRBuilder.HandledOpcodes);
    }

    [Theory]
    [InlineData(Code.Conv_Ovf_I)]
    [InlineData(Code.Conv_Ovf_I1)]
    [InlineData(Code.Conv_Ovf_I2)]
    [InlineData(Code.Conv_Ovf_I4)]
    [InlineData(Code.Conv_Ovf_I8)]
    [InlineData(Code.Conv_Ovf_U)]
    [InlineData(Code.Conv_Ovf_U1)]
    [InlineData(Code.Conv_Ovf_U2)]
    [InlineData(Code.Conv_Ovf_U4)]
    [InlineData(Code.Conv_Ovf_U8)]
    [InlineData(Code.Conv_Ovf_I_Un)]
    [InlineData(Code.Conv_Ovf_I1_Un)]
    [InlineData(Code.Conv_Ovf_I2_Un)]
    [InlineData(Code.Conv_Ovf_I4_Un)]
    [InlineData(Code.Conv_Ovf_I8_Un)]
    [InlineData(Code.Conv_Ovf_U_Un)]
    [InlineData(Code.Conv_Ovf_U1_Un)]
    [InlineData(Code.Conv_Ovf_U2_Un)]
    [InlineData(Code.Conv_Ovf_U4_Un)]
    [InlineData(Code.Conv_Ovf_U8_Un)]
    public void HandledOpcodes_AllCheckedConversionOpcodes(Code code)
    {
        Assert.Contains(code, IRBuilder.HandledOpcodes);
    }
}

/// <summary>
/// Behavioral test: compile FeatureTest and verify no unsupported opcode warnings.
/// </summary>
[Collection("FeatureTest")]
public class ILOpcodeRuntimeCoverageTests
{
    private readonly FeatureTestFixture _fixture;

    public ILOpcodeRuntimeCoverageTests(FeatureTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void FeatureTest_NoUnexpectedUnsupportedOpcodeWarnings()
    {
        var module = _fixture.GetReleaseModule();
        var warnings = module.GetAllMethods()
            .SelectMany(m => m.BasicBlocks)
            .SelectMany(b => b.Instructions)
            .OfType<IRComment>()
            .Where(c => c.Text.StartsWith("WARNING: Unsupported IL instruction"))
            .Select(c => c.Text)
            .Distinct()
            .ToList();

        Assert.True(warnings.Count == 0,
            $"FeatureTest contains {warnings.Count} unexpected unsupported IL instruction(s):\n"
            + string.Join("\n", warnings.Select(w => $"  {w}")));
    }
}
