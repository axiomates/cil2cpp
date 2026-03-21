namespace CIL2CPP.Core.IR;

/// <summary>
/// Base class for all IR instructions.
/// </summary>
public abstract class IRInstruction
{
    /// <summary>Generate C++ code for this instruction.</summary>
    public abstract string ToCpp();

    /// <summary>Source location for debug mapping. Null in Release mode.</summary>
    public SourceLocation? DebugInfo { get; set; }

    /// <summary>
    /// Collect type references from this instruction into the provided sets.
    /// Called by IRMethod.ComputeTypeReferences() after body compilation.
    /// Replaces the fragile post-render string scanning in CollectTypeInfoRefs/CollectBodyPointerTypeRefs.
    /// </summary>
    public virtual void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames) { }

    /// <summary>Helper: add a type name to the pointer type set if it looks like a mangled C++ type name.</summary>
    protected static void TryAddPointerType(string? typeName, HashSet<string> pointerTypeNames)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        var name = typeName.TrimEnd('*').Trim();
        if (name.Length <= 3) return;
        if (!name.Contains('_')) return;
        if (!char.IsUpper(name[0])) return;
        if (name.StartsWith("cil2cpp::")) return;
        pointerTypeNames.Add(name);
    }

    /// <summary>Helper: add a type name to the TypeInfo set if it's not a runtime type.</summary>
    protected static void TryAddTypeInfo(string? typeName, HashSet<string> typeInfoNames)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        var name = typeName.TrimEnd('*').Trim();
        if (name.StartsWith("cil2cpp::")) return;
        if (name.Length == 0) return;
        typeInfoNames.Add(name);
    }
}

// ============ Concrete IR Instructions ============

public class IRComment : IRInstruction
{
    public string Text { get; set; } = "";
    public override string ToCpp() => $"// {Text}";
}

public class IRAssign : IRInstruction
{
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
    public override string ToCpp() => $"{Target} = {Value};";
}

public class IRDeclareLocal : IRInstruction
{
    public string TypeName { get; set; } = "";
    public string VarName { get; set; } = "";
    public string? InitValue { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddPointerType(TypeName, pointerTypeNames);
    }

    public override string ToCpp() =>
        InitValue != null
            ? $"{TypeName} {VarName} = {InitValue};"
            : $"{TypeName} {VarName} = {{0}};";
}

public class IRReturn : IRInstruction
{
    public string? Value { get; set; }
    public override string ToCpp() =>
        Value != null ? $"return {Value};" : "return;";
}

public class IRCall : IRInstruction
{
    public string FunctionName { get; set; } = "";
    public List<string> Arguments { get; } = new();
    public string? ResultVar { get; set; }
    /// <summary>C++ return type for the call (used for cross-scope variable pre-declarations).</summary>
    public string? ResultTypeCpp { get; set; }
    public bool IsVirtual { get; set; }
    public int VTableSlot { get; set; } = -1;
    public string? VTableReturnType { get; set; }
    public List<string>? VTableParamTypes { get; set; }
    public bool IsInterfaceCall { get; set; }
    public string? InterfaceTypeCppName { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        // Interface dispatch references the interface TypeInfo
        TryAddTypeInfo(InterfaceTypeCppName, typeInfoNames);
        // GVM dispatch references TypeInfos for each target type
        if (GenericVirtualTargets != null)
            foreach (var (typeInfo, _) in GenericVirtualTargets)
            {
                // Strip _TypeInfo suffix to get the type name
                var name = typeInfo.EndsWith("_TypeInfo") ? typeInfo[..^"_TypeInfo".Length] : typeInfo;
                TryAddTypeInfo(name, typeInfoNames);
            }
        // VTable param/return types may reference pointer types
        if (VTableReturnType != null) TryAddPointerType(VTableReturnType, pointerTypeNames);
        if (VTableParamTypes != null)
            foreach (var pt in VTableParamTypes)
                TryAddPointerType(pt, pointerTypeNames);
        // Arguments may contain &XXX_TypeInfo references (from ldtoken, constrained boxing, etc.)
        foreach (var arg in Arguments)
        {
            var idx = arg.IndexOf("_TypeInfo");
            if (idx <= 0) continue;
            // Extract the type name before _TypeInfo — walk backwards from idx
            // Skip leading '&' or '(' chars
            int start = idx - 1;
            while (start >= 0 && (char.IsLetterOrDigit(arg[start]) || arg[start] == '_')) start--;
            start++;
            if (start < idx)
            {
                var typeName = arg[start..idx];
                if (!string.IsNullOrEmpty(typeName) && !typeName.StartsWith("cil2cpp"))
                    TryAddTypeInfo(typeName, typeInfoNames);
            }
        }
    }
    /// <summary>
    /// IL parameter key for deferred disambiguation fixup. Set when the disambiguation
    /// lookup fails at emit time (e.g., type created after body compilation in Pass 3.6).
    /// Format: "ilParam1,ilParam2" matching DisambiguatedMethodNames key suffix.
    /// </summary>
    public string? DeferredDisambigKey { get; set; }

