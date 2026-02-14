using Xunit;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Tests;

public class ILInstructionCategoryTests
{
    // ===== IsLoad =====

    [Theory]
    [InlineData(Code.Ldarg_0)]
    [InlineData(Code.Ldarg_1)]
    [InlineData(Code.Ldarg_2)]
    [InlineData(Code.Ldarg_3)]
    [InlineData(Code.Ldarg_S)]
    [InlineData(Code.Ldarg)]
    public void IsLoad_LoadArgOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsLoad(code));
    }

    [Theory]
    [InlineData(Code.Ldloc_0)]
    [InlineData(Code.Ldloc_1)]
    [InlineData(Code.Ldloc_2)]
    [InlineData(Code.Ldloc_3)]
    [InlineData(Code.Ldloc_S)]
    [InlineData(Code.Ldloc)]
    public void IsLoad_LoadLocalOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsLoad(code));
    }

    [Theory]
    [InlineData(Code.Ldc_I4_0)]
    [InlineData(Code.Ldc_I4_1)]
    [InlineData(Code.Ldc_I4_M1)]
    [InlineData(Code.Ldc_I4_S)]
    [InlineData(Code.Ldc_I4)]
    [InlineData(Code.Ldc_I8)]
    [InlineData(Code.Ldc_R4)]
    [InlineData(Code.Ldc_R8)]
    [InlineData(Code.Ldstr)]
    [InlineData(Code.Ldnull)]
    public void IsLoad_LoadConstOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsLoad(code));
    }

    [Theory]
    [InlineData(Code.Ldfld)]
    [InlineData(Code.Ldsfld)]
    [InlineData(Code.Ldflda)]
    [InlineData(Code.Ldsflda)]
    public void IsLoad_LoadFieldOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsLoad(code));
    }

    [Theory]
    [InlineData(Code.Ldelem_I4)]
    [InlineData(Code.Ldelem_Ref)]
    [InlineData(Code.Ldelem_Any)]
    [InlineData(Code.Ldlen)]
    public void IsLoad_LoadArrayOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsLoad(code));
    }

    [Theory]
    [InlineData(Code.Stloc_0)]
    [InlineData(Code.Add)]
    [InlineData(Code.Ret)]
    [InlineData(Code.Nop)]
    public void IsLoad_NonLoadOpcodes_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsLoad(code));
    }

    // ===== IsStore =====

    [Theory]
    [InlineData(Code.Starg)]
    [InlineData(Code.Starg_S)]
    public void IsStore_StoreArgOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsStore(code));
    }

    [Theory]
    [InlineData(Code.Stloc_0)]
    [InlineData(Code.Stloc_1)]
    [InlineData(Code.Stloc_2)]
    [InlineData(Code.Stloc_3)]
    [InlineData(Code.Stloc_S)]
    [InlineData(Code.Stloc)]
    public void IsStore_StoreLocalOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsStore(code));
    }

    [Theory]
    [InlineData(Code.Stfld)]
    [InlineData(Code.Stsfld)]
    public void IsStore_StoreFieldOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsStore(code));
    }

    [Theory]
    [InlineData(Code.Stelem_I4)]
    [InlineData(Code.Stelem_Ref)]
    [InlineData(Code.Stelem_Any)]
    [InlineData(Code.Stelem_I)]
    public void IsStore_StoreArrayOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsStore(code));
    }

    [Theory]
    [InlineData(Code.Ldloc_0)]
    [InlineData(Code.Add)]
    [InlineData(Code.Ret)]
    public void IsStore_NonStoreOpcodes_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsStore(code));
    }

    // ===== IsArithmetic =====

    [Theory]
    [InlineData(Code.Add)]
    [InlineData(Code.Sub)]
    [InlineData(Code.Mul)]
    [InlineData(Code.Div)]
    [InlineData(Code.Rem)]
    [InlineData(Code.Neg)]
    public void IsArithmetic_BasicOps_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsArithmetic(code));
    }

    [Theory]
    [InlineData(Code.Add_Ovf)]
    [InlineData(Code.Add_Ovf_Un)]
    [InlineData(Code.Sub_Ovf)]
    [InlineData(Code.Sub_Ovf_Un)]
    [InlineData(Code.Mul_Ovf)]
    [InlineData(Code.Mul_Ovf_Un)]
    [InlineData(Code.Div_Un)]
    [InlineData(Code.Rem_Un)]
    public void IsArithmetic_OverflowVariants_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsArithmetic(code));
    }

    [Theory]
    [InlineData(Code.And)]
    [InlineData(Code.Ceq)]
    [InlineData(Code.Ret)]
    public void IsArithmetic_NonArithmetic_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsArithmetic(code));
    }

    // ===== IsBitwise =====

    [Theory]
    [InlineData(Code.And)]
    [InlineData(Code.Or)]
    [InlineData(Code.Xor)]
    [InlineData(Code.Not)]
    [InlineData(Code.Shl)]
    [InlineData(Code.Shr)]
    [InlineData(Code.Shr_Un)]
    public void IsBitwise_AllOps_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsBitwise(code));
    }

    [Theory]
    [InlineData(Code.Add)]
    [InlineData(Code.Ceq)]
    [InlineData(Code.Ret)]
    public void IsBitwise_NonBitwise_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsBitwise(code));
    }

    // ===== IsComparison =====

    [Theory]
    [InlineData(Code.Ceq)]
    [InlineData(Code.Cgt)]
    [InlineData(Code.Cgt_Un)]
    [InlineData(Code.Clt)]
    [InlineData(Code.Clt_Un)]
    public void IsComparison_AllOps_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsComparison(code));
    }

    [Theory]
    [InlineData(Code.Add)]
    [InlineData(Code.Beq)]
    [InlineData(Code.Ret)]
    public void IsComparison_NonComparison_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsComparison(code));
    }

    // ===== IsBranch =====

    [Theory]
    [InlineData(Code.Br)]
    [InlineData(Code.Br_S)]
    public void IsBranch_UnconditionalBr_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsBranch(code));
    }

    [Theory]
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
    public void IsBranch_ConditionalBr_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsBranch(code));
    }

    [Fact]
    public void IsBranch_Switch_ReturnsTrue()
    {
        Assert.True(ILInstructionCategory.IsBranch(Code.Switch));
    }

    [Theory]
    [InlineData(Code.Add)]
    [InlineData(Code.Ret)]
    [InlineData(Code.Nop)]
    public void IsBranch_NonBranch_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsBranch(code));
    }

    // ===== IsCall =====

    [Theory]
    [InlineData(Code.Call)]
    [InlineData(Code.Callvirt)]
    [InlineData(Code.Calli)]
    [InlineData(Code.Newobj)]
    public void IsCall_CallOpcodes_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsCall(code));
    }

    [Theory]
    [InlineData(Code.Ret)]
    [InlineData(Code.Ldarg_0)]
    [InlineData(Code.Add)]
    public void IsCall_NonCall_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsCall(code));
    }

    // ===== IsConversion =====

    [Theory]
    [InlineData(Code.Conv_I)]
    [InlineData(Code.Conv_I1)]
    [InlineData(Code.Conv_I2)]
    [InlineData(Code.Conv_I4)]
    [InlineData(Code.Conv_I8)]
    public void IsConversion_SignedConversions_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsConversion(code));
    }

    [Theory]
    [InlineData(Code.Conv_U)]
    [InlineData(Code.Conv_U1)]
    [InlineData(Code.Conv_U2)]
    [InlineData(Code.Conv_U4)]
    [InlineData(Code.Conv_U8)]
    public void IsConversion_UnsignedConversions_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsConversion(code));
    }

    [Theory]
    [InlineData(Code.Conv_R4)]
    [InlineData(Code.Conv_R8)]
    [InlineData(Code.Conv_R_Un)]
    public void IsConversion_FloatConversions_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsConversion(code));
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
    public void IsConversion_OverflowConversions_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsConversion(code));
    }

    [Theory]
    [InlineData(Code.Add)]
    [InlineData(Code.Ldarg_0)]
    [InlineData(Code.Ret)]
    public void IsConversion_NonConversion_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsConversion(code));
    }

    // ===== IsObjectOperation =====

    [Theory]
    [InlineData(Code.Newobj)]
    [InlineData(Code.Newarr)]
    public void IsObjectOperation_NewOps_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsObjectOperation(code));
    }

    [Theory]
    [InlineData(Code.Castclass)]
    [InlineData(Code.Isinst)]
    public void IsObjectOperation_CastOps_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsObjectOperation(code));
    }

    [Theory]
    [InlineData(Code.Box)]
    [InlineData(Code.Unbox)]
    [InlineData(Code.Unbox_Any)]
    public void IsObjectOperation_BoxOps_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsObjectOperation(code));
    }

    [Theory]
    [InlineData(Code.Ldobj)]
    [InlineData(Code.Stobj)]
    [InlineData(Code.Cpobj)]
    [InlineData(Code.Initobj)]
    public void IsObjectOperation_ValueOps_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsObjectOperation(code));
    }

    [Theory]
    [InlineData(Code.Add)]
    [InlineData(Code.Ret)]
    [InlineData(Code.Ldarg_0)]
    public void IsObjectOperation_NonObjOp_ReturnsFalse(Code code)
    {
        Assert.False(ILInstructionCategory.IsObjectOperation(code));
    }

    // ===== Unsigned branch variants =====

    [Theory]
    [InlineData(Code.Bge_Un)]
    [InlineData(Code.Bge_Un_S)]
    [InlineData(Code.Bgt_Un)]
    [InlineData(Code.Bgt_Un_S)]
    [InlineData(Code.Ble_Un)]
    [InlineData(Code.Ble_Un_S)]
    [InlineData(Code.Blt_Un)]
    [InlineData(Code.Blt_Un_S)]
    public void IsBranch_UnsignedConditionalBr_ReturnsTrue(Code code)
    {
        Assert.True(ILInstructionCategory.IsBranch(code));
    }
}
