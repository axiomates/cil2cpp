namespace CIL2CPP.Core.CodeGen;

public partial class CppCodeGenerator
{
    /// <summary>
    /// Known C++ implementations for BCL generic specialization stubs that cannot
    /// compile from IL (due to static abstract interface methods, JIT intrinsics, etc.).
    /// Key: C++ function name, Value: C++ function body (without braces).
    /// </summary>
    private static readonly Dictionary<string, string> KnownStubImplementations = new()
    {
        // ===== Number Formatting: UInt32ToDecChars<Char> =====
        // Write decimal characters backward into buffer, no leading zeros
        ["System_Number_UInt32ToDecChars_System_Char__System_Char_ptr_System_UInt32"] =
            """
                do {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + (value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        // Write decimal characters backward with minimum digit count
        ["System_Number_UInt32ToDecChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + (value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        // ===== Number Formatting: WriteDigits<Char> =====
        // Write digits forward into buffer
        ["System_Number_WriteDigits_System_Char"] =
            """
                for (int32_t i = count - 1; i >= 1; i--) {
                    ptr[i] = static_cast<char16_t>(u'0' + (value % 10));
                    value /= 10;
                }
                ptr[0] = static_cast<char16_t>(u'0' + value);
            """,

        // ===== Number Formatting: UInt64ToDecChars<Char> =====
        // 64-bit version, write backward with minimum digit count
        ["System_Number_UInt64ToDecChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        // 64-bit version, no leading zeros
        ["System_Number_UInt64ToDecChars_System_Char__System_Char_ptr_System_UInt64"] =
            """
                do {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        // ===== Number Formatting: UInt32/UInt64ToDecChars<Byte> =====
        // Byte versions (used by UTF-8 formatting paths)
        ["System_Number_UInt32ToDecChars_System_Byte"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + (value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        ["System_Number_UInt64ToDecChars_System_Byte"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + static_cast<uint8_t>(value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        // ===== Number Formatting: UInt32/UInt64ToDecChars<Byte> no-leading-zeros =====
        ["System_Number_UInt32ToDecChars_System_Byte__System_Byte_ptr_System_UInt32"] =
            """
                do {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + (value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        ["System_Number_UInt64ToDecChars_System_Byte__System_Byte_ptr_System_UInt64"] =
            """
                do {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + static_cast<uint8_t>(value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        // ===== Number Formatting: Hex/Binary chars =====
        ["System_Number_Int32ToHexChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    uint8_t digit = static_cast<uint8_t>(value & 0xF);
                    *(--buffer) = static_cast<char16_t>(digit < 10 ? u'0' + digit : hexBase + digit - 10);
                    value >>= 4;
                }
                return buffer;
            """,

        ["System_Number_Int64ToHexChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    uint8_t digit = static_cast<uint8_t>(value & 0xF);
                    *(--buffer) = static_cast<char16_t>(digit < 10 ? u'0' + digit : hexBase + digit - 10);
                    value >>= 4;
                }
                return buffer;
            """,

        ["System_Number_UInt32ToBinaryChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--buffer) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value & 1));
                    value >>= 1;
                }
                return buffer;
            """,

        ["System_Number_UInt64ToBinaryChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--buffer) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value & 1));
                    value >>= 1;
                }
                return buffer;
            """,

        // ===== WriteDigits<Byte> =====
        ["System_Number_WriteDigits_System_Byte"] =
            """
                for (int32_t i = count - 1; i >= 1; i--) {
                    ptr[i] = static_cast<uint8_t>('0' + (value % 10));
                    value /= 10;
                }
                ptr[0] = static_cast<uint8_t>('0' + value);
            """,

        // ===== BitOperations =====
        ["System_Numerics_BitOperations_PopCount__System_UInt64"] =
            """
                int32_t count = 0;
                while (value) { count += static_cast<int32_t>(value & 1); value >>= 1; }
                return count;
            """,
    };

    /// <summary>
    /// Try to get a known C++ implementation for a stub function.
    /// Returns null if no known implementation exists.
    /// </summary>
    private static string? GetKnownStubBody(string cppName)
    {
        return KnownStubImplementations.TryGetValue(cppName, out var body) ? body : null;
    }

    /// <summary>
    /// Check if a method's IR body is a stub (single block with only IRReturn).
    /// These are generated by IRBuilder.GenerateStubBody for methods that can't compile from IL.
    /// </summary>
    private static bool IsStubBody(IR.IRMethod method)
    {
        if (method.BasicBlocks.Count != 1) return false;
        var block = method.BasicBlocks[0];
        return block.Instructions.Count == 1 && block.Instructions[0] is IR.IRReturn;
    }
}
