using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// ValueTuple method interception.
/// ValueTuple is a BCL generic struct whose method bodies are not in user assemblies.
/// Field access (ldfld Item1, Item2, ...) works via existing field handling.
/// We intercept constructor and method calls to emit inline C++.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Check if a type reference is System.ValueTuple`N (any arity).
    /// </summary>
    private static bool IsValueTupleType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName.StartsWith("System.ValueTuple`");
    }

    /// <summary>
    /// Get the generic type argument names for a ValueTuple type (IL full names).
    /// Returns empty list if not a GenericInstanceType.
    /// </summary>
    private static List<string> GetValueTupleTypeArgs(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType git)
            return git.GenericArguments.Select(a => a.FullName).ToList();
        return new List<string>();
    }

    /// <summary>
    /// Emit a field-to-string conversion for ValueTuple ToString/Equals/GetHashCode.
    /// </summary>
    private string EmitFieldToString(IRBasicBlock block, string fieldExpr, string fieldTypeName, ref int tempCounter)
    {
        var tmp = $"__t{tempCounter++}";
        if (fieldTypeName == "System.String")
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {fieldExpr} ? {fieldExpr} : cil2cpp::string_literal(\"null\");"
            });
        }
        else if (fieldTypeName is "System.Int32" or "System.Int64"
            or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
            or "System.UInt32" or "System.UInt64" or "System.IntPtr" or "System.UIntPtr")
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::string_from_int32((int32_t){fieldExpr});"
            });
        }
        else if (fieldTypeName == "System.Boolean")
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = {fieldExpr} ? cil2cpp::string_literal(\"True\") : cil2cpp::string_literal(\"False\");"
            });
        }
        else if (fieldTypeName is "System.Double" or "System.Single")
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::string_from_double((double){fieldExpr});"
            });
        }
        else if (fieldTypeName == "System.Char")
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::string_from_int32((int32_t){fieldExpr});"
            });
        }
        else
        {
            // Reference type or unknown value type: use object_to_string
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = cil2cpp::object_to_string((cil2cpp::Object*){fieldExpr});"
            });
        }
        return tmp;
    }

    /// <summary>
    /// Emit field equality comparison for ValueTuple.Equals.
    /// </summary>
    private static string EmitFieldEquality(string leftExpr, string rightExpr, string fieldTypeName)
    {
        if (fieldTypeName == "System.String")
            return $"cil2cpp::string_equals({leftExpr}, {rightExpr})";
        if (CppNameMapper.IsValueType(fieldTypeName)
            || fieldTypeName is "System.Boolean" or "System.Char")
            return $"({leftExpr} == {rightExpr})";
        // Reference type
        return $"cil2cpp::object_equals((cil2cpp::Object*){leftExpr}, (cil2cpp::Object*){rightExpr})";
    }

    /// <summary>
    /// Emit field hash computation for ValueTuple.GetHashCode.
    /// </summary>
    private static string EmitFieldHash(string fieldExpr, string fieldTypeName)
    {
        if (fieldTypeName == "System.String")
            return $"({fieldExpr} ? cil2cpp::string_get_hash_code({fieldExpr}) : 0)";
        if (CppNameMapper.IsValueType(fieldTypeName)
            || fieldTypeName is "System.Boolean" or "System.Char")
            return $"(int32_t){fieldExpr}";
        // Reference type
        return $"({fieldExpr} ? cil2cpp::object_get_hash_code((cil2cpp::Object*){fieldExpr}) : 0)";
    }

    /// <summary>
    /// Get the field name for index i (0-based). Item1..Item7, then Rest.
    /// </summary>
    private static string GetTupleFieldName(int index) =>
        index < 7 ? $"f_Item{index + 1}" : "f_Rest";

    /// <summary>
    /// Handle calls to ValueTuple methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitValueTupleCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsValueTupleType(methodRef.DeclaringType)) return false;

        // Wrap thisArg in parentheses to handle ldloca pattern: &loc_0 → (&loc_0)->
        string WrapThis(string raw) => raw.StartsWith("&") ? $"({raw})" : raw;

        var typeArgs = GetValueTupleTypeArgs(methodRef.DeclaringType);

        switch (methodRef.Name)
        {
            case ".ctor":
            {
                // ldloca + call .ctor(T1, T2, ...) pattern
                // Stack: [thisAddr, arg1, arg2, ...]
                var args = new List<string>();
                for (int i = 0; i < methodRef.Parameters.Count; i++)
                    args.Add(stack.Count > 0 ? stack.Pop() : "0");
                args.Reverse();
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");

                // For ValueTuple`8, the 8th parameter maps to f_Rest (nested tuple)
                var code = "";
                int normalCount = Math.Min(7, args.Count);
                for (int i = 0; i < normalCount; i++)
                    code += $"{thisArg}->f_Item{i + 1} = {args[i]}; ";
                if (args.Count > 7)
                    code += $"{thisArg}->f_Rest = {args[7]}; ";
                block.Instructions.Add(new IRRawCpp { Code = code.TrimEnd() });
                return true;
            }
            case "ToString":
            {
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                var tmp = $"__t{tempCounter++}";

                // Build: "(item1, item2, ...)"
                var openLit = _module.RegisterStringLiteral("(");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {openLit};"
                });
                var current = tmp;

                int fieldCount = Math.Min(typeArgs.Count, 7);
                for (int i = 0; i < fieldCount; i++)
                {
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
                    var fieldStr = EmitFieldToString(block, $"{thisArg}->{GetTupleFieldName(i)}", typeArgs[i], ref tempCounter);
                    var withField = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {withField} = cil2cpp::string_concat({current}, {fieldStr});"
                    });
                    current = withField;
                }

                var closeLit = _module.RegisterStringLiteral(")");
                var final = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {final} = cil2cpp::string_concat({current}, {closeLit});"
                });
                stack.Push(final);
                return true;
            }
            case "Equals":
            {
                // Equals(object other) — box comparison
                var arg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                var tmp = $"__t{tempCounter++}";

                if (typeArgs.Count == 0)
                {
                    // Fallback: no type info available
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = false;"
                    });
                    stack.Push(tmp);
                    return true;
                }

                // Unbox the argument to the same tuple type, then compare field by field
                var tupleCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
                var unboxed = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {unboxed} = cil2cpp::unbox<{tupleCpp}>({arg});"
                });

                // Build: field1==field1 && field2==field2 && ...
                var conditions = new List<string>();
                int fieldCount = Math.Min(typeArgs.Count, 7);
                for (int i = 0; i < fieldCount; i++)
                {
                    var fieldName = GetTupleFieldName(i);
                    conditions.Add(EmitFieldEquality(
                        $"{thisArg}->{fieldName}", $"{unboxed}.{fieldName}", typeArgs[i]));
                }
                var conjExpr = string.Join(" && ", conditions);
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {conjExpr};"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetHashCode":
            {
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                var tmp = $"__t{tempCounter++}";

                if (typeArgs.Count == 0)
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = (int32_t)0;"
                    });
                    stack.Push(tmp);
                    return true;
                }

                // hash = ((hash * 31) + field_hash) for each field
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t {tmp} = 0;"
                });
                int fieldCount = Math.Min(typeArgs.Count, 7);
                for (int i = 0; i < fieldCount; i++)
                {
                    var fieldHash = EmitFieldHash($"{thisArg}->{GetTupleFieldName(i)}", typeArgs[i]);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{tmp} = {tmp} * 31 + {fieldHash};"
                    });
                }
                stack.Push(tmp);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Handle ValueTuple constructor via newobj (value type — can't use gc::alloc).
    /// Returns true if handled.
    /// </summary>
    private bool TryEmitValueTupleNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsValueTupleType(ctorRef.DeclaringType)) return false;

        var typeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        // Collect constructor args
        var args = new List<string>();
        for (int i = 0; i < ctorRef.Parameters.Count; i++)
            args.Add(stack.Count > 0 ? stack.Pop() : "0");
        args.Reverse();

        // Emit: TypeCpp tmp = {}; tmp.f_Item1 = arg1; ...
        // For ValueTuple`8, the 8th parameter maps to f_Rest (nested tuple)
        var code = $"{typeCpp} {tmp} = {{}};";
        int normalCount = Math.Min(7, args.Count);
        for (int i = 0; i < normalCount; i++)
            code += $" {tmp}.f_Item{i + 1} = {args[i]};";
        if (args.Count > 7)
            code += $" {tmp}.f_Rest = {args[7]};";

        block.Instructions.Add(new IRRawCpp { Code = code });
        stack.Push(tmp);
        return true;
    }
}
