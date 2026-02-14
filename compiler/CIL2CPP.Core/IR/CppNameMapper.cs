namespace CIL2CPP.Core.IR;

/// <summary>
/// Maps .NET type and member names to valid C++ identifiers.
/// </summary>
public static class CppNameMapper
{
    // User-defined value types (structs and enums) registered during IR build
    private static readonly HashSet<string> _userValueTypes = new();

    public static void RegisterValueType(string ilTypeName) => _userValueTypes.Add(ilTypeName);
    public static void ClearValueTypes() => _userValueTypes.Clear();

    private static readonly Dictionary<string, string> PrimitiveTypeMap = new()
    {
        ["System.Void"] = "void",
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "uint8_t",
        ["System.SByte"] = "int8_t",
        ["System.Int16"] = "int16_t",
        ["System.UInt16"] = "uint16_t",
        ["System.Int32"] = "int32_t",
        ["System.UInt32"] = "uint32_t",
        ["System.Int64"] = "int64_t",
        ["System.UInt64"] = "uint64_t",
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.Char"] = "char16_t",
        ["System.String"] = "cil2cpp::String",
        ["System.Object"] = "cil2cpp::Object",
        ["System.IntPtr"] = "intptr_t",
        ["System.UIntPtr"] = "uintptr_t",
    };

    /// <summary>
    /// Check if a type name is a primitive type.
    /// </summary>
    public static bool IsPrimitive(string ilTypeName)
    {
        return PrimitiveTypeMap.ContainsKey(ilTypeName);
    }

    /// <summary>
    /// Check if a type is a value type.
    /// </summary>
    public static bool IsValueType(string ilTypeName)
    {
        return ilTypeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" or
            "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or
            "System.Int64" or "System.UInt64" or "System.Single" or "System.Double" or
            "System.Char" or "System.IntPtr" or "System.UIntPtr" => true,
            _ => _userValueTypes.Contains(ilTypeName)
        };
    }

    /// <summary>
    /// Get the C++ type name for a .NET type.
    /// </summary>
    public static string GetCppTypeName(string ilTypeName, bool isPointer = false)
    {
        // Handle pointer/ref types
        if (ilTypeName.EndsWith("&"))
        {
            var baseType = ilTypeName[..^1];
            return GetCppTypeName(baseType) + "*";
        }

        if (ilTypeName.EndsWith("*"))
        {
            var baseType = ilTypeName[..^1];
            return GetCppTypeName(baseType) + "*";
        }

        // Handle array types
        if (ilTypeName.EndsWith("[]"))
        {
            return "cil2cpp::Array*";
        }

        // Primitive types
        if (PrimitiveTypeMap.TryGetValue(ilTypeName, out var cppName))
        {
            if (!IsValueType(ilTypeName) && !isPointer && ilTypeName != "System.Void")
                return cppName + "*";
            return cppName;
        }

        // User-defined types - mangle the name
        // For generic instance types (e.g. "Foo`1<System.Int32>"), use the dedicated mangler
        // to avoid trailing underscores from the closing '>'
        var backtickIdx = ilTypeName.IndexOf('`');
        if (backtickIdx > 0 && ilTypeName.Contains('<'))
        {
            var angleBracket = ilTypeName.IndexOf('<');
            var openTypeName = ilTypeName[..angleBracket];
            var argsStr = ilTypeName[(angleBracket + 1)..^1];
            var args = argsStr.Split(',').Select(a => a.Trim()).ToList();
            return MangleGenericInstanceTypeName(openTypeName, args);
        }

        return MangleTypeName(ilTypeName);
    }

    /// <summary>
    /// Get the C++ type name for variable declarations.
    /// Reference types get a pointer suffix.
    /// </summary>
    public static string GetCppTypeForDecl(string ilTypeName)
    {
        if (ilTypeName == "System.Void") return "void";

        if (IsValueType(ilTypeName))
            return GetCppTypeName(ilTypeName);

        var cppType = GetCppTypeName(ilTypeName);
        if (!cppType.EndsWith("*"))
            return cppType + "*";
        return cppType;
    }

    /// <summary>
    /// Mangle a .NET type name into a valid C++ identifier.
    /// </summary>
    public static string MangleTypeName(string ilFullName)
    {
        return ilFullName
            .Replace(".", "_")
            .Replace("/", "_")  // Nested types
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace("`", "_")
            .Replace(" ", "")
            .Replace("+", "_")
            .Replace("=", "_")  // e.g. __StaticArrayInitTypeSize=20
            .Replace("-", "_");
    }

    /// <summary>
    /// Mangle a generic instance type name into a valid C++ identifier.
    /// E.g., ("Wrapper`1", ["System.Int32"]) → "Wrapper_1_System_Int32"
    /// </summary>
    public static string MangleGenericInstanceTypeName(string openTypeName, IReadOnlyList<string> typeArgs)
    {
        var baseName = MangleTypeName(openTypeName);
        var argParts = string.Join("_", typeArgs.Select(MangleTypeName));
        return $"{baseName}_{argParts}";
    }

    /// <summary>
    /// Whether a type is a compiler-generated implementation detail (e.g. &lt;PrivateImplementationDetails&gt;).
    /// These should be filtered from C++ code generation.
    /// </summary>
    public static bool IsCompilerGeneratedType(string ilFullName)
    {
        return ilFullName.StartsWith("<PrivateImplementationDetails>");
    }

    /// <summary>
    /// Mangle a method name into a valid C++ function name.
    /// </summary>
    public static string MangleMethodName(string typeCppName, string methodName)
    {
        var safeName = methodName
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace("|", "_");

        return $"{typeCppName}_{safeName}";
    }

    /// <summary>
    /// Mangle a field name into a valid C++ identifier.
    /// </summary>
    public static string MangleFieldName(string fieldName)
    {
        // Remove leading underscore common in C# private fields
        var name = fieldName.TrimStart('_');
        if (name.Length == 0) name = fieldName;

        // Handle compiler-generated backing field names like <Name>k__BackingField
        name = name.Replace("<", "_").Replace(">", "_");

        // Prefix with f_ to avoid C++ keyword conflicts
        return $"f_{name}";
    }

    /// <summary>
    /// Generate C++ default value for a type.
    /// </summary>
    public static string GetDefaultValue(string typeName)
    {
        return typeName switch
        {
            // IL type names
            "System.Boolean" => "false",
            "System.Byte" or "System.SByte" or
            "System.Int16" or "System.UInt16" or
            "System.Int32" or "System.UInt32" or
            "System.Int64" or "System.UInt64" or
            "System.IntPtr" or "System.UIntPtr" => "0",
            "System.Single" => "0.0f",
            "System.Double" => "0.0",
            "System.Char" => "u'\\0'",
            // C++ type names
            "bool" => "false",
            "uint8_t" or "int8_t" or
            "int16_t" or "uint16_t" or
            "int32_t" or "uint32_t" or
            "int64_t" or "uint64_t" or
            "intptr_t" or "uintptr_t" => "0",
            "float" => "0.0f",
            "double" => "0.0",
            "char16_t" => "u'\\0'",
            _ => _userValueTypes.Contains(typeName) ? "{}" : "nullptr"
        };
    }
}
