namespace CIL2CPP.Core.IR;

/// <summary>
/// Maps .NET type and member names to valid C++ identifiers.
/// </summary>
public static class CppNameMapper
{
    // User-defined value types (structs and enums) registered during IR build.
    // Guarded by _vtLock because IRBuilder.Build() writes (Add/Clear) while
    // CppCodeGenerator.Generate() may read (Contains) from a different thread.
    private static readonly HashSet<string> _userValueTypes = new();
    private static readonly object _vtLock = new();

    public static void RegisterValueType(string ilTypeName)
    {
        lock (_vtLock) _userValueTypes.Add(ilTypeName);
    }

    public static void ClearValueTypes()
    {
        lock (_vtLock) _userValueTypes.Clear();
    }

    internal static bool IsRegisteredValueType(string typeName)
    {
        lock (_vtLock) return _userValueTypes.Contains(typeName);
    }

    /// <summary>
    /// Maps BCL exception IL type names to runtime C++ type names.
    /// These types are defined in the runtime (exception.h), not in generated code.
    /// </summary>
    private static readonly Dictionary<string, string> RuntimeExceptionTypeMap = new()
    {
        // Base
        ["System.Exception"] = "cil2cpp::Exception",
        // SystemException hierarchy
        ["System.NullReferenceException"] = "cil2cpp::NullReferenceException",
        ["System.IndexOutOfRangeException"] = "cil2cpp::IndexOutOfRangeException",
        ["System.InvalidCastException"] = "cil2cpp::InvalidCastException",
        ["System.InvalidOperationException"] = "cil2cpp::InvalidOperationException",
        ["System.ObjectDisposedException"] = "cil2cpp::ObjectDisposedException",
        ["System.NotSupportedException"] = "cil2cpp::NotSupportedException",
        ["System.PlatformNotSupportedException"] = "cil2cpp::PlatformNotSupportedException",
        ["System.NotImplementedException"] = "cil2cpp::NotImplementedException",
        ["System.ArgumentException"] = "cil2cpp::ArgumentException",
        ["System.ArgumentNullException"] = "cil2cpp::ArgumentNullException",
        ["System.ArgumentOutOfRangeException"] = "cil2cpp::ArgumentOutOfRangeException",
        ["System.ArithmeticException"] = "cil2cpp::ArithmeticException",
        ["System.OverflowException"] = "cil2cpp::OverflowException",
        ["System.DivideByZeroException"] = "cil2cpp::DivideByZeroException",
        ["System.FormatException"] = "cil2cpp::FormatException",
        ["System.RankException"] = "cil2cpp::RankException",
        ["System.ArrayTypeMismatchException"] = "cil2cpp::ArrayTypeMismatchException",
        ["System.TypeInitializationException"] = "cil2cpp::TypeInitializationException",
        ["System.TimeoutException"] = "cil2cpp::TimeoutException",
        // Task-related
        ["System.AggregateException"] = "cil2cpp::AggregateException",
        ["System.OperationCanceledException"] = "cil2cpp::OperationCanceledException",
        ["System.Threading.Tasks.TaskCanceledException"] = "cil2cpp::TaskCanceledException",
        // Collections
        ["System.Collections.Generic.KeyNotFoundException"] = "cil2cpp::KeyNotFoundException",
        // IO
        ["System.IO.IOException"] = "cil2cpp::IOException",
        ["System.IO.FileNotFoundException"] = "cil2cpp::FileNotFoundException",
        ["System.IO.DirectoryNotFoundException"] = "cil2cpp::DirectoryNotFoundException",
    };

    /// <summary>
    /// Check if a type name is a BCL exception type provided by the runtime.
    /// </summary>
    public static bool IsRuntimeExceptionType(string ilTypeName)
        => RuntimeExceptionTypeMap.ContainsKey(ilTypeName);