    /// <summary>
    /// Generic virtual method dispatch targets. Used when callvirt targets a method-level
    /// generic virtual method (e.g., ExecuteCore&lt;TResult, TState&gt;). Standard vtable dispatch
    /// cannot handle method-level generics because each vtable slot holds only one function pointer
    /// while generic methods have multiple specializations.
    /// Each entry maps a concrete type's TypeInfo name to the monomorphized override function name.
    /// </summary>
    public List<(string TypeInfoName, string FunctionName)>? GenericVirtualTargets { get; set; }

    public override string ToCpp()
    {
        var args = string.Join(", ", Arguments);
        string call;

        // Generic virtual method dispatch — type-check chain
        if (GenericVirtualTargets != null && GenericVirtualTargets.Count > 0 && Arguments.Count > 0)
        {
            return EmitGenericVirtualDispatch();
        }
        else if (IsVirtual && IsInterfaceCall && VTableSlot >= 0 && Arguments.Count > 0)
        {
            var paramTypesStr = VTableParamTypes != null
                ? string.Join(", ", VTableParamTypes) : "void*";
            var fnPtrType = $"{VTableReturnType ?? "void"}(*)({paramTypesStr})";
            var thisExpr = Arguments[0];
            // Cast arguments to match function pointer param types (handles Dog* → Object* etc.)
            var castArgs = BuildCastArgs();
            // CLR throws NullReferenceException on callvirt with null this — emit null check
            var nullCheck = $"cil2cpp::null_check((void*){thisExpr}); ";
            call = $"(({fnPtrType})(cil2cpp::obj_get_interface_vtable(((cil2cpp::Object*){thisExpr}), &{InterfaceTypeCppName}_TypeInfo)->methods[{VTableSlot}]))({castArgs})";
            var stmt = ResultVar != null ? $"{ResultVar} = {call};" : $"{call};";
            return nullCheck + stmt;
        }
        else if (IsVirtual && VTableSlot >= 0 && Arguments.Count > 0)
        {
            var paramTypesStr = VTableParamTypes != null
                ? string.Join(", ", VTableParamTypes) : "void*";
            var fnPtrType = $"{VTableReturnType ?? "void"}(*)({paramTypesStr})";
            var thisExpr = Arguments[0];
            // Cast arguments to match function pointer param types (handles Dog* → Object* etc.)
            var castArgs = BuildCastArgs();
            // CLR throws NullReferenceException on callvirt with null this — emit null check
            var nullCheck = $"cil2cpp::null_check((void*){thisExpr}); ";
            call = $"(({fnPtrType})(((cil2cpp::Object*){thisExpr})->__type_info->vtable->methods[{VTableSlot}]))({castArgs})";
            var stmt = ResultVar != null ? $"{ResultVar} = {call};" : $"{call};";
            return nullCheck + stmt;
        }
        else
        {
            call = $"{FunctionName}({args})";
        }

        return ResultVar != null ? $"{ResultVar} = {call};" : $"{call};";
    }

    /// <summary>
    /// Emit type-check dispatch chain for generic virtual method calls.
    /// Each constructed subtype gets a branch checking __type_info, calling its
    /// monomorphized override. Falls back to the direct function name (stub) if no match.
    /// </summary>
    private string EmitGenericVirtualDispatch()
    {
        var thisExpr = Arguments[0];
        var otherArgs = Arguments.Count > 1
            ? string.Join(", ", Arguments.Skip(1)) : "";
        var nullCheck = $"cil2cpp::null_check((void*){thisExpr}); ";

        var sb = new System.Text.StringBuilder();
        sb.Append(nullCheck);

        // Pre-declare result variable before if-else chain (AddAutoDeclarations can't
        // handle variable assignments inside branches of a GVM dispatch chain)
        if (ResultVar != null && ResultTypeCpp != null)
            sb.Append($"{ResultTypeCpp} {ResultVar}{{}}; ");
        else if (ResultVar != null)
            sb.Append($"auto {ResultVar} = decltype({ResultVar}){{}}; ");

        // Emit: if/else chain checking __type_info.
        // Each branch casts `this` to the concrete target type (flat struct model — no C++ inheritance).
        for (int i = 0; i < GenericVirtualTargets!.Count; i++)
        {
            var (typeInfo, funcName) = GenericVirtualTargets[i];
            var prefix = i == 0 ? "if" : "else if";
            sb.Append($"{prefix} (((cil2cpp::Object*){thisExpr})->__type_info == &{typeInfo}) ");

            // Derive the concrete type name from the TypeInfo name (strip _TypeInfo suffix)
            var concreteType = typeInfo.EndsWith("_TypeInfo")
                ? typeInfo[..^"_TypeInfo".Length] + "*"
                : $"void*";
            var castThis = $"({concreteType}){thisExpr}";
            var branchArgs = otherArgs.Length > 0 ? $"{castThis}, {otherArgs}" : castThis;
            var callExpr = $"{funcName}({branchArgs})";
            if (ResultVar != null)
                sb.Append($"{{ {ResultVar} = {callExpr}; }} ");
            else
                sb.Append($"{{ {callExpr}; }} ");
        }

        // Fallback: direct call with original args (may be a stub, but ensures linkability)
        var fallbackArgs = string.Join(", ", Arguments);
        var fallbackCall = $"{FunctionName}({fallbackArgs})";
        if (ResultVar != null)
            sb.Append($"else {{ {ResultVar} = {fallbackCall}; }}");
        else
            sb.Append($"else {{ {fallbackCall}; }}");

        return sb.ToString();
    }

