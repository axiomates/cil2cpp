namespace CIL2CPP.Core.IR;

/// <summary>
/// IL evaluation stack entry with optional C++ type tracking.
/// The implicit conversion from string allows gradual migration:
/// existing Push("expr") calls work unchanged, while new code can
/// Push(new StackEntry("expr", "Type*")) to carry type info.
/// </summary>
public readonly record struct StackEntry(string Expr, string? CppType = null)
{
    /// <summary>Allow implicit conversion from string for backward compatibility.</summary>
    public static implicit operator StackEntry(string expr) => new(expr);

    /// <summary>True if CppType is a pointer type (ends with '*').</summary>
    public bool IsPointer => CppType?.EndsWith("*") == true;

    /// <summary>True if the expression is an address-of (starts with '&amp;').</summary>
    public bool IsAddressOf => Expr.StartsWith("&");

    /// <summary>True if the expression is the null literal.</summary>
    public bool IsNullLiteral => Expr is "nullptr";
}

/// <summary>
/// Extension methods for Stack&lt;StackEntry&gt; providing convenient
/// expression-only access for backward compatibility with string-based code.
/// </summary>
public static class StackEntryExtensions
{
    /// <summary>Pop and return only the expression string (or "0" if empty).</summary>
    public static string PopExpr(this Stack<StackEntry> stack)
        => stack.Count > 0 ? stack.Pop().Expr : "0";

    /// <summary>Pop and return expression string with custom default for empty stack.</summary>
    public static string PopExprOr(this Stack<StackEntry> stack, string defaultExpr)
        => stack.Count > 0 ? stack.Pop().Expr : defaultExpr;

    /// <summary>Peek and return only the expression string (or "0" if empty).</summary>
    public static string PeekExpr(this Stack<StackEntry> stack)
        => stack.Count > 0 ? stack.Peek().Expr : "0";

    /// <summary>Pop and return the full StackEntry (or default "0" entry if empty).</summary>
    public static StackEntry PopEntry(this Stack<StackEntry> stack)
        => stack.Count > 0 ? stack.Pop() : new StackEntry("0");

    /// <summary>Peek and return the full StackEntry (or default "0" entry if empty).</summary>
    public static StackEntry PeekEntry(this Stack<StackEntry> stack)
        => stack.Count > 0 ? stack.Peek() : new StackEntry("0");
}
