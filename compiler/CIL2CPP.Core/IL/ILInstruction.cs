using Mono.Cecil.Cil;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Represents an IL instruction extracted from method body.
/// </summary>
public class ILInstruction
{
    private readonly Instruction _instruction;

    /// <summary>
    /// Offset of this instruction in the method body.
    /// </summary>
    public int Offset => _instruction.Offset;

    /// <summary>
    /// The opcode of this instruction.
    /// </summary>
    public Code OpCode => _instruction.OpCode.Code;

    /// <summary>
    /// The opcode name (e.g., "ldarg.0", "add", "call").
    /// </summary>
    public string OpCodeName => _instruction.OpCode.Name;

    /// <summary>
    /// The operand of this instruction (if any).
    /// </summary>
    public object? Operand => _instruction.Operand;

    /// <summary>
    /// Gets the operand as a string representation.
    /// </summary>
    public string OperandString => FormatOperand();

    /// <summary>
    /// Stack behavior - how many values this instruction pops.
    /// </summary>
    public StackBehaviour PopBehavior => _instruction.OpCode.StackBehaviourPop;

    /// <summary>
    /// Stack behavior - how many values this instruction pushes.
    /// </summary>
    public StackBehaviour PushBehavior => _instruction.OpCode.StackBehaviourPush;

    /// <summary>
    /// Flow control type of this instruction.
    /// </summary>
    public FlowControl FlowControl => _instruction.OpCode.FlowControl;

    public ILInstruction(Instruction instruction)
    {
        _instruction = instruction;
    }

    private string FormatOperand()
    {
        if (_instruction.Operand == null)
            return "";

        return _instruction.Operand switch
        {
            Instruction target => $"IL_{target.Offset:X4}",
            Instruction[] targets => string.Join(", ", targets.Select(t => $"IL_{t.Offset:X4}")),
            string s => $"\"{s}\"",
            _ => _instruction.Operand.ToString() ?? ""
        };
    }

    public override string ToString()
    {
        var operand = OperandString;
        if (string.IsNullOrEmpty(operand))
            return $"IL_{Offset:X4}: {OpCodeName}";
        return $"IL_{Offset:X4}: {OpCodeName} {operand}";
    }

    internal Instruction GetCecilInstruction() => _instruction;
}

/// <summary>
/// Categorizes IL instructions by type for easier processing.
/// </summary>
public static class ILInstructionCategory
{
    public static bool IsLoad(Code code) => code switch
    {
        Code.Ldarg or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S => true,
        Code.Ldloc or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc_S => true,
        Code.Ldc_I4 or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or
        Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or
        Code.Ldc_I4_M1 or Code.Ldc_I4_S => true,
        Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8 => true,
        Code.Ldstr => true,
        Code.Ldnull => true,
        Code.Ldfld or Code.Ldsfld or Code.Ldflda or Code.Ldsflda => true,
        Code.Ldelem_Any or Code.Ldelem_I or Code.Ldelem_I1 or Code.Ldelem_I2 or
        Code.Ldelem_I4 or Code.Ldelem_I8 or Code.Ldelem_R4 or Code.Ldelem_R8 or
        Code.Ldelem_Ref or Code.Ldelem_U1 or Code.Ldelem_U2 or Code.Ldelem_U4 => true,
        Code.Ldlen => true,
        _ => false
    };

    public static bool IsStore(Code code) => code switch
    {
        Code.Starg or Code.Starg_S => true,
        Code.Stloc or Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3 or Code.Stloc_S => true,
        Code.Stfld or Code.Stsfld => true,
        Code.Stelem_Any or Code.Stelem_I or Code.Stelem_I1 or Code.Stelem_I2 or
        Code.Stelem_I4 or Code.Stelem_I8 or Code.Stelem_R4 or Code.Stelem_R8 or Code.Stelem_Ref => true,
        _ => false
    };

    public static bool IsArithmetic(Code code) => code switch
    {
        Code.Add or Code.Add_Ovf or Code.Add_Ovf_Un => true,
        Code.Sub or Code.Sub_Ovf or Code.Sub_Ovf_Un => true,
        Code.Mul or Code.Mul_Ovf or Code.Mul_Ovf_Un => true,
        Code.Div or Code.Div_Un => true,
        Code.Rem or Code.Rem_Un => true,
        Code.Neg => true,
        _ => false
    };

    public static bool IsBitwise(Code code) => code switch
    {
        Code.And or Code.Or or Code.Xor or Code.Not => true,
        Code.Shl or Code.Shr or Code.Shr_Un => true,
        _ => false
    };

    public static bool IsComparison(Code code) => code switch
    {
        Code.Ceq or Code.Cgt or Code.Cgt_Un or Code.Clt or Code.Clt_Un => true,
        _ => false
    };

    public static bool IsBranch(Code code) => code switch
    {
        Code.Br or Code.Br_S => true,
        Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S => true,
        Code.Beq or Code.Beq_S or Code.Bne_Un or Code.Bne_Un_S => true,
        Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => true,
        Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S => true,
        Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => true,
        Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S => true,
        Code.Switch => true,
        _ => false
    };

    public static bool IsCall(Code code) => code switch
    {
        Code.Call or Code.Callvirt or Code.Calli => true,
        Code.Newobj => true,
        _ => false
    };

    public static bool IsConversion(Code code) => code switch
    {
        Code.Conv_I or Code.Conv_I1 or Code.Conv_I2 or Code.Conv_I4 or Code.Conv_I8 => true,
        Code.Conv_U or Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4 or Code.Conv_U8 => true,
        Code.Conv_R4 or Code.Conv_R8 or Code.Conv_R_Un => true,
        Code.Conv_Ovf_I or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I2 or Code.Conv_Ovf_I4 or Code.Conv_Ovf_I8 => true,
        Code.Conv_Ovf_U or Code.Conv_Ovf_U1 or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U4 or Code.Conv_Ovf_U8 => true,
        Code.Conv_Ovf_I_Un or Code.Conv_Ovf_I1_Un or Code.Conv_Ovf_I2_Un or Code.Conv_Ovf_I4_Un or Code.Conv_Ovf_I8_Un => true,
        Code.Conv_Ovf_U_Un or Code.Conv_Ovf_U1_Un or Code.Conv_Ovf_U2_Un or Code.Conv_Ovf_U4_Un or Code.Conv_Ovf_U8_Un => true,
        _ => false
    };

    public static bool IsObjectOperation(Code code) => code switch
    {
        Code.Newobj or Code.Newarr => true,
        Code.Castclass or Code.Isinst => true,
        Code.Box or Code.Unbox or Code.Unbox_Any => true,
        Code.Ldobj or Code.Stobj or Code.Cpobj or Code.Initobj => true,
        _ => false
    };
}
