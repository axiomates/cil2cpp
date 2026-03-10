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
#include <cstring>
#include <string>
#include <cil2cpp/object.h>
#include <cil2cpp/string.h>
#include <cil2cpp/array.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/memberinfo.h>
#include <cil2cpp/reflection.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/threading.h>

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

// QCall handle types — must match generated struct layout
struct System_Runtime_CompilerServices_QCallTypeHandle { void* f__ptr; intptr_t f__handle; };
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
extern "C" int64_t System_Array_get_LongLength(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" intptr_t System_Array_GetFlattenedIndex__System_Int32(void* /*__this*/, int32_t rawIndex) { cil2cpp::stub_called(__func__); return {}; }
extern "C" intptr_t System_Array_GetFlattenedIndex__System_ReadOnlySpan_1_System_Int32_(void* /*__this*/, System_ReadOnlySpan_1_System_Int32 indices) { cil2cpp::stub_called(__func__); return {}; }
extern "C" int32_t System_Array_GetLowerBound(void* /*__this*/, int32_t dimension) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_Array_InternalSetValue(void* /*__this*/, void* /*value*/, intptr_t flattenedIndex) { cil2cpp::stub_called(__func__); }
extern "C" bool System_Array_IsSimpleCopy(void* /*sourceArray*/, void* /*destinationArray*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void System_Array_SetValue__System_Object_System_Int32(void* /*__this*/, void* /*value*/, int32_t index) { cil2cpp::stub_called(__func__); }
extern "C" void System_Array_SetValue__System_Object_System_Int32__(void* /*__this*/, void* /*value*/, void* /*indices*/) { cil2cpp::stub_called(__func__); }

// ===== System.DefaultBinder =====
extern "C" void System_DefaultBinder__ctor(void* /*__this*/) { }
extern "C" void* System_DefaultBinder_BindToField(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*value*/, void* /*cultureInfo*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_DefaultBinder_BindToMethod(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*args*/, void* /*modifiers*/, void* /*cultureInfo*/, void* /*names*/, void* /*state*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_DefaultBinder_ChangeType(void* /*__this*/, void* /*value*/, void* /*type*/, void* /*cultureInfo*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_DefaultBinder_ReorderArgumentArray(void* /*__this*/, void* /*args*/, void* /*state*/) { cil2cpp::stub_called(__func__); }
extern "C" void* System_DefaultBinder_SelectMethod(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*types*/, void* /*modifiers*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_DefaultBinder_SelectProperty(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*returnType*/, void* /*indexes*/, void* /*modifiers*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Delegate =====
extern "C" void* System_Delegate_FindMethodHandle(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Delegate_get_Target(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_Delegate_InternalEqualMethodHandles(void* /*left*/, void* /*right*/) { cil2cpp::stub_called(__func__); return false; }

// ===== System.Enum =====
extern "C" void System_Enum_GetEnumValuesAndNames(
    System_Runtime_CompilerServices_QCallTypeHandle enumType,
    System_Runtime_CompilerServices_ObjectHandleOnStack values,
    System_Runtime_CompilerServices_ObjectHandleOnStack names,
    Interop_BOOL getNames)
{
    // QCallTypeHandle.f__ptr is RuntimeType** — dereference to get Type*
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(enumType.f__ptr);
    if (!rtPtr || !*rtPtr) return;
    auto* ti = (*rtPtr)->type_info;
    if (!ti || !ti->enum_names) return;

    auto count = static_cast<int32_t>(ti->enum_count);

    // Determine underlying type size from cor_element_type
    int elemSize = 4;  // default Int32
    uint8_t corType = ti->underlying_type ? ti->underlying_type->cor_element_type : 0x08;
    switch (corType) {
        case 0x04: case 0x05: elemSize = 1; break; // I1, U1 (SByte, Byte)
        case 0x06: case 0x07: elemSize = 2; break; // I2, U2 (Int16, UInt16)
        case 0x08: case 0x09: elemSize = 4; break; // I4, U4
        case 0x0A: case 0x0B: elemSize = 8; break; // I8, U8
    }

    // Create values array — element type is the underlying type
    auto* underlyingTi = ti->underlying_type ? ti->underlying_type : ti;
    auto* valArr = cil2cpp::array_create(underlyingTi, count);
    auto* data = static_cast<uint8_t*>(cil2cpp::array_data(valArr));
    for (int32_t i = 0; i < count; i++) {
        int64_t v = ti->enum_values[i];
        switch (elemSize) {
            case 1: reinterpret_cast<uint8_t*>(data)[i] = static_cast<uint8_t>(v); break;
            case 2: reinterpret_cast<uint16_t*>(data)[i] = static_cast<uint16_t>(v); break;
            case 4: reinterpret_cast<uint32_t*>(data)[i] = static_cast<uint32_t>(v); break;
            case 8: reinterpret_cast<uint64_t*>(data)[i] = static_cast<uint64_t>(v); break;
        }
    }
    // ObjectHandleOnStack.f__ptr is Object** — write through it
    *reinterpret_cast<cil2cpp::Object**>(values.f__ptr) = reinterpret_cast<cil2cpp::Object*>(valArr);

    // Create names array if requested
    if (getNames) {
        auto* nameArr = cil2cpp::array_create(&cil2cpp::System_String_TypeInfo, count);
        auto** nameData = static_cast<cil2cpp::String**>(cil2cpp::array_data(nameArr));
        for (int32_t i = 0; i < count; i++) {
            nameData[i] = cil2cpp::string_literal(ti->enum_names[i]);
        }
        *reinterpret_cast<cil2cpp::Object**>(names.f__ptr) = reinterpret_cast<cil2cpp::Object*>(nameArr);
    }
}

// ===== System.Exception =====
extern "C" System_Exception_DispatchState System_Exception_CaptureDispatchState(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Exception_GetClassName(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_Exception_GetMessageFromNativeResources__System_Exception_ExceptionMessageKind_System_Runtime_CompilerServices_StringHandleOnStack(System_Exception_ExceptionMessageKind kind, System_Runtime_CompilerServices_StringHandleOnStack retMesg) { cil2cpp::stub_called(__func__); }
extern "C" void System_Exception_GetStackTracesDeepCopy(void* /*exception*/, void* /*currentStackTrace*/, void* /*dynamicMethodArray*/) { cil2cpp::stub_called(__func__); }
extern "C" bool System_Exception_IsImmutableAgileException(void* /*e*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void System_Exception_PrepareForForeignExceptionRaise() { cil2cpp::stub_called(__func__); }
extern "C" void System_Exception_RestoreDispatchState(void* /*__this*/, void* /*dispatchState*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Exception_SaveStackTracesFromDeepCopy(void* /*exception*/, void* /*currentStackTrace*/, void* /*dynamicMethodArray*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Exception_SetCurrentStackTrace(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void* System_Exception_SetRemoteStackTrace(void* __this, void* stackTrace) {
    if (__this) static_cast<cil2cpp::Exception*>(__this)->f__remoteStackTraceString = static_cast<cil2cpp::String*>(stackTrace);
    return __this;
}
extern "C" void* System_Exception_ToString(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Object =====
extern "C" void System_Object__ctor(void* /*__this*/) { }
extern "C" void System_Object_Finalize(void* /*__this*/) { }

// ===== System.Reflection.Assembly =====
extern "C" void* System_Reflection_Assembly_get_FullName(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" uint32_t System_Reflection_Assembly_GetAssemblyCount() { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_Assembly_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* /*attributeType*/, bool inherit) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_Reflection_Assembly_GetEntryAssemblyNative(System_Runtime_CompilerServices_ObjectHandleOnStack retAssembly) { cil2cpp::stub_called(__func__); }
extern "C" void System_Reflection_Assembly_GetExecutingAssemblyNative(System_Runtime_CompilerServices_StackCrawlMarkHandle stackMark, System_Runtime_CompilerServices_ObjectHandleOnStack retAssembly) { cil2cpp::stub_called(__func__); }
extern "C" void* System_Reflection_Assembly_GetManifestResourceNames(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_Assembly_GetManifestResourceStream__System_String(void* /*__this*/, void* /*name*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_Assembly_get_Location(void* /*__this*/) {
    return reinterpret_cast<void*>(cil2cpp::string_literal(""));
}
extern "C" void* System_Reflection_Assembly_GetName(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_Assembly_GetName__System_Boolean(void* /*__this*/, bool copiedName) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_Assembly_GetType__System_String_System_Boolean_System_Boolean(void* /*__this*/, void* /*name*/, bool throwOnError, bool ignoreCase) { cil2cpp::stub_called(__func__); return nullptr; }

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
extern "C" void* System_Reflection_FieldInfo_GetRawConstantValue(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_FieldInfo_GetValue(void* __this, void* obj) {
    return cil2cpp::fieldinfo_get_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj));
}
extern "C" void System_Reflection_FieldInfo_SetValue__System_Object_System_Object_System_Reflection_BindingFlags_System_Reflection_Binder_System_Globalization_CultureInfo(void* __this, void* obj, void* value, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*culture*/) {
    cil2cpp::fieldinfo_set_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Object*>(value));
}
extern "C" void System_Reflection_FieldInfo_SetValue__System_Object_System_Object(void* __this, void* obj, void* value) {
    cil2cpp::fieldinfo_set_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Object*>(value));
}
extern "C" System_RuntimeFieldHandle System_Reflection_FieldInfo_get_FieldHandle(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" bool System_Reflection_FieldInfo_get_IsStatic(void* __this) {
    return cil2cpp::fieldinfo_get_is_static(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this));
}

// ===== System.Reflection.MemberInfo =====
extern "C" void System_Reflection_MemberInfo__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_MemberInfo_CacheEquals(void* /*__this*/, void* /*o*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_Equals(void* /*__this*/, void* /*obj*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void* System_Reflection_MemberInfo_get_DeclaringType(void* __this) {
    return cil2cpp::memberinfo_get_declaring_type(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" bool System_Reflection_MemberInfo_get_IsCollectible(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" System_Reflection_MemberTypes System_Reflection_MemberInfo_get_MemberType(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" int32_t System_Reflection_MemberInfo_get_MetadataToken(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_MemberInfo_get_Module(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_MemberInfo_get_Name(void* __this) {
    return cil2cpp::memberinfo_get_name(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" void* System_Reflection_MemberInfo_get_ReflectedType(void* __this) {
    // ReflectedType == DeclaringType for our purposes
    return cil2cpp::memberinfo_get_declaring_type(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Boolean(void* /*__this*/, bool inherit) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* /*attributeType*/, bool inherit) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributesData(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" int32_t System_Reflection_MemberInfo_GetHashCode(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAs(void* /*__this*/, void* /*other*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeConstructorInfo(void* /*__this*/, void* /*other*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeEventInfo(void* /*__this*/, void* /*other*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeFieldInfo(void* /*__this*/, void* /*other*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeMethodInfo(void* /*__this*/, void* /*other*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimePropertyInfo(void* /*__this*/, void* /*other*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_RuntimeType(void* /*__this*/, void* /*other*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MemberInfo_IsDefined(void* /*__this*/, void* /*attributeType*/, bool inherit) { cil2cpp::stub_called(__func__); return false; }

// ===== System.Reflection.MethodBase =====
// Helper to extract native MethodInfo* from a ManagedMethodInfo receiver
static cil2cpp::MethodInfo* _get_native_mi(void* __this) {
    if (!__this) return nullptr;
    return reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this)->native_info;
}
extern "C" void System_Reflection_MethodBase__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_MethodBase_Equals(void* /*__this*/, void* /*obj*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" System_Reflection_MethodAttributes System_Reflection_MethodBase_get_Attributes(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni ? static_cast<System_Reflection_MethodAttributes>(ni->flags) : System_Reflection_MethodAttributes{};
}
extern "C" System_Reflection_CallingConventions System_Reflection_MethodBase_get_CallingConvention(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" bool System_Reflection_MethodBase_get_ContainsGenericParameters(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MethodBase_get_IsGenericMethod(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MethodBase_get_IsGenericMethodDefinition(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_MethodBase_get_IsPublic(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && (ni->flags & 0x0007) == 0x0006;
}
extern "C" bool System_Reflection_MethodBase_get_IsStatic(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && (ni->flags & 0x0010) != 0;
}
extern "C" System_RuntimeMethodHandle System_Reflection_MethodBase_get_MethodHandle(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_Reflection_MethodImplAttributes System_Reflection_MethodBase_get_MethodImplementationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_MethodBase_GetGenericArguments(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" int32_t System_Reflection_MethodBase_GetHashCode(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_Reflection_MethodImplAttributes System_Reflection_MethodBase_GetMethodImplementationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_MethodBase_GetParameters(void* __this) {
    return cil2cpp::methodinfo_get_parameters(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodBase_GetParametersNoCopy(void* __this) {
    return cil2cpp::methodinfo_get_parameters(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodBase_GetParameterTypes(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_MethodBase_Invoke__System_Object_System_Object__(void* __this, void* obj, void* parameters) {
    return cil2cpp::methodinfo_invoke(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this),
        reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Array*>(parameters));
}
extern "C" void* System_Reflection_MethodBase_Invoke__System_Object_System_Reflection_BindingFlags_System_Reflection_Binder_System_Object___System_Globalization_CultureInfo(void* __this, void* obj, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* parameters, void* /*culture*/) {
    return cil2cpp::methodinfo_invoke(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this),
        reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Array*>(parameters));
}

