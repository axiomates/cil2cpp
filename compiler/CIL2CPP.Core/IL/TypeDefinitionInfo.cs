using Mono.Cecil;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Represents type information extracted from IL.
/// </summary>
public class TypeDefinitionInfo
{
    private readonly TypeDefinition _type;

    public string Name => _type.Name;
    public string Namespace => _type.Namespace;
    public string FullName => _type.FullName;
    public string? BaseTypeName => _type.BaseType?.FullName;

    public bool IsClass => _type.IsClass;
    public bool IsInterface => _type.IsInterface;
    // Cecil's IsValueType can fail for compiler-generated structs (e.g. async state machines)
    // when the BCL assembly isn't loaded. Fall back to checking BaseType.FullName.
    public bool IsValueType => _type.IsValueType
        || _type.BaseType?.FullName == "System.ValueType";
    public bool IsEnum => _type.IsEnum;
    public bool IsAbstract => _type.IsAbstract;
    public bool IsSealed => _type.IsSealed;
    public bool IsPublic => _type.IsPublic;
    public bool IsNested => _type.IsNested;

    /// <summary>For enum types, the underlying integer type name.</summary>
    public string? EnumUnderlyingType => _type.IsEnum
        ? _type.Fields.FirstOrDefault(f => f.Name == "value__")?.FieldType.FullName
        : null;

    public bool HasGenericParameters => _type.HasGenericParameters;
    public IReadOnlyList<string> GenericParameterNames =>
        _type.GenericParameters.Select(p => p.Name).ToList();

    public IReadOnlyList<FieldInfo> Fields { get; }
    public IReadOnlyList<MethodInfo> Methods { get; }
    public IReadOnlyList<string> InterfaceNames { get; }

    public TypeDefinitionInfo(TypeDefinition type)
    {
        _type = type;

        Fields = type.Fields
            .Select(f => new FieldInfo(f))
            .ToList();

        Methods = type.Methods
            .Select(m => new MethodInfo(m))
            .ToList();

        InterfaceNames = type.Interfaces
            .Select(i => i.InterfaceType.FullName)
            .ToList();
    }

    /// <summary>
    /// Gets the Cecil TypeDefinition for advanced operations.
    /// </summary>
    internal TypeDefinition GetCecilType() => _type;
}

/// <summary>
/// Represents field information extracted from IL.
/// </summary>
public class FieldInfo
{
    private readonly FieldDefinition _field;

    public string Name => _field.Name;
    public string TypeName => _field.FieldType.FullName;
    public bool IsStatic => _field.IsStatic;
    public bool IsPublic => _field.IsPublic;
    public bool IsPrivate => _field.IsPrivate;
    public bool IsInitOnly => _field.IsInitOnly;
    public object? ConstantValue => _field.HasConstant ? _field.Constant : null;

    public FieldInfo(FieldDefinition field)
    {
        _field = field;
    }

    internal FieldDefinition GetCecilField() => _field;
}

/// <summary>
/// Represents method information extracted from IL.
/// </summary>
public class MethodInfo
{
    private readonly MethodDefinition _method;

    public string Name => _method.Name;
    public string ReturnTypeName => _method.ReturnType.FullName;
    public bool IsStatic => _method.IsStatic;
    public bool IsPublic => _method.IsPublic;
    public bool IsPrivate => _method.IsPrivate;
    public bool IsVirtual => _method.IsVirtual;
    public bool IsAbstract => _method.IsAbstract;
    public bool IsNewSlot => _method.IsNewSlot;
    public bool IsConstructor => _method.IsConstructor;
    public bool HasBody => _method.HasBody;

    public bool IsInternalCall => (_method.ImplAttributes & Mono.Cecil.MethodImplAttributes.InternalCall) != 0;
    public bool HasGenericParameters => _method.HasGenericParameters;
    public IReadOnlyList<string> GenericParameterNames =>
        _method.GenericParameters.Select(p => p.Name).ToList();

    public IReadOnlyList<ParameterInfo> Parameters { get; }

    public MethodInfo(MethodDefinition method)
    {
        _method = method;
        Parameters = method.Parameters
            .Select(p => new ParameterInfo(p))
            .ToList();
    }

    /// <summary>
    /// Gets the IL instructions of this method.
    /// </summary>
    public IEnumerable<ILInstruction> GetInstructions()
    {
        if (!_method.HasBody)
            yield break;

        foreach (var instr in _method.Body.Instructions)
        {
            yield return new ILInstruction(instr);
        }
    }

    /// <summary>
    /// Gets local variables defined in this method.
    /// </summary>
    public IEnumerable<LocalVariableInfo> GetLocalVariables()
    {
        if (!_method.HasBody)
            yield break;

        foreach (var local in _method.Body.Variables)
        {
            yield return new LocalVariableInfo(local);
        }
    }

    /// <summary>
    /// Gets the sequence points for this method (debug symbols required).
    /// Returns empty if no debug symbols are available.
    /// </summary>
    public IReadOnlyList<SequencePointInfo> GetSequencePoints()
    {
        if (!_method.HasBody || !_method.DebugInformation.HasSequencePoints)
            return Array.Empty<SequencePointInfo>();

        return _method.DebugInformation.SequencePoints
            .Select(sp => new SequencePointInfo(sp))
            .ToList();
    }

    /// <summary>
    /// Whether this method has exception handlers.
    /// </summary>
    public bool HasExceptionHandlers =>
        _method.HasBody && _method.Body.HasExceptionHandlers;

    /// <summary>
    /// Gets the exception handlers for this method.
    /// </summary>
    public IReadOnlyList<ExceptionHandlerInfo> GetExceptionHandlers()
    {
        if (!HasExceptionHandlers)
            return Array.Empty<ExceptionHandlerInfo>();
        return _method.Body.ExceptionHandlers
            .Select(h => new ExceptionHandlerInfo(h))
            .ToList();
    }

    internal MethodDefinition GetCecilMethod() => _method;
}

/// <summary>
/// Represents an exception handler region from IL metadata.
/// </summary>
public class ExceptionHandlerInfo
{
    private readonly Mono.Cecil.Cil.ExceptionHandler _handler;

    public Mono.Cecil.Cil.ExceptionHandlerType HandlerType => _handler.HandlerType;
    public int TryStart => _handler.TryStart?.Offset ?? 0;
    public int TryEnd => _handler.TryEnd?.Offset ?? int.MaxValue;
    public int HandlerStart => _handler.HandlerStart?.Offset ?? 0;
    public int HandlerEnd => _handler.HandlerEnd?.Offset ?? int.MaxValue;
    public string? CatchTypeName => _handler.CatchType?.FullName;

    public ExceptionHandlerInfo(Mono.Cecil.Cil.ExceptionHandler handler)
    {
        _handler = handler;
    }
}

/// <summary>
/// Represents parameter information.
/// </summary>
public class ParameterInfo
{
    public string Name { get; }
    public string TypeName { get; }
    public int Index { get; }

    public ParameterInfo(ParameterDefinition param)
    {
        Name = param.Name;
        TypeName = param.ParameterType.FullName;
        Index = param.Index;
    }
}

/// <summary>
/// Represents local variable information.
/// </summary>
public class LocalVariableInfo
{
    public int Index { get; }
    public string TypeName { get; }

    public LocalVariableInfo(Mono.Cecil.Cil.VariableDefinition variable)
    {
        Index = variable.Index;
        TypeName = variable.VariableType.FullName;
    }
}
