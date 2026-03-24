using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Generic parameter variance (ECMA-335 II.9.11).
/// </summary>
public enum GenericVariance : byte
{
    Invariant = 0,
    Covariant = 1,      // out T — only in return positions
    Contravariant = 2,  // in T — only in parameter positions
}

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

    /// <summary>Properties (for reflection metadata emission)</summary>
    public List<IRProperty> Properties { get; } = new();

    /// <summary>Virtual method table</summary>
    public List<IRVTableEntry> VTable { get; } = new();

    /// <summary>Interface implementation vtables (for concrete types)</summary>
    public List<IRInterfaceImpl> InterfaceImpls { get; } = new();

    /// <summary>Calculated object size in bytes (includes trailing alignment padding)</summary>
    public int InstanceSize { get; set; }

    /// <summary>
    /// Managed field size in bytes (no trailing alignment padding).
    /// Used by initobj/cpobj to match ECMA-335 semantics where initobj zeros
    /// exactly the managed size, not the C++ sizeof (which includes padding).
    /// </summary>
    public int ManagedFieldSize { get; set; }

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

    /// <summary>
    /// For generic sharing: the canonical type whose sharable methods this type reuses.
    /// Non-null means this type is a "shared" type (struct is a C++ using alias).
    /// </summary>
    public IRType? CanonicalType { get; set; }

    /// <summary>
    /// True if this type IS the canonical representative for __Canon generic sharing.
    /// Its sharable method bodies are shared by all types with CanonicalType pointing here.
    /// </summary>
    public bool IsCanonicalInstance { get; set; }

    /// <summary>True if this type shares struct layout and sharable methods with a canonical type.</summary>
    public bool HasCanonicalSharing => CanonicalType != null;
    /// <summary>
    /// True if this type is a CLR primitive (System.Byte, System.Int32, etc.)
    /// that maps to a C++ built-in type. Struct definition is NOT emitted,
    /// but methods ARE compiled from BCL IL.
    /// </summary>
    public bool IsPrimitiveType { get; set; }
    public bool IsPublic { get; set; }
    public bool IsNestedPublic { get; set; }
    public bool IsNotPublic { get; set; }
    public bool IsNestedAssembly { get; set; }
    public bool IsByRefLike { get; set; }

    /// <summary>
    /// Character set from [StructLayout(CharSet=...)] for P/Invoke struct marshaling.
    /// Determines ByValTStr char type: Unicode → char16_t, Ansi → char.
    /// </summary>
    public PInvokeCharSet StructCharSet { get; set; } = PInvokeCharSet.Ansi;

    /// <summary>For array types: the C++ mangled name of the element type (e.g., "System_Int32")</summary>
    public string? ArrayElementTypeCppName { get; set; }

    /// <summary>ECMA-335 metadata token (from Cecil MetadataToken.ToUInt32())</summary>
    public uint MetadataToken { get; set; }

    /// <summary>Concrete type argument names for generic instances (e.g., ["System.Int32"])</summary>
    public List<string> GenericArguments { get; set; } = new();

    /// <summary>Generic parameter variances for open generic types (Covariant, Contravariant, Invariant).</summary>
    public List<GenericVariance> GenericParameterVariances { get; } = new();

    /// <summary>For generic instances: the open generic type's CppName (e.g., "System_IEnumerable_1").</summary>
    public string? GenericDefinitionCppName { get; set; }

    /// <summary>For generic instances: the open generic type's IL full name (e.g., "System.Collections.Generic.IEnumerable`1").</summary>
    public string? GenericDefinitionILName { get; set; }

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
    /// Explicit struct size in bytes from [StructLayout(Size = N)] / ClassSize metadata.
    /// Used for fixed-size buffers (InlineArray) where ClassSize > sum of fields.
    /// Zero means no explicit size (normal layout).
    /// </summary>
    public int ExplicitSize { get; set; }

    /// <summary>
    /// True if this type uses LayoutKind.Explicit (ECMA-335 II.10.1.2).
    /// Fields have explicit byte offsets and may overlap (unions).
    /// </summary>
    public bool IsExplicitLayout { get; set; }

    /// <summary>
    /// True if this type has [InlineArray(N)] attribute (C# inline array).
    /// The struct has a single field repeated N times; ExplicitSize = N * sizeof(element).
    /// </summary>
    public bool IsInlineArray { get; set; }

    /// <summary>Custom attributes applied to this type</summary>
    public List<IRCustomAttribute> CustomAttributes { get; } = new();

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

    /// <summary>Raw ECMA-335 FieldAttributes value (II.23.1.5)</summary>
    public uint Attributes { get; set; }

    /// <summary>Custom attributes applied to this field</summary>
    public List<IRCustomAttribute> CustomAttributes { get; } = new();

    /// <summary>
    /// True when this field hides (shadows) a base class field with the same name.
    /// C# `new` keyword on auto-properties creates a separate backing field with the same name
    /// but different type. Both fields coexist in the object layout at different offsets.
    /// The CppName is disambiguated with a "__own" suffix to avoid name collisions in C++ structs.
    /// </summary>
    public bool HidesBaseField { get; set; }

    /// <summary>
    /// Explicit field offset in bytes from [FieldOffset(N)] for LayoutKind.Explicit types.
    /// Null means sequential layout (offset auto-computed).
    /// </summary>
    public int? ExplicitOffset { get; set; }

    /// <summary>[MarshalAs] unmanaged type for P/Invoke struct field marshaling (ByValTStr, ByValArray)</summary>
    public MarshalAsType? MarshalAs { get; set; }

    /// <summary>SizeConst from [MarshalAs] — element count for ByValArray or char count for ByValTStr</summary>
    public int MarshalSizeConst { get; set; }

    /// <summary>Element type IL name for ByValArray (from Cecil FixedArrayMarshalInfo.ElementType)</summary>
    public string? MarshalElementTypeName { get; set; }
}

/// <summary>
/// Represents a property in the IR (for reflection metadata).
/// </summary>
public class IRProperty
{
    public string Name { get; set; } = "";
    public string PropertyTypeName { get; set; } = "";  // IL full type name
    public IRMethod? Getter { get; set; }
    public IRMethod? Setter { get; set; }
    public uint Attributes { get; set; }
    public IRType? DeclaringType { get; set; }
    public List<IRCustomAttribute> CustomAttributes { get; } = new();
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
