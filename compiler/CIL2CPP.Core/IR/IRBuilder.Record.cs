namespace CIL2CPP.Core.IR;

/// <summary>
/// Record type method synthesis.
/// Records have compiler-generated methods that reference unsupported BCL types
/// (StringBuilder, EqualityComparer&lt;T&gt;). We synthesize replacements.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Synthesize replacement method bodies for all record-generated methods.
    /// </summary>
    private void SynthesizeRecordMethods(IRType type)
    {
        foreach (var method in type.Methods)
        {
            switch (method.Name)
            {
                case "ToString" when !method.IsStatic:
                    SynthesizeRecordToString(method, type);
                    break;
                case "GetHashCode" when !method.IsStatic:
                    SynthesizeRecordGetHashCode(method, type);
                    break;
                case "Equals" when !method.IsStatic && method.Parameters.Count == 1
                    && method.Parameters[0].CppTypeName.Contains(type.CppName):
                    SynthesizeRecordTypedEquals(method, type);
                    break;
                case "Equals" when !method.IsStatic && method.Parameters.Count == 1:
                    SynthesizeRecordObjectEquals(method, type);
                    break;
                case "<Clone>$":
                    SynthesizeRecordClone(method, type);
                    break;
                case "op_Equality":
                    SynthesizeRecordOpEquality(method, type, isInequality: false);
                    break;
                case "op_Inequality":
                    SynthesizeRecordOpEquality(method, type, isInequality: true);
                    break;
                case "PrintMembers":
                    SynthesizeRecordPrintMembers(method);
                    break;
                case "get_EqualityContract":
                    SynthesizeRecordEqualityContract(method, type);
                    break;
            }
        }
    }

    /// <summary>
    /// Get record property fields (backing fields with &lt;Name&gt;k__BackingField pattern).
    /// Returns tuples of (propertyName, cppFieldName, fieldTypeName).
    /// </summary>
    private static List<(string PropName, string CppFieldName, string FieldTypeName)>
        GetRecordPropertyFields(IRType type)
    {
        var result = new List<(string, string, string)>();
        foreach (var field in type.Fields)
        {
            // Skip __type_info and __sync_block (runtime struct overhead)
            if (field.Name.StartsWith("__")) continue;

            // Extract property name from <Name>k__BackingField pattern
            var propName = field.Name;
            if (field.Name.StartsWith("<") && field.Name.Contains(">k__BackingField"))
            {
                propName = field.Name.Substring(1, field.Name.IndexOf('>') - 1);
            }

            result.Add((propName, field.CppName, field.FieldTypeName));
        }
        return result;
    }

    /// <summary>
    /// Check if a field type is a reference type (needs special handling in Equals/ToString).
    /// </summary>
    private static bool IsReferenceFieldType(string fieldTypeName)
    {
        return !CppNameMapper.IsValueType(fieldTypeName)
            && fieldTypeName != "System.Boolean"
            && fieldTypeName != "System.Char";
    }

    private void SynthesizeRecordToString(IRMethod method, IRType type)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);

        int tempCounter = 0;
        var fields = GetRecordPropertyFields(type);

        // Start with type name
        var headerLit = _module.RegisterStringLiteral($"{type.Name} {{ ");
        var current = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {current} = {headerLit};"
        });

        for (int i = 0; i < fields.Count; i++)
        {
            var (propName, cppFieldName, fieldTypeName) = fields[i];

            // Add separator for non-first fields
            if (i > 0)
            {
                var commaLit = _module.RegisterStringLiteral(", ");
                var next = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {next} = cil2cpp::string_concat({current}, {commaLit});"
                });
                current = next;
            }

            // Add "PropName = "
            var labelLit = _module.RegisterStringLiteral($"{propName} = ");
            var withLabel = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {withLabel} = cil2cpp::string_concat({current}, {labelLit});"
            });

            // Add field value as string
            var valStr = $"__t{tempCounter++}";
            if (fieldTypeName == "System.String")
            {
                // String: use directly (or "null" if null)
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {valStr} = __this->{cppFieldName} ? __this->{cppFieldName} : cil2cpp::string_literal(\"null\");"
                });
            }
            else if (fieldTypeName == "System.Int32" || fieldTypeName == "System.Int64"
                || fieldTypeName == "System.Byte" || fieldTypeName == "System.Int16")
            {
                // Integer types: convert via Console_Write pattern (int to string)
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {valStr} = cil2cpp::string_from_int32((int32_t)__this->{cppFieldName});"
                });
            }
            else if (fieldTypeName == "System.Boolean")
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {valStr} = __this->{cppFieldName} ? cil2cpp::string_literal(\"True\") : cil2cpp::string_literal(\"False\");"
                });
            }
            else if (fieldTypeName == "System.Double" || fieldTypeName == "System.Single")
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {valStr} = cil2cpp::string_from_double((double)__this->{cppFieldName});"
                });
            }
            else
            {
                // Reference type: use object_to_string
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {valStr} = cil2cpp::object_to_string((cil2cpp::Object*)__this->{cppFieldName});"
                });
            }

            var withVal = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {withVal} = cil2cpp::string_concat({withLabel}, {valStr});"
            });
            current = withVal;
        }

        // Close with " }"
        var closeLit = _module.RegisterStringLiteral(" }");
        var final = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {final} = cil2cpp::string_concat({current}, {closeLit});"
        });
        block.Instructions.Add(new IRReturn { Value = final });
    }

    private void SynthesizeRecordGetHashCode(IRMethod method, IRType type)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);

        var fields = GetRecordPropertyFields(type);

        block.Instructions.Add(new IRRawCpp
        {
            Code = "int32_t __hash = 0;"
        });

        foreach (var (_, cppFieldName, fieldTypeName) in fields)
        {
            if (fieldTypeName == "System.String")
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"__hash = __hash * 31 + (__this->{cppFieldName} ? cil2cpp::string_get_hash_code(__this->{cppFieldName}) : 0);"
                });
            }
            else if (CppNameMapper.IsValueType(fieldTypeName)
                || fieldTypeName == "System.Boolean" || fieldTypeName == "System.Char")
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"__hash = __hash * 31 + (int32_t)__this->{cppFieldName};"
                });
            }
            else
            {
                // Reference type
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"__hash = __hash * 31 + (__this->{cppFieldName} ? cil2cpp::object_get_hash_code((cil2cpp::Object*)__this->{cppFieldName}) : 0);"
                });
            }
        }

        block.Instructions.Add(new IRReturn { Value = "__hash" });
    }

    private void SynthesizeRecordTypedEquals(IRMethod method, IRType type)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);

        var otherParam = method.Parameters[0].CppName;
        var fields = GetRecordPropertyFields(type);

        // Value types: parameter is passed by value, use "." accessor
        // Reference types: parameter is pointer, use "->" accessor + null check
        var accessor = type.IsValueType ? "." : "->";
        if (!type.IsValueType)
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"if ({otherParam} == nullptr) return false;"
            });
        }

        // Compare each field
        foreach (var (_, cppFieldName, fieldTypeName) in fields)
        {
            if (fieldTypeName == "System.String")
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if (!cil2cpp::string_equals(__this->{cppFieldName}, {otherParam}{accessor}{cppFieldName})) return false;"
                });
            }
            else if (CppNameMapper.IsValueType(fieldTypeName)
                || fieldTypeName == "System.Boolean" || fieldTypeName == "System.Char")
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if (__this->{cppFieldName} != {otherParam}{accessor}{cppFieldName}) return false;"
                });
            }
            else
            {
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if (!cil2cpp::object_equals((cil2cpp::Object*)__this->{cppFieldName}, (cil2cpp::Object*){otherParam}{accessor}{cppFieldName})) return false;"
                });
            }
        }

        block.Instructions.Add(new IRReturn { Value = "true" });
    }

    private void SynthesizeRecordObjectEquals(IRMethod method, IRType type)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);

        var otherParam = method.Parameters[0].CppName;

        // Find the typed Equals method
        var typedEqualsName = type.Methods
            .FirstOrDefault(m => m.Name == "Equals" && m.Parameters.Count == 1
                && m.Parameters[0].CppTypeName.Contains(type.CppName))?.CppName;

        if (typedEqualsName != null)
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"if ({otherParam} == nullptr) return false;"
            });
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"if (!cil2cpp::object_is_instance_of({otherParam}, &{type.CppName}_TypeInfo)) return false;"
            });

            if (type.IsValueType)
            {
                // Value type: unbox the object parameter, pass by value
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"return {typedEqualsName}(__this, cil2cpp::unbox<{type.CppName}>({otherParam}));"
                });
            }
            else
            {
                // Reference type: cast pointer
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"return {typedEqualsName}(__this, ({type.CppName}*){otherParam});"
                });
            }
        }
        else
        {
            block.Instructions.Add(new IRReturn { Value = $"(cil2cpp::Object*)__this == {otherParam}" });
        }
    }

    private void SynthesizeRecordClone(IRMethod method, IRType type)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);

        // Value types don't have Clone — copy via assignment
        if (type.IsValueType)
        {
            block.Instructions.Add(new IRReturn { Value = "(cil2cpp::Object*)__this" });
            return;
        }

        var fields = GetRecordPropertyFields(type);

        // Allocate new object
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto __clone = ({type.CppName}*)cil2cpp::gc::alloc(sizeof({type.CppName}), &{type.CppName}_TypeInfo);"
        });

        // Copy all fields
        foreach (var (_, cppFieldName, _) in fields)
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"__clone->{cppFieldName} = __this->{cppFieldName};"
            });
        }

        block.Instructions.Add(new IRReturn { Value = $"(cil2cpp::Object*)__clone" });
    }

    private void SynthesizeRecordOpEquality(IRMethod method, IRType type, bool isInequality)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);

        var left = method.Parameters[0].CppName;
        var right = method.Parameters[1].CppName;

        // Find typed Equals
        var typedEqualsName = type.Methods
            .FirstOrDefault(m => m.Name == "Equals" && m.Parameters.Count == 1
                && m.Parameters[0].CppTypeName.Contains(type.CppName))?.CppName;

        if (typedEqualsName != null)
        {
            if (type.IsValueType)
            {
                // Value types: parameters are values, take address for __this
                var equalExpr = $"{typedEqualsName}(&{left}, {right})";
                var result = isInequality ? $"!{equalExpr}" : equalExpr;
                block.Instructions.Add(new IRReturn { Value = result });
            }
            else
            {
                // Reference types: null check + pointer-based Equals
                var equalExpr = $"({left} == nullptr ? {right} == nullptr : {typedEqualsName}({left}, {right}))";
                var result = isInequality ? $"!{equalExpr}" : equalExpr;
                block.Instructions.Add(new IRReturn { Value = result });
            }
        }
        else
        {
            var result = isInequality ? $"{left} != {right}" : $"{left} == {right}";
            block.Instructions.Add(new IRReturn { Value = result });
        }
    }

    private void SynthesizeRecordPrintMembers(IRMethod method)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);
        block.Instructions.Add(new IRReturn { Value = "true" });
    }

    private void SynthesizeRecordEqualityContract(IRMethod method, IRType type)
    {
        var block = new IRBasicBlock { Id = 0 };
        method.BasicBlocks.Clear();
        method.BasicBlocks.Add(block);
        // Return pointer to TypeInfo as a stand-in for System.Type
        block.Instructions.Add(new IRReturn
        {
            Value = $"(cil2cpp::Object*)&{type.CppName}_TypeInfo"
        });
    }
}
