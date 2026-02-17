using Mono.Cecil;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    /// <summary>
    /// Collect custom attributes from a Cecil ICustomAttributeProvider.
    /// Supports primitive, string, Type, enum, and array constructor arguments.
    /// </summary>
    private static List<IRCustomAttribute> CollectAttributes(ICustomAttributeProvider provider)
    {
        var result = new List<IRCustomAttribute>();
        if (!provider.HasCustomAttributes) return result;

        foreach (var attr in provider.CustomAttributes)
        {
            // Skip compiler-generated attributes that aren't useful for reflection
            var attrTypeName = attr.AttributeType.FullName;
            if (IsCompilerInternalAttribute(attrTypeName)) continue;

            var irAttr = new IRCustomAttribute
            {
                AttributeTypeName = attrTypeName,
                AttributeTypeCppName = CppNameMapper.MangleTypeName(attrTypeName),
            };

            // Collect constructor arguments
            if (attr.HasConstructorArguments)
            {
                foreach (var arg in attr.ConstructorArguments)
                {
                    var irArg = ConvertAttributeArg(arg);
                    if (irArg != null)
                        irAttr.ConstructorArgs.Add(irArg);
                }
            }

            result.Add(irAttr);
        }

        return result;
    }

    /// <summary>
    /// Convert a Cecil CustomAttributeArgument to an IRAttributeArg.
    /// Returns null if the argument type is unsupported.
    /// </summary>
    private static IRAttributeArg? ConvertAttributeArg(CustomAttributeArgument arg)
    {
        var typeName = arg.Type.FullName;

        // Type argument: typeof(T)
        if (typeName == "System.Type" && arg.Value is TypeReference typeRef)
        {
            return new IRAttributeArg
            {
                TypeName = "System.Type",
                Kind = AttributeArgKind.Type,
                Value = typeRef.FullName,
            };
        }

        // Array argument
        if (arg.Type is ArrayType arrayType && arg.Value is CustomAttributeArgument[] elements)
        {
            var irArg = new IRAttributeArg
            {
                TypeName = arrayType.FullName,
                Kind = AttributeArgKind.Array,
                ArrayElements = new List<IRAttributeArg>(),
            };
            foreach (var elem in elements)
            {
                var irElem = ConvertAttributeArg(elem);
                if (irElem != null)
                    irArg.ArrayElements.Add(irElem);
            }
            return irArg;
        }

        // Enum argument: resolve checks if the type is an enum
        var resolvedType = TryResolveType(arg.Type);
        if (resolvedType is { IsEnum: true })
        {
            return new IRAttributeArg
            {
                TypeName = typeName,
                Kind = AttributeArgKind.Enum,
                Value = Convert.ToInt64(arg.Value),
            };
        }

        // Primitive / string
        return typeName switch
        {
            "System.String" => new IRAttributeArg
            {
                TypeName = typeName,
                Kind = AttributeArgKind.String,
                Value = arg.Value,
            },
            "System.Single" or "System.Double" => new IRAttributeArg
            {
                TypeName = typeName,
                Kind = AttributeArgKind.Float,
                Value = arg.Value,
            },
            "System.Boolean" or "System.Byte" or "System.SByte" or
            "System.Int16" or "System.UInt16" or
            "System.Int32" or "System.UInt32" or
            "System.Int64" or "System.UInt64" or
            "System.Char" => new IRAttributeArg
            {
                TypeName = typeName,
                Kind = AttributeArgKind.Int,
                Value = arg.Value,
            },
            _ => null, // unsupported type
        };
    }

    /// <summary>
    /// Try to resolve a TypeReference to a TypeDefinition (for enum detection).
    /// Returns null if resolution fails.
    /// </summary>
    private static TypeDefinition? TryResolveType(TypeReference typeRef)
    {
        try { return typeRef.Resolve(); }
        catch { return null; }
    }

    /// <summary>
    /// Check if an attribute type name is a compiler-internal attribute
    /// that should not be exposed through reflection.
    /// </summary>
    private static bool IsCompilerInternalAttribute(string attrTypeName) => attrTypeName switch
    {
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute" => true,
        "System.Runtime.CompilerServices.NullableAttribute" => true,
        "System.Runtime.CompilerServices.NullableContextAttribute" => true,
        "System.Runtime.CompilerServices.IsReadOnlyAttribute" => true,
        "System.Runtime.CompilerServices.IsByRefLikeAttribute" => true,
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute" => true,
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute" => true,
        "System.Diagnostics.CodeAnalysis.ScopedRefAttribute" => true,
        "System.ParamArrayAttribute" => true,
        "Microsoft.CodeAnalysis.EmbeddedAttribute" => true,
        _ => false,
    };

    /// <summary>
    /// Populate custom attributes on all IR types, methods, and fields.
    /// Called as a separate pass after type shells and method shells are created.
    /// </summary>
    private void PopulateCustomAttributes()
    {
        foreach (var typeDef in _allTypes!)
        {
            if (typeDef.HasGenericParameters) continue;

            var cecilType = typeDef.GetCecilType();
            if (cecilType == null) continue;

            if (!_typeCache.TryGetValue(typeDef.FullName, out var irType)) continue;

            // Type attributes
            irType.CustomAttributes.AddRange(CollectAttributes(cecilType));

            // Field attributes
            foreach (var field in cecilType.Fields)
            {
                var irField = irType.Fields.FirstOrDefault(f => f.Name == field.Name)
                    ?? irType.StaticFields.FirstOrDefault(f => f.Name == field.Name);
                if (irField != null)
                {
                    irField.CustomAttributes.AddRange(CollectAttributes(field));
                }
            }

            // Method attributes
            foreach (var method in cecilType.Methods)
            {
                var irMethod = irType.Methods.FirstOrDefault(m =>
                    m.Name == method.Name && m.Parameters.Count == method.Parameters.Count);
                if (irMethod != null)
                {
                    irMethod.CustomAttributes.AddRange(CollectAttributes(method));
                }
            }
        }
    }
}
