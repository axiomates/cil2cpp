/**
 * CIL2CPP Runtime - CoreRuntimeTypes Method Bridge
 *
 * Provides extern "C" implementations for methods on CoreRuntimeTypes
 * (Object, Exception, Array, Type, Delegate, Reflection, Thread, etc.).
 * The compiler does NOT generate bodies for these -- the runtime owns them.
 *
 * These functions use C linkage so the linker matches them by name against
 * the forward declarations in the generated header. Pointer parameters use
 * void* for ABI compatibility (all pointers are the same size).
 */

#include <cstdint>
#include <cil2cpp/object.h>
#include <cil2cpp/string.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/memberinfo.h>

// Value types used by CoreRuntimeTypes method signatures.
// These must be ABI-compatible with the generated struct definitions.

// Enums -> int32_t (all .NET enums are int32_t underlying)
typedef int32_t System_Reflection_BindingFlags;
typedef int32_t System_Reflection_CallingConventions;
typedef int32_t System_Reflection_MemberTypes;
typedef int32_t System_Reflection_TypeAttributes;
typedef int32_t System_Reflection_FieldAttributes;
typedef int32_t System_Reflection_MethodAttributes;
typedef int32_t System_Reflection_MethodImplAttributes;
typedef int32_t System_Reflection_ParameterAttributes;
typedef int32_t System_Reflection_AssemblyNameFlags;
typedef int32_t System_Reflection_CorElementType;
typedef int32_t System_Reflection_InvocationFlags;
typedef int32_t System_Configuration_Assemblies_AssemblyHashAlgorithm;
typedef int32_t System_DelegateBindingFlags;
typedef int32_t System_Threading_ThreadPriority;
typedef int32_t System_Runtime_InteropServices_GCHandleType;
typedef int32_t System_TypeNameFormatFlags;
typedef int32_t System_TypeNameKind;
typedef int32_t System_Exception_ExceptionMessageKind;
typedef int32_t System_RuntimeType_CheckValueStatus;
typedef int32_t Interop_BOOL;

// Handle structs (wrapping a pointer)
struct System_RuntimeTypeHandle { void* f_m_type; };
struct System_RuntimeMethodHandleInternal { void* f_value; };
struct System_RuntimeMethodHandle { void* f_m_value; };
struct System_RuntimeFieldHandleInternal { void* f_value; };
struct System_RuntimeFieldHandle { System_RuntimeFieldHandleInternal f_m_fieldHandle; };
struct System_Reflection_MetadataToken { int32_t f_Value; };
struct System_Reflection_MetadataImport { intptr_t f_m_metadataImport2; };
struct System_Threading_ThreadHandle { void* f_m_ptr; };
struct System_Runtime_CompilerServices_TypeHandle { void* f_m_asTAddr; };
struct System_RuntimeTypeHandle_IntroducedMethodEnumerator { void* f__handle; };

// QCall handle types (CLR-internal, each wraps a pointer)
struct System_Runtime_CompilerServices_QCallTypeHandle { void* f__ptr; };
struct System_Runtime_CompilerServices_QCallAssembly { void* f__ptr; };
struct System_Runtime_CompilerServices_ObjectHandleOnStack { void* f__ptr; };
struct System_Runtime_CompilerServices_StringHandleOnStack { void* f__ptr; };
struct System_Runtime_CompilerServices_StackCrawlMarkHandle { void* f__ptr; };

// Span types (pointer + length)
struct System_ReadOnlySpan_1_System_Int32 { void* f__reference; int32_t f__length; };
struct System_ReadOnlySpan_1_System_String { void* f__reference; int32_t f__length; };
struct System_ReadOnlySpan_1_System_IntPtr { void* f__reference; int32_t f__length; };
struct System_Span_1_System_IntPtr { void* f__reference; int32_t f__length; };

// Exception dispatch state (3 pointers)
struct System_Exception_DispatchState { void* f_StackTrace; void* f_RemoteStackTrace; void* f_Source; };

// Guid (128 bits = 4+2+2+8 bytes)
struct System_Guid { int32_t f__a; int16_t f__b; int16_t f__c; uint8_t f__d; uint8_t f__e; uint8_t f__f; uint8_t f__g; uint8_t f__h; uint8_t f__i; uint8_t f__j; uint8_t f__k; };

// MdUtf8String (pointer)
struct System_MdUtf8String { void* f_m_pStringHeap; int32_t f_m_StringHeapByteLength; };

// InterfaceMapping (2 arrays)
struct System_Reflection_InterfaceMapping { void* f_TargetType; void* f_InterfaceType; void* f_InterfaceMethods; void* f_TargetMethods; };

// RuntimeType.ListBuilder<T> (array + count)
struct System_RuntimeType_ListBuilder_1_System_Reflection_MethodInfo { void* f__items; void* f__item; int32_t f__count; int32_t f__capacity; };
typedef System_RuntimeType_ListBuilder_1_System_Reflection_MethodInfo System_RuntimeType_ListBuilder_1_System_Reflection_ConstructorInfo;
typedef System_RuntimeType_ListBuilder_1_System_Reflection_MethodInfo System_RuntimeType_ListBuilder_1_System_Reflection_PropertyInfo;
typedef System_RuntimeType_ListBuilder_1_System_Reflection_MethodInfo System_RuntimeType_ListBuilder_1_System_Reflection_EventInfo;
typedef System_RuntimeType_ListBuilder_1_System_Reflection_MethodInfo System_RuntimeType_ListBuilder_1_System_Reflection_FieldInfo;
typedef System_RuntimeType_ListBuilder_1_System_Reflection_MethodInfo System_RuntimeType_ListBuilder_1_System_Type;

// ===== System.Array =====
extern "C" void System_Array_CopySlow(void* /*sourceArray*/, int32_t sourceIndex, void* /*destinationArray*/, int32_t destinationIndex, int32_t length) { }
extern "C" int64_t System_Array_get_LongLength(void* /*__this*/) { return {}; }
extern "C" intptr_t System_Array_GetFlattenedIndex__System_Int32(void* /*__this*/, int32_t rawIndex) { return {}; }
extern "C" intptr_t System_Array_GetFlattenedIndex__System_ReadOnlySpan_1_System_Int32_(void* /*__this*/, System_ReadOnlySpan_1_System_Int32 indices) { return {}; }
extern "C" int32_t System_Array_GetLowerBound(void* /*__this*/, int32_t dimension) { return {}; }
extern "C" void System_Array_InternalSetValue(void* /*__this*/, void* /*value*/, intptr_t flattenedIndex) { }
extern "C" bool System_Array_IsSimpleCopy(void* /*sourceArray*/, void* /*destinationArray*/) { return false; }
extern "C" void System_Array_SetValue__System_Object_System_Int32(void* /*__this*/, void* /*value*/, int32_t index) { }
extern "C" void System_Array_SetValue__System_Object_System_Int32__(void* /*__this*/, void* /*value*/, void* /*indices*/) { }