    /// <summary>
    /// Build argument list with casts to match VTableParamTypes.
    /// This handles implicit upcasts (e.g., Dog* passed to Object* parameter).
    /// </summary>
    private string BuildCastArgs()
    {
        if (VTableParamTypes == null || VTableParamTypes.Count == 0)
            return string.Join(", ", Arguments);

        var parts = new List<string>();
        for (int i = 0; i < Arguments.Count; i++)
        {
            if (i < VTableParamTypes.Count && VTableParamTypes[i].EndsWith("*"))
                parts.Add($"({VTableParamTypes[i]}){Arguments[i]}");
            else
                parts.Add(Arguments[i]);
        }
        return string.Join(", ", parts);
    }
}

public class IRNewObj : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public string CtorName { get; set; } = "";
    public List<string> CtorArgs { get; } = new();
    public string ResultVar { get; set; } = "";
    /// <summary>
    /// IL parameter key for deferred disambiguation fixup (Pass 3.7).
    /// Set when the constructor name couldn't be disambiguated at emit time.
    /// </summary>
    public string? DeferredDisambigKey { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddTypeInfo(TypeCppName, typeInfoNames);
        TryAddPointerType(TypeCppName, pointerTypeNames);
    }

    public override string ToCpp()
    {
        var lines = new List<string>
        {
            $"{ResultVar} = ({TypeCppName}*)cil2cpp::gc::alloc(sizeof({TypeCppName}), &{TypeCppName}_TypeInfo);",
        };

        var allArgs = new List<string> { ResultVar };
        allArgs.AddRange(CtorArgs);
        lines.Add($"{CtorName}({string.Join(", ", allArgs)});");

        return string.Join("\n    ", lines);
    }
}

public class IRBinaryOp : IRInstruction
{
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
    public string Op { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsUnsigned { get; set; }
    /// <summary>When true, emit std::fmod() instead of % operator (C++ % is invalid for float/double).</summary>
    public bool IsFloatRemainder { get; set; }
    public override string ToCpp()
    {
        // C++ % operator is invalid for float/double (MSVC C2296/C2297).
        // Use std::fmod() for floating-point remainder.
        if (IsFloatRemainder)
            return $"{ResultVar} = std::fmod({Left}, {Right});";
        if (IsUnsigned)
        {
            // ECMA-335 III.1.5: unsigned comparisons on floats use unordered (NaN → true).
            // Use unsigned_gt/lt/ge/le which handle both integer (as-unsigned) and float (NaN-aware).
            // Cast to int32_t: ECMA-335 comparisons produce int32 (0/1), not C++ bool.
            // Without the cast, C++ auto infers bool, causing MSVC C4805 when the result
            // is used in bitwise ops (&, |, ^) with integer operands.
            if (Op == ">")
                return $"{ResultVar} = (int32_t)cil2cpp::unsigned_gt({Left}, {Right});";
            if (Op == "<")
                return $"{ResultVar} = (int32_t)cil2cpp::unsigned_lt({Left}, {Right});";
            if (Op == ">=")
                return $"{ResultVar} = (int32_t)cil2cpp::unsigned_ge({Left}, {Right});";
            if (Op == "<=")
                return $"{ResultVar} = (int32_t)cil2cpp::unsigned_le({Left}, {Right});";
            return $"{ResultVar} = cil2cpp::to_unsigned({Left}) {Op} cil2cpp::to_unsigned({Right});";
        }
        // ECMA-335 III.4.1: signed comparisons (clt/cgt).
        // CLI evaluation stack doesn't distinguish signed/unsigned — signedness comes from the
        // instruction. Use signed_lt/gt/ge/le to ensure signed semantics even when C++ operand
        // types are unsigned (e.g., uint64_t from ulong fields in Int128).
        // Cast to int32_t: same reason as above (MSVC C4805 prevention).
        if (Op == ">")
            return $"{ResultVar} = (int32_t)cil2cpp::signed_gt({Left}, {Right});";
        if (Op == "<")
            return $"{ResultVar} = (int32_t)cil2cpp::signed_lt({Left}, {Right});";
        if (Op == ">=")
            return $"{ResultVar} = (int32_t)cil2cpp::signed_ge({Left}, {Right});";
        if (Op == "<=")
            return $"{ResultVar} = (int32_t)cil2cpp::signed_le({Left}, {Right});";
        // == and != also produce bool in C++ — cast to int32_t for ECMA-335 compliance.
        if (Op is "==" or "!=")
            return $"{ResultVar} = (int32_t)({Left} {Op} {Right});";
        return $"{ResultVar} = {Left} {Op} {Right};";
    }
}

public class IRUnaryOp : IRInstruction
{
    public string Operand { get; set; } = "";
    public string Op { get; set; } = "";
    public string ResultVar { get; set; } = "";
    /// <summary>C++ result type for cross-scope variable pre-declarations.</summary>
    public string? ResultTypeCpp { get; set; }
    public override string ToCpp() => $"{ResultVar} = {Op}{Operand};";
}

public class IRBranch : IRInstruction
{
    public string TargetLabel { get; set; } = "";
    public override string ToCpp() => $"goto {TargetLabel};";
}

public class IRConditionalBranch : IRInstruction
{
    public string Condition { get; set; } = "";
    public string TrueLabel { get; set; } = "";
    public string? FalseLabel { get; set; }

