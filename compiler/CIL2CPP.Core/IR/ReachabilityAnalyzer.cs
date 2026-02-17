using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Result of reachability analysis: the sets of reachable types and methods.
/// </summary>
public class ReachabilityResult
{
    public HashSet<TypeDefinition> ReachableTypes { get; } = new();
    public HashSet<MethodDefinition> ReachableMethods { get; } = new();

    public bool IsReachable(TypeDefinition type) => ReachableTypes.Contains(type);
    public bool IsReachable(MethodDefinition method) => ReachableMethods.Contains(method);
}

/// <summary>
/// Performs reachability analysis (tree shaking) starting from entry point(s).
/// Uses method-level granularity: only methods that are actually called are marked reachable.
/// All types (user and BCL) use the same worklist-driven analysis — no blanket seeding.
/// BCL types in deep internal namespaces are filtered at the boundary.
/// </summary>
public class ReachabilityAnalyzer
{
    private readonly AssemblySet _assemblySet;
    private readonly ReachabilityResult _result = new();
    private readonly Queue<MethodDefinition> _worklist = new();
    private readonly HashSet<string> _processedMethods = new();

    // Track dispatched virtual method slots for deferred override resolution.
    // Key: "MethodName/ParamCount"
    private readonly HashSet<string> _dispatchedVirtualSlots = new();

    public ReachabilityAnalyzer(AssemblySet assemblySet)
    {
        _assemblySet = assemblySet;
    }

    /// <summary>
    /// Run the reachability analysis and return the result.
    /// When forceLibraryMode is true, seeds from all types/methods regardless of entry point.
    /// </summary>
    public ReachabilityResult Analyze(bool forceLibraryMode = false)
    {
        // Seed: find the entry point
        var entryPoint = _assemblySet.RootAssembly.EntryPoint;
        if (forceLibraryMode)
        {
            // Forced library mode: seed ALL types and methods (used by test fixture)
            SeedAllTypes(_assemblySet.RootAssembly);
        }
        else if (entryPoint != null)
        {
            SeedMethod(entryPoint);
        }
        else
        {
            // Library mode: seed all public types/methods
            SeedAllPublicTypes(_assemblySet.RootAssembly);
        }

        // Worklist fixpoint
        while (_worklist.Count > 0)
        {
            var method = _worklist.Dequeue();
            ProcessMethod(method);
        }

        return _result;
    }

    private void SeedMethod(MethodDefinition method)
    {
        if (!_result.ReachableMethods.Add(method))
            return;

        MarkTypeReachable(method.DeclaringType);
        _worklist.Enqueue(method);

        // If this is a virtual method, track it as a dispatched slot
        // and mark overrides in all already-reachable types
        if (method.IsVirtual)
            DispatchVirtualSlot(method);
    }

    private void SeedAllTypes(AssemblyDefinition assembly)
    {
        foreach (var type in assembly.MainModule.Types)
        {
            if (type.Name == "<Module>") continue;

            MarkTypeReachable(type);
            foreach (var method in type.Methods)
                SeedMethod(method);

            foreach (var nested in type.NestedTypes)
            {
                MarkTypeReachable(nested);
                foreach (var method in nested.Methods)
                    SeedMethod(method);
            }
        }
    }

    private void SeedAllPublicTypes(AssemblyDefinition assembly)
    {
        foreach (var type in assembly.MainModule.Types)
        {
            if (type.Name == "<Module>") continue;
            if (!type.IsPublic) continue;

            MarkTypeReachable(type);
            foreach (var method in type.Methods)
            {
                if (method.IsPublic || method.IsFamily)
                    SeedMethod(method);
            }
        }
    }

    private void MarkTypeReachable(TypeDefinition type)
    {
        // BCL boundary filtering — deep internal types are not useful to compile
        // Don't even add them to the reachable set (avoids incomplete type definitions)
        if (IsBclBoundaryType(type))
            return;

        if (!_result.ReachableTypes.Add(type))
            return;

        // Mark base types reachable (needed for struct layout / vtable)
        if (type.BaseType != null)
        {
            var baseTypeDef = TryResolve(type.BaseType);
            if (baseTypeDef != null)
                MarkTypeReachable(baseTypeDef);
        }

        // Mark interface types reachable
        foreach (var iface in type.Interfaces)
        {
            var ifaceDef = TryResolve(iface.InterfaceType);
            if (ifaceDef != null)
                MarkTypeReachable(ifaceDef);
        }

        // Mark static constructor reachable
        var cctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic);
        if (cctor != null)
            SeedMethod(cctor);

        // Mark finalizer reachable
        var finalizer = type.Methods.FirstOrDefault(m => m.Name == "Finalize" && m.IsVirtual);
        if (finalizer != null)
            SeedMethod(finalizer);

        // Mark field types reachable (needed for struct layout)
        foreach (var field in type.Fields)
        {
            var fieldTypeDef = TryResolve(field.FieldType);
            if (fieldTypeDef != null)
                MarkTypeReachable(fieldTypeDef);
        }

