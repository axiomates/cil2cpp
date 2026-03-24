using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Resolves canonical forms for generic type instantiations.
/// Reference-type generic arguments are replaced with __Canon to enable
/// method body sharing across all reference-type specializations.
/// Value-type arguments are preserved (different struct layouts).
///
/// Architecture reference: .NET NativeAOT and Unity IL2CPP both use
/// __Canon for reference-type generic sharing.
/// </summary>
public static class CanonicalTypeResolver
{
    /// <summary>
    /// The canonical placeholder type name for reference-type generic arguments.
    /// All reference types are pointer-sized (Object*) at runtime, so their
    /// generic instantiations have identical struct layouts and method logic.
    /// </summary>
    public const string CanonTypeName = "__Canon";

    /// <summary>
    /// Compute the canonical form of generic type arguments.
    /// Reference-type args → __Canon, value-type args → unchanged.
    /// Returns null if no canonicalization needed (all value types).
    /// </summary>
    public static List<string>? Canonicalize(
        List<string> typeArgs,
        Func<string, bool> isValueType)
    {
        bool anyChanged = false;
        var result = new List<string>(typeArgs.Count);

        foreach (var arg in typeArgs)
        {
            if (isValueType(arg))
            {
                result.Add(arg);
            }
            else
            {
                result.Add(CanonTypeName);
                anyChanged = true;
            }
        }

        return anyChanged ? result : null;
    }

    /// <summary>
    /// Build the canonical key string for a generic instantiation.
    /// Format: "OpenTypeName&lt;arg1,arg2,...&gt;"
    /// </summary>
    public static string GetCanonicalKey(string openTypeName, List<string> canonicalArgs)
    {
        return $"{openTypeName}<{string.Join(",", canonicalArgs)}>";
    }

    /// <summary>
    /// Analyze which methods on an open generic type are non-sharable.
    /// Non-sharable methods cannot use __Canon shared bodies because they:
    /// 1. Are static (no this → no TypeInfo for dispatch)
    /// 2. Access static fields of their own type (per-type statics)
    /// 3. Use ldtoken with a generic type parameter (typeof(T))
    /// 4. Call a non-sharable method on the same type (transitive)
    ///
    /// Returns the set of Cecil method full names that are non-sharable.
    /// This analysis runs once per open generic type definition and is cached.
    /// </summary>
    public static HashSet<string> AnalyzeMethodSharability(TypeDefinition openType)
    {
        var nonShareable = new HashSet<string>();

        // Step 1: Mark directly non-sharable methods
        foreach (var method in openType.Methods)
        {
            if (IsDirectlyNonShareable(method, openType))
            {
                nonShareable.Add(method.FullName);
            }
        }

        // Step 2: Propagate non-sharability through intra-type call graph
        PropagateNonSharability(openType, nonShareable);

        return nonShareable;
    }

