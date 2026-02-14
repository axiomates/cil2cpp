using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Represents a type in the IR.
/// </summary>
public class IRType
{
    /// <summary>Original .NET full name (e.g., "MyNamespace.MyClass")</summary>
    public string ILFullName { get; set; } = "";

    /// <summary>C++ mangled name (e.g., "MyNamespace_MyClass")</summary>
    public string CppName { get; set; } = "";

    /// <summary>Short name</summary>
    public string Name { get; set; } = "";

    /// <summary>Namespace</summary>
    public string Namespace { get; set; } = "";

    /// <summary>Base type (null for System.Object)</summary>
    public IRType? BaseType { get; set; }

    /// <summary>Implemented interfaces</summary>
    public List<IRType> Interfaces { get; } = new();

    /// <summary>Instance fields (in layout order)</summary>
    public List<IRField> Fields { get; } = new();

    /// <summary>Static fields</summary>
    public List<IRField> StaticFields { get; } = new();

    /// <summary>All methods</summary>
    public List<IRMethod> Methods { get; } = new();

    /// <summary>Virtual method table</summary>
    public List<IRVTableEntry> VTable { get; } = new();

    /// <summary>Interface implementation vtables (for concrete types)</summary>
    public List<IRInterfaceImpl> InterfaceImpls { get; } = new();

    /// <summary>Calculated object size in bytes</summary>
    public int InstanceSize { get; set; }

    // Type classification
    public bool IsValueType { get; set; }
    public bool IsInterface { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsSealed { get; set; }
    public bool IsEnum { get; set; }
    public bool HasCctor { get; set; }
    public bool IsDelegate { get; set; }
    public bool IsGenericInstance { get; set; }
    public bool IsRecord { get; set; }

    /// <summary>Concrete type argument names for generic instances (e.g., ["System.Int32"])</summary>
    public List<string> GenericArguments { get; set; } = new();

    /// <summary>Underlying integer type for enums (e.g., "System.Int32")</summary>
    public string? EnumUnderlyingType { get; set; }

    /// <summary>The Finalize() method, if this type has one.</summary>
    public IRMethod? Finalizer { get; set; }

    /// <summary>Assembly origin classification (User, ThirdParty, BCL).</summary>
    public AssemblyKind SourceKind { get; set; } = AssemblyKind.User;

    /// <summary>
    /// True if this type is already provided by the C++ runtime (e.g., System.Object, System.String).
    /// These types should not emit struct/method definitions in generated code.
    /// </summary>
    public bool IsRuntimeProvided { get; set; }

    /// <summary>
    /// Get the C++ type name for use in declarations.
    /// Value types are used directly, reference types as pointers.
    /// </summary>
    public string GetCppTypeName(bool asPointer = false)
    {
        if (IsValueType && !asPointer)
            return CppName;
        return CppName + "*";
    }
}

/// <summary>
/// Represents a field in the IR.
/// </summary>
public class IRField
{
    public string Name { get; set; } = "";
    public string CppName { get; set; } = "";
    public IRType? FieldType { get; set; }
    public string FieldTypeName { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool IsPublic { get; set; }
    public int Offset { get; set; }
    public IRType? DeclaringType { get; set; }
    public object? ConstantValue { get; set; }
}

/// <summary>
/// Virtual table entry.
/// </summary>
public class IRVTableEntry
{
    public int Slot { get; set; }
    public string MethodName { get; set; } = "";
    public IRMethod? Method { get; set; }
    public IRType? DeclaringType { get; set; }
}

/// <summary>
/// Maps an interface to the implementing methods for a concrete type.
/// </summary>
public class IRInterfaceImpl
{
    public IRType Interface { get; set; } = null!;
    public List<IRMethod?> MethodImpls { get; } = new();
}