    public override string ToCpp()
    {
        if (FalseLabel != null)
            return $"if ({Condition}) goto {TrueLabel}; else goto {FalseLabel};";
        return $"if ({Condition}) goto {TrueLabel};";
    }
}

public class IRLabel : IRInstruction
{
    public string LabelName { get; set; } = "";
    public override string ToCpp() => $"{LabelName}:";
}

public class IRSwitch : IRInstruction
{
    public string ValueExpr { get; set; } = "";
    public List<string> CaseLabels { get; } = new();

    public override string ToCpp()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"switch ({ValueExpr}) {{");
        for (int i = 0; i < CaseLabels.Count; i++)
            sb.AppendLine($"        case {i}: goto {CaseLabels[i]};");
        sb.Append("    }");
        return sb.ToString();
    }
}

public class IRFieldAccess : IRInstruction
{
    public string ObjectExpr { get; set; } = "";
    public string FieldCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }
    /// <summary>
    /// True when the object expression is a value (struct) rather than a pointer.
    /// Uses `.` instead of `->` for field access.
    /// </summary>
    public bool IsValueAccess { get; set; }
    /// <summary>
    /// Optional C++ type name to cast the object to before field access.
    /// Used when the stack type (e.g. Object*) differs from the field's declaring type.
    /// </summary>
    public string? CastToType { get; set; }
    /// <summary>C++ type of the field being accessed (used for cross-scope variable pre-declarations).</summary>
    public string? ResultTypeCpp { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddPointerType(CastToType, pointerTypeNames);
        TryAddPointerType(ResultTypeCpp, pointerTypeNames);
    }

    public override string ToCpp()
    {
        var obj = ObjectExpr;
        // Apply declaring-type cast if needed (e.g. Object* → ContingentProperties*)
        if (CastToType != null && !IsValueAccess)
            obj = $"(({CastToType}*){obj})";
        string access;
        if (IsValueAccess)
        {
            // Value type: use dot accessor
            access = $"{obj}.{FieldCppName}";
        }
        else if (obj.StartsWith("&") || obj.StartsWith("(("))
        {
            // Address or cast expression: needs parentheses for correct precedence
            access = $"({obj})->{FieldCppName}";
        }
        else
        {
            access = $"{obj}->{FieldCppName}";
        }

        if (IsStore)
            return $"{access} = {StoreValue};";
        // Cast result to IL field type for flat struct model (runtime field may be Object*)
        // Skip cast for unresolved generic params (!0, !!0), primitive types, and value types
        if (ResultTypeCpp != null && ResultTypeCpp.EndsWith("*")
            && ResultTypeCpp != "void*" && !IsValueAccess
            && !ResultTypeCpp.Contains('!') && !ResultTypeCpp.Contains('<'))
            return $"{ResultVar} = ({ResultTypeCpp})(void*){access};";
        return $"{ResultVar} = {access};";
    }
}

