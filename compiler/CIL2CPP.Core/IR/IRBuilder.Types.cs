using CIL2CPP.Core.IL;
using Mono.Cecil;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    private IRType CreateTypeShell(TypeDefinitionInfo typeDef)
    {
        var cppName = CppNameMapper.MangleTypeName(typeDef.FullName);

        var irType = new IRType
        {
            ILFullName = typeDef.FullName,
            CppName = cppName,
            Name = typeDef.Name,
            Namespace = typeDef.Namespace,
            IsValueType = typeDef.IsValueType,
            IsInterface = typeDef.IsInterface,
            IsAbstract = typeDef.IsAbstract,
            IsSealed = typeDef.IsSealed,
            IsEnum = typeDef.IsEnum,
        };

        if (typeDef.IsEnum)
            irType.EnumUnderlyingType = typeDef.EnumUnderlyingType ?? "System.Int32";

        // Detect delegate types (base is System.MulticastDelegate)
        if (typeDef.BaseTypeName is "System.MulticastDelegate" or "System.Delegate")
            irType.IsDelegate = true;

        // Register value types for CppNameMapper so it doesn't add pointer suffix
        // Register both IL name (for IsValueType lookups) and C++ name (for GetDefaultValue)
        if (typeDef.IsValueType)
        {
            CppNameMapper.RegisterValueType(typeDef.FullName);
            CppNameMapper.RegisterValueType(cppName);
        }

        // Extract generic parameter variance (ECMA-335 II.9.11)
        var cecilType = typeDef.GetCecilType();
        if (cecilType.HasGenericParameters)
        {
            foreach (var gp in cecilType.GenericParameters)
            {
                var variance = (gp.Attributes & GenericParameterAttributes.VarianceMask) switch
                {
                    GenericParameterAttributes.Covariant => GenericVariance.Covariant,
                    GenericParameterAttributes.Contravariant => GenericVariance.Contravariant,
                    _ => GenericVariance.Invariant,
                };
                irType.GenericParameterVariances.Add(variance);
            }
        }

        return irType;
    }

    private void PopulateTypeDetails(TypeDefinitionInfo typeDef, IRType irType)
    {
        // Base type
        if (typeDef.BaseTypeName != null && _typeCache.TryGetValue(typeDef.BaseTypeName, out var baseType))
        {
            irType.BaseType = baseType;
        }

        // Interfaces (deduplicate: same type may be populated from multiple assemblies)
        foreach (var ifaceName in typeDef.InterfaceNames)
        {
            if (_typeCache.TryGetValue(ifaceName, out var iface) &&
                !irType.Interfaces.Contains(iface))
            {
                irType.Interfaces.Add(iface);
            }
        }

        // Fields (skip CLR-internal value__ field for enums)
        // Skip fields that already exist (same type may be populated from multiple assemblies)
        foreach (var fieldDef in typeDef.Fields)
        {
            if (irType.IsEnum && fieldDef.Name == "value__") continue;

            // Deduplicate: skip if a field with the same name+static already exists
            var targetList = fieldDef.IsStatic ? irType.StaticFields : irType.Fields;
            if (targetList.Any(f => f.Name == fieldDef.Name)) continue;

            var irField = new IRField
            {
                Name = fieldDef.Name,
                CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                FieldTypeName = fieldDef.TypeName,
                IsStatic = fieldDef.IsStatic,
                IsPublic = fieldDef.IsPublic,
                DeclaringType = irType,
            };

            if (_typeCache.TryGetValue(fieldDef.TypeName, out var fieldType))
            {
                irField.FieldType = fieldType;
            }

            irField.ConstantValue = fieldDef.ConstantValue;
            irField.Attributes = (uint)fieldDef.GetCecilField().Attributes;

            targetList.Add(irField);
        }

        // Read explicit struct size from ClassSize metadata (ECMA-335 II.10.1.2).
        // This is critical for fixed-size buffer types (InlineArray / FixedBuffer)
        // where ClassSize > sum of declared fields (e.g., fixed byte[12] has one
        // FixedElementField:byte but ClassSize=12).
        var cecilType = typeDef.GetCecilType();
        if (irType.IsValueType && cecilType.HasLayoutInfo && cecilType.ClassSize > 0)
        {
            irType.ExplicitSize = cecilType.ClassSize;
        }

        // Calculate instance size
        CalculateInstanceSize(irType);
    }

    // Object header: TypeInfo* (8 bytes) + sync_block UInt32 (4 bytes) + padding (4 bytes) = 16
    private const int ObjectHeaderSize = 16;
    private const int PointerAlignment = 8;

    private static int Align(int offset, int alignment) =>
        (offset + alignment - 1) & ~(alignment - 1);

    private void CalculateInstanceSize(IRType irType)
    {
        // Start with object header for reference types
        int size = irType.IsValueType ? 0 : ObjectHeaderSize;

        // Add base type fields
        if (irType.BaseType != null)
        {
            size = irType.BaseType.InstanceSize;
        }

        // Add own fields
        foreach (var field in irType.Fields)
        {
            int fieldSize = GetFieldSize(field.FieldTypeName);
            int alignment = GetFieldAlignment(field.FieldTypeName);

            size = Align(size, alignment);
            field.Offset = size;
            size += fieldSize;
        }

        // For types with explicit size (fixed-size buffers / InlineArray),
        // the actual size may be larger than the sum of declared fields.
        if (irType.ExplicitSize > 0 && irType.ExplicitSize > size)
            size = irType.ExplicitSize;

        // Align total size to pointer boundary
        size = Align(size, PointerAlignment);
        irType.InstanceSize = size;
    }

    // Field sizes per ECMA-335 §I.8.2.1 (Built-in Value Types)
    private int GetFieldSize(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Int16" or "System.UInt16" or "System.Char" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            _ => 8 // Pointer size on 64-bit (reference types, IntPtr/UIntPtr). FIXME: 32-bit targets need 4.
        };
    }

    private int GetFieldAlignment(string typeName)
    {
        return Math.Min(GetFieldSize(typeName), 8);
    }

    /// <summary>
    /// Scan all method signatures for types not in the IR module.
    /// If a referenced type resolves (via Cecil) to an enum, register it as a value type
    /// and add it to ExternalEnumTypes so the header generator emits a using alias
    /// instead of a struct forward declaration.
    /// </summary>
    /// <returns>Set of mangled enum names that were newly discovered in this scan.</returns>
    private HashSet<string> ScanExternalEnumTypes()
    {
        var checkedTypes = new HashSet<string>();
        var newlyDiscovered = new HashSet<string>();

        foreach (var irType in _module.Types)
        {
            foreach (var method in irType.Methods)
            {
                // Check return type
                if (!string.IsNullOrEmpty(method.ReturnTypeCpp) && method.ReturnTypeCpp != "void")
                    TryRegisterExternalEnum(method.ReturnTypeCpp, checkedTypes, newlyDiscovered);

                // Check parameter types
                foreach (var param in method.Parameters)
                    TryRegisterExternalEnum(param.CppTypeName, checkedTypes, newlyDiscovered);

                // Check local variable types
                foreach (var local in method.Locals)
                    TryRegisterExternalEnum(local.CppTypeName, checkedTypes, newlyDiscovered);
            }

            // Check field types (including static fields)
            foreach (var field in irType.Fields)
                TryRegisterExternalEnum(CppNameMapper.GetCppTypeForDecl(field.FieldTypeName), checkedTypes, newlyDiscovered);
            foreach (var field in irType.StaticFields)
                TryRegisterExternalEnum(CppNameMapper.GetCppTypeForDecl(field.FieldTypeName), checkedTypes, newlyDiscovered);
        }

        return newlyDiscovered;
    }

    /// <summary>
    /// After ScanExternalEnumTypes registers enum types as value types,
    /// fix up method signatures that were resolved before the registration.
    /// Removes the pointer suffix (*) from parameters/return types that are now known to be enums.
    /// Only fixes types matching the given set of newly-discovered enums, to avoid
    /// stripping legitimate ref pointers from types resolved after earlier registration.
    /// </summary>
    private void FixupExternalEnumTypes(HashSet<string> newlyDiscoveredEnums)
    {
        if (newlyDiscoveredEnums.Count == 0) return;

        foreach (var irType in _module.Types)
        {
            foreach (var method in irType.Methods)
            {
                // Fix return type — strip exactly one trailing * if the base is a newly-discovered enum
                if (method.ReturnTypeCpp != null)
                    method.ReturnTypeCpp = FixupEnumPointerSuffix(method.ReturnTypeCpp, newlyDiscoveredEnums);

                // Fix parameter types — strip one * per level of enum indirection
                // Note: ref/out enum params have ** (one for ref, one for wrong enum→pointer);
                // we strip one to get * (correct for ref/out value type)
                foreach (var param in method.Parameters)
                    param.CppTypeName = FixupEnumPointerSuffix(param.CppTypeName, newlyDiscoveredEnums);

                // Fix local variable types
                foreach (var local in method.Locals)
                    local.CppTypeName = FixupEnumPointerSuffix(local.CppTypeName, newlyDiscoveredEnums);
            }
        }
    }

    /// <summary>
    /// Strip exactly one trailing '*' from a type name if the base type (without all '*')
    /// is in the given set of enum names to fix. This handles:
    ///   "EnumType*"  → "EnumType"       (enum was wrongly treated as reference type)
    ///   "EnumType**" → "EnumType*"      (ref/out enum: one * for ref, one wrongly added)
    ///   "EnumType"   → "EnumType"       (already correct)
    /// </summary>
    private static string FixupEnumPointerSuffix(string cppTypeName, HashSet<string> enumsToFix)
    {
        if (!cppTypeName.EndsWith("*")) return cppTypeName;
        var baseType = cppTypeName.TrimEnd('*').Trim();
        if (enumsToFix.Contains(baseType))
        {
            // Strip exactly one '*'
            return cppTypeName[..^1];
        }
        return cppTypeName;
    }

    /// <summary>
    /// Check if a C++ type name corresponds to an external enum type.
    /// If the type isn't in the IR module and resolves to a Cecil enum definition,
    /// register it as a value type and add to ExternalEnumTypes.
    /// </summary>
    private void TryRegisterExternalEnum(string cppTypeName, HashSet<string> checkedTypes, HashSet<string> newlyDiscovered)
    {
        // Strip pointer suffix — enum types are value types, shouldn't have *
        var raw = cppTypeName.TrimEnd('*').Trim();
        if (raw.Length == 0 || raw.StartsWith("cil2cpp::")) return;
        if (!checkedTypes.Add(raw)) return;

        // Skip if already known (in type cache or already registered)
        if (_module.ExternalEnumTypes.ContainsKey(raw)) return;

        // If already registered as a value type (e.g., enum is an IRType in the module),
        // method signatures already have correct pointer levels. We still need to register
        // in ExternalEnumTypes (for header generation), but must NOT add to newlyDiscovered
        // (which would trigger fixup that strips legitimate ref pointers).
        bool alreadyValueType = CppNameMapper.IsValueType(raw);

        // Try to find the IL type name for this mangled C++ name
        // Scan all loaded assemblies for types whose mangled name matches
        var assemblies = GetLoadedAssemblies();
        foreach (var asm in assemblies)
        {
            foreach (var module in asm.Modules)
            {
                if (FindAndRegisterExternalEnum(module.Types, raw))
                {
                    // Only track as newly discovered if it was actually an enum
                    // AND wasn't already a known value type (no fixup needed for those)
                    if (_module.ExternalEnumTypes.ContainsKey(raw) && !alreadyValueType)
                        newlyDiscovered.Add(raw);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Recursively search Cecil types for an enum with the given mangled name.
    /// Returns true if found and registered.
    /// </summary>
    private bool FindAndRegisterExternalEnum(
        IEnumerable<Mono.Cecil.TypeDefinition> types, string mangledName)
    {
        foreach (var typeDef in types)
        {
            if (CppNameMapper.MangleTypeName(typeDef.FullName) == mangledName)
            {
                if (!typeDef.IsEnum) return true; // Found but not an enum — stop searching

                var underlyingCpp = GetEnumUnderlyingCppType(typeDef);
                _module.ExternalEnumTypes[mangledName] = underlyingCpp;
                CppNameMapper.RegisterValueType(typeDef.FullName);
                CppNameMapper.RegisterValueType(mangledName);
                return true;
            }

            // Search nested types
            if (typeDef.HasNestedTypes && FindAndRegisterExternalEnum(typeDef.NestedTypes, mangledName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get all loaded Cecil assemblies from the AssemblySet.
    /// </summary>
    private IEnumerable<Mono.Cecil.AssemblyDefinition> GetLoadedAssemblies()
    {
        foreach (var asm in _assemblySet.LoadedAssemblies.Values)
            yield return asm;
    }

    /// <summary>
    /// Get the C++ type name for an enum's underlying type from Cecil metadata.
    /// </summary>
    private static string GetEnumUnderlyingCppType(Mono.Cecil.TypeDefinition enumType)
    {
        // The underlying type is stored in the special "value__" field
        foreach (var field in enumType.Fields)
        {
            if (field.Name == "value__")
            {
                return field.FieldType.FullName switch
                {
                    "System.Byte" => "uint8_t",
                    "System.SByte" => "int8_t",
                    "System.Int16" => "int16_t",
                    "System.UInt16" => "uint16_t",
                    "System.Int32" => "int32_t",
                    "System.UInt32" => "uint32_t",
                    "System.Int64" => "int64_t",
                    "System.UInt64" => "uint64_t",
                    _ => "int32_t",
                };
            }
        }
        return "int32_t"; // default
    }

    /// <summary>
    /// Discover types referenced by compiled method bodies but not yet in the IR module.
    /// Scans Cecil method definitions for parameter/local types that need full struct definitions.
    /// </summary>
    private void DiscoverMissingReferencedTypes()
    {
        if (_allTypes == null) return;

        var typesToAdd = new List<TypeDefinition>();
        var seen = new HashSet<string>(_typeCache.Keys);

        // Scan compiled method bodies for ldfld/stfld/ldflda/stsfld references
        // Only discover types whose fields are ACCESSED (needed for struct definition)
        foreach (var typeDef in _allTypes)
        {
            if (typeDef.HasGenericParameters) continue;
            foreach (var method in typeDef.Methods)
            {
                if (!method.HasBody) continue;
                // Only scan methods whose bodies will be compiled
                if (!_reachability.IsReachable(method.GetCecilMethod()))
                    continue;
                var cecil = method.GetCecilMethod();
                foreach (var instr in cecil.Body.Instructions)
                {
                    if (instr.Operand is FieldReference fieldRef)
                    {
                        // Only discover types for field access instructions
                        var code = instr.OpCode.Code;
                        if (code is Mono.Cecil.Cil.Code.Ldfld or Mono.Cecil.Cil.Code.Stfld
                            or Mono.Cecil.Cil.Code.Ldflda or Mono.Cecil.Cil.Code.Ldsflda
                            or Mono.Cecil.Cil.Code.Ldsfld or Mono.Cecil.Cil.Code.Stsfld)
                        {
                            TryDiscoverCecilType(fieldRef.DeclaringType, seen, typesToAdd);
                        }
                    }
                }
            }
        }

        // Add discovered types to the module
        foreach (var cecilType in typesToAdd)
        {
            var typeDefInfo = new TypeDefinitionInfo(cecilType);
            var irType = CreateTypeShell(typeDefInfo);

            var assemblyName = cecilType.Module.Assembly.Name.Name;
            irType.SourceKind = _assemblySet.ClassifyAssembly(assemblyName);
            irType.IsRuntimeProvided = RuntimeProvidedTypes.Contains(cecilType.FullName);

            _module.Types.Add(irType);
            _typeCache[cecilType.FullName] = irType;

            // Populate fields/base types
            PopulateTypeDetails(typeDefInfo, irType);
        }
    }

    private void TryDiscoverCecilType(TypeReference? typeRef, HashSet<string> seen, List<TypeDefinition> toAdd)
    {
        if (typeRef == null) return;
        // Unwrap wrapper types
        if (typeRef is ByReferenceType byRef)
            typeRef = byRef.ElementType;
        if (typeRef is PointerType ptr)
            typeRef = ptr.ElementType;
        if (typeRef is GenericInstanceType git)
            typeRef = git.ElementType;
        if (typeRef is ArrayType) return;
        if (typeRef is GenericParameter) return;

        try
        {
            var resolved = typeRef.Resolve();
            if (resolved == null) return;
            if (resolved.HasGenericParameters) return;
            if (seen.Contains(resolved.FullName)) return;
            seen.Add(resolved.FullName);

            // Skip primitives
            if (CppNameMapper.IsPrimitive(resolved.FullName)) return;
            if (resolved.FullName == "<Module>") return;

            toAdd.Add(resolved);
        }
        catch { }
    }
}
