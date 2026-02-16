namespace CIL2CPP.Core.IR;

/// <summary>
/// Represents an entire compiled module (assembly) in IR form.
/// </summary>
public class IRModule
{
    public string Name { get; set; } = "";
    public List<IRType> Types { get; } = new();
    public Dictionary<string, IRStringLiteral> StringLiterals { get; } = new();
    public IRMethod? EntryPoint { get; set; }

    /// <summary>
    /// Get all methods across all types.
    /// </summary>
    public IEnumerable<IRMethod> GetAllMethods()
    {
        return Types.SelectMany(t => t.Methods);
    }

    /// <summary>
    /// Find a type by its full .NET name.
    /// </summary>
    public IRType? FindType(string fullName)
    {
        return Types.FirstOrDefault(t => t.ILFullName == fullName);
    }

    /// <summary>
    /// Register a string literal and return its ID.
    /// </summary>
    public string RegisterStringLiteral(string value)
    {
        if (!StringLiterals.ContainsKey(value))
        {
            var id = $"__str_{StringLiterals.Count}";
            StringLiterals[value] = new IRStringLiteral { Id = id, Value = value };
        }
        return StringLiterals[value].Id;
    }

    public List<IRArrayInitData> ArrayInitDataBlobs { get; } = new();

    /// <summary>
    /// Register a static byte blob for array initialization (RuntimeHelpers.InitializeArray).
    /// Returns the C++ identifier for the static data array.
    /// </summary>
    public string RegisterArrayInitData(byte[] data)
    {
        var id = $"__arr_init_{ArrayInitDataBlobs.Count}";
        ArrayInitDataBlobs.Add(new IRArrayInitData { Id = id, Data = data });
        return id;
    }

    /// <summary>
    /// Primitive types that need TypeInfo definitions for array element types.
    /// Key: IL full name (e.g. "System.Int32"), Value: (CppMangled, CppType, ElementSize expression).
    /// </summary>
    public Dictionary<string, PrimitiveTypeInfoEntry> PrimitiveTypeInfos { get; } = new();

    /// <summary>
    /// Disambiguated method names for overloaded methods whose C++ names would collide.
    /// Key: "OriginalCppName|param1CppType,param2CppType", Value: disambiguated C++ name.
    /// </summary>
    public Dictionary<string, string> DisambiguatedMethodNames { get; } = new();

    /// <summary>
    /// Enum types referenced in method signatures but not in the IR module.
    /// Key: C++ mangled name, Value: C++ underlying type (e.g. "int32_t", "uint8_t").
    /// These need <c>using X = int32_t;</c> in the header instead of <c>struct X;</c>.
    /// </summary>
    public Dictionary<string, string> ExternalEnumTypes { get; } = new();

    /// <summary>
    /// Register a primitive type that needs a TypeInfo (used as array element type).
    /// </summary>
    public void RegisterPrimitiveTypeInfo(string ilFullName)
    {
        if (PrimitiveTypeInfos.ContainsKey(ilFullName)) return;
        var cppMangled = CppNameMapper.MangleTypeName(ilFullName);
        var cppType = CppNameMapper.GetCppTypeName(ilFullName);
        PrimitiveTypeInfos[ilFullName] = new PrimitiveTypeInfoEntry
        {
            ILFullName = ilFullName,
            CppMangledName = cppMangled,
            CppTypeName = cppType
        };
    }
}

public class IRStringLiteral
{
    public string Id { get; set; } = "";
    public string Value { get; set; } = "";
}

public class IRArrayInitData
{
    public string Id { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public class PrimitiveTypeInfoEntry
{
    public string ILFullName { get; set; } = "";
    public string CppMangledName { get; set; } = "";
    public string CppTypeName { get; set; } = "";
}
