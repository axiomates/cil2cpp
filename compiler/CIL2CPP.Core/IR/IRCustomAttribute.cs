namespace CIL2CPP.Core.IR;

/// <summary>
/// Represents a custom attribute applied to a type, method, or field.
/// </summary>
public class IRCustomAttribute
{
    /// <summary>Full IL name of the attribute type (e.g., "System.ObsoleteAttribute")</summary>
    public string AttributeTypeName { get; set; } = "";

    /// <summary>C++ mangled name of the attribute type</summary>
    public string AttributeTypeCppName { get; set; } = "";

    /// <summary>Constructor arguments</summary>
    public List<IRAttributeArg> ConstructorArgs { get; } = new();
}

/// <summary>
/// The kind of value stored in a custom attribute argument.
/// </summary>
public enum AttributeArgKind
{
    /// <summary>Primitive integer type (bool, byte, char, int16..int64)</summary>
    Int,
    /// <summary>Floating-point type (float, double)</summary>
    Float,
    /// <summary>String</summary>
    String,
    /// <summary>Type reference (typeof(T)) — stored as type name string</summary>
    Type,
    /// <summary>Enum value — stored as underlying integer</summary>
    Enum,
    /// <summary>Array of attribute arguments</summary>
    Array,
}

/// <summary>
/// Represents a single constructor argument of a custom attribute.
/// Supports primitives, strings, Type, enums, and arrays.
/// </summary>
public class IRAttributeArg
{
    /// <summary>IL type name of the argument (e.g., "System.String", "System.Type")</summary>
    public string TypeName { get; set; } = "";

    /// <summary>The kind of value stored</summary>
    public AttributeArgKind Kind { get; set; }

    /// <summary>The argument value (null, string, or boxed primitive/enum)</summary>
    public object? Value { get; set; }

    /// <summary>Array elements (only when Kind == Array)</summary>
    public List<IRAttributeArg>? ArrayElements { get; set; }
}