public class IRStaticFieldAccess : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public string FieldCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }
    /// <summary>C++ type of the static field being accessed (used for cross-scope variable pre-declarations).</summary>
    public string? ResultTypeCpp { get; set; }

    public override string ToCpp()
    {
        var fullName = $"{TypeCppName}_statics.{FieldCppName}";
        if (IsStore)
            return $"{fullName} = {StoreValue};";
        // Cast result to IL field type for flat struct model
        if (ResultTypeCpp != null && ResultTypeCpp.EndsWith("*") && ResultTypeCpp != "void*"
            && !ResultTypeCpp.Contains('!') && !ResultTypeCpp.Contains('<'))
            return $"{ResultVar} = ({ResultTypeCpp})(void*){fullName};";
        return $"{ResultVar} = {fullName};";
    }
}

public class IRArrayAccess : IRInstruction
{
    public string ArrayExpr { get; set; } = "";
    public string IndexExpr { get; set; } = "";
    public string ElementType { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddPointerType(ElementType, pointerTypeNames);
    }

    public override string ToCpp()
    {
        // Ensure the array expression is cast to Array* for array_get/array_set.
        // Jagged arrays: inner arrays are loaded as Object* (from array_get<Object*>),
        // but array_get<T>/array_set<T> require Array* as first argument.
        // Always cast — redundant for Array*-typed expressions but necessary for Object*.
        var arrExpr = $"(cil2cpp::Array*){ArrayExpr}";
        if (IsStore)
            return $"cil2cpp::array_set<{ElementType}>({arrExpr}, {IndexExpr}, {StoreValue});";
        return $"{ResultVar} = cil2cpp::array_get<{ElementType}>({arrExpr}, {IndexExpr});";
    }
}

public class IRCast : IRInstruction
{
    public string SourceExpr { get; set; } = "";
    public string TargetTypeCpp { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsSafe { get; set; } // 'as' vs cast
    /// <summary>TypeInfo name for the cast (without _TypeInfo suffix). Defaults to TargetTypeCpp sans pointer.</summary>
    public string? TypeInfoCppName { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        var typeInfo = TypeInfoCppName ?? TargetTypeCpp.TrimEnd('*');
        TryAddTypeInfo(typeInfo, typeInfoNames);
        TryAddPointerType(TargetTypeCpp, pointerTypeNames);
    }

    public override string ToCpp()
    {
        var typeInfo = TypeInfoCppName ?? TargetTypeCpp.TrimEnd('*');
        if (IsSafe)
            return $"{ResultVar} = ({TargetTypeCpp})cil2cpp::object_as((cil2cpp::Object*){SourceExpr}, &{typeInfo}_TypeInfo);";
        return $"{ResultVar} = ({TargetTypeCpp})cil2cpp::object_cast((cil2cpp::Object*){SourceExpr}, &{typeInfo}_TypeInfo);";
    }
}

public class IRConversion : IRInstruction
{
    public string SourceExpr { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string ResultVar { get; set; } = "";
    /// <summary>
    /// C++ type of the source expression (if known). Used to detect pointer→integer casts
    /// that need C-style cast instead of static_cast (MSVC C2440).
    /// </summary>
    public string? SourceCppType { get; set; }

    public override string ToCpp()
    {
        // uintptr_t/intptr_t targets may receive pointer values (conv.u/conv.i on pointers)
        // and pointer targets may receive integer values — use C-style cast for these
        // since static_cast can't handle pointer↔integer conversions
        bool useCStyleCast = TargetType is "uintptr_t" or "intptr_t"
            || TargetType.EndsWith("*");
        // Also use C-style cast when source is a pointer type and target is any integer
        // (e.g., conv.u8 on void* → (uint64_t)(ptr) instead of static_cast<uint64_t>(ptr))
        bool sourceIsPointer = SourceCppType != null && SourceCppType.EndsWith("*");
        if (!useCStyleCast && sourceIsPointer)
            useCStyleCast = true;
        if (useCStyleCast)
        {
            // When casting pointer to a non-pointer-sized integer (e.g. void* → int32_t),
            // go through uintptr_t first to avoid MSVC C4311 (pointer truncation warning)
            if (sourceIsPointer && TargetType is not "uintptr_t" and not "intptr_t"
                && !TargetType.EndsWith("*"))
                return $"{ResultVar} = ({TargetType})(uintptr_t)({SourceExpr});";
            return $"{ResultVar} = ({TargetType})({SourceExpr});";
        }
        // ECMA-335: conv.u8 zero-extends from int32 (not sign-extends).
        // In C++, static_cast<uint64_t>(int32_t) sign-extends, so we first
        // convert to unsigned (same width) via to_unsigned(), then widen.
        if (TargetType == "uint64_t")
            return $"{ResultVar} = static_cast<uint64_t>(cil2cpp::to_unsigned({SourceExpr}));";
        return $"{ResultVar} = static_cast<{TargetType}>({SourceExpr});";
    }
}

public class IRNullCheck : IRInstruction
{
    public string Expr { get; set; } = "";
    public override string ToCpp() => $"cil2cpp::null_check({Expr});";
}

public class IRInitObj : IRInstruction
{
    public string AddressExpr { get; set; } = "";
    public string TypeCppName { get; set; } = "";
    /// <summary>
    /// ECMA-335 III.4.12: For reference types, initobj sets the location to null.
    /// For value types, it zeroes the memory.
    /// </summary>
    public bool IsReferenceType { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddPointerType(TypeCppName, pointerTypeNames);
    }

