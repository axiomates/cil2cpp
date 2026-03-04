namespace CIL2CPP.Core.CodeGen;

public partial class CppCodeGenerator
{
    /// <summary>
    /// Known C++ implementations for BCL generic specialization stubs that cannot
    /// compile from IL (due to static abstract interface methods, JIT intrinsics, etc.).
    /// Key: C++ function name, Value: C++ function body (without braces).
    /// </summary>
    private static readonly Dictionary<string, string> KnownStubImplementations = new()
    {
        // ===== Number Formatting: UInt32ToDecChars<Char> =====
        // Write decimal characters backward into buffer, no leading zeros
        ["System_Number_UInt32ToDecChars_System_Char__System_Char_ptr_System_UInt32"] =
            """
                do {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + (value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        // Write decimal characters backward with minimum digit count
        ["System_Number_UInt32ToDecChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + (value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        // ===== Number Formatting: WriteDigits<Char> =====
        // Write digits forward into buffer
        ["System_Number_WriteDigits_System_Char"] =
            """
                for (int32_t i = count - 1; i >= 1; i--) {
                    ptr[i] = static_cast<char16_t>(u'0' + (value % 10));
                    value /= 10;
                }
                ptr[0] = static_cast<char16_t>(u'0' + value);
            """,

        // ===== Number Formatting: UInt64ToDecChars<Char> =====
        // 64-bit version, write backward with minimum digit count
        ["System_Number_UInt64ToDecChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        // 64-bit version, no leading zeros
        ["System_Number_UInt64ToDecChars_System_Char__System_Char_ptr_System_UInt64"] =
            """
                do {
                    *(--bufferEnd) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        // ===== Number Formatting: UInt32/UInt64ToDecChars<Byte> =====
        // Byte versions (used by UTF-8 formatting paths)
        ["System_Number_UInt32ToDecChars_System_Byte"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + (value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        ["System_Number_UInt64ToDecChars_System_Byte"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + static_cast<uint8_t>(value % 10));
                    value /= 10;
                }
                return bufferEnd;
            """,

        // ===== Number Formatting: UInt32/UInt64ToDecChars<Byte> no-leading-zeros =====
        ["System_Number_UInt32ToDecChars_System_Byte__System_Byte_ptr_System_UInt32"] =
            """
                do {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + (value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        ["System_Number_UInt64ToDecChars_System_Byte__System_Byte_ptr_System_UInt64"] =
            """
                do {
                    *(--bufferEnd) = static_cast<uint8_t>('0' + static_cast<uint8_t>(value % 10));
                    value /= 10;
                } while (value != 0);
                return bufferEnd;
            """,

        // ===== Number Formatting: Hex/Binary chars =====
        ["System_Number_Int32ToHexChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    uint8_t digit = static_cast<uint8_t>(value & 0xF);
                    *(--buffer) = static_cast<char16_t>(digit < 10 ? u'0' + digit : hexBase + digit - 10);
                    value >>= 4;
                }
                return buffer;
            """,

        ["System_Number_Int64ToHexChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    uint8_t digit = static_cast<uint8_t>(value & 0xF);
                    *(--buffer) = static_cast<char16_t>(digit < 10 ? u'0' + digit : hexBase + digit - 10);
                    value >>= 4;
                }
                return buffer;
            """,

        ["System_Number_UInt32ToBinaryChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--buffer) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value & 1));
                    value >>= 1;
                }
                return buffer;
            """,

        ["System_Number_UInt64ToBinaryChars_System_Char"] =
            """
                while (--digits >= 0 || value != 0) {
                    *(--buffer) = static_cast<char16_t>(u'0' + static_cast<char16_t>(value & 1));
                    value >>= 1;
                }
                return buffer;
            """,

        // ===== WriteDigits<Byte> =====
        ["System_Number_WriteDigits_System_Byte"] =
            """
                for (int32_t i = count - 1; i >= 1; i--) {
                    ptr[i] = static_cast<uint8_t>('0' + (value % 10));
                    value /= 10;
                }
                ptr[0] = static_cast<uint8_t>('0' + value);
            """,

        // ===== BitOperations =====
        ["System_Numerics_BitOperations_PopCount__System_UInt64"] =
            """
                int32_t count = 0;
                while (value) { count += static_cast<int32_t>(value & 1); value >>= 1; }
                return count;
            """,

        // ===== Interop.Kernel32.LocalFree(void*) =====
        // Forwards to the intptr_t overload with explicit cast (void* → intptr_t)
        ["Interop_Kernel32_LocalFree__System_Void"] =
            """
                return reinterpret_cast<void*>(Interop_Kernel32_LocalFree(reinterpret_cast<intptr_t>(ptr)));
            """,

        // ===== OperationCanceledException.get_CancellationToken =====
        // Returns default CancellationToken (runtime stores as void*)
        ["System_OperationCanceledException_get_CancellationToken"] =
            """
                // HACK: CancellationToken is stored as void* in runtime — return default
                return {};
            """,

        // ===== RuntimeHelpers.GetHashCode =====
        // Identity hash code based on object pointer (BoehmGC doesn't move objects).
        // BCL IL uses ObjectMethodTable/GetMethodTable which are CLR internal.
        ["System_Runtime_CompilerServices_RuntimeHelpers_GetHashCode"] =
            """
                if (!o) return 0;
                auto addr = reinterpret_cast<uintptr_t>(o);
                addr ^= addr >> 33;
                addr *= 0xff51afd7ed558ccdULL;
                addr ^= addr >> 33;
                addr *= 0xc4ceb9fe1a85ec53ULL;
                addr ^= addr >> 33;
                return static_cast<int32_t>(addr);
            """,

        // ===== Type.GetRootElementType =====
        // In AOT, we don't create ByRef/Pointer/Array types at runtime,
        // so HasElementType is always false and GetRootElementType just returns 'this'.
        ["System_Type_GetRootElementType"] =
            """
                return (System_Type*)__this;
            """,

    };

    /// <summary>
    /// Try to get a known C++ implementation for a stub function.
    /// Returns null if no known implementation exists.
    /// </summary>
    private static string? GetKnownStubBody(string cppName)
    {
        if (KnownStubImplementations.TryGetValue(cppName, out var body))
            return body;

        // Pattern-based matching for generic specializations:

        // SpanHelpers.DontNegate<T>.NegateIfNeeded(bool) → identity (return equals)
        // These are JIT intrinsic helper structs whose IL bodies can't compile in AOT.
        if (cppName.Contains("_DontNegate_") && cppName.EndsWith("_NegateIfNeeded__System_Boolean"))
            return "    return equals;";

        // SpanHelpers.Negate<T>.NegateIfNeeded(bool) → negation (return !equals)
        if (cppName.Contains("_Negate_") && !cppName.Contains("DontNegate")
            && cppName.EndsWith("_NegateIfNeeded__System_Boolean"))
            return "    return !equals;";

        // IndexOfAnyAsciiSearcher.INegator.NegateIfNeeded(bool) — interface stub, default identity
        if (cppName.Contains("_INegator_NegateIfNeeded__System_Boolean"))
            return "    return result;";

        return null;
    }

    /// <summary>
    /// Check if a method's IR body is a stub (single block with only IRReturn).
    /// These are generated by IRBuilder.GenerateStubBody for methods that can't compile from IL.
    /// </summary>
    private static bool IsStubBody(IR.IRMethod method)
    {
        if (method.BasicBlocks.Count != 1) return false;
        var block = method.BasicBlocks[0];
        return block.Instructions.Count == 1 && block.Instructions[0] is IR.IRReturn;
    }

    /// <summary>
    /// AOT replacement bodies for BCL methods that use AOT-incompatible patterns
    /// (e.g., typeof(IEquatable&lt;&gt;).MakeGenericType, Activator.CreateInstance with runtime types).
    /// These bypass all codegen gates — the IL body is never emitted, only this custom C++ body.
    /// </summary>
    private static string? GetAotReplacementBody(string? cppName, HashSet<string>? knownTypes = null)
    {
        if (cppName == null) return null;

        // JsonWriter.BuildStateArray: uses StateArrayTemplate.ToList() which needs IEnumerable<T>
        // on arrays. Arrays don't have interface vtables in our runtime. Replace with direct copy.
        if (cppName == "Newtonsoft_Json_JsonWriter_BuildStateArray")
            return """
                    // AOT replacement: avoid Enumerable.ToList() on array (no IEnumerable<T> interface)
                    Newtonsoft_Json_JsonWriter_ensure_cctor();
                    auto tmpl = (cil2cpp::Array*)(void*)Newtonsoft_Json_JsonWriter_statics.f_StateArrayTemplate;
                    if (!tmpl) return nullptr;
                    auto len = cil2cpp::array_length(tmpl);
                    // Create State[][] result by copying template
                    auto result = cil2cpp::array_create(&Newtonsoft_Json_JsonWriter_State___TypeInfo, len);
                    for (int32_t i = 0; i < len; i++) {
                        auto row = cil2cpp::array_get<cil2cpp::Object*>(tmpl, i);
                        cil2cpp::array_set<cil2cpp::Object*>(result, i, row);
                    }
                    return result;
                """;

        // ComparerHelpers.CreateDefaultEqualityComparer(Type) → ObjectEqualityComparer<Object>
        // BCL uses MakeGenericType which is AOT-incompatible. Return universal fallback comparer
        // that delegates to Object.Equals/GetHashCode via virtual dispatch.
        if (cppName == "System_Collections_Generic_ComparerHelpers_CreateDefaultEqualityComparer")
            return """
                    // AOT replacement: return ObjectEqualityComparer<Object> as universal fallback
                    auto obj = (System_Collections_Generic_ObjectEqualityComparer_1_System_Object*)
                        cil2cpp::gc::alloc(sizeof(System_Collections_Generic_ObjectEqualityComparer_1_System_Object),
                            &System_Collections_Generic_ObjectEqualityComparer_1_System_Object_TypeInfo);
                    return (cil2cpp::Object*)obj;
                """;

        // ComparerHelpers.CreateDefaultComparer(Type) → ObjectComparer<Object>
        // Same AOT-incompatible pattern as CreateDefaultEqualityComparer.
        if (cppName == "System_Collections_Generic_ComparerHelpers_CreateDefaultComparer")
            return """
                    // AOT replacement: return ObjectComparer<Object> as universal fallback
                    auto obj = (System_Collections_Generic_ObjectComparer_1_System_Object*)
                        cil2cpp::gc::alloc(sizeof(System_Collections_Generic_ObjectComparer_1_System_Object),
                            &System_Collections_Generic_ObjectComparer_1_System_Object_TypeInfo);
                    return (cil2cpp::Object*)obj;
                """;

        // EqualityComparer<T>._cctor → allocate ObjectEqualityComparer<T>
        // The BCL cctor calls CreateDefaultEqualityComparer(typeof(T)) which uses MakeGenericType (AOT-incompatible).
        // We pre-generate ObjectEqualityComparer<T> for each T and allocate the correct specialization directly.
        // This ensures the object implements IEqualityComparer<T> for correct interface dispatch.
        // Fallback to ObjectEqualityComparer<Object> if the specific specialization wasn't generated
        // (e.g., for SIMD types or other filtered type arguments).
        if (cppName.StartsWith("System_Collections_Generic_EqualityComparer_1_") && cppName.EndsWith("__cctor"))
        {
            var typePart = cppName[..^"__cctor".Length];
            var innerType = typePart["System_Collections_Generic_EqualityComparer_1_".Length..];
            var objEqType = $"System_Collections_Generic_ObjectEqualityComparer_1_{innerType}";
            // Fall back to Object variant if the specific specialization doesn't exist
            if (knownTypes != null && !knownTypes.Contains(objEqType))
                objEqType = "System_Collections_Generic_ObjectEqualityComparer_1_System_Object";
            return $"""
                    // AOT replacement: allocate ObjectEqualityComparer<T> with correct IEqualityComparer<T> interface
                    auto obj = cil2cpp::gc::alloc(sizeof({objEqType}), &{objEqType}_TypeInfo);
                    {typePart}_statics.f__Default_k__BackingField = ({typePart}*)(void*)obj;
                """;
        }

        // Comparer<T>._cctor → same pattern for Comparer
        if (cppName.StartsWith("System_Collections_Generic_Comparer_1_") && cppName.EndsWith("__cctor"))
        {
            var typePart = cppName[..^"__cctor".Length];
            var innerType = typePart["System_Collections_Generic_Comparer_1_".Length..];
            var objCmpType = $"System_Collections_Generic_ObjectComparer_1_{innerType}";
            if (knownTypes != null && !knownTypes.Contains(objCmpType))
                objCmpType = "System_Collections_Generic_ObjectComparer_1_System_Object";
            return $"""
                    // AOT replacement: allocate ObjectComparer<T> with correct IComparer<T> interface
                    auto obj = cil2cpp::gc::alloc(sizeof({objCmpType}), &{objCmpType}_TypeInfo);
                    {typePart}_statics.f__Default_k__BackingField = ({typePart}*)(void*)obj;
                """;
        }

        // RuntimeType.IsAssignableFrom(Type) — the IL body uses CLR internal RuntimeTypeHandle
        // infrastructure (QCall, CanCastTo, etc.) which are all stubs.
        // AOT: delegate to our runtime's TypeInfo-based assignability check.
        if (cppName == "System_RuntimeType_IsAssignableFrom__System_Type")
            return """
                    // AOT: use runtime TypeInfo assignability check
                    if (!c) return false;
                    auto* self_ti = ((cil2cpp::Type*)__this)->type_info;
                    auto* c_ti = ((cil2cpp::Type*)c)->type_info;
                    if (!self_ti || !c_ti) return false;
                    return cil2cpp::type_is_assignable_from(self_ti, c_ti);
                """;

        // RuntimeType.IsSubclassOf(Type) — same CLR internal dependency.
        if (cppName == "System_RuntimeType_IsSubclassOf")
            return """
                    // AOT: walk base type chain using TypeInfo
                    if (!type) return false;
                    auto* self_ti = ((cil2cpp::Type*)__this)->type_info;
                    auto* target_ti = ((cil2cpp::Type*)type)->type_info;
                    if (!self_ti || !target_ti || self_ti == target_ti) return false;
                    auto* cur = self_ti->base_type;
                    while (cur) {
                        if (cur == target_ti) return true;
                        cur = cur->base_type;
                    }
                    return false;
                """;

        // RuntimeType.GetInterfaces() — requires RuntimeTypeCache (GCHandle, QCall stubs).
        // AOT: build array from TypeInfo.interfaces.
        if (cppName == "System_RuntimeType_GetInterfaces")
            return """
                    // AOT: build Type[] from TypeInfo interfaces
                    auto* ti = ((cil2cpp::Type*)__this)->type_info;
                    if (!ti || !ti->interfaces || ti->interface_count == 0)
                        return cil2cpp::array_create(&System_Type_TypeInfo, 0);
                    auto* arr = cil2cpp::array_create(&System_Type_TypeInfo, ti->interface_count);
                    for (uint32_t i = 0; i < ti->interface_count; i++) {
                        auto* iface_type = cil2cpp::icall::Type_GetTypeFromHandle(ti->interfaces[i]);
                        cil2cpp::array_set<cil2cpp::Object*>(arr, i, iface_type);
                    }
                    return arr;
                """;

        // RuntimeType.GetCustomAttributes(bool) → return empty Attribute[]
        // The IL body walks RuntimeModule metadata (RuntimeTypeHandle.GetModule → AddCustomAttributes),
        // which is AOT-incompatible since GetModule is a stub returning nullptr.
        // TODO: convert TypeInfo.custom_attributes to managed Attribute objects
        if (cppName == "System_RuntimeType_GetCustomAttributes__System_Boolean")
            return """
                    // AOT: no RuntimeModule metadata — return empty Attribute[]
                    return cil2cpp::array_create(&System_Attribute_TypeInfo, 0);
                """;

        // RuntimeType.GetCustomAttributes(Type, bool) → same issue
        if (cppName == "System_RuntimeType_GetCustomAttributes__System_Type_System_Boolean")
            return """
                    // AOT: no RuntimeModule metadata — return empty Attribute[]
                    return cil2cpp::array_create(&System_Attribute_TypeInfo, 0);
                """;

        // RuntimeType.get_FullName — BCL goes through RuntimeTypeCache → RuntimeModule (CLR internal).
        // AOT: return full_name from TypeInfo directly.
        // HACK: string_create_utf8 uses runtime's System::String_TypeInfo which has vtable=nullptr.
        // Patch to codegen's System_String_TypeInfo so virtual dispatch (Equals etc.) works.
        if (cppName == "System_RuntimeType_get_FullName")
            return """
                    auto* ti = ((cil2cpp::Type*)__this)->type_info;
                    if (!ti || !ti->full_name) return nullptr;
                    auto* s = cil2cpp::string_create_utf8(ti->full_name);
                    if (s) ((cil2cpp::Object*)s)->__type_info = &System_String_TypeInfo;
                    return s;
                """;

        // RuntimeType.get_Name — same CLR internal path via RuntimeTypeCache.
        if (cppName == "System_RuntimeType_get_Name")
            return """
                    auto* ti = ((cil2cpp::Type*)__this)->type_info;
                    if (!ti || !ti->name) return nullptr;
                    auto* s = cil2cpp::string_create_utf8(ti->name);
                    if (s) ((cil2cpp::Object*)s)->__type_info = &System_String_TypeInfo;
                    return s;
                """;

        // RuntimeType.get_Namespace — same CLR internal path.
        if (cppName == "System_RuntimeType_get_Namespace")
            return """
                    auto* ti = ((cil2cpp::Type*)__this)->type_info;
                    if (!ti || !ti->namespace_name) return nullptr;
                    auto* s = cil2cpp::string_create_utf8(ti->namespace_name);
                    if (s) ((cil2cpp::Object*)s)->__type_info = &System_String_TypeInfo;
                    return s;
                """;

        // Newtonsoft.Json JsonTypeReflector.GetAttribute<T>(Type) / GetAttribute<T>(MemberInfo)
        // After checking direct attributes (which we return null for), these methods call
        // type.GetInterfaces() and iterate looking for attributes on interfaces.
        // RuntimeType.GetInterfaces() crashes because it requires CLR internal RuntimeTypeCache
        // infrastructure (GCHandle-based caching, QCall, etc.) which are all stubs in AOT.
        // AOT: no custom attribute metadata → always return null.
        if (cppName.StartsWith("Newtonsoft_Json_Serialization_JsonTypeReflector_GetAttribute_"))
            return """
                    // AOT: no custom attribute metadata — attribute not found
                    return nullptr;
                """;

        // Newtonsoft.Json JsonTypeReflector.GetAssociatedMetadataType(Type)
        // Calls GetCustomAttributes<MetadataTypeAttribute> which would crash.
        // AOT: no metadata type association → return null.
        if (cppName == "Newtonsoft_Json_Serialization_JsonTypeReflector_GetAssociatedMetadataType")
            return """
                    // AOT: no MetadataTypeAttribute — no associated metadata type
                    return nullptr;
                """;

        // Newtonsoft.Json ReflectionUtils.GetAttributes<T> — all variants call
        // GetCustomAttributes().Cast<T>().ToArray(), which crashes because arrays
        // don't have non-generic ICollection interface vtables in our runtime.
        // AOT: no reflection metadata → return empty array.
        if (cppName.StartsWith("Newtonsoft_Json_Utilities_ReflectionUtils_GetAttributes"))
            return """
                    // AOT: no custom attribute metadata — return empty array
                    return cil2cpp::array_create(&System_Attribute_TypeInfo, 0);
                """;

        // Newtonsoft.Json ReflectionUtils.GetAttribute<T> calls GetAttributes<T>().FirstOrDefault()
        // AOT: no reflection metadata → always returns null.
        if (cppName.StartsWith("Newtonsoft_Json_Utilities_ReflectionUtils_GetAttribute_"))
            return """
                    // AOT: no custom attribute metadata — attribute not found
                    return nullptr;
                """;

        // Newtonsoft.Json CachedAttributeGetter<T>.GetAttribute(object) → calls ThreadSafeStore.Get
        // The cctor for several specializations is stubbed (body has codegen errors),
        // leaving the ThreadSafeStore field null → crash.
        // Since all underlying JsonTypeReflector.GetAttribute<T> methods return nullptr (AOT),
        // the cache would only ever store nullptr. Short-circuit to return nullptr directly.
        if (cppName.StartsWith("Newtonsoft_Json_Serialization_CachedAttributeGetter_1_") && cppName.Contains("_GetAttribute"))
            return """
                    // AOT: no custom attribute metadata — attribute not found
                    return nullptr;
                """;

        return null;
    }
}
