using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private IRType CreateTypeShell(TypeDefinitionInfo typeDef)
    {
        var cppName = CppNameMapper.MangleTypeName(typeDef.FullName);

        var irType = new IRType
        {
            ILFullName = typeDef.FullName,
            CppName = cppName,
            Name = typeDef.Name,
            Namespace = typeDef.Namespace,
            IsValueType = typeDef.IsValueType,
            IsInterface = typeDef.IsInterface,
            IsAbstract = typeDef.IsAbstract,
            IsSealed = typeDef.IsSealed,
            IsEnum = typeDef.IsEnum,
        };

        if (typeDef.IsEnum)
            irType.EnumUnderlyingType = typeDef.EnumUnderlyingType ?? "System.Int32";

        // Detect delegate types (base is System.MulticastDelegate)
        if (typeDef.BaseTypeName is "System.MulticastDelegate" or "System.Delegate")
            irType.IsDelegate = true;

        // Register value types for CppNameMapper so it doesn't add pointer suffix
        // Register both IL name (for IsValueType lookups) and C++ name (for GetDefaultValue)
        if (typeDef.IsValueType)
        {
            CppNameMapper.RegisterValueType(typeDef.FullName);
            CppNameMapper.RegisterValueType(cppName);
        }

        return irType;
    }

    private void PopulateTypeDetails(TypeDefinitionInfo typeDef, IRType irType)
    {
        // Base type
        if (typeDef.BaseTypeName != null && _typeCache.TryGetValue(typeDef.BaseTypeName, out var baseType))
        {
            irType.BaseType = baseType;
        }

        // Interfaces
        foreach (var ifaceName in typeDef.InterfaceNames)
        {
            if (_typeCache.TryGetValue(ifaceName, out var iface))
            {
                irType.Interfaces.Add(iface);
            }
        }

        // Fields (skip CLR-internal value__ field for enums)
        foreach (var fieldDef in typeDef.Fields)
        {
            if (irType.IsEnum && fieldDef.Name == "value__") continue;
            var irField = new IRField
            {
                Name = fieldDef.Name,
                CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                FieldTypeName = fieldDef.TypeName,
                IsStatic = fieldDef.IsStatic,
                IsPublic = fieldDef.IsPublic,
                DeclaringType = irType,
            };

            if (_typeCache.TryGetValue(fieldDef.TypeName, out var fieldType))
            {
                irField.FieldType = fieldType;
            }

            irField.ConstantValue = fieldDef.ConstantValue;

            if (fieldDef.IsStatic)
                irType.StaticFields.Add(irField);
            else
                irType.Fields.Add(irField);
        }

        // Calculate instance size
        CalculateInstanceSize(irType);
    }

    private void CalculateInstanceSize(IRType irType)
    {
        // Start with object header (vtable pointer + GC mark + sync block)
        int size = irType.IsValueType ? 0 : 16; // sizeof(Object)

        // Add base type fields
        if (irType.BaseType != null)
        {
            size = irType.BaseType.InstanceSize;
        }

        // Add own fields
        foreach (var field in irType.Fields)
        {
            int fieldSize = GetFieldSize(field.FieldTypeName);
            int alignment = GetFieldAlignment(field.FieldTypeName);

            // Align
            size = (size + alignment - 1) & ~(alignment - 1);
            field.Offset = size;
            size += fieldSize;
        }

        // Align to pointer size
        size = (size + 7) & ~7;
        irType.InstanceSize = size;
    }

    // Field sizes per ECMA-335 §I.8.2.1 (Built-in Value Types)
    private int GetFieldSize(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Int16" or "System.UInt16" or "System.Char" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            _ => 8 // Pointer size (reference types)
        };
    }

    private int GetFieldAlignment(string typeName)
    {
        return Math.Min(GetFieldSize(typeName), 8);
    }
}