        // All types: check for virtual method overrides that match dispatched slots
        // (user types are no longer blanket-seeded — worklist discovers reachable methods)
        MarkDispatchedOverrides(type);
    }

    /// <summary>
    /// Track a virtual method as dispatched and mark overrides in all reachable types.
    /// </summary>
    private void DispatchVirtualSlot(MethodDefinition method)
    {
        var slot = $"{method.Name}/{method.Parameters.Count}";
        if (!_dispatchedVirtualSlots.Add(slot))
            return;

        // Check all already-reachable types for overrides of this slot
        foreach (var type in _result.ReachableTypes.ToArray())
        {
            MarkOverrideIfExists(type, method.Name, method.Parameters.Count);
        }
    }

    /// <summary>
    /// When a type becomes reachable, check if it overrides any dispatched virtual slot.
    /// </summary>
    private void MarkDispatchedOverrides(TypeDefinition type)
    {
        foreach (var slot in _dispatchedVirtualSlots)
        {
            var parts = slot.Split('/');
            if (parts.Length == 2 && int.TryParse(parts[1], out var paramCount))
            {
                MarkOverrideIfExists(type, parts[0], paramCount);
            }
        }
    }

    private void MarkOverrideIfExists(TypeDefinition type, string methodName, int paramCount)
    {
        var overrideMethod = type.Methods.FirstOrDefault(m =>
            m.IsVirtual && m.Name == methodName && m.Parameters.Count == paramCount);
        if (overrideMethod != null)
            SeedMethod(overrideMethod);
    }

    private bool IsUserAssembly(TypeDefinition type)
    {
        return type.Module.Assembly.Name.Name == _assemblySet.RootAssemblyName;
    }

    /// <summary>
    /// Primitive types that map directly to C++ primitives.
    /// Their struct definitions are NOT emitted (they use C++ built-in types),
    /// but their methods ARE compiled from BCL IL (CompareTo, Equals, etc.).
    /// </summary>
    internal static readonly HashSet<string> PrimitiveTypeNames = new()
    {
        "System.Void", "System.Boolean", "System.Byte", "System.SByte",
        "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64", "System.Single", "System.Double",
        "System.Char", "System.IntPtr", "System.UIntPtr",
    };

    /// <summary>
    /// Filter out BCL types that should not be compiled to C++.
    /// Unity IL2CPP model: ALL BCL types with IL bodies compile to C++.
    /// Primitive types pass through (their methods compile, but struct is skipped).
    /// Only filter compiler internals and module-level types.
    /// </summary>
    private bool IsBclBoundaryType(TypeDefinition type)
    {
        // Non-BCL types always pass through
        var assemblyName = type.Module.Assembly.Name.Name;
        if (!IsBclAssembly(assemblyName))
            return false;

        // Primitive types pass through — methods compile from BCL IL,
        // struct emission is skipped by the code generator (IsPrimitiveType flag)
        // System.Void is the exception — no useful methods to compile
        if (type.FullName == "System.Void")
            return true;

        // Filter module-level types only
        var fullName = type.FullName;
        if (fullName == "<Module>")
            return true;
        // Note: <PrivateImplementationDetails> is NOT filtered — BCL code uses it for
        // static array initialization data, and we need its statics in generated code.

        // Allow all other BCL types through — they compile from IL
        return false;
    }

    private static bool IsBclAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("System.") ||
               assemblyName == "System" ||
               assemblyName == "mscorlib" ||
               assemblyName == "netstandard" ||
               assemblyName.StartsWith("Microsoft.");
    }

    private void ProcessMethod(MethodDefinition method)
    {
        var key = GetMethodKey(method);
        if (!_processedMethods.Add(key))
            return;

        if (!method.HasBody)
            return;

        foreach (var instr in method.Body.Instructions)
        {
            switch (instr.OpCode.Code)
            {
                // Method references
                case Code.Call:
                case Code.Callvirt:
                case Code.Newobj:
                case Code.Ldftn:
                case Code.Ldvirtftn:
                    ProcessMethodRef(instr.Operand as MethodReference);
                    break;

                // Type references
                case Code.Newarr:
                case Code.Castclass:
                case Code.Isinst:
                case Code.Box:
                case Code.Unbox:
                case Code.Unbox_Any:
                case Code.Initobj:
                case Code.Ldobj:
                case Code.Stobj:
                case Code.Ldelem_Any:
                case Code.Stelem_Any:
                case Code.Ldelema:
                case Code.Sizeof:
                case Code.Constrained:
                    ProcessTypeRef(instr.Operand as TypeReference);
                    // Also resolve explicit interface implementations on the constrained type.
                    // The next instruction is call/callvirt with the interface method — we need
                    // to find and mark the concrete implementation on the constrained type.
                    ProcessConstrainedCall(instr.Operand as TypeReference, instr.Next);
                    break;

                // Field references
                case Code.Ldfld:
                case Code.Stfld:
                case Code.Ldsfld:
                case Code.Stsfld:
                case Code.Ldflda:
                case Code.Ldsflda:
                    ProcessFieldRef(instr.Operand as FieldReference);
                    break;

                // Ldtoken can be field, type, or method
                case Code.Ldtoken:
                    if (instr.Operand is MethodReference tokenMethod)
                        ProcessMethodRef(tokenMethod);
                    else if (instr.Operand is TypeReference tokenType)
                        ProcessTypeRef(tokenType);
                    else if (instr.Operand is FieldReference tokenField)
                        ProcessFieldRef(tokenField);
                    break;
            }
        }
    }

    private void ProcessMethodRef(MethodReference? methodRef)
    {
        if (methodRef == null) return;

        // Mark the declaring type reachable
        var declaringTypeDef = TryResolve(methodRef.DeclaringType);
        if (declaringTypeDef != null)
            MarkTypeReachable(declaringTypeDef);

        // Resolve and seed the target method
        var methodDef = TryResolveMethod(methodRef);
        if (methodDef != null)
            SeedMethod(methodDef);
    }

    /// <summary>
    /// When a constrained. prefix is followed by call/callvirt, resolve the interface method
    /// to the concrete type's explicit interface implementation and mark it reachable.
    /// Without this, static abstract interface methods (e.g., IUtfChar&lt;Char&gt;.CastFrom)
    /// remain as stubs because only the abstract interface method (with no body) is discovered.
    /// </summary>
    private void ProcessConstrainedCall(TypeReference? constrainedType, Mono.Cecil.Cil.Instruction? nextInstr)
    {
        if (constrainedType == null || nextInstr == null) return;
        if (nextInstr.OpCode.Code is not (Code.Call or Code.Callvirt)) return;

        var methodRef = nextInstr.Operand as MethodReference;
        if (methodRef == null) return;

        var constrainedTypeDef = TryResolve(constrainedType);
        if (constrainedTypeDef == null) return;

        var interfaceMethodName = methodRef.Name;

        // Search for explicit interface implementations that match the method name
        foreach (var method in constrainedTypeDef.Methods)
        {
            if (!method.IsStatic) continue;

            // Exact match or explicit interface impl suffix match
            // e.g., "CastFrom" matches "System.IUtfChar<System.Char>.CastFrom"
            bool nameMatch = method.Name == interfaceMethodName;
            if (!nameMatch)
            {
                var lastDot = method.Name.LastIndexOf('.');
                if (lastDot >= 0 && method.Name.AsSpan(lastDot + 1).SequenceEqual(interfaceMethodName))
                    nameMatch = true;
            }

            if (nameMatch && method.Parameters.Count == methodRef.Parameters.Count)
                SeedMethod(method);
        }
    }

    private void ProcessTypeRef(TypeReference? typeRef)
    {
        if (typeRef == null) return;

        var typeDef = TryResolve(typeRef);
        if (typeDef != null)
            MarkTypeReachable(typeDef);
    }

    private void ProcessFieldRef(FieldReference? fieldRef)
    {
        if (fieldRef == null) return;

        var declaringTypeDef = TryResolve(fieldRef.DeclaringType);
        if (declaringTypeDef != null)
            MarkTypeReachable(declaringTypeDef);

        var fieldTypeDef = TryResolve(fieldRef.FieldType);
        if (fieldTypeDef != null)
            MarkTypeReachable(fieldTypeDef);
    }

    private TypeDefinition? TryResolve(TypeReference typeRef)
    {
        try
        {
            // Handle generic instances
            if (typeRef is GenericInstanceType git)
            {
                // Resolve the element type
                var elemDef = TryResolve(git.ElementType);
                // Mark generic argument types as reachable
                foreach (var arg in git.GenericArguments)
                {
                    var argDef = TryResolve(arg);
                    if (argDef != null)
                        MarkTypeReachable(argDef);
                }
                return elemDef;
            }

            // Handle array types
            if (typeRef is ArrayType arrayType)
            {
                var elemDef = TryResolve(arrayType.ElementType);
                if (elemDef != null)
                    MarkTypeReachable(elemDef);
                return null; // Arrays are handled by the runtime
            }

            // Handle by-ref and pointer types
            if (typeRef is ByReferenceType byRef)
                return TryResolve(byRef.ElementType);
            if (typeRef is PointerType ptr)
                return TryResolve(ptr.ElementType);

            // Skip generic parameters (T, U, etc.)
            if (typeRef is GenericParameter)
                return null;

            var resolved = typeRef.Resolve();
            if (resolved != null)
            {
                // Ensure the assembly is loaded in our set
                _assemblySet.LoadAssembly(resolved.Module.Assembly.Name.Name);
            }
            return resolved;
        }
        catch
        {
            return null;
        }
    }

    private MethodDefinition? TryResolveMethod(MethodReference methodRef)
    {
        try
        {
            // Handle generic instance methods
            if (methodRef is GenericInstanceMethod gim)
            {
                foreach (var arg in gim.GenericArguments)
                    TryResolve(arg);
                return TryResolveMethod(gim.ElementMethod);
            }

            return methodRef.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static string GetMethodKey(MethodDefinition method)
    {
        return $"{method.DeclaringType.FullName}::{method.FullName}";
    }
}
