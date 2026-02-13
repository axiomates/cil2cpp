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
}

public class IRStringLiteral
{
    public string Id { get; set; } = "";
    public string Value { get; set; } = "";
}