// ===== System.Reflection.MethodInfo =====
extern "C" void* System_Reflection_MethodInfo_CreateDelegate__System_Type_System_Object(void* /*__this*/, void* /*delegateType*/, void* /*target*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" int32_t System_Reflection_MethodInfo_get_GenericParameterCount(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_MethodInfo_get_ReturnType(void* __this) {
    return cil2cpp::methodinfo_get_return_type(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodInfo_GetGenericMethodDefinition(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_MethodInfo_MakeGenericMethod(void* /*__this*/, void* /*typeArguments*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_MethodInfo_get_ReturnTypeCustomAttributes(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_MethodInfo_GetBaseDefinition(void* __this) { cil2cpp::stub_called(__func__); return __this; }

// ===== System.Reflection.ParameterInfo =====
extern "C" void System_Reflection_ParameterInfo__ctor(void* /*__this*/) { }
extern "C" System_Reflection_ParameterAttributes System_Reflection_ParameterInfo_get_Attributes(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_ParameterInfo_get_DefaultValue(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_Reflection_ParameterInfo_get_HasDefaultValue(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsIn(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsOptional(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsOut(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void* System_Reflection_ParameterInfo_get_Member(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" int32_t System_Reflection_ParameterInfo_get_MetadataToken(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_ParameterInfo_get_Name(void* __this) {
    return cil2cpp::parameterinfo_get_name(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" void* System_Reflection_ParameterInfo_get_ParameterType(void* __this) {
    return cil2cpp::parameterinfo_get_parameter_type(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" int32_t System_Reflection_ParameterInfo_get_Position(void* __this) {
    return cil2cpp::parameterinfo_get_position(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" void* System_Reflection_ParameterInfo_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* /*attributeType*/, bool inherit) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_ParameterInfo_GetCustomAttributesData(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_Reflection_ParameterInfo_IsDefined(void* /*__this*/, void* /*attributeType*/, bool inherit) { cil2cpp::stub_called(__func__); return false; }

// ===== System.Reflection.RuntimeAssembly. =====
extern "C" int32_t System_Reflection_RuntimeAssembly__GetCodeBase_g____PInvoke_14_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, System_Runtime_CompilerServices_StringHandleOnStack __retString_native) { cil2cpp::stub_called(__func__); return {}; }
extern "C" int32_t System_Reflection_RuntimeAssembly__GetManifestResourceInfo_g____PInvoke_60_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__resourceName_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack __assemblyRef_native, System_Runtime_CompilerServices_StringHandleOnStack __retFileName_native) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_Reflection_RuntimeAssembly__GetModule_g____PInvoke_52_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__name_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack __retModule_native) { cil2cpp::stub_called(__func__); }
extern "C" void System_Reflection_RuntimeAssembly__GetModules_g____PInvoke_90_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, int32_t __loadIfNotFound_native, int32_t __getResourceModules_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retModuleHandles_native) { cil2cpp::stub_called(__func__); }
extern "C" void* System_Reflection_RuntimeAssembly__GetResource_g____PInvoke_37_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__resourceName_native*/, void* /*__length_native*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_Reflection_RuntimeAssembly__GetTypeCore_g____PInvoke_26_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__typeName_native*/, void* /*__nestedTypeNames_native*/, int32_t __nestedTypeNamesLength_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retType_native) { cil2cpp::stub_called(__func__); }
extern "C" void System_Reflection_RuntimeAssembly__GetTypeCoreIgnoreCase_g____PInvoke_27_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__typeName_native*/, void* /*__nestedTypeNames_native*/, int32_t __nestedTypeNamesLength_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retType_native) { cil2cpp::stub_called(__func__); }
extern "C" void System_Reflection_RuntimeAssembly__GetVersion_g____PInvoke_72_0(System_Runtime_CompilerServices_QCallAssembly __assembly_native, void* /*__majVer_native*/, void* /*__minVer_native*/, void* /*__buildNum_native*/, void* /*__revNum_native*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Reflection_RuntimeAssembly__InternalLoad_g____PInvoke_49_0(void* /*__pAssemblyNameParts_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack __requestingAssembly_native, System_Runtime_CompilerServices_StackCrawlMarkHandle __stackMark_native, int32_t __throwOnFileNotFound_native, System_Runtime_CompilerServices_ObjectHandleOnStack __assemblyLoadContext_native, System_Runtime_CompilerServices_ObjectHandleOnStack __retAssembly_native) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetEntryPoint =====
extern "C" void System_Reflection_RuntimeAssembly_GetEntryPoint(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_ObjectHandleOnStack retMethod) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetExportedTypes =====
extern "C" void System_Reflection_RuntimeAssembly_GetExportedTypes__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_ObjectHandleOnStack retTypes) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetFlags =====
extern "C" System_Reflection_AssemblyNameFlags System_Reflection_RuntimeAssembly_GetFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_Reflection_AssemblyNameFlags System_Reflection_RuntimeAssembly_GetFlags__System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallAssembly assembly) { cil2cpp::stub_called(__func__); return {}; }

// ===== System.Reflection.RuntimeAssembly.GetForwardedType =====
extern "C" void System_Reflection_RuntimeAssembly_GetForwardedType(System_Runtime_CompilerServices_QCallAssembly assembly, System_Reflection_MetadataToken mdtExternalType, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetFullName =====
extern "C" void System_Reflection_RuntimeAssembly_GetFullName(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetHashAlgorithm =====
extern "C" System_Configuration_Assemblies_AssemblyHashAlgorithm System_Reflection_RuntimeAssembly_GetHashAlgorithm__System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallAssembly assembly) { cil2cpp::stub_called(__func__); return {}; }

// ===== System.Reflection.RuntimeAssembly.GetImageRuntimeVersion =====
extern "C" void System_Reflection_RuntimeAssembly_GetImageRuntimeVersion(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetIsCollectible =====
extern "C" Interop_BOOL System_Reflection_RuntimeAssembly_GetIsCollectible(System_Runtime_CompilerServices_QCallAssembly assembly) { cil2cpp::stub_called(__func__); return {}; }

// ===== System.Reflection.RuntimeAssembly.GetLocale =====
extern "C" void System_Reflection_RuntimeAssembly_GetLocale__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetLocation =====
extern "C" void System_Reflection_RuntimeAssembly_GetLocation(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retString) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetPublicKey =====
extern "C" void* System_Reflection_RuntimeAssembly_GetPublicKey(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_Reflection_RuntimeAssembly_GetPublicKey__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_ObjectHandleOnStack retPublicKey) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetSimpleName =====
extern "C" void* System_Reflection_RuntimeAssembly_GetSimpleName(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_Reflection_RuntimeAssembly_GetSimpleName__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallAssembly assembly, System_Runtime_CompilerServices_StringHandleOnStack retSimpleName) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeAssembly.GetTypeCore =====
extern "C" void* System_Reflection_RuntimeAssembly_GetTypeCore__System_String_System_ReadOnlySpan_1_System_String__System_Boolean_System_Boolean(void* /*__this*/, void* /*typeName*/, System_ReadOnlySpan_1_System_String nestedTypeNames, bool throwOnError, bool ignoreCase) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeAssembly.GetVersion =====
extern "C" void* System_Reflection_RuntimeAssembly_GetVersion(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeAssembly.InternalGetSatelliteAssembly =====
// (provided by generated code: compiled from IL or linker stub)

// ===== System.Reflection.RuntimeConstructorInfo. =====
extern "C" void* System_Reflection_RuntimeConstructorInfo__get_Signature_g__LazyCreateSignature_21_0(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeConstructorInfo.ComputeAndUpdateInvocationFlags =====
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeConstructorInfo_ComputeAndUpdateInvocationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }

// ===== System.Reflection.RuntimeConstructorInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeConstructorInfo_GetRuntimeModule(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeConstructorInfo.InvokeClassConstructor =====
extern "C" void System_Reflection_RuntimeConstructorInfo_InvokeClassConstructor(void* /*__this*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeConstructorInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeConstructorInfo_ThrowNoInvokeException(void* /*__this*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeConstructorInfo.get =====
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ArgumentTypes(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeConstructorInfo_get_InvocationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Invoker(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ReflectedTypeInternal(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Signature(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeFieldInfo =====
extern "C" void System_Reflection_RuntimeFieldInfo__ctor(void* /*__this*/, void* /*reflectedTypeCache*/, void* /*declaringType*/, System_Reflection_BindingFlags bindingFlags) { }

// ===== System.Reflection.RuntimeFieldInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeFieldInfo_GetRuntimeModule(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeFieldInfo.get =====
extern "C" void* System_Reflection_RuntimeFieldInfo_get_ReflectedTypeInternal(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo. =====
extern "C" void* System_Reflection_RuntimeMethodInfo__get_Signature_g__LazyCreateSignature_25_0(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.ComputeAndUpdateInvocationFlags =====
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeMethodInfo_ComputeAndUpdateInvocationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }

// ===== System.Reflection.RuntimeMethodInfo.CreateDelegateInternal =====
extern "C" void* System_Reflection_RuntimeMethodInfo_CreateDelegateInternal(void* /*__this*/, void* /*delegateType*/, void* /*firstArgument*/, System_DelegateBindingFlags bindingFlags) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.FetchNonReturnParameters =====
extern "C" void* System_Reflection_RuntimeMethodInfo_FetchNonReturnParameters(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.GetParentDefinition =====
extern "C" void* System_Reflection_RuntimeMethodInfo_GetParentDefinition(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeMethodInfo_GetRuntimeModule(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeMethodInfo.InvokePropertySetter =====
extern "C" void System_Reflection_RuntimeMethodInfo_InvokePropertySetter(void* /*__this*/, void* /*obj*/, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*parameter*/, void* /*culture*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeMethodInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeMethodInfo_ThrowNoInvokeException(void* /*__this*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeMethodInfo.get =====
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ArgumentTypes(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeMethodInfo_get_InvocationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Invoker(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ReflectedTypeInternal(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Signature(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetGetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetGetMethod(void* /*__this*/, bool nonPublic) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetIndexParametersNoCopy =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetIndexParametersNoCopy(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetRuntimeModule(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetSetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetSetMethod(void* /*__this*/, bool nonPublic) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.get =====
extern "C" System_Reflection_BindingFlags System_Reflection_RuntimePropertyInfo_get_BindingFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_RuntimePropertyInfo_get_ReflectedTypeInternal(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_RuntimePropertyInfo_get_Signature(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.TypeInfo =====
extern "C" void System_Reflection_TypeInfo__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_TypeInfo_IsAssignableFrom(void* /*__this*/, void* /*typeInfo*/) { cil2cpp::stub_called(__func__); return false; }

// ===== System.RuntimeType =====
// AllocateValueType: compiled from IL (method-level emission gate)
extern "C" bool System_RuntimeType_CanValueSpecialCast(void* /*valueType*/, void* /*targetType*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void System_RuntimeType_GetGUID(void* /*__this*/, void* /*result*/) { cil2cpp::stub_called(__func__); }
extern "C" void* System_RuntimeType_InvokeDispMethod(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags invokeAttr, void* /*target*/, void* /*args*/, void* /*byrefModifiers*/, int32_t culture, void* /*namedParameters*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeType_IsDelegate(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }

// ===== System.RuntimeTypeHandle =====
// Methods removed from SkipAllMethodsTypes were compiled from IL by SocketTest.
// But RuntimeTypeHandle is back in SkipAllMethodsTypes due to pointer-level mismatches.
// Add back stubs that were removed in the batch cleanup.
extern "C" void System_RuntimeTypeHandle__ctor(void* /*__this*/, void* /*type*/) { }
extern "C" void* System_RuntimeTypeHandle_GetRuntimeType(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_IsNullHandle(void* /*__this*/) { return true; }
struct TypeHandle_Stub { void* m_asTAddr; };
extern "C" TypeHandle_Stub System_RuntimeTypeHandle_GetNativeTypeHandle(void* /*__this*/) { return {nullptr}; }
extern "C" bool System_RuntimeTypeHandle_IsByRef(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsPointer(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsFunctionPointer(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
// Helper: extract generic definition name from a closed generic full_name.
// E.g., "System.Collections.Generic.GenericComparer`1<System.Int32>" → "System.Collections.Generic.GenericComparer`1"
static std::string extract_generic_definition(const char* full_name) {
    std::string s(full_name);
    auto pos = s.find('<');
    if (pos != std::string::npos)
        return s.substr(0, pos);
    return s;
}

extern "C" void* System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_RuntimeType_System_RuntimeType(
    void* type, void* genericParameter)
{
    auto* templateType = reinterpret_cast<cil2cpp::Type*>(type);
    auto* paramType = reinterpret_cast<cil2cpp::Type*>(genericParameter);
    if (!templateType || !templateType->type_info || !paramType || !paramType->type_info)
        return nullptr;

    // Extract generic definition from template type
    auto def = extract_generic_definition(templateType->type_info->full_name);
    // Build closed generic name: "GenericDef`1<ParamType>"
    std::string targetName = def + "<" + paramType->type_info->full_name + ">";

    auto* targetInfo = cil2cpp::type_get_by_name(targetName.c_str());
    if (!targetInfo) {
        fprintf(stderr, "[AOT] CreateInstanceForAnotherGenericParameter: type '%s' not found (template='%s', param='%s')\n",
            targetName.c_str(), templateType->type_info->full_name, paramType->type_info->full_name);
        fflush(stderr);
        return nullptr;
    }

    return cil2cpp::object_alloc(targetInfo);
}

extern "C" void* System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_RuntimeType_System_RuntimeType_System_RuntimeType(
    void* type, void* genericParameter1, void* genericParameter2)
{
    auto* templateType = reinterpret_cast<cil2cpp::Type*>(type);
    auto* param1 = reinterpret_cast<cil2cpp::Type*>(genericParameter1);
    auto* param2 = reinterpret_cast<cil2cpp::Type*>(genericParameter2);
    if (!templateType || !templateType->type_info || !param1 || !param1->type_info || !param2 || !param2->type_info)
        return nullptr;

    auto def = extract_generic_definition(templateType->type_info->full_name);
    std::string targetName = def + "<" + param1->type_info->full_name + ", " + param2->type_info->full_name + ">";

    auto* targetInfo = cil2cpp::type_get_by_name(targetName.c_str());
    if (!targetInfo) {
        fprintf(stderr, "[AOT] CreateInstanceForAnotherGenericParameter: type '%s' not found (template='%s')\n",
            targetName.c_str(), templateType->type_info->full_name);
        fflush(stderr);
        return nullptr;
    }

    return cil2cpp::object_alloc(targetInfo);
}
extern "C" void* System_RuntimeTypeHandle_ConstructName__System_TypeNameFormatFlags(void* /*__this*/, System_TypeNameFormatFlags formatFlags) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_ContainsGenericVariables(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" System_ReadOnlySpan_1_System_IntPtr System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_RuntimeTypeHandle___System_Span_1_System_IntPtr_(void* /*inHandles*/, System_Span_1_System_IntPtr stackScratch) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_Type___System_Int32Ref(void* /*inHandles*/, void* /*length*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_Equals__System_Object(void* /*__this*/, void* /*obj*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_Equals__System_RuntimeTypeHandle(System_RuntimeTypeHandle* __this, System_RuntimeTypeHandle other) {
    return __this->f_m_type == other.f_m_type;
}
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_IntPtr(void* /*__this*/, intptr_t objHandle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetConstraints(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" intptr_t System_RuntimeTypeHandle_GetGCHandle__System_Runtime_InteropServices_GCHandleType(void* /*__this*/, System_Runtime_InteropServices_GCHandleType type) { cil2cpp::stub_called(__func__); return {}; }
extern "C" int32_t System_RuntimeTypeHandle_GetGenericVariableIndex(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" int32_t System_RuntimeTypeHandle_GetHashCode(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetInstantiationInternal(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_GetInstantiationPublic(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_RuntimeTypeHandle_IntroducedMethodEnumerator System_RuntimeTypeHandle_GetIntroducedMethods(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_RuntimeTypeHandle System_RuntimeTypeHandle_GetNativeHandle(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetTypeChecked(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_MdUtf8String System_RuntimeTypeHandle_GetUtf8Name(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" bool System_RuntimeTypeHandle_HasElementType(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_RuntimeType(void* /*__this*/, void* /*inst*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_Type__(void* /*__this*/, void* /*inst*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_IsArray(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsComObject(void* /*type*/, bool isGenericCOM) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsPrimitive(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsSZArray(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsTypeDefinition(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsVisible(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void* System_RuntimeTypeHandle_MakeArray__System_Int32(void* /*__this*/, int32_t rank) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_MakeByRef(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_MakePointer(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_MakeSZArray(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_RuntimeType_System_Reflection_RuntimeAssembly(void* /*type*/, void* /*assembly*/) { cil2cpp::stub_called(__func__); }
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_RuntimeType___System_RuntimeType___System_RuntimeType(void* /*paramType*/, void* /*typeContext*/, void* /*methodContext*/, void* /*toType*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" intptr_t System_RuntimeTypeHandle_ToIntPtr(System_RuntimeTypeHandle value) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_RuntimeTypeHandle_VerifyInterfaceIsImplemented__System_RuntimeTypeHandle(void* /*__this*/, System_RuntimeTypeHandle interfaceHandle) { cil2cpp::stub_called(__func__); }
extern "C" bool System_RuntimeTypeHandle__IsVisible(System_Runtime_CompilerServices_QCallTypeHandle typeHandle) { cil2cpp::stub_called(__func__); return false; }
extern "C" intptr_t System_RuntimeTypeHandle_get_Value(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" int32_t System_RuntimeTypeHandle___IsVisible_g____PInvoke_67_0(System_Runtime_CompilerServices_QCallTypeHandle __typeHandle_native) { cil2cpp::stub_called(__func__); return {}; }
extern "C" intptr_t System_RuntimeTypeHandle__GetMetadataImport(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle__GetUtf8Name(void* /*type*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_CanCastTo(void* /*type*/, void* /*target*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void System_RuntimeTypeHandle_ConstructName__System_Runtime_CompilerServices_QCallTypeHandle_System_TypeNameFormatFlags_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_TypeNameFormatFlags formatFlags, System_Runtime_CompilerServices_StringHandleOnStack retString) { cil2cpp::stub_called(__func__); }
extern "C" bool System_RuntimeTypeHandle_ContainsGenericVariables__System_RuntimeType(void* /*handle*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle baseType, void* /*pTypeHandles*/, int32_t cTypeHandles, System_Runtime_CompilerServices_ObjectHandleOnStack instantiatedObject) { cil2cpp::stub_called(__func__); }
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtr(System_Runtime_CompilerServices_QCallTypeHandle typeHandle, intptr_t objHandle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_Runtime_CompilerServices_ObjectHandleOnStack_void_ptrPtr_System_VoidPtrPtr_void_ptrPtr_Interop_BOOLPtr(System_Runtime_CompilerServices_ObjectHandleOnStack pRuntimeType, void* /*ppfnAllocator*/, void* /*pvAllocatorFirstArg*/, void* /*ppfnCtor*/, void* /*pfCtorIsPublic*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_RuntimeType_void_ptrRef_System_VoidRefPtr_void_ptrRef_System_BooleanRef(void* /*rt*/, void* /*pfnAllocator*/, void* /*vAllocatorFirstArg*/, void* /*pfnCtor*/, void* /*ctorIsPublic*/) { cil2cpp::stub_called(__func__); }
extern "C" void* System_RuntimeTypeHandle_GetArgumentTypesFromFunctionPointer(void* /*type*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_TypeAttributes System_RuntimeTypeHandle_GetAttributes(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetBaseType(void* /*type*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_RuntimeTypeHandle_GetConstraints__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack types) { cil2cpp::stub_called(__func__); }
extern "C" System_Reflection_CorElementType System_RuntimeTypeHandle_GetCorElementType(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetDeclaringType(void* /*type*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_GetFields(void* /*type*/, void* /*result*/, void* /*count*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetFirstIntroducedMethod(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" intptr_t System_RuntimeTypeHandle_GetGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_InteropServices_GCHandleType(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_InteropServices_GCHandleType type) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_RuntimeTypeHandle_GetGenericTypeDefinition(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack retType) { cil2cpp::stub_called(__func__); }
extern "C" int32_t System_RuntimeTypeHandle_GetGenericVariableIndex__System_RuntimeType(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_RuntimeTypeHandle_GetInstantiation(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack types, Interop_BOOL fAsRuntimeTypeArray) { cil2cpp::stub_called(__func__); }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallTypeHandle_System_RuntimeMethodHandleInternal(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_QCallTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_RuntimeTypeHandle_System_RuntimeMethodHandleInternal(void* /*__this*/, System_RuntimeTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetInterfaces(void* /*type*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_MetadataImport System_RuntimeTypeHandle_GetMetadataImport(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetMethodAt(void* /*type*/, int32_t slot) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetModule(void* /*type*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_RuntimeTypeHandle_GetNextIntroducedMethod(void* /*method*/) { cil2cpp::stub_called(__func__); }
extern "C" int32_t System_RuntimeTypeHandle_GetNumVirtuals(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_RuntimeTypeHandle_Instantiate__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, void* /*pInst*/, int32_t numGenericArgs, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" Interop_BOOL System_RuntimeTypeHandle_IsCollectible(System_Runtime_CompilerServices_QCallTypeHandle handle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" bool System_RuntimeTypeHandle_IsGenericVariable(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsInterface(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsUnmanagedFunctionPointer(void* /*type*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_RuntimeTypeHandle_IsValueType(void* type) {
    return cil2cpp::type_get_is_value_type(reinterpret_cast<cil2cpp::Type*>(type));
}
extern "C" void System_RuntimeTypeHandle_MakeArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, int32_t rank, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_MakeByRef__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_MakePointer__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_MakeSZArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_QCallAssembly assembly) { cil2cpp::stub_called(__func__); }
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_IntPtrPtr_System_Int32_System_IntPtrPtr_System_Int32_System_RuntimeType(void* /*paramType*/, void* /*pTypeContext*/, int32_t typeContextLength, void* /*pMethodContext*/, int32_t methodContextLength, void* /*toType*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void System_RuntimeTypeHandle_VerifyInterfaceIsImplemented__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallTypeHandle(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_QCallTypeHandle interfaceHandle) { cil2cpp::stub_called(__func__); }

// ===== System.Runtime.CompilerServices.MethodTable =====

// ===== System.Runtime.InteropServices.Marshalling =====
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_FromManaged(void* /*__this*/, void* /*handle*/) { cil2cpp::stub_called(__func__); }
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_ToUnmanaged(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_Free(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_FromManaged(void* /*__this*/, void* /*handle*/) { cil2cpp::stub_called(__func__); }
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_ToUnmanaged(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_Free(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_FromManaged(void* /*__this*/, void* /*handle*/) { cil2cpp::stub_called(__func__); }
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_ToUnmanaged(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_Free(void* /*__this*/) { cil2cpp::stub_called(__func__); }

// ===== System.Threading.CancellationTokenSource =====
// Removed from CoreRuntimeTypes — all methods now compile from BCL IL.

// ===== System.Threading.Thread =====
extern "C" void System_Threading_Thread__ctor__System_Threading_ParameterizedThreadStart(void* __this, void* start) {
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    t->start_delegate = reinterpret_cast<cil2cpp::Delegate*>(start);
}
extern "C" void System_Threading_Thread__ctor__System_Threading_ThreadStart(void* __this, void* start) {
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    t->start_delegate = reinterpret_cast<cil2cpp::Delegate*>(start);
}
extern "C" void System_Threading_Thread__InformThreadNameChange_g____PInvoke_26_0(System_Threading_ThreadHandle __t_native, void* /*__name_native*/, int32_t __len_native) { cil2cpp::stub_called(__func__); }
extern "C" bool System_Threading_Thread_get_IsBackground(void* __this) {
    return reinterpret_cast<cil2cpp::ManagedThread*>(__this)->is_background;
}
extern "C" bool System_Threading_Thread_get_IsThreadPoolThread(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" System_Threading_ThreadPriority System_Threading_Thread_get_Priority(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" uint64_t System_Threading_Thread_GetCurrentOSThreadId() { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_Threading_ThreadHandle System_Threading_Thread_GetNativeHandle(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_Threading_Thread_ResetThreadPoolThread(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_ResetThreadPoolThreadSlow(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_set_IsBackground(void* __this, bool value) {
    reinterpret_cast<cil2cpp::ManagedThread*>(__this)->is_background = value;
}
extern "C" void System_Threading_Thread_set_Name(void* /*__this*/, void* /*value*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_set_IsThreadPoolThread(void* /*__this*/, bool value) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_set_Priority(void* /*__this*/, System_Threading_ThreadPriority value) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_SetThreadPoolWorkerThreadName(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_Start__System_Boolean_System_Boolean(void* /*__this*/, bool captureContext, bool internalThread) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_StartCore(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_StartInternal(System_Threading_ThreadHandle t, int32_t stackSize, int32_t priority, void* /*pThreadName*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_ThreadNameChanged(void* /*__this*/, void* /*value*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_UnsafeStart(void* /*__this*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_Threading_Thread_UnsafeStart__System_Object(void* /*__this*/, void* /*parameter*/) { cil2cpp::stub_called(__func__); }
extern "C" Interop_BOOL System_Threading_Thread_YieldInternal() { cil2cpp::stub_called(__func__); return {}; }

// ===== System.Type =====
extern "C" void* System_Type_get_Assembly(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_get_AssemblyQualifiedName(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_get_BaseType(void* __this) {
    return reinterpret_cast<void*>(cil2cpp::type_get_base_type(reinterpret_cast<cil2cpp::Type*>(__this)));
}
extern "C" void* System_Type_get_FullName(void* __this) {
    return reinterpret_cast<void*>(cil2cpp::type_get_full_name(reinterpret_cast<cil2cpp::Type*>(__this)));
}
extern "C" System_Guid System_Type_get_GUID(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" bool System_Type_IsArrayImpl(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Type_get_IsClass(void* __this) {
    return cil2cpp::type_get_is_class(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_get_Module(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_get_Namespace(void* __this) {
    return reinterpret_cast<void*>(cil2cpp::type_get_namespace(reinterpret_cast<cil2cpp::Type*>(__this)));
}
extern "C" void* System_Type_get_UnderlyingSystemType(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_TypeAttributes System_Type_GetAttributeFlagsImpl(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Type_GetConstructorImpl(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetConstructors__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetElementType(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetEvent__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetEvents__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetField__System_String(void* /*__this*/, void* /*name*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetField__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetFields(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetFields__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetInterface__System_String_System_Boolean(void* /*__this*/, void* /*name*/, bool ignoreCase) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetInterfaces(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetMembers__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetMethod__System_String(void* /*__this*/, void* /*name*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetMethodImpl__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Reflection_CallingConventions_System_Type___System_Reflection_ParameterModifier__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, System_Reflection_CallingConventions callConvention, void* /*types*/, void* /*modifiers*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetMethods(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetMethods__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetNestedType__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetNestedTypes__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetProperties__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetPropertyImpl(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr, void* /*binder*/, void* /*returnType*/, void* /*types*/, void* /*modifiers*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_Type_HasElementTypeImpl(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void* System_Type_InvokeMember__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Object_System_Object___System_Reflection_ParameterModifier___System_Globalization_CultureInfo_System_String__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*target*/, void* /*args*/, void* /*modifiers*/, void* /*culture*/, void* /*namedParameters*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_Type_IsByRefImpl(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Type_IsCOMObjectImpl(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Type_IsPointerImpl(void* /*__this*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" bool System_Type_IsPrimitiveImpl(void* __this) {
    return cil2cpp::type_get_is_primitive(reinterpret_cast<cil2cpp::Type*>(__this));
}

// Type virtual methods that base System.Type throws NotSupportedException("SubclassOverride") for.
// RuntimeType overrides them but is a CoreRuntimeType (vtable excluded), so virtual dispatch
// hits the base throwing implementations. Implement here using TypeInfo so they work correctly.

extern "C" bool System_Type_get_IsByRefLike(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) cil2cpp::throw_null_reference();
    return t->type_info->flags & cil2cpp::TypeFlags::IsByRefLike;
}

extern "C" int32_t System_Type_GetArrayRank(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) cil2cpp::throw_null_reference();
    if (t->type_info->flags & cil2cpp::TypeFlags::Array) return 1;
    cil2cpp::throw_invalid_operation();
    return 0;
}

extern "C" void* System_Type_GetGenericTypeDefinition(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) cil2cpp::throw_null_reference();
    if (t->type_info->generic_definition_name) {
        auto* def_info = cil2cpp::type_get_by_name(t->type_info->generic_definition_name);
        if (def_info) return cil2cpp::type_get_type_object(def_info);
    }
    if (t->type_info->flags & cil2cpp::TypeFlags::Generic) return __this;
    cil2cpp::throw_invalid_operation();
    return nullptr;
}

extern "C" void* System_Type_GetGenericArguments(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) cil2cpp::throw_null_reference();
    auto count = t->type_info->generic_argument_count;
    auto** args = t->type_info->generic_arguments;
    auto* arr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, count));
    if (count > 0 && args) {
        auto** data = reinterpret_cast<cil2cpp::Type**>(cil2cpp::array_data(arr));
        for (uint32_t i = 0; i < count; i++) {
            data[i] = args[i] ? cil2cpp::type_get_type_object(args[i]) : nullptr;
        }
    }
    return arr;
}

// Convert C++ mangled name to IL-style dotted name.
// E.g. "System_Collections_Generic_IEquatable_1" → "System.Collections.Generic.IEquatable`1"
// The last "_N" (where N is a digit) is converted to "`N" (generic arity).
static std::string cpp_name_to_il_name(const char* cpp_name) {
    std::string result(cpp_name);
    // Replace underscores with dots
    for (auto& c : result) {
        if (c == '_') c = '.';
    }
    // Fix generic arity: last ".N" where N is digits → "`N"
    // E.g. "System.IEquatable.1" → "System.IEquatable`1"
    auto last_dot = result.rfind('.');
    if (last_dot != std::string::npos) {
        bool all_digits = true;
        for (size_t i = last_dot + 1; i < result.size(); i++) {
            if (result[i] < '0' || result[i] > '9') { all_digits = false; break; }
        }
        if (all_digits && last_dot + 1 < result.size()) {
            result[last_dot] = '`';
        }
    }
    return result;
}

extern "C" void* System_Type_MakeGenericType(void* __this, cil2cpp::Array* typeArguments) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) cil2cpp::throw_null_reference();
    if (!typeArguments) cil2cpp::throw_null_reference();

    const char* def_name = t->type_info->full_name;
    if (!def_name) cil2cpp::throw_invalid_operation();

    auto arg_count = cil2cpp::array_length(typeArguments);
    auto** args = reinterpret_cast<cil2cpp::Type**>(cil2cpp::array_data(typeArguments));

    // Determine if full_name is in C++ format (underscores) or IL format (dots with backtick)
    std::string base_name;
    if (std::strchr(def_name, '.') != nullptr || std::strchr(def_name, '`') != nullptr) {
        base_name = def_name; // Already in IL format
    } else {
        base_name = cpp_name_to_il_name(def_name); // Convert from C++ format
    }

    // Build "GenericType`N<Arg1, Arg2>"
    std::string name = base_name;
    name += '<';
    for (int32_t i = 0; i < arg_count; i++) {
        if (i > 0) name += ", ";
        if (!args[i] || !args[i]->type_info || !args[i]->type_info->full_name) {
            cil2cpp::throw_null_reference();
        }
        name += args[i]->type_info->full_name;
    }
    name += '>';

    auto* result = cil2cpp::type_get_by_name(name.c_str());
    if (result) return cil2cpp::type_get_type_object(result);

    // Type not found — wasn't monomorphized at compile time (AOT limitation).
    // Return nullptr: many callers (GetGenericType, TryMakeGenericType) either check for null
    // or propagate the result. Throwing would crash callers without try-catch.
    return nullptr;
}

// ===== System.ValueType =====
extern "C" bool System_ValueType_CanCompareBits(void* /*obj*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" int32_t System_ValueType_GetHashCode(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_ValueType_ToString(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== Bridge functions for QCall/P-Invoke wrappers and gated CoreRuntimeTypes =====

// ValueType.CanCompareBitsOrUseFastGetHashCode — P/Invoke wrapper (CLR-internal MethodTable check)
// Returns whether the value type's fields can be compared bit-by-bit.
// Conservative: always return false (forces field-by-field comparison).
extern "C" int32_t System_ValueType__CanCompareBitsOrUseFastGetHashCodeHelper_g____PInvoke_2_0(void* /*__pMT_native*/) { return 0; }

// Delegate.InternalAllocLike — QCall variant (ObjectHandleOnStack)
// The non-QCall variant (InternalAlloc) is in icall.cpp. This is the QCall bridge.
extern "C" void System_Delegate_InternalAllocLike__System_Runtime_CompilerServices_ObjectHandleOnStack(void* /*d*/) { cil2cpp::stub_called(__func__); }

// RuntimeTypeHandle.GetNumVirtualsAndStaticVirtuals — QCall wrapper
// Returns the virtual method count from TypeInfo.
extern "C" int32_t System_RuntimeTypeHandle_GetNumVirtualsAndStaticVirtuals__System_Runtime_CompilerServices_QCallTypeHandle(void* /*type*/) { return 0; }

// Thread.InformThreadNameChange — P/Invoke wrapper (CLR-internal thread naming)
// No-op for AOT — we don't have CLR thread naming infrastructure.
extern "C" void System_Threading_Thread__InformThreadNameChange_g____PInvoke_29_0(void* /*__t_native*/, void* /*__name_native*/, int32_t /*__len_native*/) { }

// Array.GetValue(int[]) — multi-dimensional array indexing
// Flattens the indices array and delegates to array_get_value.
extern "C" cil2cpp::Object* System_Array_GetValue__System_Int32__(cil2cpp::Array* __this, cil2cpp::Array* indices) {
    if (!__this || !indices) { cil2cpp::throw_null_reference(); }
    // For 1D arrays, just use the first index
    auto* idx = static_cast<int32_t*>(cil2cpp::array_data(indices));
    auto len = cil2cpp::array_length(indices);
    if (len == 1) {
        return static_cast<cil2cpp::Object*>(cil2cpp::array_get_value(__this, idx[0]));
    }
    // Multi-dim: compute flattened index (simplified — assumes row-major)
    // TODO: proper MdArray bounds handling
    intptr_t flat = 0;
    intptr_t stride = 1;
    for (intptr_t i = static_cast<intptr_t>(len) - 1; i >= 0; --i) {
        flat += idx[i] * stride;
        stride *= cil2cpp::array_length(__this); // Simplified: assumes square
    }
    return static_cast<cil2cpp::Object*>(cil2cpp::array_get_value(__this, static_cast<int32_t>(flat)));
}

// Reflection.MethodBase.GetParametersAsSpan — returns empty span (most callers handle null/empty)
extern "C" void* System_Reflection_MethodBase_GetParametersAsSpan(void* /*__this*/) { return nullptr; }

// Reflection.TypeInfo.GetDeclaredProperty — returns null (property not found)
extern "C" void* System_Reflection_TypeInfo_GetDeclaredProperty(void* /*__this*/, void* /*name*/) { return nullptr; }