    public override string ToCpp() =>
        IsReferenceType
            ? $"*({TypeCppName}**)({AddressExpr}) = nullptr;"
            : $"std::memset({AddressExpr}, 0, sizeof({TypeCppName}));";
}

public class IRBox : IRInstruction
{
    public string ValueExpr { get; set; } = "";
    public string ValueTypeCppName { get; set; } = "";
    /// <summary>TypeInfo reference name (mangled IL name). Falls back to ValueTypeCppName if null.</summary>
    public string? TypeInfoCppName { get; set; }
    public string ResultVar { get; set; } = "";

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        var typeInfoName = TypeInfoCppName ?? ValueTypeCppName;
        TryAddTypeInfo(typeInfoName, typeInfoNames);
    }

    public override string ToCpp()
    {
        var typeInfoName = TypeInfoCppName ?? ValueTypeCppName;
        return $"{ResultVar} = cil2cpp::box<{ValueTypeCppName}>({ValueExpr}, &{typeInfoName}_TypeInfo);";
    }
}

public class IRUnbox : IRInstruction
{
    public string ObjectExpr { get; set; } = "";
    public string ValueTypeCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsUnboxAny { get; set; }
    /// <summary>C++ result type for cross-scope variable pre-declarations.</summary>
    public string? ResultTypeCpp { get; set; }
    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddPointerType(ValueTypeCppName, pointerTypeNames);
    }

    // Always cast to Object* — generated flat structs don't inherit from cil2cpp::Object,
    // but have the same memory layout (type_info + sync_block + fields).
    public override string ToCpp() => IsUnboxAny
        ? $"{ResultVar} = cil2cpp::unbox<{ValueTypeCppName}>(reinterpret_cast<cil2cpp::Object*>({ObjectExpr}));"
        : $"{ResultVar} = cil2cpp::unbox_ptr<{ValueTypeCppName}>(reinterpret_cast<cil2cpp::Object*>({ObjectExpr}));";
}

public class IRStaticCtorGuard : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public override string ToCpp() => $"{TypeCppName}_ensure_cctor();";
}

// ============ Exception Handling Instructions ============

public class IRTryBegin : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_TRY";
}

public class IRCatchBegin : IRInstruction
{
    public string? ExceptionTypeCppName { get; set; }
    /// <summary>True when this catch follows a filter handler in the same try block.
    /// Uses inline if(!__exc_caught) instead of the } else if (...) macro chain.</summary>
    public bool AfterFilter { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddTypeInfo(ExceptionTypeCppName, typeInfoNames);
    }

    public override string ToCpp()
    {
        if (!AfterFilter)
            return ExceptionTypeCppName != null
                ? $"CIL2CPP_CATCH({ExceptionTypeCppName})" : "CIL2CPP_CATCH_ALL";
        // After a filter: we're already inside the else block, use conditional check
        if (ExceptionTypeCppName != null)
            return $"if (!__exc_caught && cil2cpp::object_is_instance_of(reinterpret_cast<cil2cpp::Object*>(__exc_ctx.current_exception), &{ExceptionTypeCppName}_TypeInfo)) {{ __exc_caught = true;";
        return "if (!__exc_caught) { __exc_caught = true;";
    }
}

public class IRFinallyBegin : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_FINALLY";
}

/// <summary>
/// Fault handler: like finally but only runs on exception (not normal exit).
/// Emits CIL2CPP_FINALLY with a conditional guard around the handler body.
/// The matching endfinally/endfault in IL closes the guard with IRFaultEnd.
/// </summary>
public class IRFaultBegin : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_FINALLY\n    if (__exc_ctx.current_exception != nullptr) {";
}

/// <summary>
/// Closes the conditional guard opened by IRFaultBegin.
/// Emitted just before endfinally/endfault in fault handler regions.
/// </summary>
public class IRFaultEnd : IRInstruction
{
    public override string ToCpp() => "    }";
}

public class IRTryEnd : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_END_TRY";
}

public class IRFilterBegin : IRInstruction
{
    /// <summary>True if this is the first handler in the try block (needs } else { to enter handler chain).</summary>
    public bool IsFirst { get; set; } = true;
    /// <summary>Per-try-block filter index (0-based). Used for goto labels in filter chains.</summary>
    public int FilterIndex { get; set; }
    public override string ToCpp() => IsFirst
        ? "CIL2CPP_FILTER_BEGIN"
        : $"__filter_next_{FilterIndex}:";
}