// ===== System.DefaultBinder =====
extern "C" void System_DefaultBinder__ctor(void* /*__this*/) { }
extern "C" void* System_DefaultBinder_BindToField(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*value*/, void* /*cultureInfo*/) { return nullptr; }
extern "C" void* System_DefaultBinder_BindToMethod(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*args*/, void* /*modifiers*/, void* /*cultureInfo*/, void* /*names*/, void* /*state*/) { return nullptr; }
extern "C" void* System_DefaultBinder_ChangeType(void* /*__this*/, void* /*value*/, void* /*type*/, void* /*cultureInfo*/) { return nullptr; }
extern "C" void System_DefaultBinder_ReorderArgumentArray(void* /*__this*/, void* /*args*/, void* /*state*/) { }
extern "C" void* System_DefaultBinder_SelectMethod(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_DefaultBinder_SelectProperty(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*returnType*/, void* /*indexes*/, void* /*modifiers*/) { return nullptr; }

// ===== System.Delegate =====
extern "C" void* System_Delegate_FindMethodHandle(void* /*__this*/) { return nullptr; }
extern "C" void* System_Delegate_get_Target(void* /*__this*/) { return nullptr; }
extern "C" bool System_Delegate_InternalEqualMethodHandles(void* /*left*/, void* /*right*/) { return false; }

// ===== System.Enum =====
extern "C" void System_Enum_GetEnumValuesAndNames(System_Runtime_CompilerServices_QCallTypeHandle enumType, System_Runtime_CompilerServices_ObjectHandleOnStack values, System_Runtime_CompilerServices_ObjectHandleOnStack names, Interop_BOOL getNames) { }
extern "C" void* System_Enum_GetValue(void* /*__this*/) { return nullptr; }
extern "C" System_Reflection_CorElementType System_Enum_InternalGetCorElementType(void* /*__this*/) { return {}; }
extern "C" void* System_Enum_ToString__System_String(void* /*__this*/, void* /*format*/) { return nullptr; }

// ===== System.Exception =====
extern "C" System_Exception_DispatchState System_Exception_CaptureDispatchState(void* /*__this*/) { return {}; }
extern "C" void* System_Exception_GetClassName(void* /*__this*/) { return nullptr; }
extern "C" void System_Exception_GetMessageFromNativeResources__System_Exception_ExceptionMessageKind_System_Runtime_CompilerServices_StringHandleOnStack(System_Exception_ExceptionMessageKind kind, System_Runtime_CompilerServices_StringHandleOnStack retMesg) { }
extern "C" void System_Exception_GetStackTracesDeepCopy(void* /*exception*/, void* /*currentStackTrace*/, void* /*dynamicMethodArray*/) { }
extern "C" bool System_Exception_IsImmutableAgileException(void* /*e*/) { return false; }
extern "C" void System_Exception_PrepareForForeignExceptionRaise() { }
extern "C" void System_Exception_RestoreDispatchState(void* /*__this*/, void* /*dispatchState*/) { }
extern "C" void System_Exception_SaveStackTracesFromDeepCopy(void* /*exception*/, void* /*currentStackTrace*/, void* /*dynamicMethodArray*/) { }
extern "C" void System_Exception_SetCurrentStackTrace(void* /*__this*/) { }
extern "C" void* System_Exception_SetRemoteStackTrace(void* __this, void* stackTrace) {
    if (__this) static_cast<cil2cpp::Exception*>(__this)->f_remoteStackTraceString = static_cast<cil2cpp::String*>(stackTrace);
    return __this;
}
extern "C" void* System_Exception_ToString(void* /*__this*/) { return nullptr; }

// ===== System.Object =====
extern "C" void System_Object__ctor(void* /*__this*/) { }
extern "C" void System_Object_Finalize(void* /*__this*/) { }

// ===== System.Reflection.Assembly =====
extern "C" void* System_Reflection_Assembly_get_FullName(void* /*__this*/) { return nullptr; }
extern "C" uint32_t System_Reflection_Assembly_GetAssemblyCount() { return {}; }
extern "C" void* System_Reflection_Assembly_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* /*attributeType*/, bool inherit) { return nullptr; }
extern "C" void System_Reflection_Assembly_GetEntryAssemblyNative(System_Runtime_CompilerServices_ObjectHandleOnStack retAssembly) { }
extern "C" void System_Reflection_Assembly_GetExecutingAssemblyNative(System_Runtime_CompilerServices_StackCrawlMarkHandle stackMark, System_Runtime_CompilerServices_ObjectHandleOnStack retAssembly) { }
extern "C" void* System_Reflection_Assembly_GetManifestResourceNames(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_Assembly_GetManifestResourceStream__System_String(void* /*__this*/, void* /*name*/) { return nullptr; }
extern "C" void* System_Reflection_Assembly_GetName(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_Assembly_GetName__System_Boolean(void* /*__this*/, bool copiedName) { return nullptr; }
extern "C" void* System_Reflection_Assembly_GetType__System_String_System_Boolean_System_Boolean(void* /*__this*/, void* /*name*/, bool throwOnError, bool ignoreCase) { return nullptr; }

// ===== System.Reflection.FieldInfo =====
extern "C" void System_Reflection_FieldInfo__ctor(void* /*__this*/) { }
extern "C" System_Reflection_FieldAttributes System_Reflection_FieldInfo_get_Attributes(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (fi && fi->native_info) return static_cast<System_Reflection_FieldAttributes>(fi->native_info->flags);
    return {};
}
extern "C" void* System_Reflection_FieldInfo_get_FieldType(void* __this) {
    return cil2cpp::fieldinfo_get_field_type(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this));
}
extern "C" void* System_Reflection_FieldInfo_GetRawConstantValue(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_FieldInfo_GetValue(void* __this, void* obj) {
    return cil2cpp::fieldinfo_get_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj));
}
extern "C" void System_Reflection_FieldInfo_SetValue__System_Object_System_Object_System_Reflection_BindingFlags_System_Reflection_Binder_System_Globalization_CultureInfo(void* __this, void* obj, void* value, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*culture*/) {
    cil2cpp::fieldinfo_set_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Object*>(value));
}
extern "C" System_RuntimeFieldHandle System_Reflection_FieldInfo_get_FieldHandle(void* /*__this*/) { return {}; }
extern "C" bool System_Reflection_FieldInfo_get_IsStatic(void* __this) {
    return cil2cpp::fieldinfo_get_is_static(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this));
}

