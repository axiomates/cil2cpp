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
    /// Handle calls to ValueTuple methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitValueTupleCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsValueTupleType(methodRef.DeclaringType)) return false;

        // Wrap thisArg in parentheses to handle ldloca pattern: &loc_0 → (&loc_0)->
        string WrapThis(string raw) => raw.StartsWith("&") ? $"({raw})" : raw;

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
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                var litId = _module.RegisterStringLiteral("(ValueTuple)");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {litId};"
                });
                stack.Push(tmp);
                return true;
            }
            case "Equals":
            {
                var arg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = false; /* ValueTuple.Equals stub */"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetHashCode":
            {
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = 0; /* ValueTuple.GetHashCode stub */"
                });
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
