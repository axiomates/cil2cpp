using Mono.Cecil;
using Mono.Cecil.Cil;

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
                Value = arg.Value is ulong u ? unchecked((long)u) : Convert.ToInt64(arg.Value),
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

            // Property attributes
            foreach (var prop in cecilType.Properties)
            {
                var irProp = irType.Properties.FirstOrDefault(p => p.Name == prop.Name);
                if (irProp != null)
                {
                    irProp.CustomAttributes.AddRange(CollectAttributes(prop));
                }
            }
        }
    }

    /// <summary>
    /// Ensure attribute constructors referenced by custom attributes are compiled.
    /// Attribute constructors are never called from IL (they run at CLR metadata-time),
    /// so the reachability analyzer doesn't mark them. We need them compiled so that
    /// runtime _construct_attribute can call them via find_method_info.
    /// </summary>
    private void EnsureAttributeConstructorsCompiled()
    {
        // Collect unique (attribute type name, arg count) pairs from all custom attributes
        var neededCtors = new HashSet<(string TypeName, int ArgCount)>();

        foreach (var irType in _module.Types)
        {
            CollectAttributeCtorNeeds(irType.CustomAttributes, neededCtors);
            foreach (var field in irType.Fields.Concat(irType.StaticFields))
                CollectAttributeCtorNeeds(field.CustomAttributes, neededCtors);
            foreach (var method in irType.Methods)
                CollectAttributeCtorNeeds(method.CustomAttributes, neededCtors);
            foreach (var prop in irType.Properties)
                CollectAttributeCtorNeeds(prop.CustomAttributes, neededCtors);
        }

        if (neededCtors.Count == 0) return;

        int compiled = 0;
        foreach (var (attrTypeName, argCount) in neededCtors)
        {
            if (!_typeCache.TryGetValue(attrTypeName, out var attrIrType)) continue;

            // Find the .ctor method shell with matching parameter count
            var ctorMethod = attrIrType.Methods.FirstOrDefault(m =>
                m.Name == ".ctor" && m.Parameters.Count == argCount && m.BasicBlocks.Count == 0);
            if (ctorMethod == null) continue;

            // Find the Cecil TypeDefinition to get the constructor body
            var cecilTypeDef = _allTypes!
                .FirstOrDefault(t => t.FullName == attrTypeName)?.GetCecilType();
            if (cecilTypeDef == null) continue;

            // Find matching Cecil constructor
            var cecilCtor = cecilTypeDef.Methods.FirstOrDefault(m =>
                m.IsConstructor && !m.IsStatic && m.Parameters.Count == argCount && m.HasBody);
            if (cecilCtor == null) continue;

            // Skip constructors with CLR internal dependencies (references types
            // like MethodTable, EEType, etc. that don't exist in AOT)
            if (HasClrInternalDependencies(cecilCtor)) continue;

            // Skip attribute types defined in .NET framework assemblies.
            // Framework attribute constructors (e.g., [Obsolete], [EditorBrowsable]) may trigger
            // cctor chains on framework types that reference uninitialized TypeInfos at startup.
            // Only compile constructors for attributes from user code and NuGet libraries.
            if (IsFrameworkAssembly(cecilTypeDef.Module.Assembly)) continue;

            // Set up locals
            ctorMethod.Locals.Clear();
            if (cecilCtor.Body.HasVariables)
            {
                foreach (var localDef in cecilCtor.Body.Variables)
                {
                    ctorMethod.Locals.Add(new IRLocal
                    {
                        Index = localDef.Index,
                        CppName = $"loc_{localDef.Index}",
                        CppTypeName = ResolveTypeForDecl(
                            ResolveGenericTypeName(localDef.VariableType, new Dictionary<string, string>())),
                    });
                }
            }

            // Compile the body
            var methodInfo = new IL.MethodInfo(cecilCtor);
            ConvertMethodBody(methodInfo, ctorMethod);
            compiled++;

            // Compile transitive callees (e.g., property setters called from the constructor)
            CompileTransitiveUnreachableCallees(cecilCtor);
        }
    }

    private static void CollectAttributeCtorNeeds(
        IReadOnlyList<IRCustomAttribute> attrs,
        HashSet<(string TypeName, int ArgCount)> neededCtors)
    {
        foreach (var attr in attrs)
        {
            neededCtors.Add((attr.AttributeTypeName, attr.ConstructorArgs.Count));
        }
    }

    /// <summary>
    /// Check if a Cecil assembly is a .NET framework/runtime assembly by public key token.
    /// All framework assemblies use well-known Microsoft/ECMA public key tokens.
    /// Framework attribute constructors may trigger unsafe cctor chains at startup,
    /// so only user code and NuGet library attribute constructors are compiled.
    /// </summary>
    internal static bool IsFrameworkAssembly(Mono.Cecil.AssemblyDefinition assembly)
    {
        var token = assembly.Name.PublicKeyToken;
        if (token == null || token.Length == 0) return false;

        // .NET framework public key tokens (hex):
        // b03f5f7f11d50a3a — ECMA key (System.Runtime, System.Collections, etc.)
        // 7cec85d7bea7798e — System.Private.CoreLib
        // b77a5c561934e089 — mscorlib, System.*
        // 31bf3856ad364e35 — Microsoft.Extensions.*, System.ComponentModel.*
        // cc7b13ffcd2ddd51 — .NET platform assemblies
        return IsKnownFrameworkToken(token);
    }

    private static bool IsKnownFrameworkToken(byte[] token)
    {
        if (token.Length != 8) return false;
        // Compare against known tokens as byte arrays
        ReadOnlySpan<byte> t = token;
        return t.SequenceEqual(stackalloc byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a })  // ECMA
            || t.SequenceEqual(stackalloc byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e })  // CoreLib
            || t.SequenceEqual(stackalloc byte[] { 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89 })  // mscorlib
            || t.SequenceEqual(stackalloc byte[] { 0x31, 0xbf, 0x38, 0x56, 0xad, 0x36, 0x4e, 0x35 })  // MS Extensions
            || t.SequenceEqual(stackalloc byte[] { 0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51 }); // Platform
    }
}