// ===== System.Reflection.MemberInfo =====
extern "C" void System_Reflection_MemberInfo__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_MemberInfo_CacheEquals(void* /*__this*/, void* /*o*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_Equals(void* /*__this*/, void* /*obj*/) { return false; }
extern "C" void* System_Reflection_MemberInfo_get_DeclaringType(void* __this) {
    return cil2cpp::memberinfo_get_declaring_type(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" bool System_Reflection_MemberInfo_get_IsCollectible(void* /*__this*/) { return false; }
extern "C" System_Reflection_MemberTypes System_Reflection_MemberInfo_get_MemberType(void* /*__this*/) { return {}; }
extern "C" int32_t System_Reflection_MemberInfo_get_MetadataToken(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_MemberInfo_get_Module(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_MemberInfo_get_Name(void* __this) {
    return cil2cpp::memberinfo_get_name(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" void* System_Reflection_MemberInfo_get_ReflectedType(void* __this) {
    // ReflectedType == DeclaringType for our purposes
    return cil2cpp::memberinfo_get_declaring_type(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Boolean(void* /*__this*/, bool inherit) { return nullptr; }
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* /*attributeType*/, bool inherit) { return nullptr; }
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributesData(void* /*__this*/) { return nullptr; }
extern "C" int32_t System_Reflection_MemberInfo_GetHashCode(void* /*__this*/) { return {}; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAs(void* /*__this*/, void* /*other*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeConstructorInfo(void* /*__this*/, void* /*other*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeEventInfo(void* /*__this*/, void* /*other*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeFieldInfo(void* /*__this*/, void* /*other*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeMethodInfo(void* /*__this*/, void* /*other*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimePropertyInfo(void* /*__this*/, void* /*other*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_RuntimeType(void* /*__this*/, void* /*other*/) { return false; }
extern "C" bool System_Reflection_MemberInfo_IsDefined(void* /*__this*/, void* /*attributeType*/, bool inherit) { return false; }

// ===== System.Reflection.MethodBase =====
// Helper to extract native MethodInfo* from a ManagedMethodInfo receiver
static cil2cpp::MethodInfo* _get_native_mi(void* __this) {
    if (!__this) return nullptr;
    return reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this)->native_info;
}
extern "C" void System_Reflection_MethodBase__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_MethodBase_Equals(void* /*__this*/, void* /*obj*/) { return false; }
extern "C" System_Reflection_MethodAttributes System_Reflection_MethodBase_get_Attributes(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni ? static_cast<System_Reflection_MethodAttributes>(ni->flags) : System_Reflection_MethodAttributes{};
}
extern "C" System_Reflection_CallingConventions System_Reflection_MethodBase_get_CallingConvention(void* /*__this*/) { return {}; }
extern "C" bool System_Reflection_MethodBase_get_ContainsGenericParameters(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_MethodBase_get_IsGenericMethod(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_MethodBase_get_IsGenericMethodDefinition(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_MethodBase_get_IsPublic(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && (ni->flags & 0x0007) == 0x0006;
}
extern "C" bool System_Reflection_MethodBase_get_IsStatic(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && (ni->flags & 0x0010) != 0;
}
extern "C" System_RuntimeMethodHandle System_Reflection_MethodBase_get_MethodHandle(void* /*__this*/) { return {}; }
extern "C" System_Reflection_MethodImplAttributes System_Reflection_MethodBase_get_MethodImplementationFlags(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_MethodBase_GetGenericArguments(void* /*__this*/) { return nullptr; }
extern "C" int32_t System_Reflection_MethodBase_GetHashCode(void* /*__this*/) { return {}; }
extern "C" System_Reflection_MethodImplAttributes System_Reflection_MethodBase_GetMethodImplementationFlags(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_MethodBase_GetParameters(void* __this) {
    return cil2cpp::methodinfo_get_parameters(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodBase_GetParametersNoCopy(void* __this) {
    return cil2cpp::methodinfo_get_parameters(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodBase_GetParameterTypes(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_MethodBase_Invoke__System_Object_System_Object__(void* __this, void* obj, void* parameters) {
    return cil2cpp::methodinfo_invoke(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this),
        reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Array*>(parameters));
}
extern "C" void* System_Reflection_MethodBase_Invoke__System_Object_System_Reflection_BindingFlags_System_Reflection_Binder_System_Object___System_Globalization_CultureInfo(void* __this, void* obj, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* parameters, void* /*culture*/) {
    return cil2cpp::methodinfo_invoke(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this),
        reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Array*>(parameters));
}

// ===== System.Reflection.MethodInfo =====
extern "C" void* System_Reflection_MethodInfo_CreateDelegate__System_Type_System_Object(void* /*__this*/, void* /*delegateType*/, void* /*target*/) { return nullptr; }
extern "C" int32_t System_Reflection_MethodInfo_get_GenericParameterCount(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_MethodInfo_get_ReturnType(void* __this) {
    return cil2cpp::methodinfo_get_return_type(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodInfo_GetGenericMethodDefinition(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_MethodInfo_MakeGenericMethod(void* /*__this*/, void* /*typeArguments*/) { return nullptr; }
extern "C" void* System_Reflection_MethodInfo_get_ReturnTypeCustomAttributes(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_MethodInfo_GetBaseDefinition(void* __this) { return __this; }

// ===== System.Reflection.ParameterInfo =====
extern "C" void System_Reflection_ParameterInfo__ctor(void* /*__this*/) { }
extern "C" System_Reflection_ParameterAttributes System_Reflection_ParameterInfo_get_Attributes(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_ParameterInfo_get_DefaultValue(void* /*__this*/) { return nullptr; }
extern "C" bool System_Reflection_ParameterInfo_get_HasDefaultValue(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsIn(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsOptional(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsOut(void* /*__this*/) { return false; }
extern "C" void* System_Reflection_ParameterInfo_get_Member(void* /*__this*/) { return nullptr; }
extern "C" int32_t System_Reflection_ParameterInfo_get_MetadataToken(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_ParameterInfo_get_Name(void* __this) {
    return cil2cpp::parameterinfo_get_name(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" void* System_Reflection_ParameterInfo_get_ParameterType(void* __this) {
    return cil2cpp::parameterinfo_get_parameter_type(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" int32_t System_Reflection_ParameterInfo_get_Position(void* __this) {
    return cil2cpp::parameterinfo_get_position(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" void* System_Reflection_ParameterInfo_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* /*attributeType*/, bool inherit) { return nullptr; }
extern "C" void* System_Reflection_ParameterInfo_GetCustomAttributesData(void* /*__this*/) { return nullptr; }
extern "C" bool System_Reflection_ParameterInfo_IsDefined(void* /*__this*/, void* /*attributeType*/, bool inherit) { return false; }

// ===== System.Reflection.RuntimeAssembly. =====
extern "C" int32_t System_Reflection_RuntimeAssembly__GetCodeBase_g____PInvoke_14_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, System_Runtime_CompilerServices_StringHandleOnStack __retString_native) { return {}; }
extern "C" int32_t System_Reflection_RuntimeAssembly__GetManifestResourceInfo_g____PInvoke_60_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__resourceName_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack __assemblyRef_native, System_Runtime_CompilerServices_StringHandleOnStack __retFileName_native) { return {}; }
extern "C" void System_Reflection_RuntimeAssembly__GetModule_g____PInvoke_52_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__name_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack __retModule_native) { }
extern "C" void System_Reflection_RuntimeAssembly__GetModules_g____PInvoke_90_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, int32_t __loadIfNotFound_native, int32_t __getResourceModules_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retModuleHandles_native) { }
extern "C" void* System_Reflection_RuntimeAssembly__GetResource_g____PInvoke_37_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__resourceName_native*/, void* /*__length_native*/) { return nullptr; }
extern "C" void System_Reflection_RuntimeAssembly__GetTypeCore_g____PInvoke_26_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__typeName_native*/, void* /*__nestedTypeNames_native*/, int32_t __nestedTypeNamesLength_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retType_native) { }
extern "C" void System_Reflection_RuntimeAssembly__GetTypeCoreIgnoreCase_g____PInvoke_27_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__typeName_native*/, void* /*__nestedTypeNames_native*/, int32_t __nestedTypeNamesLength_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retType_native) { }
extern "C" void System_Reflection_RuntimeAssembly__GetVersion_g____PInvoke_72_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__majVer_native*/, void* /*__minVer_native*/, void* /*__buildNum_native*/, void* /*__revNum_native*/) { }
extern "C" void System_Reflection_RuntimeAssembly__InternalLoad_g____PInvoke_49_0(void* /*__pAssemblyNameParts_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack __requestingAssembly_native, System_Runtime_CompilerServices_StackCrawlMarkHandle __stackMark_native, int32_t __throwOnFileNotFound_native, System_Runtime_CompilerServices_ObjectHandleOnStack __assemblyLoadContext_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retAssembly_native) { }

// ===== System.Reflection.RuntimeAssembly.GetEntryPoint =====
extern "C" void System_Reflection_RuntimeAssembly_GetEntryPoint(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_ObjectHandleOnStack retMethod) { }

// ===== System.Reflection.RuntimeAssembly.GetExportedTypes =====
extern "C" void System_Reflection_RuntimeAssembly_GetExportedTypes__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_ObjectHandleOnStack retTypes) { }

// ===== System.Reflection.RuntimeAssembly.GetFlags =====
extern "C" System_Reflection_AssemblyNameFlags System_Reflection_RuntimeAssembly_GetFlags(void* /*__this*/) { return {}; }
extern "C" System_Reflection_AssemblyNameFlags System_Reflection_RuntimeAssembly_GetFlags__System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallAssembly assembly) { return {}; }

// ===== System.Reflection.RuntimeAssembly.GetForwardedType =====
extern "C" void System_Reflection_RuntimeAssembly_GetForwardedType(System_Runtime_CompilerServices_QCallAssembly assembly, System_Reflection_MetadataToken mdtExternalType, System_Runtime_CompilerServices_ObjectHandleOnStack type) { }

// ===== System.Reflection.RuntimeAssembly.GetFullName =====
extern "C" void System_Reflection_RuntimeAssembly_GetFullName(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { }

// ===== System.Reflection.RuntimeAssembly.GetHashAlgorithm =====
extern "C" System_Configuration_Assemblies_AssemblyHashAlgorithm System_Reflection_RuntimeAssembly_GetHashAlgorithm__System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallAssembly assembly) { return {}; }

// ===== System.Reflection.RuntimeAssembly.GetImageRuntimeVersion =====
extern "C" void System_Reflection_RuntimeAssembly_GetImageRuntimeVersion(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { }

// ===== System.Reflection.RuntimeAssembly.GetIsCollectible =====
extern "C" Interop_BOOL System_Reflection_RuntimeAssembly_GetIsCollectible(System_Runtime_CompilerServices_QCallAssembly assembly) { return {}; }

// ===== System.Reflection.RuntimeAssembly.GetLocale =====
extern "C" void System_Reflection_RuntimeAssembly_GetLocale__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { }

// ===== System.Reflection.RuntimeAssembly.GetLocation =====
extern "C" void System_Reflection_RuntimeAssembly_GetLocation(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { }

// ===== System.Reflection.RuntimeAssembly.GetPublicKey =====
extern "C" void* System_Reflection_RuntimeAssembly_GetPublicKey(void* /*__this*/) { return nullptr; }
extern "C" void System_Reflection_RuntimeAssembly_GetPublicKey__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_ObjectHandleOnStack retPublicKey) { }

// ===== System.Reflection.RuntimeAssembly.GetSimpleName =====
extern "C" void* System_Reflection_RuntimeAssembly_GetSimpleName(void* /*__this*/) { return nullptr; }
extern "C" void System_Reflection_RuntimeAssembly_GetSimpleName__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retSimpleName) { }

// ===== System.Reflection.RuntimeAssembly.GetTypeCore =====
extern "C" void* System_Reflection_RuntimeAssembly_GetTypeCore__System_String_System_ReadOnlySpan_1_System_String__System_Boolean_System_Boolean(void* /*__this*/, void* /*typeName*/, System_ReadOnlySpan_1_System_String nestedTypeNames, bool throwOnError, bool ignoreCase) { return nullptr; }

// ===== System.Reflection.RuntimeAssembly.GetVersion =====
extern "C" void* System_Reflection_RuntimeAssembly_GetVersion(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeAssembly.InternalGetSatelliteAssembly =====
extern "C" void* System_Reflection_RuntimeAssembly_InternalGetSatelliteAssembly(void* /*__this*/, void* /*culture*/, void* /*version*/, bool throwOnFileNotFound) { return nullptr; }

// ===== System.Reflection.RuntimeConstructorInfo. =====
extern "C" void* System_Reflection_RuntimeConstructorInfo__get_Signature_g__LazyCreateSignature_21_0(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeConstructorInfo.ComputeAndUpdateInvocationFlags =====
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeConstructorInfo_ComputeAndUpdateInvocationFlags(void* /*__this*/) { return {}; }

// ===== System.Reflection.RuntimeConstructorInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeConstructorInfo_GetRuntimeModule(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeConstructorInfo.InvokeClassConstructor =====
extern "C" void System_Reflection_RuntimeConstructorInfo_InvokeClassConstructor(void* /*__this*/) { }

// ===== System.Reflection.RuntimeConstructorInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeConstructorInfo_ThrowNoInvokeException(void* /*__this*/) { }

// ===== System.Reflection.RuntimeConstructorInfo.get =====
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ArgumentTypes(void* /*__this*/) { return nullptr; }
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeConstructorInfo_get_InvocationFlags(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Invoker(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ReflectedTypeInternal(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Signature(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeFieldInfo =====
extern "C" void System_Reflection_RuntimeFieldInfo__ctor(void* /*__this*/, void* /*reflectedTypeCache*/, void* /*declaringType*/, System_Reflection_BindingFlags bindingFlags) { }

// ===== System.Reflection.RuntimeFieldInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeFieldInfo_GetRuntimeModule(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeFieldInfo.get =====
extern "C" void* System_Reflection_RuntimeFieldInfo_get_ReflectedTypeInternal(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo. =====
extern "C" void* System_Reflection_RuntimeMethodInfo__get_Signature_g__LazyCreateSignature_25_0(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.ComputeAndUpdateInvocationFlags =====
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeMethodInfo_ComputeAndUpdateInvocationFlags(void* /*__this*/) { return {}; }

// ===== System.Reflection.RuntimeMethodInfo.CreateDelegateInternal =====
extern "C" void* System_Reflection_RuntimeMethodInfo_CreateDelegateInternal(void* /*__this*/, void* /*delegateType*/, void* /*firstArgument*/, System_DelegateBindingFlags bindingFlags) { return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.FetchNonReturnParameters =====
extern "C" void* System_Reflection_RuntimeMethodInfo_FetchNonReturnParameters(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.GetParentDefinition =====
extern "C" void* System_Reflection_RuntimeMethodInfo_GetParentDefinition(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeMethodInfo_GetRuntimeModule(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.InvokePropertySetter =====
extern "C" void System_Reflection_RuntimeMethodInfo_InvokePropertySetter(void* /*__this*/, void* /*obj*/, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*parameter*/, void* /*culture*/) { }

// ===== System.Reflection.RuntimeMethodInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeMethodInfo_ThrowNoInvokeException(void* /*__this*/) { }

// ===== System.Reflection.RuntimeMethodInfo.get =====
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ArgumentTypes(void* /*__this*/) { return nullptr; }
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeMethodInfo_get_InvocationFlags(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Invoker(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ReflectedTypeInternal(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Signature(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetGetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetGetMethod(void* /*__this*/, bool nonPublic) { return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetIndexParametersNoCopy =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetIndexParametersNoCopy(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetRuntimeModule(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetSetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetSetMethod(void* /*__this*/, bool nonPublic) { return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.get =====
extern "C" System_Reflection_BindingFlags System_Reflection_RuntimePropertyInfo_get_BindingFlags(void* /*__this*/) { return {}; }
extern "C" void* System_Reflection_RuntimePropertyInfo_get_ReflectedTypeInternal(void* /*__this*/) { return nullptr; }
extern "C" void* System_Reflection_RuntimePropertyInfo_get_Signature(void* /*__this*/) { return nullptr; }

// ===== System.Reflection.TypeInfo =====
extern "C" void System_Reflection_TypeInfo__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_TypeInfo_IsAssignableFrom(void* /*__this*/, void* /*typeInfo*/) { return false; }

// ===== System.RuntimeType =====
extern "C" void* System_RuntimeType__CreateInstanceImpl_g__CreateInstanceLocal_145_0(void* /*__this*/, bool wrapExceptions) { return nullptr; }
extern "C" void* System_RuntimeType_AllocateValueType(void* /*type*/, void* /*value*/) { return nullptr; }
extern "C" bool System_RuntimeType_CanValueSpecialCast(void* /*valueType*/, void* /*targetType*/) { return false; }
extern "C" bool System_RuntimeType_CheckValue__System_ObjectRef_System_Reflection_Binder_System_Globalization_CultureInfo_System_Reflection_BindingFlags(void* /*__this*/, void* /*value*/, void* /*binder*/, void* /*culture*/, System_Reflection_BindingFlags invokeAttr) { return false; }
extern "C" void System_RuntimeType_CreateInstanceCheckThis(void* /*__this*/) { }
extern "C" void* System_RuntimeType_CreateInstanceDefaultCtor(void* /*__this*/, bool publicOnly, bool wrapExceptions) { return nullptr; }
extern "C" void* System_RuntimeType_CreateInstanceImpl(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, void* /*args*/, void* /*culture*/) { return nullptr; }
extern "C" void* System_RuntimeType_get_Cache(void* /*__this*/) { return nullptr; }
extern "C" void* System_RuntimeType_get_CacheIfExists(void* /*__this*/) { return nullptr; }
extern "C" bool System_RuntimeType_get_DomainInitialized(void* /*__this*/) { return false; }
extern "C" void* System_RuntimeType_get_GenericCache(void* /*__this*/) { return nullptr; }
extern "C" bool System_RuntimeType_get_IsActualEnum(void* /*__this*/) { return false; }
extern "C" bool System_RuntimeType_get_IsNullableOfT(void* /*__this*/) { return false; }
extern "C" void* System_RuntimeType_GetBaseType(void* /*__this*/) { return nullptr; }
extern "C" void* System_RuntimeType_GetCachedName(void* /*__this*/, System_TypeNameKind kind) { return nullptr; }
extern "C" System_RuntimeType_ListBuilder_1_System_Reflection_ConstructorInfo System_RuntimeType_GetConstructorCandidates(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, System_Reflection_CallingConventions callConv, void* /*types*/, bool allowPrefixLookup) { return {}; }
extern "C" void* System_RuntimeType_GetDefaultMemberName(void* /*__this*/) { return nullptr; }
extern "C" void* System_RuntimeType_GetEmptyArray(void* /*__this*/) { return nullptr; }
extern "C" System_RuntimeType_ListBuilder_1_System_Reflection_EventInfo System_RuntimeType_GetEventCandidates(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, bool allowPrefixLookup) { return {}; }
extern "C" System_RuntimeType_ListBuilder_1_System_Reflection_FieldInfo System_RuntimeType_GetFieldCandidates(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, bool allowPrefixLookup) { return {}; }
extern "C" void* System_RuntimeType_GetGenericArgumentsInternal(void* /*__this*/) { return nullptr; }
extern "C" void System_RuntimeType_GetGUID(void* /*__this*/, void* /*result*/) { }
extern "C" System_RuntimeType_ListBuilder_1_System_Reflection_MethodInfo System_RuntimeType_GetMethodCandidates(void* /*__this*/, void* /*name*/, int32_t genericParameterCount, System_Reflection_BindingFlags bindingAttr, System_Reflection_CallingConventions callConv, void* /*types*/, bool allowPrefixLookup) { return {}; }
extern "C" void* System_RuntimeType_GetMethodImplCommon(void* /*__this*/, void* /*name*/, int32_t genericParameterCount, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConv, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" System_Runtime_CompilerServices_TypeHandle System_RuntimeType_GetNativeTypeHandle(void* /*__this*/) { return {}; }
extern "C" System_RuntimeType_ListBuilder_1_System_Type System_RuntimeType_GetNestedTypeCandidates(void* /*__this*/, void* /*fullname*/, System_Reflection_BindingFlags bindingAttr, bool allowPrefixLookup) { return {}; }
extern "C" System_RuntimeType_ListBuilder_1_System_Reflection_PropertyInfo System_RuntimeType_GetPropertyCandidates(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*types*/, bool allowPrefixLookup) { return {}; }
extern "C" void* System_RuntimeType_GetRuntimeModule(void* /*__this*/) { return nullptr; }
extern "C" intptr_t System_RuntimeType_GetUnderlyingNativeHandle(void* /*__this*/) { return {}; }
extern "C" void* System_RuntimeType_InitializeCache(void* /*__this*/) { return nullptr; }
extern "C" void* System_RuntimeType_InvokeDispMethod(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags invokeAttr, void* /*target*/, void* /*args*/, void* /*byrefModifiers*/, int32_t culture, void* /*namedParameters*/) { return nullptr; }
extern "C" bool System_RuntimeType_IsDelegate(void* /*__this*/) { return false; }
extern "C" bool System_RuntimeType_IsGenericCOMObjectImpl(void* /*__this*/) { return false; }
extern "C" void System_RuntimeType_set_DomainInitialized(void* /*__this*/, bool value) { }
extern "C" void System_RuntimeType_set_GenericCache(void* /*__this*/, void* /*value*/) { }
extern "C" System_RuntimeType_CheckValueStatus System_RuntimeType_TryChangeType(void* /*__this*/, void* /*value*/, void* /*copyBack*/) { return {}; }
extern "C" System_RuntimeType_CheckValueStatus System_RuntimeType_TryChangeTypeSpecial(void* /*__this*/, void* /*value*/) { return {}; }

// ===== System.RuntimeTypeHandle =====
extern "C" int32_t System_RuntimeTypeHandle___IsVisible_g____PInvoke_67_0(System_Runtime_CompilerServices_QCallTypeHandle __typeHandle_native) { return {}; }
extern "C" void System_RuntimeTypeHandle__ctor(void* /*__this*/, void* /*type*/) { }
extern "C" intptr_t System_RuntimeTypeHandle__GetMetadataImport(void* /*type*/) { return {}; }
extern "C" void* System_RuntimeTypeHandle__GetUtf8Name(void* /*type*/) { return nullptr; }
extern "C" bool System_RuntimeTypeHandle__IsVisible(System_Runtime_CompilerServices_QCallTypeHandle typeHandle) { return false; }
extern "C" bool System_RuntimeTypeHandle_CanCastTo(void* /*type*/, void* /*target*/) { return false; }
extern "C" void System_RuntimeTypeHandle_ConstructName__System_Runtime_CompilerServices_QCallTypeHandle_System_TypeNameFormatFlags_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_TypeNameFormatFlags formatFlags, System_Runtime_CompilerServices_StringHandleOnStack retString) { }
extern "C" void* System_RuntimeTypeHandle_ConstructName__System_TypeNameFormatFlags(void* /*__this*/, System_TypeNameFormatFlags formatFlags) { return nullptr; }
extern "C" bool System_RuntimeTypeHandle_ContainsGenericVariables(void* /*__this*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_ContainsGenericVariables__System_RuntimeType(void* /*handle*/) { return false; }
extern "C" System_ReadOnlySpan_1_System_IntPtr System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_RuntimeTypeHandle___System_Span_1_System_IntPtr_(void* /*inHandles*/, System_Span_1_System_IntPtr stackScratch) { return {}; }
extern "C" void* System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_Type___System_Int32Ref(void* /*inHandles*/, void* /*length*/) { return nullptr; }
extern "C" void System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle baseType, void* /*pTypeHandles*/, int32_t cTypeHandles, System_Runtime_CompilerServices_ObjectHandleOnStack instantiatedObject) { }
extern "C" void* System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_RuntimeType_System_RuntimeType(void* /*type*/, void* /*genericParameter*/) { return nullptr; }
extern "C" void* System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_RuntimeType_System_RuntimeType_System_RuntimeType(void* /*type*/, void* /*genericParameter1*/, void* /*genericParameter2*/) { return nullptr; }
extern "C" bool System_RuntimeTypeHandle_Equals__System_Object(void* /*__this*/, void* /*obj*/) { return false; }
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_IntPtr(void* /*__this*/, intptr_t objHandle) { return {}; }
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtr(System_Runtime_CompilerServices_QCallTypeHandle typeHandle, intptr_t objHandle) { return {}; }
extern "C" intptr_t System_RuntimeTypeHandle_get_Value(void* /*__this*/) { return {}; }
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_Runtime_CompilerServices_ObjectHandleOnStack_void_ptrPtr_System_VoidPtrPtr_void_ptrPtr_Interop_BOOLPtr(System_Runtime_CompilerServices_ObjectHandleOnStack pRuntimeType, void* /*ppfnAllocator*/, void* /*pvAllocatorFirstArg*/, void* /*ppfnCtor*/, void* /*pfCtorIsPublic*/) { }
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_RuntimeType_void_ptrRef_System_VoidRefPtr_void_ptrRef_System_BooleanRef(void* /*rt*/, void* /*pfnAllocator*/, void* /*vAllocatorFirstArg*/, void* /*pfnCtor*/, void* /*ctorIsPublic*/) { }
extern "C" void* System_RuntimeTypeHandle_GetArgumentTypesFromFunctionPointer(void* /*type*/) { return nullptr; }
extern "C" System_Reflection_TypeAttributes System_RuntimeTypeHandle_GetAttributes(void* /*type*/) { return {}; }
extern "C" void* System_RuntimeTypeHandle_GetBaseType(void* /*type*/) { return nullptr; }
extern "C" void* System_RuntimeTypeHandle_GetConstraints(void* /*__this*/) { return nullptr; }
extern "C" void System_RuntimeTypeHandle_GetConstraints__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack types) { }
extern "C" System_Reflection_CorElementType System_RuntimeTypeHandle_GetCorElementType(void* /*type*/) { return {}; }
extern "C" void* System_RuntimeTypeHandle_GetDeclaringType(void* /*type*/) { return nullptr; }
extern "C" bool System_RuntimeTypeHandle_GetFields(void* /*type*/, void* /*result*/, void* /*count*/) { return false; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetFirstIntroducedMethod(void* /*type*/) { return {}; }
extern "C" intptr_t System_RuntimeTypeHandle_GetGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_InteropServices_GCHandleType(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_InteropServices_GCHandleType type) { return {}; }
extern "C" intptr_t System_RuntimeTypeHandle_GetGCHandle__System_Runtime_InteropServices_GCHandleType(void* /*__this*/, System_Runtime_InteropServices_GCHandleType type) { return {}; }
extern "C" void System_RuntimeTypeHandle_GetGenericTypeDefinition(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack retType) { }
extern "C" int32_t System_RuntimeTypeHandle_GetGenericVariableIndex(void* /*__this*/) { return {}; }
extern "C" int32_t System_RuntimeTypeHandle_GetGenericVariableIndex__System_RuntimeType(void* /*type*/) { return {}; }
extern "C" int32_t System_RuntimeTypeHandle_GetHashCode(void* /*__this*/) { return {}; }
extern "C" void System_RuntimeTypeHandle_GetInstantiation(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack types, Interop_BOOL fAsRuntimeTypeArray) { }
extern "C" void* System_RuntimeTypeHandle_GetInstantiationInternal(void* /*__this*/) { return nullptr; }
extern "C" void* System_RuntimeTypeHandle_GetInstantiationPublic(void* /*__this*/) { return nullptr; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallTypeHandle_System_RuntimeMethodHandleInternal(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_QCallTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) { return {}; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_RuntimeTypeHandle_System_RuntimeMethodHandleInternal(void* /*__this*/, System_RuntimeTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) { return {}; }
extern "C" void* System_RuntimeTypeHandle_GetInterfaces(void* /*type*/) { return nullptr; }
extern "C" System_RuntimeTypeHandle_IntroducedMethodEnumerator System_RuntimeTypeHandle_GetIntroducedMethods(void* /*type*/) { return {}; }
extern "C" System_Reflection_MetadataImport System_RuntimeTypeHandle_GetMetadataImport(void* /*type*/) { return {}; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetMethodAt(void* /*type*/, int32_t slot) { return {}; }
extern "C" void* System_RuntimeTypeHandle_GetModule(void* /*type*/) { return nullptr; }
extern "C" System_RuntimeTypeHandle System_RuntimeTypeHandle_GetNativeHandle(void* /*__this*/) { return {}; }
extern "C" void System_RuntimeTypeHandle_GetNextIntroducedMethod(void* /*method*/) { }
extern "C" int32_t System_RuntimeTypeHandle_GetNumVirtuals(void* /*type*/) { return {}; }
extern "C" void* System_RuntimeTypeHandle_GetRuntimeType(void* /*__this*/) { return nullptr; }
extern "C" void* System_RuntimeTypeHandle_GetTypeChecked(void* /*__this*/) { return nullptr; }
extern "C" System_MdUtf8String System_RuntimeTypeHandle_GetUtf8Name(void* /*type*/) { return {}; }
extern "C" bool System_RuntimeTypeHandle_HasElementType(void* /*type*/) { return false; }
extern "C" void System_RuntimeTypeHandle_Instantiate__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, void* /*pInst*/, int32_t numGenericArgs, System_Runtime_CompilerServices_ObjectHandleOnStack type) { }
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_RuntimeType(void* /*__this*/, void* /*inst*/) { return nullptr; }
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_Type__(void* /*__this*/, void* /*inst*/) { return nullptr; }
extern "C" bool System_RuntimeTypeHandle_IsArray(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsByRef(void* /*type*/) { return false; }
extern "C" Interop_BOOL System_RuntimeTypeHandle_IsCollectible(System_Runtime_CompilerServices_QCallTypeHandle handle) { return {}; }
extern "C" bool System_RuntimeTypeHandle_IsComObject(void* /*type*/, bool isGenericCOM) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsFunctionPointer(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsGenericVariable(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsInterface(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsPointer(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsPrimitive(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsSZArray(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsTypeDefinition(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsUnmanagedFunctionPointer(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsValueType(void* /*type*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_IsVisible(void* /*type*/) { return false; }
extern "C" void* System_RuntimeTypeHandle_MakeArray__System_Int32(void* /*__this*/, int32_t rank) { return nullptr; }
extern "C" void System_RuntimeTypeHandle_MakeArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, int32_t rank, System_Runtime_CompilerServices_ObjectHandleOnStack type) { }
extern "C" void* System_RuntimeTypeHandle_MakeByRef(void* /*__this*/) { return nullptr; }
extern "C" void System_RuntimeTypeHandle_MakeByRef__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { }
extern "C" void* System_RuntimeTypeHandle_MakePointer(void* /*__this*/) { return nullptr; }
extern "C" void System_RuntimeTypeHandle_MakePointer__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { }
extern "C" void* System_RuntimeTypeHandle_MakeSZArray(void* /*__this*/) { return nullptr; }
extern "C" void System_RuntimeTypeHandle_MakeSZArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { }
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_QCallAssembly assembly) { }
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_RuntimeType_System_Reflection_RuntimeAssembly(void* /*type*/, void* /*assembly*/) { }
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_IntPtrPtr_System_Int32_System_IntPtrPtr_System_Int32_System_RuntimeType(void* /*paramType*/, void* /*pTypeContext*/, int32_t typeContextLength, void* /*pMethodContext*/, int32_t methodContextLength, void* /*toType*/) { return false; }
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_RuntimeType___System_RuntimeType___System_RuntimeType(void* /*paramType*/, void* /*typeContext*/, void* /*methodContext*/, void* /*toType*/) { return false; }
extern "C" intptr_t System_RuntimeTypeHandle_ToIntPtr(System_RuntimeTypeHandle value) { return {}; }
extern "C" void System_RuntimeTypeHandle_VerifyInterfaceIsImplemented__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallTypeHandle(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_QCallTypeHandle interfaceHandle) { }
extern "C" void System_RuntimeTypeHandle_VerifyInterfaceIsImplemented__System_RuntimeTypeHandle(void* /*__this*/, System_RuntimeTypeHandle interfaceHandle) { }

// ===== System.Runtime.CompilerServices.MethodTable =====
extern "C" uint32_t System_Runtime_CompilerServices_MethodTable_GetNumInstanceFieldBytes(void* /*__this*/) { return {}; }

// ===== System.Runtime.InteropServices.Marshalling =====
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_FromManaged(void* /*__this*/, void* /*handle*/) { }
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_ToUnmanaged(void* /*__this*/) { return {}; }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_Free(void* /*__this*/) { }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_FromManaged(void* /*__this*/, void* /*handle*/) { }
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_ToUnmanaged(void* /*__this*/) { return {}; }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_Free(void* /*__this*/) { }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_FromManaged(void* /*__this*/, void* /*handle*/) { }
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_ToUnmanaged(void* /*__this*/) { return {}; }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_Free(void* /*__this*/) { }

// ===== System.Threading.CancellationTokenSource =====
extern "C" void System_Threading_CancellationTokenSource__ctor(void* /*__this*/) { }
extern "C" bool System_Threading_CancellationTokenSource_get_IsCancellationCompleted(void* /*__this*/) { return false; }
extern "C" bool System_Threading_CancellationTokenSource_get_IsCancellationRequested(void* /*__this*/) { return false; }
extern "C" void System_Threading_CancellationTokenSource_NotifyCancellation(void* /*__this*/, bool /*throwOnFirstException*/) { }
struct System_Threading_CancellationTokenRegistration { int64_t f_id; void* f_node; };
extern "C" System_Threading_CancellationTokenRegistration System_Threading_CancellationTokenSource_Register(void* /*__this*/, void* /*callback*/, void* /*stateForCallback*/, void* /*syncContext*/, void* /*executionContext*/) { return {}; }

// ===== System.Threading.Thread =====
extern "C" void System_Threading_Thread__ctor__System_Threading_ParameterizedThreadStart(void* /*__this*/, void* /*start*/) { }
extern "C" void System_Threading_Thread__ctor__System_Threading_ThreadStart(void* /*__this*/, void* /*start*/) { }
extern "C" void System_Threading_Thread__InformThreadNameChange_g____PInvoke_26_0(System_Threading_ThreadHandle __t_native, void* /*__name_native*/, int32_t __len_native) { }
extern "C" bool System_Threading_Thread_get_IsBackground(void* /*__this*/) { return false; }
extern "C" bool System_Threading_Thread_get_IsThreadPoolThread(void* /*__this*/) { return false; }
extern "C" System_Threading_ThreadPriority System_Threading_Thread_get_Priority(void* /*__this*/) { return {}; }
extern "C" uint64_t System_Threading_Thread_GetCurrentOSThreadId() { return {}; }
extern "C" System_Threading_ThreadHandle System_Threading_Thread_GetNativeHandle(void* /*__this*/) { return {}; }
extern "C" void System_Threading_Thread_ResetThreadPoolThread(void* /*__this*/) { }
extern "C" void System_Threading_Thread_ResetThreadPoolThreadSlow(void* /*__this*/) { }
extern "C" void System_Threading_Thread_set_IsBackground(void* /*__this*/, bool value) { }
extern "C" void System_Threading_Thread_set_Name(void* /*__this*/, void* /*value*/) { }
extern "C" void System_Threading_Thread_set_IsThreadPoolThread(void* /*__this*/, bool value) { }
extern "C" void System_Threading_Thread_set_Priority(void* /*__this*/, System_Threading_ThreadPriority value) { }
extern "C" void System_Threading_Thread_SetThreadPoolWorkerThreadName(void* /*__this*/) { }
extern "C" void System_Threading_Thread_Start__System_Boolean_System_Boolean(void* /*__this*/, bool captureContext, bool internalThread) { }
extern "C" void System_Threading_Thread_StartCore(void* /*__this*/) { }
extern "C" void System_Threading_Thread_StartInternal(System_Threading_ThreadHandle t, int32_t stackSize, int32_t priority, void* /*pThreadName*/) { }
extern "C" void System_Threading_Thread_ThreadNameChanged(void* /*__this*/, void* /*value*/) { }
extern "C" void System_Threading_Thread_UnsafeStart(void* /*__this*/) { }
extern "C" void System_Threading_Thread_UnsafeStart__System_Object(void* /*__this*/, void* /*parameter*/) { }
extern "C" Interop_BOOL System_Threading_Thread_YieldInternal() { return {}; }

// ===== System.Type =====
extern "C" void System_Type__ctor(void* /*__this*/) { }
extern "C" bool System_Type_Equals__System_Type(void* /*__this*/, void* /*o*/) { return false; }
extern "C" void* System_Type_FormatTypeName(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_get_Assembly(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_get_AssemblyQualifiedName(void* /*__this*/) { return nullptr; }
extern "C" System_Reflection_TypeAttributes System_Type_get_Attributes(void* /*__this*/) { return {}; }
extern "C" void* System_Type_get_BaseType(void* /*__this*/) { return nullptr; }
extern "C" bool System_Type_get_ContainsGenericParameters(void* /*__this*/) { return false; }
extern "C" void* System_Type_get_DeclaringMethod(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_get_FullName(void* /*__this*/) { return nullptr; }
extern "C" int32_t System_Type_get_GenericParameterPosition(void* /*__this*/) { return {}; }
extern "C" void* System_Type_get_GenericTypeArguments(void* /*__this*/) { return nullptr; }
extern "C" System_Guid System_Type_get_GUID(void* /*__this*/) { return {}; }
extern "C" bool System_Type_get_HasElementType(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsArray(void* /*__this*/) { return false; }
extern "C" bool System_Type_IsArrayImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsByRef(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsByRefLike(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsClass(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsCOMObject(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsConstructedGenericType(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsEnum(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsFunctionPointer(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsGenericMethodParameter(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsGenericParameter(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsGenericType(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsGenericTypeDefinition(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsGenericTypeParameter(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsInterface(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsNested(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsPointer(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsPrimitive(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsSealed(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsSignatureType(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsSZArray(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsTypeDefinition(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsUnmanagedFunctionPointer(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsVariableBoundArray(void* /*__this*/) { return false; }
extern "C" bool System_Type_get_IsVisible(void* /*__this*/) { return false; }
extern "C" void* System_Type_get_Module(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_get_Namespace(void* /*__this*/) { return nullptr; }
extern "C" System_RuntimeTypeHandle System_Type_get_TypeHandle(void* /*__this*/) { return {}; }
extern "C" void* System_Type_get_UnderlyingSystemType(void* /*__this*/) { return nullptr; }
extern "C" int32_t System_Type_GetArrayRank(void* /*__this*/) { return {}; }
extern "C" System_Reflection_TypeAttributes System_Type_GetAttributeFlagsImpl(void* /*__this*/) { return {}; }
extern "C" void* System_Type_GetConstructor__System_Reflection_BindingFlags_System_Reflection_Binder_System_Reflection_CallingConventions_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetConstructor__System_Reflection_BindingFlags_System_Reflection_Binder_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetConstructorImpl(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetConstructors__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetElementType(void* /*__this*/) { return nullptr; }
extern "C" void System_Type_GetEnumData(void* /*__this*/, void* /*enumNames*/, void* /*enumValues*/) { }
extern "C" void* System_Type_GetEnumNames(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetEnumRawConstantValues(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetEvent__System_String(void* /*__this*/, void* /*name*/) { return nullptr; }
extern "C" void* System_Type_GetEvent__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetEvents(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetEvents__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetField__System_String(void* /*__this*/, void* /*name*/) { return nullptr; }
extern "C" void* System_Type_GetField__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetFields(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetFields__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetFunctionPointerCallingConventions(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetFunctionPointerParameterTypes(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetFunctionPointerReturnType(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetGenericArguments(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetGenericParameterConstraints(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetGenericTypeDefinition(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetInterface__System_String_System_Boolean(void* /*__this*/, void* /*name*/, bool ignoreCase) { return nullptr; }
extern "C" System_Reflection_InterfaceMapping System_Type_GetInterfaceMap(void* /*__this*/, void* /*interfaceType*/) { return {}; }
extern "C" void* System_Type_GetInterfaces(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetMember__System_String(void* /*__this*/, void* /*name*/) { return nullptr; }
extern "C" void* System_Type_GetMember__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetMember__System_String_System_Reflection_MemberTypes_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_MemberTypes type, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetMembers__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetMemberWithSameMetadataDefinitionAs(void* /*__this*/, void* /*member*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String(void* /*__this*/, void* /*name*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Int32_System_Reflection_BindingFlags_System_Reflection_Binder_System_Reflection_CallingConventions_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, int32_t genericParameterCount, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Int32_System_Reflection_BindingFlags_System_Reflection_Binder_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, int32_t genericParameterCount, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Int32_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, int32_t genericParameterCount, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Reflection_CallingConventions_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Type__(void* /*__this*/, void* /*name*/, void* /*types*/) { return nullptr; }
extern "C" void* System_Type_GetMethod__System_String_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethodImpl__System_String_System_Int32_System_Reflection_BindingFlags_System_Reflection_Binder_System_Reflection_CallingConventions_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, int32_t genericParameterCount, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethodImpl__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Reflection_CallingConventions_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetMethods(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_GetMethods__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetNestedType__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetNestedTypes__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetProperties__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetProperty__System_String(void* /*__this*/, void* /*name*/) { return nullptr; }
extern "C" void* System_Type_GetProperty__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { return nullptr; }
extern "C" void* System_Type_GetProperty__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Type_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, void* /*returnType*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetProperty__System_String_System_Type_System_Type__(void* /*__this*/, void* /*name*/, void* /*returnType*/, void* /*types*/) { return nullptr; }
extern "C" void* System_Type_GetProperty__System_String_System_Type_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, void* /*returnType*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetPropertyImpl(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, void* /*returnType*/, void* /*types*/, void* /*modifiers*/) { return nullptr; }
extern "C" void* System_Type_GetRootElementType(void* /*__this*/) { return nullptr; }
extern "C" bool System_Type_HasElementTypeImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_ImplementInterface(void* /*__this*/, void* /*ifaceType*/) { return false; }
extern "C" void* System_Type_InvokeMember__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Object_System_Object___System_Reflection_ParameterModifier___System_Globalization_CultureInfo_System_String__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*target*/, void* /*args*/, void* /*modifiers*/, void* /*culture*/, void* /*namedParameters*/) { return nullptr; }
extern "C" bool System_Type_IsAssignableFrom(void* /*__this*/, void* /*c*/) { return false; }
extern "C" bool System_Type_IsAssignableTo(void* /*__this*/, void* /*targetType*/) { return false; }
extern "C" bool System_Type_IsByRefImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_IsCOMObjectImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_IsContextfulImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_IsInstanceOfType(void* /*__this*/, void* /*o*/) { return false; }
extern "C" bool System_Type_IsMarshalByRefImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_IsPointerImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_IsPrimitiveImpl(void* /*__this*/) { return false; }
extern "C" bool System_Type_IsSubclassOf(void* /*__this*/, void* /*c*/) { return false; }
extern "C" void* System_Type_MakeArrayType(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_MakeArrayType__System_Int32(void* /*__this*/, int32_t rank) { return nullptr; }
extern "C" void* System_Type_MakeByRefType(void* /*__this*/) { return nullptr; }
extern "C" void* System_Type_MakeGenericType(void* /*__this*/, void* /*typeArguments*/) { return nullptr; }
extern "C" void* System_Type_MakePointerType(void* /*__this*/) { return nullptr; }

// ===== System.ValueType =====
extern "C" bool System_ValueType_CanCompareBits(void* /*obj*/) { return false; }
extern "C" int32_t System_ValueType_GetHashCode(void* /*__this*/) { return {}; }
extern "C" void* System_ValueType_ToString(void* /*__this*/) { return nullptr; }