    /// <summary>
    /// Get the runtime C++ name for a BCL exception type, or null if not a runtime exception.
    /// </summary>
    public static string? GetRuntimeExceptionCppName(string ilTypeName)
        => RuntimeExceptionTypeMap.TryGetValue(ilTypeName, out var name) ? name : null;

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
        if (ilTypeName is "System.Boolean" or "System.Byte" or "System.SByte" or
            "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or
            "System.Int64" or "System.UInt64" or "System.Single" or "System.Double" or
            "System.Char" or "System.IntPtr" or "System.UIntPtr")
            return true;
        lock (_vtLock)
        {
            if (_userValueTypes.Contains(ilTypeName))
                return true;
            // For generic instantiations (e.g. "System.Span`1<System.Byte>"),
            // check if the open generic type is a registered value type
            var angleBracket = ilTypeName.IndexOf('<');
            if (angleBracket > 0 && _userValueTypes.Contains(ilTypeName[..angleBracket]))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get the C++ type name for a .NET type.
    /// </summary>
    public static string GetCppTypeName(string ilTypeName, bool isPointer = false)
    {
        // Strip modreq/modopt (e.g. "System.Void modreq(IsExternalInit)" → "System.Void")
        var modIdx = ilTypeName.IndexOf(" modreq(", StringComparison.Ordinal);
        if (modIdx < 0) modIdx = ilTypeName.IndexOf(" modopt(", StringComparison.Ordinal);
        if (modIdx >= 0) ilTypeName = ilTypeName[..modIdx];

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

        // Handle array types (single-dim and multi-dim)
        if (ilTypeName.EndsWith("[]"))
        {
            return "cil2cpp::Array*";
        }
        // Multi-dimensional arrays: T[0...,0...] or T[,] → MdArray*
        // Must check only the LAST bracket section to avoid matching commas in generic args
        // e.g., "SharedArrayPool`1/ThreadLocalArray`1<System.Byte>[]" has comma in generic args
        if (ilTypeName.EndsWith("]") && ilTypeName.Contains("["))
        {
            var lastBracket = ilTypeName.LastIndexOf('[');
            var section = ilTypeName[lastBracket..];
            if (section.Contains(',') || section.Contains("..."))
                return "cil2cpp::MdArray*";
        }

        // Primitive types
        if (PrimitiveTypeMap.TryGetValue(ilTypeName, out var cppName))
        {
            if (!IsValueType(ilTypeName) && !isPointer && ilTypeName != "System.Void")
                return cppName + "*";
            return cppName;
        }

        // Runtime exception types — use cil2cpp:: names for type declarations.
        // (MangleTypeName returns flat identifiers; this returns C++ qualified names.)
        if (RuntimeExceptionTypeMap.TryGetValue(ilTypeName, out var runtimeExcName))
            return runtimeExcName;

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
        // Strip modreq/modopt early (e.g. init-only setters: "System.Void modreq(IsExternalInit)")
        var modIdx = ilTypeName.IndexOf(" modreq(", StringComparison.Ordinal);
        if (modIdx < 0) modIdx = ilTypeName.IndexOf(" modopt(", StringComparison.Ordinal);
        if (modIdx >= 0) ilTypeName = ilTypeName[..modIdx];

        if (ilTypeName == "System.Void") return "void";

        // Handle ByReference types (ref/out) — recurse on element type + add pointer.
        // Critical for ref T where T is a reference type: ref Encoding → Encoding** (not Encoding*)
        if (ilTypeName.EndsWith("&"))
            return GetCppTypeForDecl(ilTypeName[..^1]) + "*";

        // Handle pointer types — recurse on element type + add pointer.
        if (ilTypeName.EndsWith("*"))
            return GetCppTypeForDecl(ilTypeName[..^1]) + "*";

        if (IsValueType(ilTypeName))
            return GetCppTypeName(ilTypeName);

        var cppType = GetCppTypeName(ilTypeName);
        if (!cppType.EndsWith("*"))
            return cppType + "*";
        return cppType;
    }

    /// <summary>
    /// Mangle a .NET type name into a valid C++ identifier.
    /// Always produces flat identifiers (no :: or other non-identifier characters).
    /// For type declarations (where cil2cpp::Exception* is needed), use GetCppTypeName() instead.
    /// </summary>
    public static string MangleTypeName(string ilFullName)
    {
        return ilFullName
            .Replace(".", "_")
            .Replace("/", "_")  // Nested types
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace("&", "_ref") // ByReference types (ref/out params)
            .Replace("*", "_ptr") // Pointer types (void*, int*)
            .Replace("`", "_")
            .Replace(" ", "")
            .Replace("+", "_")
            .Replace("=", "_")  // e.g. __StaticArrayInitTypeSize=20
            .Replace("-", "_")
            .Replace("[", "_")  // Array types (e.g., System.String[])
            .Replace("]", "_")
            .Replace("|", "_"); // Local function names (e.g., g__Func|42_0)
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
    /// Mangle a type name to a valid C++ identifier, correctly handling generic instance syntax.
    /// Unlike MangleTypeName which blindly replaces all special chars (producing trailing underscore
    /// from closing '>'), this method detects generic instances and uses MangleGenericInstanceTypeName
    /// to produce the correct name. Use this for template arguments (box/unbox) and TypeInfo references.
    /// </summary>
    public static string MangleTypeNameClean(string ilFullName)
    {
        // Generic instance: Foo`N<Arg1,Arg2,...> — parse and use MangleGenericInstanceTypeName
        var backtickIdx = ilFullName.IndexOf('`');
        if (backtickIdx > 0 && ilFullName.Contains('<') && ilFullName.EndsWith(">"))
        {
            var angleBracket = ilFullName.IndexOf('<');
            var openTypeName = ilFullName[..angleBracket];
            var argsStr = ilFullName[(angleBracket + 1)..^1];
            var args = ParseGenericArgs(argsStr);
            // Recursively clean each arg to handle nested generics like List<Dictionary<K,V>>
            var cleanArgs = args.Select(MangleTypeNameClean).ToList();
            var baseName = MangleTypeName(openTypeName);
            return $"{baseName}_{string.Join("_", cleanArgs)}";
        }
        return MangleTypeName(ilFullName);
    }

    /// <summary>
    /// Parse generic type arguments, handling nested angle brackets.
    /// E.g., "String,List`1&lt;Int32&gt;" → ["String", "List`1&lt;Int32&gt;"]
    /// </summary>
    private static List<string> ParseGenericArgs(string argsStr)
    {
        var args = new List<string>();
        var depth = 0;
        var start = 0;
        for (int i = 0; i < argsStr.Length; i++)
        {
            if (argsStr[i] == '<') depth++;
            else if (argsStr[i] == '>') depth--;
            else if (argsStr[i] == ',' && depth == 0)
            {
                args.Add(argsStr[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < argsStr.Length)
            args.Add(argsStr[start..].Trim());
        return args;
    }

    /// <summary>
    /// Whether a type is a compiler-generated implementation detail (e.g. &lt;PrivateImplementationDetails&gt;).
    /// These should be filtered from C++ code generation.
    /// </summary>
    public static bool IsCompilerGeneratedType(string ilFullName)
    {
        // <PrivateImplementationDetails> is NOT filtered — BCL code uses it for
        // static array initialization data that we need when compiling BCL IL.
        // Previously filtered when BCL was hand-mapped, now needed.
        return false;
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
            .Replace(",", "_")
            .Replace("`", "_")
            .Replace(" ", "")
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
    /// Mangle an arbitrary identifier (parameter name, local name) into a valid C++ identifier.
    /// </summary>
    /// <summary>
    /// C++ keywords and alternative operator tokens that cannot be used as identifiers.
    /// </summary>
    private static readonly HashSet<string> CppKeywords = new()
    {
        "and", "and_eq", "bitand", "bitor", "compl", "not", "not_eq",
        "or", "or_eq", "xor", "xor_eq",
        "alignas", "alignof", "asm", "auto", "bool", "break", "case", "catch",
        "char", "class", "const", "constexpr", "continue", "default", "delete",
        "do", "double", "dynamic_cast", "else", "enum", "explicit", "export",
        "extern", "false", "float", "for", "friend", "goto", "if", "inline",
        "int", "long", "mutable", "namespace", "new", "noexcept", "nullptr",
        "operator", "private", "protected", "public", "register", "return",
        "short", "signed", "sizeof", "static", "struct", "switch", "template",
        "this", "throw", "true", "try", "typedef", "typeid", "typename",
        "union", "unsigned", "using", "virtual", "void", "volatile", "while",
    };

    public static string MangleIdentifier(string name)
    {
        var mangled = name.Replace("<", "_").Replace(">", "_").Replace(".", "_");
        if (CppKeywords.Contains(mangled))
            mangled = mangled + "_";  // suffix to avoid _asm/__asm MSVC conflicts
        return mangled;
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
            _ => IsRegisteredValueType(typeName) ? "{}" : "nullptr"
        };
    }
}
