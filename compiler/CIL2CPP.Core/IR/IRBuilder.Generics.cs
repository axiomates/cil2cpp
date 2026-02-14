using Mono.Cecil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    // Active generic type parameter map (set during ConvertMethodBodyWithGenerics)
    private Dictionary<string, string>? _activeTypeParamMap;

    /// <summary>
    /// Construct a unique key for a generic method instantiation.
    /// Shared between scanning (CollectGenericMethod) and call emission (EmitMethodCall).
    /// </summary>
    private static string MakeGenericMethodKey(string declaringType, string methodName, List<string> typeArgs)
        => $"{declaringType}::{methodName}<{string.Join(",", typeArgs)}>";

    /// <summary>
    /// Mangle a generic method instantiation name for C++ emission.
    /// Shared between scanning (CollectGenericMethod) and call emission fallback (EmitMethodCall).
    /// </summary>
    private static string MangleGenericMethodName(string declaringType, string methodName, List<string> typeArgs)
    {
        var typeCppName = CppNameMapper.MangleTypeName(declaringType);
        var argParts = string.Join("_", typeArgs.Select(CppNameMapper.MangleTypeName));
        return $"{typeCppName}_{CppNameMapper.MangleTypeName(methodName)}_{argParts}";
    }

    /// <summary>
    /// Pass 0: Scan all method bodies for GenericInstanceType references.
    /// Collects unique generic instantiations (e.g., Wrapper`1&lt;System.Int32&gt;).
    /// </summary>
    private void ScanGenericInstantiations()
    {
        foreach (var typeDef in _allTypes!)
        {
            foreach (var methodDef in typeDef.Methods)
            {
                if (!methodDef.HasBody) continue;
                var cecilMethod = methodDef.GetCecilMethod();
                if (!cecilMethod.HasBody) continue;

                // Scan local variables
                foreach (var local in cecilMethod.Body.Variables)
                {
                    CollectGenericType(local.VariableType);
                }

                // Scan instructions
                foreach (var instr in cecilMethod.Body.Instructions)
                {
                    switch (instr.Operand)
                    {
                        case MethodReference methodRef:
                            CollectGenericType(methodRef.DeclaringType);
                            if (methodRef.ReturnType is GenericInstanceType)
                                CollectGenericType(methodRef.ReturnType);
                            foreach (var p in methodRef.Parameters)
                                CollectGenericType(p.ParameterType);
                            // Collect generic method instantiations
                            if (methodRef is GenericInstanceMethod gim)
                                CollectGenericMethod(gim);
                            break;
                        case FieldReference fieldRef:
                            CollectGenericType(fieldRef.DeclaringType);
                            CollectGenericType(fieldRef.FieldType);
                            break;
                        case TypeReference typeRef:
                            CollectGenericType(typeRef);
                            break;
                    }
                }
            }
        }
    }

    private void CollectGenericType(TypeReference typeRef)
    {
        if (typeRef is not GenericInstanceType git) return;

        // Skip if any type argument is still a generic parameter (unresolved)
        if (git.GenericArguments.Any(a => a is GenericParameter))
            return;

        var openTypeName = git.ElementType.FullName;
        var typeArgs = git.GenericArguments.Select(a => a.FullName).ToList();
        var key = $"{openTypeName}<{string.Join(",", typeArgs)}>";

        if (_genericInstantiations.ContainsKey(key)) return;

        var mangledName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, typeArgs);
        var cecilOpenType = git.ElementType.Resolve();

        _genericInstantiations[key] = new GenericInstantiationInfo(
            openTypeName, typeArgs, mangledName, cecilOpenType);
    }

    private void CollectGenericMethod(GenericInstanceMethod gim)
    {
        var elementMethod = gim.ElementMethod;
        var declaringType = elementMethod.DeclaringType.FullName;
        var methodName = elementMethod.Name;

        // Skip if any type argument is still a generic parameter (unresolved)
        if (gim.GenericArguments.Any(a => a is GenericParameter))
            return;

        var typeArgs = gim.GenericArguments.Select(a => a.FullName).ToList();
        var key = MakeGenericMethodKey(declaringType, methodName, typeArgs);
        if (_genericMethodInstantiations.ContainsKey(key)) return;

        var cecilMethod = elementMethod.Resolve();
        if (cecilMethod == null) return;

        var mangledName = MangleGenericMethodName(declaringType, methodName, typeArgs);

        _genericMethodInstantiations[key] = new GenericMethodInstantiationInfo(
            declaringType, methodName, typeArgs, mangledName, cecilMethod);
    }

    /// <summary>
    /// Create specialized IRMethods for each generic method instantiation found in Pass 0.
    /// </summary>
    private void CreateGenericMethodSpecializations()
    {
        foreach (var (key, info) in _genericMethodInstantiations)
        {
            var cecilMethod = info.CecilMethod;

            // Find the declaring IRType
            if (!_typeCache.TryGetValue(info.DeclaringTypeName, out var declaringIrType))
                continue;

            // Build method-level type parameter map
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < cecilMethod.GenericParameters.Count && i < info.TypeArguments.Count; i++)
            {
                typeParamMap[cecilMethod.GenericParameters[i].Name] = info.TypeArguments[i];
            }

            var returnTypeName = ResolveGenericTypeName(cecilMethod.ReturnType, typeParamMap);

            var irMethod = new IRMethod
            {
                Name = cecilMethod.Name,
                CppName = info.MangledName,
                DeclaringType = declaringIrType,
                ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(returnTypeName),
                IsStatic = cecilMethod.IsStatic,
                IsVirtual = cecilMethod.IsVirtual,
                IsAbstract = cecilMethod.IsAbstract,
                IsConstructor = cecilMethod.IsConstructor,
                IsStaticConstructor = cecilMethod.IsConstructor && cecilMethod.IsStatic,
                IsGenericInstance = true,
            };

            // Parameters
            foreach (var paramDef in cecilMethod.Parameters)
            {
                var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                irMethod.Parameters.Add(new IRParameter
                {
                    Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                    CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                    CppTypeName = CppNameMapper.GetCppTypeForDecl(paramTypeName),
                    ILTypeName = paramTypeName,
                    Index = paramDef.Index,
                });
            }

            // Local variables
            if (cecilMethod.HasBody)
            {
                foreach (var localDef in cecilMethod.Body.Variables)
                {
                    var localTypeName = ResolveGenericTypeName(localDef.VariableType, typeParamMap);
                    irMethod.Locals.Add(new IRLocal
                    {
                        Index = localDef.Index,
                        CppName = $"loc_{localDef.Index}",
                        CppTypeName = CppNameMapper.GetCppTypeForDecl(localTypeName),
                    });
                }
            }

            declaringIrType.Methods.Add(irMethod);

            // Convert method body with generic substitution context
            // (not added to methodBodies — we convert immediately with the type param map)
            if (cecilMethod.HasBody && !cecilMethod.IsAbstract)
            {
                var methodInfo = new IL.MethodInfo(cecilMethod);
                ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
            }
        }
    }

    /// <summary>
    /// Create specialized IRTypes for each generic instantiation found in Pass 0.
    /// </summary>
    private void CreateGenericSpecializations()
    {
        foreach (var (key, info) in _genericInstantiations)
        {
            var isAsyncBcl = IsAsyncBclGenericType(info.OpenTypeName);

            // Skip types we can't resolve — except async BCL types (synthetic fields)
            if (info.CecilOpenType == null && !isAsyncBcl) continue;

            var openType = info.CecilOpenType; // may be null for async BCL types

            // Build type parameter map
            var typeParamMap = new Dictionary<string, string>();
            if (openType != null)
            {
                for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                    typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];
            }
            else if (isAsyncBcl && info.TypeArguments.Count > 0)
            {
                // Async BCL types always have TResult as their single generic parameter
                typeParamMap["TResult"] = info.TypeArguments[0];
            }

            // Determine type flags
            bool isValueType;
            string namespaceName;
            if (openType != null)
            {
                isValueType = openType.IsValueType;
                namespaceName = openType.Namespace;
            }
            else
            {
                // Async BCL: TaskAwaiter<T> and AsyncTaskMethodBuilder<T> are value types
                isValueType = !info.OpenTypeName.StartsWith("System.Threading.Tasks.Task`");
                namespaceName = info.OpenTypeName.Contains("CompilerServices")
                    ? "System.Runtime.CompilerServices"
                    : "System.Threading.Tasks";
            }

            var irType = new IRType
            {
                ILFullName = key,
                CppName = info.MangledName,
                Name = info.MangledName,
                Namespace = namespaceName,
                IsValueType = isValueType,
                IsInterface = openType?.IsInterface ?? false,
                IsAbstract = openType?.IsAbstract ?? false,
                IsSealed = openType?.IsSealed ?? true,
                IsGenericInstance = true,
                GenericArguments = info.TypeArguments,
                IsRuntimeProvided = isAsyncBcl,
            };

            // Register value types
            if (isValueType)
            {
                CppNameMapper.RegisterValueType(key);
                CppNameMapper.RegisterValueType(info.MangledName);
            }

            // Fields: synthetic for async BCL, Cecil-based for everything else
            if (isAsyncBcl)
            {
                irType.Fields.AddRange(CreateAsyncSyntheticFields(info.OpenTypeName, irType, typeParamMap));
            }
            else
            {
                foreach (var fieldDef in openType!.Fields)
                {
                    var fieldTypeName = ResolveGenericTypeName(fieldDef.FieldType, typeParamMap);
                    var irField = new IRField
                    {
                        Name = fieldDef.Name,
                        CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                        FieldTypeName = fieldTypeName,
                        IsStatic = fieldDef.IsStatic,
                        IsPublic = fieldDef.IsPublic,
                        DeclaringType = irType,
                    };
                    if (fieldDef.IsStatic)
                        irType.StaticFields.Add(irField);
                    else
                        irType.Fields.Add(irField);
                }
            }

            // Calculate instance size
            CalculateInstanceSize(irType);

            // Methods: skip entirely for async BCL types (all calls are intercepted)
            if (openType != null && !isAsyncBcl)
            {
                foreach (var methodDef in openType.Methods)
                {
                    var returnTypeName = ResolveGenericTypeName(methodDef.ReturnType, typeParamMap);
                    var cppName = CppNameMapper.MangleMethodName(info.MangledName, methodDef.Name);

                    var irMethod = new IRMethod
                    {
                        Name = methodDef.Name,
                        CppName = cppName,
                        DeclaringType = irType,
                        ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(returnTypeName),
                        IsStatic = methodDef.IsStatic,
                        IsVirtual = methodDef.IsVirtual,
                        IsAbstract = methodDef.IsAbstract,
                        IsConstructor = methodDef.IsConstructor,
                        IsStaticConstructor = methodDef.IsConstructor && methodDef.IsStatic,
                    };

                    // Parameters
                    foreach (var paramDef in methodDef.Parameters)
                    {
                        var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                        irMethod.Parameters.Add(new IRParameter
                        {
                            Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                            CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                            CppTypeName = CppNameMapper.GetCppTypeForDecl(paramTypeName),
                            ILTypeName = paramTypeName,
                            Index = paramDef.Index,
                        });
                    }

                    // Local variables
                    if (methodDef.HasBody)
                    {
                        foreach (var localDef in methodDef.Body.Variables)
                        {
                            var localTypeName = ResolveGenericTypeName(localDef.VariableType, typeParamMap);
                            irMethod.Locals.Add(new IRLocal
                            {
                                Index = localDef.Index,
                                CppName = $"loc_{localDef.Index}",
                                CppTypeName = CppNameMapper.GetCppTypeForDecl(localTypeName),
                            });
                        }
                    }

                    irType.Methods.Add(irMethod);

                    // Convert method body with generic substitution context
                    // Skip BCL generic types — their method calls are intercepted instead
                    var isBclGeneric = info.OpenTypeName.StartsWith("System.Nullable`")
                        || info.OpenTypeName.StartsWith("System.ValueTuple`");
                    if (!isBclGeneric && methodDef.HasBody && !methodDef.IsAbstract)
                    {
                        var methodInfo = new IL.MethodInfo(methodDef);
                        ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
                    }
                }
            }

            _module.Types.Add(irType);
            _typeCache[key] = irType;
        }

        // Second pass: resolve base types, interfaces, HasCctor for generic specializations.
        // Done after all specializations are in the cache so cross-references work
        // (e.g., SpecialWrapper<int> : Wrapper<int> needs Wrapper<int> already cached).
        foreach (var (key, info) in _genericInstantiations)
        {
            if (info.CecilOpenType == null) continue;
            if (!_typeCache.TryGetValue(key, out var irType)) continue;

            var openType = info.CecilOpenType;
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
                typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];

            // Base type
            if (openType.BaseType != null && !irType.IsValueType)
            {
                var baseName = ResolveGenericTypeName(openType.BaseType, typeParamMap);
                if (_typeCache.TryGetValue(baseName, out var baseType))
                    irType.BaseType = baseType;
            }

            // Interfaces (Cecil flattens the list)
            foreach (var iface in openType.Interfaces)
            {
                var ifaceName = ResolveGenericTypeName(iface.InterfaceType, typeParamMap);
                if (_typeCache.TryGetValue(ifaceName, out var ifaceType))
                    irType.Interfaces.Add(ifaceType);
            }

            // Static constructor flag
            irType.HasCctor = openType.Methods.Any(m => m.IsConstructor && m.IsStatic);

            // Recalculate instance size (BaseType may contribute inherited fields)
            CalculateInstanceSize(irType);
        }
    }

    /// <summary>
    /// Resolve a Cecil TypeReference to an IL type name, substituting generic parameters.
    /// </summary>
    private string ResolveGenericTypeName(TypeReference typeRef, Dictionary<string, string> typeParamMap)
    {
        if (typeRef is GenericParameter gp)
        {
            return typeParamMap.TryGetValue(gp.Name, out var resolved) ? resolved : gp.FullName;
        }

        if (typeRef is GenericInstanceType git)
        {
            var openName = git.ElementType.FullName;
            var args = git.GenericArguments.Select(a => ResolveGenericTypeName(a, typeParamMap)).ToList();
            var key = $"{openName}<{string.Join(",", args)}>";
            return key;
        }

        if (typeRef is ArrayType at)
        {
            return ResolveGenericTypeName(at.ElementType, typeParamMap) + "[]";
        }

        if (typeRef is ByReferenceType brt)
        {
            return ResolveGenericTypeName(brt.ElementType, typeParamMap) + "&";
        }

        return typeRef.FullName;
    }

    /// <summary>
    /// Convert a method body from an open generic type with generic parameter substitution.
    /// </summary>
    private void ConvertMethodBodyWithGenerics(IL.MethodInfo methodDef, IRMethod irMethod,
        Dictionary<string, string> typeParamMap)
    {
        _activeTypeParamMap = typeParamMap;
        try
        {
            ConvertMethodBody(methodDef, irMethod);
        }
        finally
        {
            _activeTypeParamMap = null;
        }
    }

    /// <summary>
    /// Resolve a Cecil TypeReference to a cache key, handling GenericInstanceType.
    /// </summary>
    private string ResolveCacheKey(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType git)
        {
            var openTypeName = git.ElementType.FullName;
            var typeArgs = git.GenericArguments.Select(a =>
            {
                if (a is GenericParameter gp && _activeTypeParamMap != null)
                    return _activeTypeParamMap.TryGetValue(gp.Name, out var r) ? r : a.FullName;
                return a.FullName;
            }).ToList();
            return $"{openTypeName}<{string.Join(",", typeArgs)}>";
        }

        if (typeRef is GenericParameter gp2 && _activeTypeParamMap != null)
            return _activeTypeParamMap.TryGetValue(gp2.Name, out var resolved) ? resolved : typeRef.FullName;

        return typeRef.FullName;
    }

    /// <summary>
    /// Get the C++ mangled type name for a Cecil TypeReference.
    /// </summary>
    private string GetMangledTypeNameForRef(TypeReference typeRef)
    {
        var key = ResolveCacheKey(typeRef);
        if (_typeCache.TryGetValue(key, out var irType))
            return irType.CppName;
        return CppNameMapper.MangleTypeName(key);
    }

    /// <summary>
    /// Resolve a type name string (possibly containing generic syntax) to C++ declaration type.
    /// </summary>
    private string ResolveTypeForDecl(string ilTypeName)
    {
        if (_typeCache.TryGetValue(ilTypeName, out var cached))
        {
            if (cached.IsValueType)
                return cached.CppName;
            return cached.CppName + "*";
        }

        var backtickIdx = ilTypeName.IndexOf('`');
        if (backtickIdx > 0 && ilTypeName.Contains('<'))
        {
            var angleBracket = ilTypeName.IndexOf('<');
            var openTypeName = ilTypeName[..angleBracket];
            var argsStr = ilTypeName[(angleBracket + 1)..^1];
            var args = argsStr.Split(',').Select(a => a.Trim()).ToList();
            var key = $"{openTypeName}<{string.Join(",", args)}>";

            if (_typeCache.TryGetValue(key, out var genericCached))
            {
                if (genericCached.IsValueType)
                    return genericCached.CppName;
                return genericCached.CppName + "*";
            }
        }

        return CppNameMapper.GetCppTypeForDecl(ilTypeName);
    }
}
