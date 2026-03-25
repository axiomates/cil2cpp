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
            // System.Enum and System.ValueType are reference types despite Cecil IsValueType=true
            IsValueType = typeDef.IsValueType && typeDef.FullName is not "System.Enum" and not "System.ValueType",
            IsInterface = typeDef.IsInterface,
            IsAbstract = typeDef.IsAbstract,
            IsSealed = typeDef.IsSealed,
            IsEnum = typeDef.IsEnum,
            IsPublic = typeDef.GetCecilType().IsPublic,
            IsNestedPublic = typeDef.GetCecilType().IsNestedPublic,
            IsNotPublic = !typeDef.GetCecilType().IsPublic && !typeDef.GetCecilType().IsNested,
            IsNestedAssembly = typeDef.GetCecilType().IsNestedAssembly,
            MetadataToken = typeDef.GetCecilType().MetadataToken.ToUInt32(),
        };

        // Detect IsByRefLike via IsByRefLikeAttribute
        if (typeDef.GetCecilType().CustomAttributes.Any(
            a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute"))
            irType.IsByRefLike = true;

        if (typeDef.IsEnum)
            irType.EnumUnderlyingType = typeDef.EnumUnderlyingType ?? "System.Int32";

        // Detect delegate types (base is System.MulticastDelegate)
        if (typeDef.BaseTypeName is "System.MulticastDelegate" or "System.Delegate")
            irType.IsDelegate = true;

        // Register value types for CppNameMapper so it doesn't add pointer suffix
        // Register both IL name (for IsValueType lookups) and C++ name (for GetDefaultValue)
        // System.Enum and System.ValueType are abstract reference types even though
        // Cecil reports IsValueType=true (because their base is System.ValueType).
        // They must be treated as reference types (pointer semantics) in generated C++.
        if (typeDef.IsValueType && typeDef.FullName is not "System.Enum" and not "System.ValueType")
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

            // LayoutKind.Explicit: read per-field byte offset from Cecil
            if (!fieldDef.IsStatic && typeDef.GetCecilType().IsExplicitLayout)
            {
                var cf = fieldDef.GetCecilField();
                irField.ExplicitOffset = cf.Offset;
            }

            if (_typeCache.TryGetValue(fieldDef.TypeName, out var fieldType))
            {
                irField.FieldType = fieldType;
            }

            irField.ConstantValue = fieldDef.ConstantValue;
            var cecilField = fieldDef.GetCecilField();
            irField.Attributes = (uint)cecilField.Attributes;

            // C.7.3: Parse field-level [MarshalAs] for P/Invoke struct marshaling
            if (cecilField.HasMarshalInfo && cecilField.MarshalInfo != null)
            {
                irField.MarshalAs = (MarshalAsType)(int)cecilField.MarshalInfo.NativeType;
                if (cecilField.MarshalInfo is Mono.Cecil.FixedSysStringMarshalInfo fixedStrInfo)
                {
                    // ByValTStr: [MarshalAs(UnmanagedType.ByValTStr, SizeConst=N)]
                    irField.MarshalSizeConst = fixedStrInfo.Size;
                }
                else if (cecilField.MarshalInfo is Mono.Cecil.FixedArrayMarshalInfo fixedArrayInfo)
                {
                    // ByValArray: [MarshalAs(UnmanagedType.ByValArray, SizeConst=N)]
                    irField.MarshalSizeConst = fixedArrayInfo.Size;
                    irField.MarshalElementTypeName = MapNativeTypeToIL(fixedArrayInfo.ElementType);
                }
            }

            targetList.Add(irField);
        }

        // Read explicit struct size from ClassSize metadata (ECMA-335 II.10.1.2).
        // This is critical for fixed-size buffer types (InlineArray / FixedBuffer)
        // where ClassSize > sum of declared fields (e.g., fixed byte[12] has one
        // FixedElementField:byte but ClassSize=12).
        var cecilType = typeDef.GetCecilType();

        // C.7.3: Parse StructCharSet from TypeAttributes.StringFormatMask
        var typeAttrs = cecilType.Attributes;
        if ((typeAttrs & Mono.Cecil.TypeAttributes.UnicodeClass) != 0)
            irType.StructCharSet = PInvokeCharSet.Unicode;
        else if ((typeAttrs & Mono.Cecil.TypeAttributes.AutoClass) != 0)
            irType.StructCharSet = PInvokeCharSet.Auto;

        // LayoutKind.Explicit: fields have explicit byte offsets, may overlap (unions)
        if (cecilType.IsExplicitLayout)
            irType.IsExplicitLayout = true;

        if (irType.IsValueType && cecilType.HasLayoutInfo && cecilType.ClassSize > 0)
        {
            irType.ExplicitSize = cecilType.ClassSize;
        }

        // Detect [InlineArray(N)] attribute (C# 12+).
        // These types have a single field repeated N times. The runtime computes
        // size as N * sizeof(element), but ClassSize in metadata may be absent or
        // reflect 32-bit sizes (e.g., ref fields as 4 bytes instead of 8 on x64).
        // We compute ExplicitSize = N * elemSize ourselves, overriding ClassSize
        // when we can determine the element size correctly.
        if (irType.IsValueType && cecilType.HasCustomAttributes)
        {
            foreach (var attr in cecilType.CustomAttributes)
            {
                if (attr.AttributeType.FullName == "System.Runtime.CompilerServices.InlineArrayAttribute"
                    && attr.HasConstructorArguments)
                {
                    irType.IsInlineArray = true;
                    int inlineCount = (int)attr.ConstructorArguments[0].Value;
                    if (irType.Fields.Count == 1)
                    {
                        var fieldDef = cecilType.Fields.FirstOrDefault(f => !f.IsStatic);
                        int elemSize = ResolveInlineArrayElementSizeFromCecil(fieldDef?.FieldType);
                        if (elemSize > 0)
                            irType.ExplicitSize = inlineCount * elemSize;
                        // If elemSize == 0 (unknown type), keep ClassSize from metadata as fallback
                    }
                    break;
                }
            }
        }

        // Calculate instance size
        CalculateInstanceSize(irType);
    }

    /// <summary>
    /// Compute the element size for an [InlineArray] field using Cecil type metadata.
    /// Works for non-generic types where the field type is already concrete.
    /// </summary>
    private int ResolveInlineArrayElementSizeFromCecil(Mono.Cecil.TypeReference? fieldType)
    {
        if (fieldType == null) return 0;

        // Managed references (ref T) and unmanaged pointers (T*) are always pointer-sized
        if (fieldType.IsByReference || fieldType.IsPointer) return 8;
        if (fieldType.IsArray) return 8; // Arrays are reference types

        try
        {
            var resolved = fieldType.Resolve();
            if (resolved == null) return 0;
            if (!resolved.IsValueType) return 8; // Reference type → pointer

            // Value type: try primitive sizes
            return resolved.FullName switch
            {
                "System.Boolean" or "System.Byte" or "System.SByte" => 1,
                "System.Int16" or "System.UInt16" or "System.Char" => 2,
                "System.Int32" or "System.UInt32" or "System.Single" => 4,
                "System.Int64" or "System.UInt64" or "System.Double" or "System.IntPtr" or "System.UIntPtr" => 8,
                _ => 0, // Unknown value type — need IR lookup
            };
        }
        catch { return 0; }
    }

    /// <summary>
    /// Estimate the C++ size of a field given its IR type name.
    /// Returns sizeof(void*) for reference types, known sizes for primitives,
    /// or looks up the IRType's InstanceSize for value types. Returns 0 if unknown.
    /// </summary>
    private int EstimateFieldSize(string fieldTypeName)
    {
        // Reference types are always pointer-sized
        if (fieldTypeName.EndsWith("*"))
            return 8;

        var result = fieldTypeName switch
        {
            "bool" or "int8_t" or "uint8_t" or "cil2cpp::Byte" or "cil2cpp::SByte" or "cil2cpp::Boolean" => 1,
            "int16_t" or "uint16_t" or "cil2cpp::Int16" or "cil2cpp::UInt16" or "cil2cpp::Char" => 2,
            "int32_t" or "uint32_t" or "float" or "cil2cpp::Int32" or "cil2cpp::UInt32" or "cil2cpp::Single" => 4,
            "int64_t" or "uint64_t" or "double" or "cil2cpp::Int64" or "cil2cpp::UInt64" or "cil2cpp::Double" or "cil2cpp::IntPtr" or "cil2cpp::UIntPtr" => 8,
            _ => 0,
        };
        if (result > 0) return result;

        // Try to look up already-created value type by CppName
        var existing = _module.Types.FirstOrDefault(t => t.CppName == fieldTypeName && t.IsValueType);
        if (existing != null && existing.InstanceSize > 0)
            return existing.InstanceSize;

        return 0;
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
        if (irType.IsExplicitLayout)
        {
            // LayoutKind.Explicit: fields have explicit byte offsets from [FieldOffset(N)]
            // Size = max(field.Offset + fieldSize) across all fields
            int baseOffset = size; // object header for reference types
            foreach (var field in irType.Fields)
            {
                int fieldSize = GetMarshaledFieldSize(field, irType);
                int fieldOffset = baseOffset + (field.ExplicitOffset ?? 0);
                field.Offset = fieldOffset;
                int end = fieldOffset + fieldSize;
                if (end > size) size = end;
            }
        }
        else
        {
            // LayoutKind.Sequential: auto-computed offsets
            foreach (var field in irType.Fields)
            {
                int fieldSize = GetMarshaledFieldSize(field, irType);
                int alignment = GetMarshaledFieldAlignment(field, irType);

                size = Align(size, alignment);
                field.Offset = size;
                size += fieldSize;
            }
        }

        // For types with explicit size (fixed-size buffers / InlineArray),
        // the actual size may be larger than the sum of declared fields.
        if (irType.ExplicitSize > 0 && irType.ExplicitSize > size)
            size = irType.ExplicitSize;

        // Store managed field size BEFORE trailing alignment — used by initobj/cpobj
        irType.ManagedFieldSize = size;

        // Align total size to match C++ sizeof():
        // - Reference types: always pointer-aligned (contain TypeInfo* pointer)
        // - Value types: aligned to max field alignment (matches C++ struct rules)
        if (irType.IsValueType)
        {
            int structAlign = 1;
            foreach (var field in irType.Fields)
            {
                int fa = GetFieldAlignment(field.FieldTypeName);
                if (fa > structAlign) structAlign = fa;
            }
            size = Align(size, structAlign);
        }
        else
        {
            size = Align(size, PointerAlignment);
        }
        irType.InstanceSize = size;
    }

    /// <summary>
    /// Get the size of a field, accounting for [MarshalAs] overrides (ByValTStr, ByValArray).
    /// </summary>
    private int GetMarshaledFieldSize(IRField field, IRType irType)
    {
        if (field.MarshalAs == MarshalAsType.ByValTStr && field.MarshalSizeConst > 0)
        {
            int charSize = (irType.StructCharSet == PInvokeCharSet.Unicode
                || irType.StructCharSet == PInvokeCharSet.Auto) ? 2 : 1;
            return field.MarshalSizeConst * charSize;
        }
        if (field.MarshalAs == MarshalAsType.ByValArray && field.MarshalSizeConst > 0)
        {
            var elemTypeName = field.MarshalElementTypeName ?? "System.Byte";
            return field.MarshalSizeConst * GetFieldSize(elemTypeName);
        }
        return GetFieldSize(field.FieldTypeName);
    }

    /// <summary>
    /// Get the alignment of a field, accounting for [MarshalAs] overrides.
    /// </summary>
    private int GetMarshaledFieldAlignment(IRField field, IRType irType)
    {
        if (field.MarshalAs == MarshalAsType.ByValTStr && field.MarshalSizeConst > 0)
        {
            return (irType.StructCharSet == PInvokeCharSet.Unicode
                || irType.StructCharSet == PInvokeCharSet.Auto) ? 2 : 1;
        }
        if (field.MarshalAs == MarshalAsType.ByValArray && field.MarshalSizeConst > 0)
        {
            var elemTypeName = field.MarshalElementTypeName ?? "System.Byte";
            return Math.Min(GetFieldSize(elemTypeName), 8);
        }
        return GetFieldAlignment(field.FieldTypeName);
    }

    // Field sizes per ECMA-335 §I.8.2.1 (Built-in Value Types)
    private int GetFieldSize(string typeName)
    {
        // Primitive types — known sizes
        var primitiveSize = typeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Int16" or "System.UInt16" or "System.Char" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            _ => 0
        };
        if (primitiveSize > 0) return primitiveSize;

        // Non-primitive value types: look up actual InstanceSize from the type cache.
        // This handles struct-in-struct fields (e.g., Asn1Tag inside Nullable<Asn1Tag>).
        // InstanceSize includes C++ trailing alignment, matching sizeof() in the generated code.
        if (_typeCache.TryGetValue(typeName, out var irType) && irType.IsValueType && irType.InstanceSize > 0)
            return irType.InstanceSize;

        // Enums: look up the underlying type size
        if (_typeCache.TryGetValue(typeName, out var enumType) && enumType.IsEnum)
            return GetFieldSize(enumType.EnumUnderlyingType ?? "System.Int32");

        return 8; // Pointer size on 64-bit (reference types, IntPtr/UIntPtr)
    }

    [ThreadStatic] private static HashSet<string>? _alignmentVisited;

    private int GetFieldAlignment(string typeName)
    {
        // For value types in the cache, compute actual alignment (max alignment of their fields).
        // This prevents over-alignment: e.g., Asn1Tag should align to 4 (int32), not 8 (pointer).
        if (_typeCache.TryGetValue(typeName, out var irType) && irType.IsValueType && irType.Fields.Count > 0)
        {
            _alignmentVisited ??= new HashSet<string>();
            if (!_alignmentVisited.Add(typeName))
                return 8; // Cycle detected — fall back to pointer alignment
            try
            {
                int maxAlign = 1;
                foreach (var field in irType.Fields)
                {
                    int align = GetFieldAlignment(field.FieldTypeName);
                    if (align > maxAlign) maxAlign = align;
                }
                return maxAlign;
            }
            finally
            {
                _alignmentVisited.Remove(typeName);
            }
        }
        return Math.Min(GetFieldSize(typeName), 8);
    }

    /// <summary>
    /// Map Cecil NativeType enum to IL type name for ByValArray element type.
    /// </summary>
    private static string? MapNativeTypeToIL(Mono.Cecil.NativeType nativeType) => nativeType switch
    {
        Mono.Cecil.NativeType.Boolean => "System.Boolean",
        Mono.Cecil.NativeType.I1 => "System.SByte",
        Mono.Cecil.NativeType.U1 => "System.Byte",
        Mono.Cecil.NativeType.I2 => "System.Int16",
        Mono.Cecil.NativeType.U2 => "System.UInt16",
        Mono.Cecil.NativeType.I4 => "System.Int32",
        Mono.Cecil.NativeType.U4 => "System.UInt32",
        Mono.Cecil.NativeType.I8 => "System.Int64",
        Mono.Cecil.NativeType.U8 => "System.UInt64",
        Mono.Cecil.NativeType.R4 => "System.Single",
        Mono.Cecil.NativeType.R8 => "System.Double",
        _ => null,
    };

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
    /// Uses the cached _mangledNameIndex for O(1) lookup instead of O(assemblies × types) linear scan.
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

        // Use O(1) index lookup instead of scanning all assemblies
        var index = GetMangledNameIndex();
        if (index.TryGetValue(raw, out var typeDef))
        {
            if (!typeDef.IsEnum) return; // Found but not an enum — stop

            var underlyingCpp = GetEnumUnderlyingCppType(typeDef);
            _module.ExternalEnumTypes[raw] = underlyingCpp;
            CppNameMapper.RegisterValueType(typeDef.FullName);
            CppNameMapper.RegisterValueType(raw);
            if (!alreadyValueType)
                newlyDiscovered.Add(raw);
        }
    }

    /// <summary>
    /// Build or return the cached mangled-name → Cecil TypeDefinition index.
    /// Scans all loaded assemblies including nested types at all levels.
    /// </summary>
    private Dictionary<string, TypeDefinition> GetMangledNameIndex()
    {
        if (_mangledNameIndex != null) return _mangledNameIndex;

        _mangledNameIndex = new Dictionary<string, TypeDefinition>();
        foreach (var asm in _assemblySet.LoadedAssemblies.Values)
        {
            foreach (var module in asm.Modules)
            {
                IndexTypesRecursive(module.Types, _mangledNameIndex);
            }
        }
        return _mangledNameIndex;
    }

    private static void IndexTypesRecursive(
        IEnumerable<TypeDefinition> types, Dictionary<string, TypeDefinition> index)
    {
        foreach (var typeDef in types)
        {
            var mangled = CppNameMapper.MangleTypeName(typeDef.FullName);
            index.TryAdd(mangled, typeDef);
            if (typeDef.HasNestedTypes)
                IndexTypesRecursive(typeDef.NestedTypes, index);
        }
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

        // Scan compiled method bodies for:
        // 1. ldfld/stfld/ldflda/stsfld references — types whose fields are accessed
        // 2. Local variable types — value types used as locals need correct sizeof
        //    (critical for P/Invoke interop structs like WSAData passed to native functions)
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

                // Scan local variable types — value types must have correct layout
                foreach (var local in cecil.Body.Variables)
                {
                    var localType = local.VariableType;
                    TryDiscoverValueTypeLocal(localType, seen, typesToAdd);
                }

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

            AddTypeToModule(irType);
            _typeCache[cecilType.FullName] = irType;

            // Populate fields/base types
            PopulateTypeDetails(typeDefInfo, irType);
        }
    }

    /// <summary>
    /// Discover value types used as local variables that are missing from the IR module.
    /// Value types need correct sizeof for stack allocation — especially critical for
    /// P/Invoke interop structs (e.g., WSADATA) where native functions write to them.
    /// </summary>
    private void TryDiscoverValueTypeLocal(TypeReference? typeRef, HashSet<string> seen, List<TypeDefinition> toAdd)
    {
        if (typeRef == null) return;
        // Unwrap wrapper types
        if (typeRef is ByReferenceType byRef)
            typeRef = byRef.ElementType;
        if (typeRef is PointerType ptr)
            typeRef = ptr.ElementType;
        // Skip generic instances (handled by generic specialization pipeline)
        if (typeRef is GenericInstanceType) return;
        if (typeRef is ArrayType) return;
        if (typeRef is GenericParameter) return;

        try
        {
            var resolved = typeRef.Resolve();
            if (resolved == null) return;
            if (resolved.HasGenericParameters) return;
            // Only value types (structs) need correct layout for stack allocation
            // Reference types are accessed through pointers — their size doesn't matter for locals
            if (!resolved.IsValueType) return;
            if (resolved.IsEnum) return;
            if (seen.Contains(resolved.FullName)) return;
            seen.Add(resolved.FullName);

            // Skip primitives
            if (CppNameMapper.IsPrimitive(resolved.FullName)) return;
            if (resolved.FullName == "<Module>") return;

            toAdd.Add(resolved);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CIL2CPP] Warning: TryDiscoverValueTypeLocal failed to resolve '{typeRef?.FullName}': {ex.Message}");
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CIL2CPP] Warning: TryDiscoverCecilType failed to resolve '{typeRef?.FullName}': {ex.Message}");
        }
    }
}