    /// <summary>
    /// Check if a method is directly non-sharable (without considering call propagation).
    /// </summary>
    private static bool IsDirectlyNonShareable(MethodDefinition method, TypeDefinition openType)
    {
        // Static methods have no 'this' pointer → no TypeInfo for dispatch
        if (method.IsStatic)
            return true;

        // Methods without bodies (abstract, extern) are sharable declarations
        if (!method.HasBody)
            return false;

        foreach (var instr in method.Body.Instructions)
        {

            // Static field access on own type → per-type statics
            if (IsStaticFieldAccessOnOwnType(instr, openType))
                return true;

            // ldtoken with generic type parameter → typeof(T)
            if (IsLdtokenGenericParameter(instr, openType))
                return true;

            // Instructions that reference generic instance types parameterized by
            // the enclosing type's generic parameters (e.g., IEnumerator<T>, List<T>).
            // These bake a TypeInfo with __Canon into the method body, causing
            // runtime TypeInfo pointer mismatches (IEnumerator<__Canon> != IEnumerator<Object>).
            if (ReferencesGenericInstanceWithTypeParams(instr, openType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if an instruction accesses a static field on the declaring generic type.
    /// </summary>
    private static bool IsStaticFieldAccessOnOwnType(Instruction instr, TypeDefinition openType)
    {
        if (instr.OpCode != OpCodes.Ldsfld &&
            instr.OpCode != OpCodes.Stsfld &&
            instr.OpCode != OpCodes.Ldsflda)
            return false;

        if (instr.Operand is not FieldReference fieldRef)
            return false;

        // Check if the field's declaring type is the same open generic type
        // or a generic instance of it.
        return IsSameOpenType(fieldRef.DeclaringType, openType);
    }

    /// <summary>
    /// Check if an instruction is ldtoken with a generic parameter owned by the type.
    /// This corresponds to typeof(T) where T is a type-level generic parameter.
    /// </summary>
    private static bool IsLdtokenGenericParameter(Instruction instr, TypeDefinition openType)
    {
        if (instr.OpCode != OpCodes.Ldtoken)
            return false;

        if (instr.Operand is GenericParameter gp)
        {
            // Check if this generic parameter belongs to the type (not the method)
            return gp.Owner is TypeReference ownerType &&
                   IsSameOpenType(ownerType, openType);
        }

        return false;
    }

    /// <summary>
    /// Check if a type reference refers to the same open generic type.
    /// Handles both direct references and GenericInstanceType references.
    /// </summary>
    private static bool IsSameOpenType(TypeReference typeRef, TypeDefinition openType)
    {
        if (typeRef is GenericInstanceType git)
            return git.ElementType.FullName == openType.FullName;

        return typeRef.FullName == openType.FullName;
    }

    /// <summary>
    /// Check if an instruction references a GenericInstanceType whose generic arguments
    /// include the enclosing type's generic parameters. Such types produce __Canon TypeInfos
    /// that don't match concrete types at runtime (e.g., IEnumerator&lt;__Canon&gt; TypeInfo
    /// vs IEnumerator&lt;Object&gt; TypeInfo — different pointers, interface dispatch fails).
    /// </summary>
    private static bool ReferencesGenericInstanceWithTypeParams(
        Instruction instr, TypeDefinition openType)
    {
        // Only check instructions that use TypeInfo at runtime
        TypeReference? typeToCheck = null;

        switch (instr.OpCode.Code)
        {
            case Code.Callvirt:
            case Code.Call:
            case Code.Newobj:
            case Code.Ldftn:
            case Code.Ldvirtftn:
                if (instr.Operand is MethodReference methodRef)
                    typeToCheck = methodRef.DeclaringType;
                break;

            case Code.Castclass:
            case Code.Isinst:
            case Code.Box:
            case Code.Unbox:
            case Code.Unbox_Any:
            case Code.Newarr:
                typeToCheck = instr.Operand as TypeReference;
                break;

            // Static field access on OTHER generic types parameterized by the enclosing
            // type's generic params. Each canonical type's statics struct is separate, and
            // canonical types skip statics emission — so methods accessing other canonical
            // types' static fields must be non-sharable.
            // (IsStaticFieldAccessOnOwnType handles the same-type case separately.)
            case Code.Ldsfld:
            case Code.Stsfld:
            case Code.Ldsflda:
                if (instr.Operand is FieldReference sfieldRef)
                    typeToCheck = sfieldRef.DeclaringType;
                break;

            // ldtoken with a GenericInstanceType (e.g., typeof(List<T>)) bakes a TypeInfo
            // reference. IsLdtokenGenericParameter catches direct GenericParameter (typeof(T));
            // this catches GenericInstanceTypes containing the enclosing type's params.
            case Code.Ldtoken:
                if (instr.Operand is TypeReference tokenType)
                    typeToCheck = tokenType;
                else if (instr.Operand is MethodReference tokenMethod)
                    typeToCheck = tokenMethod.DeclaringType;
                else if (instr.Operand is FieldReference tokenField)
                    typeToCheck = tokenField.DeclaringType;
                break;
        }

        // Also check generic method instantiation arguments.
        // E.g., List<T>.IndexOf calls Array.IndexOf<T>(...) — the method's generic
        // argument T references the enclosing type's parameter. In the canonical body,
        // this becomes Array.IndexOf<__Canon>() which operates on different static state.
        if (instr.Operand is GenericInstanceMethod gim)
        {
            foreach (var arg in gim.GenericArguments)
            {
                if (ContainsTypeGenericParam(arg, openType))
                    return true;
            }
        }

        if (typeToCheck == null)
            return false;

        // Only skip the enclosing type for call/callvirt — those are handled by
        // intra-type call propagation (PropagateNonSharability).
        // For newobj, castclass, isinst, box, newarr, ldftn, ldvirtftn: the same-type
        // skip must NOT apply because these operations use TypeInfo at runtime.
        // A canonical body's newobj on the same type would allocate with __Canon TypeInfo
        // instead of the concrete type's TypeInfo — fundamentally wrong.
        if (instr.OpCode.Code is Code.Call or Code.Callvirt
            && IsSameOpenType(typeToCheck, openType))
            return false;

        return ContainsTypeGenericParam(typeToCheck, openType);
    }

    /// <summary>
    /// Recursively check if a TypeReference contains any generic parameter
    /// owned by the given open type. This catches types like IEnumerator&lt;T&gt;,
    /// List&lt;KeyValuePair&lt;T, int&gt;&gt;, etc.
    /// </summary>
    private static bool ContainsTypeGenericParam(TypeReference typeRef, TypeDefinition openType)
    {
        if (typeRef is GenericParameter gp)
        {
            return gp.Owner is TypeReference ownerType &&
                   IsSameOpenType(ownerType, openType);
        }

        if (typeRef is GenericInstanceType git)
        {
            foreach (var arg in git.GenericArguments)
            {
                if (ContainsTypeGenericParam(arg, openType))
                    return true;
            }
        }

        if (typeRef is ArrayType at)
            return ContainsTypeGenericParam(at.ElementType, openType);

        if (typeRef is ByReferenceType brt)
            return ContainsTypeGenericParam(brt.ElementType, openType);

        if (typeRef is PointerType pt)
            return ContainsTypeGenericParam(pt.ElementType, openType);

        return false;
    }

    /// <summary>
    /// Propagate non-sharability through the intra-type call graph.
    /// If method A calls method B on the same type, and B is non-sharable,
    /// then A is also non-sharable (the shared body would call the canonical
    /// version of B, but B needs per-type statics/typeof).
    /// </summary>
    private static void PropagateNonSharability(
        TypeDefinition openType,
        HashSet<string> nonShareable)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var method in openType.Methods)
            {
                if (nonShareable.Contains(method.FullName))
                    continue;

                if (!method.HasBody)
                    continue;

                foreach (var instr in method.Body.Instructions)
                {
                    if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                        continue;

                    if (instr.Operand is not MethodReference calleeRef)
                        continue;

                    // Check if the callee is on the same open type
                    if (!IsSameOpenType(calleeRef.DeclaringType, openType))
                        continue;

                    // Resolve the callee to find its full name for lookup
                    var calleeName = ResolveCalleeFullName(calleeRef, openType);
                    if (calleeName != null && nonShareable.Contains(calleeName))
                    {
                        nonShareable.Add(method.FullName);
                        changed = true;
                        break; // No need to scan more instructions
                    }
                }
            }
        } while (changed);
    }

    /// <summary>
    /// Resolve a method reference on the same type to its MethodDefinition.FullName.
    /// This handles the case where calleeRef.Resolve() might fail for generic instances.
    /// </summary>
    private static string? ResolveCalleeFullName(MethodReference calleeRef, TypeDefinition openType)
    {
        // Try direct resolve first
        try
        {
            var resolved = calleeRef.Resolve();
            if (resolved != null)
                return resolved.FullName;
        }
        catch
        {
            // Cecil resolution can fail for some generic patterns
        }

        // Fallback: match by name and parameter count against the open type's methods
        foreach (var method in openType.Methods)
        {
            if (method.Name == calleeRef.Name &&
                method.Parameters.Count == calleeRef.Parameters.Count)
            {
                return method.FullName;
            }
        }

        return null;
    }
}