public class IREndFilter : IRInstruction
{
    /// <summary>True if this is the last filter in the chain — rejection rethrows to outer scope.</summary>
    public bool IsLastFilter { get; set; } = true;
    /// <summary>Index of the NEXT filter (used for goto on rejection).</summary>
    public int NextFilterIndex { get; set; }
    /// <summary>Complete filter result check: accepts (sets __exc_caught) or jumps to next filter/rethrows.</summary>
    public override string ToCpp() => IsLastFilter
        ? $"if (__filter_result) {{ __exc_caught = true; }} else {{ CIL2CPP_RETHROW; }}"
        : $"if (__filter_result) {{ __exc_caught = true; }} else {{ goto __filter_next_{NextFilterIndex}; }}";
}

/// <summary>Marks the end of a filter handler body (no-op — IREndFilter is self-contained).</summary>
public class IRFilterHandlerEnd : IRInstruction
{
    public override string ToCpp() => "// end filter handler";
}

public class IRThrow : IRInstruction
{
    public string ExceptionExpr { get; set; } = "";
    public override string ToCpp() =>
        $"cil2cpp::throw_exception((cil2cpp::Exception*){ExceptionExpr});";
}

public class IRRethrow : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_RETHROW;";
}

public class IRRawCpp : IRInstruction
{
    public string Code { get; set; } = "";
    /// <summary>Optional: the temp variable this instruction writes to (for type tracking).</summary>
    public string? ResultVar { get; set; }
    /// <summary>Optional: the C++ type of the result (for cross-scope variable pre-declarations).</summary>
    public string? ResultTypeCpp { get; set; }

    /// <summary>
    /// IRRawCpp is the escape hatch for arbitrary C++ code without structured type properties.
    /// Fall back to string scanning on the Code property to capture _TypeInfo and pointer type refs.
    /// </summary>
    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        ScanCodeForTypeInfoRefs(Code, typeInfoNames);
        ScanCodeForPointerTypeRefs(Code, pointerTypeNames);
    }

    /// <summary>Scan raw C++ code for _TypeInfo references (same logic as the deleted CollectTypeInfoRefs).</summary>
    private static void ScanCodeForTypeInfoRefs(string code, HashSet<string> result)
    {
        var tiIdx = 0;
        while ((tiIdx = code.IndexOf("_TypeInfo", tiIdx)) >= 0)
        {
            var afterTi = tiIdx + 9;
            if (afterTi < code.Length)
            {
                var nextChar = code[afterTi];
                if (nextChar == '(') { tiIdx = afterTi; continue; }
                if (afterTi + 9 <= code.Length && code.Substring(afterTi, 9) == "_TypeInfo")
                {
                    var afterDouble = afterTi + 9;
                    if (afterDouble < code.Length && code[afterDouble] == '(') { tiIdx = afterDouble; continue; }
                }
                else if (char.IsLetterOrDigit(nextChar) || nextChar == '_')
                {
                    tiIdx = afterTi; continue;
                }
            }
            int start = tiIdx - 1;
            while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] == '_')) start--;
            start++;
            if (start < tiIdx)
            {
                var typeName = code[start..tiIdx];
                if (start >= 2 && code[(start - 2)..start] == "::") { tiIdx += 9; continue; }
                if (!string.IsNullOrEmpty(typeName) && !typeName.StartsWith("cil2cpp"))
                {
                    if (afterTi + 9 <= code.Length && code.Substring(afterTi, 9) == "_TypeInfo")
                    {
                        result.Add(typeName + "_TypeInfo");
                        tiIdx = afterTi + 9;
                        continue;
                    }
                    result.Add(typeName);
                }
            }
            tiIdx += 9;
        }
    }

    /// <summary>Scan raw C++ code for pointer type references (same logic as deleted CollectBodyPointerTypeRefs).</summary>
    private static void ScanCodeForPointerTypeRefs(string code, HashSet<string> result)
    {
        int i = 0;
        while (i < code.Length)
        {
            int starIdx = code.IndexOf('*', i);
            if (starIdx < 0) break;
            int end = starIdx;
            while (end > 0 && code[end - 1] == '*') end--;
            int start = end - 1;
            while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] == '_')) start--;
            start++;
            if (start < end)
            {
                var typeName = code[start..end];
                if (typeName.Length > 3 && typeName.Contains('_') && char.IsUpper(typeName[0]))
                {
                    if (!(start >= 2 && code[(start - 2)..start] == "::"))
                        result.Add(typeName);
                }
            }
            i = starIdx + 1;
        }
    }

    public override string ToCpp() => Code;
}

// ============ Delegate Instructions ============

public class IRLoadFunctionPointer : IRInstruction
{
    public string MethodCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsVirtual { get; set; }
    public string? ObjectExpr { get; set; }
    public int VTableSlot { get; set; } = -1;
    public bool IsInterfaceCall { get; set; }
    public string? InterfaceTypeCppName { get; set; }

