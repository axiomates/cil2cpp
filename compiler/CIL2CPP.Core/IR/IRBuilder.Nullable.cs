using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Nullable&lt;T&gt; method interception.
/// Nullable is a BCL generic struct whose method bodies are not in user assemblies.
/// We intercept calls and emit inline C++ instead.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Check if a type reference is System.Nullable`1 (any instantiation).
    /// </summary>
    private static bool IsNullableType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName == "System.Nullable`1";
    }

    /// <summary>
    /// Handle calls to Nullable&lt;T&gt; methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitNullableCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsNullableType(methodRef.DeclaringType)) return false;

        // Wrap thisArg in parentheses to handle ldloca pattern: &loc_0 → (&loc_0)->
        // Without parens, &loc_0->field parses as &(loc_0->field) which is wrong.
        string This()
        {
            var raw = stack.Count > 0 ? stack.Pop() : "nullptr";
            return raw.StartsWith("&") ? $"({raw})" : raw;
        }

        switch (methodRef.Name)
        {
            case ".ctor":
            {
                // ldloca + call .ctor(T value) pattern
                // Stack: [thisAddr, value]
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var thisArg = This();
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{thisArg}->f_hasValue = true; {thisArg}->f_value = {value};"
                });
                return true;
            }
            case "get_HasValue":
            {
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f_hasValue;"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_Value":
            {
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if (!{thisArg}->f_hasValue) cil2cpp::throw_invalid_operation();"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f_value;"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetValueOrDefault":
            {
                if (methodRef.Parameters.Count == 0)
                {
                    // GetValueOrDefault() — returns value regardless of hasValue
                    var thisArg = This();
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = {thisArg}->f_value;"
                    });
                    stack.Push(tmp);
                }
                else
                {
                    // GetValueOrDefault(T defaultValue)
                    var defaultVal = stack.Count > 0 ? stack.Pop() : "0";
                    var thisArg = This();
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = {thisArg}->f_hasValue ? {thisArg}->f_value : {defaultVal};"
                    });
                    stack.Push(tmp);
                }
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Handle Nullable&lt;T&gt; constructor via newobj (rare — usually ldloca+call).
    /// Returns true if handled.
    /// </summary>
    private bool TryEmitNullableNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsNullableType(ctorRef.DeclaringType)) return false;

        var typeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        if (ctorRef.Parameters.Count == 1)
        {
            var value = stack.Count > 0 ? stack.Pop() : "0";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{typeCpp} {tmp}; {tmp}.f_hasValue = true; {tmp}.f_value = {value};"
            });
        }
        else
        {
            // Default ctor (no args) — zero-initialize
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{typeCpp} {tmp} = {{}};"
            });
        }
        stack.Push(tmp);
        return true;
    }
}
