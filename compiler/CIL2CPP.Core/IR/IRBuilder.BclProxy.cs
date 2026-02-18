namespace CIL2CPP.Core.IR;

/// <summary>
/// Creates proxy IRTypes for well-known BCL interfaces whose closed generic forms
/// may not appear in the reachability result. Open generic types (e.g. IEquatable`1)
/// are loaded from BCL IL, but closed forms (e.g. IEquatable&lt;int&gt;) might not be
/// in the type cache. This proxy creates minimal IRType shells for interface dispatch.
/// For non-generic BCL interfaces, this is typically a no-op since they are loaded
/// from BCL IL via reachability analysis.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Specification for a BCL interface method.
    /// </summary>
    private record BclMethodSpec(
        string Name,
        string ReturnTypeIL,
        string[] ParameterTypeILs);

    /// <summary>
    /// Specification for a well-known BCL interface.
    /// </summary>
    private record BclInterfaceSpec(
        string Namespace,
        string Name,
        BclMethodSpec[] Methods,
        bool IsGeneric = false,
        int GenericArity = 0);

    /// <summary>
    /// Well-known non-generic BCL interfaces that user code commonly implements.
    /// Each entry maps the full IL name to its interface specification.
    /// </summary>
    private static readonly Dictionary<string, BclInterfaceSpec> WellKnownBclInterfaces = new()
    {
        ["System.IDisposable"] = new("System", "IDisposable", new[]
        {
            new BclMethodSpec("Dispose", "System.Void", Array.Empty<string>()),
        }),
        ["System.IComparable"] = new("System", "IComparable", new[]
        {
            new BclMethodSpec("CompareTo", "System.Int32", new[] { "System.Object" }),
        }),
        ["System.ICloneable"] = new("System", "ICloneable", new[]
        {
            new BclMethodSpec("Clone", "System.Object", Array.Empty<string>()),
        }),
        ["System.Collections.IEnumerable"] = new("System.Collections", "IEnumerable", new[]
        {
            new BclMethodSpec("GetEnumerator", "System.Collections.IEnumerator", Array.Empty<string>()),
        }),
        ["System.Collections.IEnumerator"] = new("System.Collections", "IEnumerator", new[]
        {
            new BclMethodSpec("MoveNext", "System.Boolean", Array.Empty<string>()),
            new BclMethodSpec("get_Current", "System.Object", Array.Empty<string>()),
            new BclMethodSpec("Reset", "System.Void", Array.Empty<string>()),
        }),
        ["System.Collections.ICollection"] = new("System.Collections", "ICollection", new[]
        {
            new BclMethodSpec("get_Count", "System.Int32", Array.Empty<string>()),
            new BclMethodSpec("get_SyncRoot", "System.Object", Array.Empty<string>()),
            new BclMethodSpec("get_IsSynchronized", "System.Boolean", Array.Empty<string>()),
            new BclMethodSpec("CopyTo", "System.Void", new[] { "System.Array", "System.Int32" }),
        }),
        ["System.IAsyncDisposable"] = new("System", "IAsyncDisposable", new[]
        {
            new BclMethodSpec("DisposeAsync", "System.Threading.Tasks.ValueTask", Array.Empty<string>()),
        }),
    };

    /// <summary>
    /// Well-known generic BCL interface patterns (keyed by open type name without arity suffix).
    /// The "T" placeholder in parameter/return types is replaced with the actual generic argument.
    /// </summary>
    private static readonly Dictionary<string, BclInterfaceSpec> WellKnownGenericBclInterfaces = new()
    {
        ["System.IEquatable`1"] = new("System", "IEquatable", new[]
        {
            new BclMethodSpec("Equals", "System.Boolean", new[] { "T" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.IComparable`1"] = new("System", "IComparable", new[]
        {
            new BclMethodSpec("CompareTo", "System.Int32", new[] { "T" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IEnumerable`1"] = new("System.Collections.Generic", "IEnumerable", new[]
        {
            new BclMethodSpec("GetEnumerator", "System.Collections.Generic.IEnumerator`1<T>", Array.Empty<string>()),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IEnumerator`1"] = new("System.Collections.Generic", "IEnumerator", new[]
        {
            new BclMethodSpec("get_Current", "T", Array.Empty<string>()),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.ICollection`1"] = new("System.Collections.Generic", "ICollection", new[]
        {
            new BclMethodSpec("get_Count", "System.Int32", Array.Empty<string>()),
            new BclMethodSpec("get_IsReadOnly", "System.Boolean", Array.Empty<string>()),
            new BclMethodSpec("Add", "System.Void", new[] { "T" }),
            new BclMethodSpec("Clear", "System.Void", Array.Empty<string>()),
            new BclMethodSpec("Contains", "System.Boolean", new[] { "T" }),
            new BclMethodSpec("Remove", "System.Boolean", new[] { "T" }),
            new BclMethodSpec("CopyTo", "System.Void", new[] { "T[]", "System.Int32" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IList`1"] = new("System.Collections.Generic", "IList", new[]
        {
            new BclMethodSpec("get_Item", "T", new[] { "System.Int32" }),
            new BclMethodSpec("set_Item", "System.Void", new[] { "System.Int32", "T" }),
            new BclMethodSpec("IndexOf", "System.Int32", new[] { "T" }),
            new BclMethodSpec("Insert", "System.Void", new[] { "System.Int32", "T" }),
            new BclMethodSpec("RemoveAt", "System.Void", new[] { "System.Int32" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IReadOnlyCollection`1"] = new("System.Collections.Generic", "IReadOnlyCollection", new[]
        {
            new BclMethodSpec("get_Count", "System.Int32", Array.Empty<string>()),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IReadOnlyList`1"] = new("System.Collections.Generic", "IReadOnlyList", new[]
        {
            new BclMethodSpec("get_Item", "T", new[] { "System.Int32" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.ISet`1"] = new("System.Collections.Generic", "ISet", new[]
        {
            new BclMethodSpec("Add", "System.Boolean", new[] { "T" }),
            new BclMethodSpec("Contains", "System.Boolean", new[] { "T" }),
            new BclMethodSpec("IsSubsetOf", "System.Boolean", new[] { "System.Collections.Generic.IEnumerable`1<T>" }),
            new BclMethodSpec("IsSupersetOf", "System.Boolean", new[] { "System.Collections.Generic.IEnumerable`1<T>" }),
            new BclMethodSpec("UnionWith", "System.Void", new[] { "System.Collections.Generic.IEnumerable`1<T>" }),
            new BclMethodSpec("IntersectWith", "System.Void", new[] { "System.Collections.Generic.IEnumerable`1<T>" }),
            new BclMethodSpec("ExceptWith", "System.Void", new[] { "System.Collections.Generic.IEnumerable`1<T>" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IComparer`1"] = new("System.Collections.Generic", "IComparer", new[]
        {
            new BclMethodSpec("Compare", "System.Int32", new[] { "T", "T" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IEqualityComparer`1"] = new("System.Collections.Generic", "IEqualityComparer", new[]
        {
            new BclMethodSpec("Equals", "System.Boolean", new[] { "T", "T" }),
            new BclMethodSpec("GetHashCode", "System.Int32", new[] { "T" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IDictionary`2"] = new("System.Collections.Generic", "IDictionary", new[]
        {
            new BclMethodSpec("get_Item", "TValue", new[] { "TKey" }),
            new BclMethodSpec("set_Item", "System.Void", new[] { "TKey", "TValue" }),
            new BclMethodSpec("get_Keys", "System.Collections.Generic.ICollection`1<TKey>", Array.Empty<string>()),
            new BclMethodSpec("get_Values", "System.Collections.Generic.ICollection`1<TValue>", Array.Empty<string>()),
            new BclMethodSpec("Add", "System.Void", new[] { "TKey", "TValue" }),
            new BclMethodSpec("ContainsKey", "System.Boolean", new[] { "TKey" }),
            new BclMethodSpec("Remove", "System.Boolean", new[] { "TKey" }),
            new BclMethodSpec("TryGetValue", "System.Boolean", new[] { "TKey", "TValue&" }),
        }, IsGeneric: true, GenericArity: 2),
        ["System.Collections.Generic.IReadOnlyDictionary`2"] = new("System.Collections.Generic", "IReadOnlyDictionary", new[]
        {
            new BclMethodSpec("get_Item", "TValue", new[] { "TKey" }),
            new BclMethodSpec("get_Keys", "System.Collections.Generic.IEnumerable`1<TKey>", Array.Empty<string>()),
            new BclMethodSpec("get_Values", "System.Collections.Generic.IEnumerable`1<TValue>", Array.Empty<string>()),
            new BclMethodSpec("ContainsKey", "System.Boolean", new[] { "TKey" }),
            new BclMethodSpec("TryGetValue", "System.Boolean", new[] { "TKey", "TValue&" }),
        }, IsGeneric: true, GenericArity: 2),
        ["System.Collections.Generic.IAsyncEnumerable`1"] = new("System.Collections.Generic", "IAsyncEnumerable", new[]
        {
            new BclMethodSpec("GetAsyncEnumerator", "System.Collections.Generic.IAsyncEnumerator`1<T>", new[] { "System.Threading.CancellationToken" }),
        }, IsGeneric: true, GenericArity: 1),
        ["System.Collections.Generic.IAsyncEnumerator`1"] = new("System.Collections.Generic", "IAsyncEnumerator", new[]
        {
            new BclMethodSpec("MoveNextAsync", "System.Threading.Tasks.ValueTask`1<System.Boolean>", Array.Empty<string>()),
            new BclMethodSpec("get_Current", "T", Array.Empty<string>()),
        }, IsGeneric: true, GenericArity: 1),
    };

    /// <summary>
    /// Creates proxy IRTypes for BCL interfaces referenced by user types.
    /// Called after Pass 1 (type shells) and before Pass 2 (type details).
    /// Scans all user types' Cecil interfaces and creates minimal IR proxies
    /// for any well-known BCL interface not already in the type cache.
    /// </summary>
    private void CreateBclInterfaceProxies()
    {
        // Collect all interface names referenced by user types via Cecil
        var referencedInterfaces = new HashSet<string>();
        foreach (var typeDef in _allTypes!)
        {
            if (typeDef.HasGenericParameters) continue;
            foreach (var ifaceName in typeDef.InterfaceNames)
            {
                if (!_typeCache.ContainsKey(ifaceName))
                    referencedInterfaces.Add(ifaceName);
            }
        }

        // Create proxies for each referenced BCL interface
        foreach (var ifaceName in referencedInterfaces)
        {
            if (_typeCache.ContainsKey(ifaceName)) continue;

            // Try non-generic match first
            if (WellKnownBclInterfaces.TryGetValue(ifaceName, out var spec))
            {
                CreateNonGenericBclProxy(ifaceName, spec);
                continue;
            }

            // Try generic match: extract open type name and type arguments
            var angleBracket = ifaceName.IndexOf('<');
            if (angleBracket > 0)
            {
                var openTypeName = ifaceName[..angleBracket];
                if (WellKnownGenericBclInterfaces.TryGetValue(openTypeName, out var genericSpec))
                {
                    var argsStr = ifaceName[(angleBracket + 1)..^1];
                    var typeArgs = SplitGenericArgs(argsStr);
                    CreateGenericBclProxy(ifaceName, openTypeName, genericSpec, typeArgs);
                }
            }
        }
    }

    /// <summary>
    /// Creates a non-generic BCL interface proxy (e.g., System.IDisposable).
    /// </summary>
    private void CreateNonGenericBclProxy(string ilFullName, BclInterfaceSpec spec)
    {
        var cppName = CppNameMapper.MangleTypeName(ilFullName);
        var irType = new IRType
        {
            ILFullName = ilFullName,
            CppName = cppName,
            Name = spec.Name,
            Namespace = spec.Namespace,
            IsInterface = true,
            IsAbstract = true,
        };

        foreach (var methodSpec in spec.Methods)
        {
            irType.Methods.Add(CreateProxyMethod(irType, methodSpec));
        }

        _module.Types.Add(irType);
        _typeCache[ilFullName] = irType;
    }

    /// <summary>
    /// Creates a generic BCL interface proxy (e.g., System.IEquatable`1&lt;System.Int32&gt;).
    /// </summary>
    private void CreateGenericBclProxy(string ilFullName, string openTypeName,
        BclInterfaceSpec spec, List<string> typeArgs)
    {
        var cppName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, typeArgs);
        var irType = new IRType
        {
            ILFullName = ilFullName,
            CppName = cppName,
            Name = spec.Name,
            Namespace = spec.Namespace,
            IsInterface = true,
            IsAbstract = true,
            IsGenericInstance = true,
            GenericArguments = typeArgs,
        };

        foreach (var methodSpec in spec.Methods)
        {
            irType.Methods.Add(CreateProxyMethod(irType, methodSpec, typeArgs));
        }

        _module.Types.Add(irType);
        _typeCache[ilFullName] = irType;

        // Resolve parent interfaces for well-known generic interfaces
        // (deferred to after all proxies are created — see ResolveBclProxyInterfaces)
    }

    /// <summary>
    /// After all BCL proxies are created, resolve parent interface references.
    /// E.g., IEnumerable&lt;T&gt; : IEnumerable, IEnumerator&lt;T&gt; : IEnumerator, IDisposable.
    /// This must run after CreateBclInterfaceProxies so all proxy types exist in _typeCache.
    /// </summary>
    private void ResolveBclProxyInterfaces()
    {
        foreach (var type in _module.Types)
        {
            if (!type.IsInterface || type.IsRuntimeProvided) continue;

            // IEnumerable<T> extends IEnumerable
            if (type.ILFullName.StartsWith("System.Collections.Generic.IEnumerable`1<"))
            {
                if (_typeCache.TryGetValue("System.Collections.IEnumerable", out var iEnumerable))
                    type.Interfaces.Add(iEnumerable);
            }
            // IEnumerator<T> extends IEnumerator, IDisposable
            else if (type.ILFullName.StartsWith("System.Collections.Generic.IEnumerator`1<"))
            {
                if (_typeCache.TryGetValue("System.Collections.IEnumerator", out var iEnumerator))
                    type.Interfaces.Add(iEnumerator);
                if (_typeCache.TryGetValue("System.IDisposable", out var iDisposable))
                    type.Interfaces.Add(iDisposable);
            }
            // IReadOnlyCollection<T> extends IEnumerable<T>, IEnumerable
            else if (type.ILFullName.StartsWith("System.Collections.Generic.IReadOnlyCollection`1<"))
            {
                var tArg = type.ILFullName["System.Collections.Generic.IReadOnlyCollection`1<".Length..^1];
                var iEnumT = $"System.Collections.Generic.IEnumerable`1<{tArg}>";
                if (_typeCache.TryGetValue(iEnumT, out var iEnumerableT))
                    type.Interfaces.Add(iEnumerableT);
                if (_typeCache.TryGetValue("System.Collections.IEnumerable", out var iEnumerable2))
                    type.Interfaces.Add(iEnumerable2);
            }
            // IReadOnlyList<T> extends IReadOnlyCollection<T>, IEnumerable<T>, IEnumerable
            else if (type.ILFullName.StartsWith("System.Collections.Generic.IReadOnlyList`1<"))
            {
                var tArg = type.ILFullName["System.Collections.Generic.IReadOnlyList`1<".Length..^1];
                var iReadOnlyCollT = $"System.Collections.Generic.IReadOnlyCollection`1<{tArg}>";
                if (_typeCache.TryGetValue(iReadOnlyCollT, out var iReadOnlyColl))
                    type.Interfaces.Add(iReadOnlyColl);
                var iEnumT = $"System.Collections.Generic.IEnumerable`1<{tArg}>";
                if (_typeCache.TryGetValue(iEnumT, out var iEnumerableT))
                    type.Interfaces.Add(iEnumerableT);
                if (_typeCache.TryGetValue("System.Collections.IEnumerable", out var iEnumerable3))
                    type.Interfaces.Add(iEnumerable3);
            }
            // IAsyncEnumerator<T> extends IAsyncDisposable
            else if (type.ILFullName.StartsWith("System.Collections.Generic.IAsyncEnumerator`1<"))
            {
                if (_typeCache.TryGetValue("System.IAsyncDisposable", out var iAsyncDisposable))
                    type.Interfaces.Add(iAsyncDisposable);
            }
        }
    }

    /// <summary>
    /// Creates a proxy IRMethod for a BCL interface method.
    /// </summary>
    private IRMethod CreateProxyMethod(IRType declaringType, BclMethodSpec methodSpec,
        List<string>? typeArgs = null)
    {
        var returnType = SubstituteGenericArgs(methodSpec.ReturnTypeIL, typeArgs);
        var irMethod = new IRMethod
        {
            Name = methodSpec.Name,
            CppName = CppNameMapper.MangleMethodName(declaringType.CppName, methodSpec.Name),
            DeclaringType = declaringType,
            ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(returnType),
            IsVirtual = true,
            IsAbstract = true,
        };

        for (int i = 0; i < methodSpec.ParameterTypeILs.Length; i++)
        {
            var paramType = SubstituteGenericArgs(methodSpec.ParameterTypeILs[i], typeArgs);
            irMethod.Parameters.Add(new IRParameter
            {
                Name = $"p{i}",
                CppName = $"p{i}",
                CppTypeName = CppNameMapper.GetCppTypeForDecl(paramType),
                ILTypeName = paramType,
                Index = i,
            });
        }

        return irMethod;
    }

    /// <summary>
    /// Replaces generic argument placeholders with actual type arguments.
    /// For arity-1 interfaces: "T" → typeArgs[0].
    /// For arity-2 interfaces: "TKey" → typeArgs[0], "TValue" → typeArgs[1].
    /// Also handles "T[]", "TValue&amp;", and nested generic references.
    /// </summary>
    private static string SubstituteGenericArgs(string typeName, List<string>? typeArgs)
    {
        if (typeArgs == null || typeArgs.Count == 0)
            return typeName;

        if (typeArgs.Count == 1)
        {
            // Simple "T" replacement
            if (typeName == "T") return typeArgs[0];
            if (typeName == "T[]") return typeArgs[0] + "[]";
            if (typeName.Contains("<T>"))
                return typeName.Replace("<T>", $"<{typeArgs[0]}>");
            return typeName;
        }

        // Multi-param: TKey → typeArgs[0], TValue → typeArgs[1]
        var result = typeName;
        if (result == "TKey") return typeArgs[0];
        if (result == "TValue") return typeArgs[1];
        if (result == "TValue&") return typeArgs[1] + "&";
        result = result.Replace("<TKey>", $"<{typeArgs[0]}>");
        result = result.Replace("<TValue>", $"<{typeArgs[1]}>");
        if (result != typeName) return result;

        return typeName;
    }

    /// <summary>
    /// Splits comma-separated generic arguments, handling nested generics.
    /// E.g., "System.Int32, System.String" → ["System.Int32", "System.String"]
    /// E.g., "System.Collections.Generic.KeyValuePair`2&lt;System.String, System.Int32&gt;" → single entry
    /// </summary>
    private static List<string> SplitGenericArgs(string argsStr)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < argsStr.Length; i++)
        {
            if (argsStr[i] == '<') depth++;
            else if (argsStr[i] == '>') depth--;
            else if (argsStr[i] == ',' && depth == 0)
            {
                result.Add(argsStr[start..i].Trim());
                start = i + 1;
            }
        }

        result.Add(argsStr[start..].Trim());
        return result;
    }
}