    public override string ToCpp()
    {
        if (IsVirtual && VTableSlot >= 0 && ObjectExpr != null)
        {
            if (IsInterfaceCall && InterfaceTypeCppName != null)
                return $"cil2cpp::null_check((void*){ObjectExpr}); {ResultVar} = cil2cpp::obj_get_interface_vtable(((cil2cpp::Object*){ObjectExpr}), &{InterfaceTypeCppName}_TypeInfo)->methods[{VTableSlot}];";
            return $"cil2cpp::null_check((void*){ObjectExpr}); {ResultVar} = ((cil2cpp::Object*){ObjectExpr})->__type_info->vtable->methods[{VTableSlot}];";
        }
        return $"{ResultVar} = (void*){MethodCppName};";
    }
}

public class IRDelegateCreate : IRInstruction
{
    public string DelegateTypeCppName { get; set; } = "";
    public string TargetExpr { get; set; } = "";
    public string FunctionPtrExpr { get; set; } = "";
    public string ResultVar { get; set; } = "";

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        TryAddTypeInfo(DelegateTypeCppName, typeInfoNames);
    }

    public override string ToCpp() =>
        $"{ResultVar} = cil2cpp::delegate_create(&{DelegateTypeCppName}_TypeInfo, (cil2cpp::Object*){TargetExpr}, {FunctionPtrExpr});";
}

public class IRDelegateInvoke : IRInstruction
{
    public string DelegateExpr { get; set; } = "";
    public string ReturnTypeCpp { get; set; } = "void";
    public List<string> ParamTypes { get; } = new();
    public List<string> Arguments { get; } = new();
    public string? ResultVar { get; set; }

    public override void CollectTypeReferences(HashSet<string> typeInfoNames, HashSet<string> pointerTypeNames)
    {
        foreach (var pt in ParamTypes)
            TryAddPointerType(pt, pointerTypeNames);
        TryAddPointerType(ReturnTypeCpp, pointerTypeNames);
    }

    public override string ToCpp()
    {
        var del = $"((cil2cpp::Delegate*){DelegateExpr})";

        // Cast arguments to expected parameter types.
        // In our flat struct model (no C++ inheritance), pointer casts between
        // generated types require explicit (void*) intermediate casts.
        var castedArgs = new List<string>();
        for (int i = 0; i < Arguments.Count; i++)
        {
            if (i < ParamTypes.Count && ParamTypes[i].EndsWith("*") && ParamTypes[i] != "void*")
            {
                castedArgs.Add($"({ParamTypes[i]})(void*){Arguments[i]}");
            }
            else
            {
                castedArgs.Add(Arguments[i]);
            }
        }

        // Helper: generate single-delegate invoke code
        string SingleInvoke(string delExpr)
        {
            var instanceParamTypes = new List<string> { "cil2cpp::Object*" };
            instanceParamTypes.AddRange(ParamTypes);
            var instanceFnPtr = $"{ReturnTypeCpp}(*)({string.Join(", ", instanceParamTypes)})";
            var instanceArgs = new List<string> { $"cil2cpp::delegate_adjust_target({delExpr}->target)" };
            instanceArgs.AddRange(castedArgs);
            var instanceCall = $"(({instanceFnPtr})({delExpr}->method_ptr))({string.Join(", ", instanceArgs)})";

            var staticFnPtr = ParamTypes.Count > 0
                ? $"{ReturnTypeCpp}(*)({string.Join(", ", ParamTypes)})"
                : $"{ReturnTypeCpp}(*)()";
            var staticCall = $"(({staticFnPtr})({delExpr}->method_ptr))({string.Join(", ", castedArgs)})";

            if (ResultVar != null)
                return $"{ResultVar} = {delExpr}->target ? {instanceCall} : {staticCall}";
            return $"if ({delExpr}->target) {{ {instanceCall}; }} else {{ {staticCall}; }}";
        }

        // Generate multicast-aware invoke: check invocation_count, loop if multicast
        var sb = new System.Text.StringBuilder();
        if (ResultVar != null)
            sb.AppendLine($"{ReturnTypeCpp} {ResultVar};");
        else
            sb.AppendLine();

        sb.Append($"    if ({del}->invocation_count > 0) {{ ");
        sb.Append($"for (int32_t __di = 0; __di < {del}->invocation_count; __di++) {{ ");
        sb.Append($"auto* __item = cil2cpp::delegate_get_invocation_item({del}, __di); ");
        sb.Append($"{SingleInvoke("__item")}; ");
        sb.Append($"}} ");
        sb.Append($"}} else {{ ");
        sb.Append($"{SingleInvoke(del)}; ");
        sb.Append($"}}");

        return sb.ToString().TrimStart('\n').TrimStart('\r');
    }
}
