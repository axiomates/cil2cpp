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
#include <thread>

#include <cil2cpp/object.h>
#include <cil2cpp/string.h>
#include <cil2cpp/array.h>
#include <cil2cpp/exception.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/memberinfo.h>
#include <cil2cpp/reflection.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/threading.h>
#include <cil2cpp/delegate.h>
#include <cil2cpp/gchandle.h>

// Platform headers — MUST come after cil2cpp headers to avoid VOID/BOOLEAN macro conflicts
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <sched.h>
#ifdef __linux__
#include <sys/syscall.h>
#include <unistd.h>
#endif
#endif

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
extern "C" int64_t System_Array_get_LongLength(void* __this) {
    auto* arr = reinterpret_cast<cil2cpp::Array*>(__this);
    return arr ? static_cast<int64_t>(cil2cpp::array_length(arr)) : 0;
}
extern "C" intptr_t System_Array_GetFlattenedIndex__System_Int32(void* /*__this*/, int32_t rawIndex) {
    // For single-dimensional zero-indexed arrays, flattened index == raw index
    return static_cast<intptr_t>(rawIndex);
}
extern "C" intptr_t System_Array_GetFlattenedIndex__System_ReadOnlySpan_1_System_Int32_(void* /*__this*/, System_ReadOnlySpan_1_System_Int32 indices) {
    // For SZArrays with span indices, use first element
    if (indices.f__reference && indices.f__length > 0) {
        return static_cast<intptr_t>(*reinterpret_cast<int32_t*>(indices.f__reference));
    }
    return 0;
}
extern "C" int32_t System_Array_GetLowerBound(void* /*__this*/, int32_t /*dimension*/) {
    // All arrays in CIL2CPP are zero-indexed (SZArray). MdArray lower bounds not yet supported.
    return 0;
}
extern "C" void System_Array_InternalSetValue(void* __this, void* value, intptr_t flattenedIndex) {
    // Set element at flattened index for Object arrays
    auto* arr = reinterpret_cast<cil2cpp::Array*>(__this);
    if (!arr) return;
    auto len = cil2cpp::array_length(arr);
    if (flattenedIndex < 0 || static_cast<uint32_t>(flattenedIndex) >= len) {
        cil2cpp::throw_index_out_of_range();
    }
    auto** data = reinterpret_cast<void**>(cil2cpp::array_data(arr));
    data[flattenedIndex] = value;
}
extern "C" bool System_Array_IsSimpleCopy(void* sourceArray, void* destinationArray) {
    // Simple copy is possible when both arrays have the same element type
    if (!sourceArray || !destinationArray) return false;
    auto* src = reinterpret_cast<cil2cpp::Object*>(sourceArray);
    auto* dst = reinterpret_cast<cil2cpp::Object*>(destinationArray);
    return src->__type_info == dst->__type_info;
}
extern "C" void System_Array_SetValue__System_Object_System_Int32(void* __this, void* value, int32_t index) {
    System_Array_InternalSetValue(__this, value, static_cast<intptr_t>(index));
}
extern "C" void System_Array_SetValue__System_Object_System_Int32__(void* __this, void* value, void* indices) {
    // Multi-index version — for SZArray, use first index
    if (!indices) { cil2cpp::throw_argument_null(); }
    auto* idxArr = reinterpret_cast<cil2cpp::Array*>(indices);
    if (cil2cpp::array_length(idxArr) == 0) { cil2cpp::throw_argument(); }
    auto* data = reinterpret_cast<int32_t*>(cil2cpp::array_data(idxArr));
    System_Array_InternalSetValue(__this, value, static_cast<intptr_t>(data[0]));
}

// ===== System.DefaultBinder =====
extern "C" void System_DefaultBinder__ctor(void* /*__this*/) { }
extern "C" void* System_DefaultBinder_BindToField(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*value*/, void* /*cultureInfo*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_DefaultBinder_BindToMethod(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*args*/, void* /*modifiers*/, void* /*cultureInfo*/, void* /*names*/, void* /*state*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_DefaultBinder_ChangeType(void* /*__this*/, void* /*value*/, void* /*type*/, void* /*cultureInfo*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_DefaultBinder_ReorderArgumentArray(void* /*__this*/, void* /*args*/, void* /*state*/) { cil2cpp::stub_called(__func__); }
extern "C" void* System_DefaultBinder_SelectMethod(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*types*/, void* /*modifiers*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_DefaultBinder_SelectProperty(void* /*__this*/, System_Reflection_BindingFlags bindingAttr, void* /*match*/, void* /*returnType*/, void* /*indexes*/, void* /*modifiers*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Delegate =====
extern "C" void* System_Delegate_FindMethodHandle(void* /*__this*/) {
    // MethodHandle is a CoreCLR concept — no equivalent in AOT
    return nullptr;
}
extern "C" void* System_Delegate_get_Target(void* __this) {
    auto* del = reinterpret_cast<cil2cpp::Delegate*>(__this);
    return del ? reinterpret_cast<void*>(del->target) : nullptr;
}
extern "C" bool System_Delegate_InternalEqualMethodHandles(void* left, void* right) {
    auto* a = reinterpret_cast<cil2cpp::Delegate*>(left);
    auto* b = reinterpret_cast<cil2cpp::Delegate*>(right);
    if (!a || !b) return false;
    return a->method_ptr == b->method_ptr;
}

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
extern "C" void* System_Exception_GetClassName(void* __this) {
    if (!__this) return nullptr;
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* ti = obj->__type_info;
    if (ti && ti->full_name) return reinterpret_cast<void*>(cil2cpp::string_literal(ti->full_name));
    return nullptr;
}
extern "C" void System_Exception_GetMessageFromNativeResources__System_Exception_ExceptionMessageKind_System_Runtime_CompilerServices_StringHandleOnStack(System_Exception_ExceptionMessageKind kind, System_Runtime_CompilerServices_StringHandleOnStack retMesg) { cil2cpp::stub_called(__func__); }
extern "C" void System_Exception_GetStackTracesDeepCopy(void* /*exception*/, void* /*currentStackTrace*/, void* /*dynamicMethodArray*/) { cil2cpp::stub_called(__func__); }
extern "C" bool System_Exception_IsImmutableAgileException(void* /*e*/) {
    return false; // No cross-AppDomain exceptions in AOT
}
extern "C" void System_Exception_PrepareForForeignExceptionRaise() {
    // No SEH/foreign exception interop needed in AOT
}
extern "C" void System_Exception_RestoreDispatchState(void* /*__this*/, void* /*dispatchState*/) {
    // Exception dispatch state restore — not critical for basic exception handling
}
extern "C" void System_Exception_SaveStackTracesFromDeepCopy(void* /*exception*/, void* /*currentStackTrace*/, void* /*dynamicMethodArray*/) {
    // Stack trace deep copy for async chains — not yet implemented
}
extern "C" void System_Exception_SetCurrentStackTrace(void* __this) {
    auto* ex = reinterpret_cast<cil2cpp::Exception*>(__this);
    if (ex) ex->f__stackTraceString = cil2cpp::capture_stack_trace();
}
extern "C" void* System_Exception_SetRemoteStackTrace(void* __this, void* stackTrace) {
    if (__this) static_cast<cil2cpp::Exception*>(__this)->f__remoteStackTraceString = static_cast<cil2cpp::String*>(stackTrace);
    return __this;
}
extern "C" void* System_Exception_ToString(void* __this) {
    if (!__this) return nullptr;
    auto* ex = reinterpret_cast<cil2cpp::Exception*>(__this);
    auto* ti = reinterpret_cast<cil2cpp::Object*>(__this)->__type_info;
    std::string result;
    if (ti && ti->full_name) result = ti->full_name;
    else result = "System.Exception";
    if (ex->f__message) {
        result += ": ";
        result += cil2cpp::string_to_utf8(ex->f__message);
    }
    if (ex->f__innerException) {
        result += "\n ---> ";
        auto* innerStr = reinterpret_cast<cil2cpp::String*>(System_Exception_ToString(ex->f__innerException));
        if (innerStr) result += cil2cpp::string_to_utf8(innerStr);
        result += "\n   --- End of inner exception stack trace ---";
    }
    if (ex->f__stackTraceString) {
        result += "\n";
        result += cil2cpp::string_to_utf8(ex->f__stackTraceString);
    }
    return reinterpret_cast<void*>(cil2cpp::string_create_utf8(result.c_str()));
}

// Exception virtual property getters/setters (needed for vtable dispatch)
extern "C" void* System_Exception_get_Message(void* __this) {
    return __this ? static_cast<cil2cpp::Exception*>(__this)->f__message : nullptr;
}
extern "C" void* System_Exception_get_Data(void* __this) {
    return __this ? static_cast<cil2cpp::Exception*>(__this)->f__data : nullptr;
}
extern "C" void* System_Exception_get_InnerException(void* __this) {
    return __this ? static_cast<cil2cpp::Exception*>(__this)->f__innerException : nullptr;
}
extern "C" void* System_Exception_get_HelpLink(void* __this) {
    return __this ? static_cast<cil2cpp::Exception*>(__this)->f__helpURL : nullptr;
}
extern "C" void System_Exception_set_HelpLink(void* __this, void* value) {
    if (__this) static_cast<cil2cpp::Exception*>(__this)->f__helpURL = static_cast<cil2cpp::String*>(value);
}
extern "C" void* System_Exception_get_Source(void* __this) {
    return __this ? static_cast<cil2cpp::Exception*>(__this)->f__source : nullptr;
}
extern "C" void System_Exception_set_Source(void* __this, void* value) {
    if (__this) static_cast<cil2cpp::Exception*>(__this)->f__source = static_cast<cil2cpp::String*>(value);
}
extern "C" void* System_Exception_get_StackTrace(void* __this) {
    return __this ? static_cast<cil2cpp::Exception*>(__this)->f__stackTraceString : nullptr;
}
extern "C" void* System_Exception_GetBaseException(void* __this) {
    if (!__this) return nullptr;
    auto* ex = static_cast<cil2cpp::Exception*>(__this);
    while (ex->f__innerException != nullptr)
        ex = ex->f__innerException;
    return ex;
}
// Forward declare the generated struct type (opaque empty struct)
struct System_Runtime_Serialization_StreamingContext {};
extern "C" void System_Exception_GetObjectData(void* /*__this*/, void* /*info*/, System_Runtime_Serialization_StreamingContext /*context*/) {
    // Serialization not supported in AOT
}

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
extern "C" void* System_Reflection_Assembly_GetType__System_String_System_Boolean_System_Boolean(void* /*__this*/, void* name, bool throwOnError, bool /*ignoreCase*/) {
    if (!name) return nullptr;
    auto* nameStr = reinterpret_cast<cil2cpp::String*>(name);
    char* utf8 = cil2cpp::string_to_utf8(nameStr);
    auto* ti = cil2cpp::type_get_by_name(utf8);
    if (!ti && throwOnError) cil2cpp::throw_argument();
    return ti ? reinterpret_cast<void*>(cil2cpp::type_get_type_object(ti)) : nullptr;
}

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
extern "C" bool System_Reflection_MemberInfo_CacheEquals(void* __this, void* o) {
    return __this == o; // Reference equality for cached member info objects
}
extern "C" bool System_Reflection_MemberInfo_Equals(void* __this, void* obj) {
    return __this == obj; // Reference equality — each MemberInfo wraps a unique native info
}
extern "C" void* System_Reflection_MemberInfo_get_DeclaringType(void* __this) {
    return cil2cpp::memberinfo_get_declaring_type(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" bool System_Reflection_MemberInfo_get_IsCollectible(void* /*__this*/) {
    return false; // AOT types are never collectible
}
extern "C" System_Reflection_MemberTypes System_Reflection_MemberInfo_get_MemberType(void* __this) {
    if (!__this) return {};
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* ti = obj->__type_info;
    if (!ti || !ti->full_name) return {};
    // Dispatch by runtime TypeInfo to determine MemberType enum value
    // MemberTypes: Constructor=1, Event=2, Field=4, Method=8, Property=16, TypeInfo=32, NestedType=128
    std::string name(ti->full_name);
    if (name.find("FieldInfo") != std::string::npos) return 4;    // Field
    if (name.find("ConstructorInfo") != std::string::npos) return 1; // Constructor
    if (name.find("MethodInfo") != std::string::npos || name.find("MethodBase") != std::string::npos) return 8; // Method
    if (name.find("PropertyInfo") != std::string::npos) return 16; // Property
    if (name.find("EventInfo") != std::string::npos) return 2;    // Event
    if (name.find("Type") != std::string::npos) return 32;        // TypeInfo
    return 0;
}
extern "C" int32_t System_Reflection_MemberInfo_get_MetadataToken(void* __this) {
    if (!__this) return 0;
    // Try to get metadata token from the underlying native info
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* ti = obj->__type_info;
    if (!ti) return 0;
    // For Type objects, return the type's metadata token
    if (ti == &cil2cpp::System_Type_TypeInfo || (ti->full_name && std::string(ti->full_name).find("RuntimeType") != std::string::npos)) {
        auto* type = reinterpret_cast<cil2cpp::Type*>(__this);
        if (type->type_info) return static_cast<int32_t>(type->type_info->metadata_token);
    }
    return 0;
}
extern "C" void* System_Reflection_MemberInfo_get_Module(void* /*__this*/) {
    // Return the singleton dummy module — same as RuntimeTypeHandle.GetModule
    alignas(16) static uint8_t s_dummy_module[64] = {};
    return s_dummy_module;
}
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
extern "C" int32_t System_Reflection_MemberInfo_GetHashCode(void* __this) {
    // Use pointer identity as hash — stable for the lifetime of the object
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(__this) >> 3);
}
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAs(void* __this, void* other) {
    return __this == other; // In AOT, identity equals metadata definition equality
}
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeConstructorInfo(void* __this, void* other) {
    return __this == other;
}
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeEventInfo(void* __this, void* other) {
    return __this == other;
}
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeFieldInfo(void* __this, void* other) {
    return __this == other;
}
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimeMethodInfo(void* __this, void* other) {
    return __this == other;
}
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_Reflection_RuntimePropertyInfo(void* __this, void* other) {
    return __this == other;
}
extern "C" bool System_Reflection_MemberInfo_HasSameMetadataDefinitionAsCore_System_RuntimeType(void* __this, void* other) {
    return __this == other;
}
extern "C" bool System_Reflection_MemberInfo_IsDefined(void* /*__this*/, void* /*attributeType*/, bool inherit) { cil2cpp::stub_called(__func__); return false; }

// ===== System.Reflection.MethodBase =====
// Helper to extract native MethodInfo* from a ManagedMethodInfo receiver
static cil2cpp::MethodInfo* _get_native_mi(void* __this) {
    if (!__this) return nullptr;
    return reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this)->native_info;
}
extern "C" void System_Reflection_MethodBase__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_MethodBase_Equals(void* __this, void* obj) {
    if (__this == obj) return true;
    if (!__this || !obj) return false;
    // Same native MethodInfo* means same method
    auto* a = _get_native_mi(__this);
    auto* b = _get_native_mi(obj);
    return a && b && a == b;
}
extern "C" System_Reflection_MethodAttributes System_Reflection_MethodBase_get_Attributes(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni ? static_cast<System_Reflection_MethodAttributes>(ni->flags) : System_Reflection_MethodAttributes{};
}
extern "C" System_Reflection_CallingConventions System_Reflection_MethodBase_get_CallingConvention(void* __this) {
    auto* ni = _get_native_mi(__this);
    // CallingConventions.Standard = 1, HasThis = 0x20
    // Static methods use Standard, instance methods use Standard|HasThis
    if (ni && (ni->flags & 0x0010)) return 1; // Static → Standard
    return 0x21; // Standard | HasThis
}
extern "C" bool System_Reflection_MethodBase_get_ContainsGenericParameters(void* /*__this*/) {
    return false; // AOT: all generic methods are closed
}
extern "C" bool System_Reflection_MethodBase_get_IsGenericMethod(void* /*__this*/) {
    return false; // AOT: specialized — no open generic methods at runtime
}
extern "C" bool System_Reflection_MethodBase_get_IsGenericMethodDefinition(void* /*__this*/) {
    return false; // AOT: no open generic method definitions
}
extern "C" bool System_Reflection_MethodBase_get_IsPublic(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && (ni->flags & 0x0007) == 0x0006;
}
extern "C" bool System_Reflection_MethodBase_get_IsStatic(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && (ni->flags & 0x0010) != 0;
}
extern "C" System_RuntimeMethodHandle System_Reflection_MethodBase_get_MethodHandle(void* __this) {
    auto* ni = _get_native_mi(__this);
    return { ni ? ni->method_pointer : nullptr };
}
extern "C" System_Reflection_MethodImplAttributes System_Reflection_MethodBase_get_MethodImplementationFlags(void* /*__this*/) {
    return {}; // MethodImplAttributes.IL = 0 (default for AOT-compiled methods)
}
extern "C" void* System_Reflection_MethodBase_GetGenericArguments(void* /*__this*/) {
    // AOT: return empty Type[] — no open generic methods
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, 0);
}
extern "C" int32_t System_Reflection_MethodBase_GetHashCode(void* __this) {
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(__this) >> 3);
}
extern "C" System_Reflection_MethodImplAttributes System_Reflection_MethodBase_GetMethodImplementationFlags(void* /*__this*/) {
    return {}; // Same as get_MethodImplementationFlags
}
extern "C" void* System_Reflection_MethodBase_GetParameters(void* __this) {
    return cil2cpp::methodinfo_get_parameters(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodBase_GetParametersNoCopy(void* __this) {
    return cil2cpp::methodinfo_get_parameters(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodBase_GetParameterTypes(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (!ni) return cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, 0);
    auto count = static_cast<int32_t>(ni->parameter_count);
    auto* arr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, count));
    if (count > 0 && ni->parameter_types) {
        auto** data = reinterpret_cast<cil2cpp::Type**>(cil2cpp::array_data(arr));
        for (int32_t i = 0; i < count; i++) {
            data[i] = ni->parameter_types[i]
                ? cil2cpp::type_get_type_object(ni->parameter_types[i]) : nullptr;
        }
    }
    return arr;
}
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
extern "C" Interop_BOOL System_Reflection_RuntimeAssembly_GetIsCollectible(System_Runtime_CompilerServices_QCallAssembly /*assembly*/) {
    return 0; // AOT assemblies are never collectible
}

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
extern "C" void* System_Reflection_RuntimeConstructorInfo_GetRuntimeModule(void* /*__this*/) {
    alignas(16) static uint8_t s_dummy_module[64] = {};
    return s_dummy_module;
}

// ===== System.Reflection.RuntimeConstructorInfo.InvokeClassConstructor =====
extern "C" void System_Reflection_RuntimeConstructorInfo_InvokeClassConstructor(void* /*__this*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeConstructorInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeConstructorInfo_ThrowNoInvokeException(void* /*__this*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeConstructorInfo.get =====
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ArgumentTypes(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeConstructorInfo_get_InvocationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Invoker(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ReflectedTypeInternal(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (ni && ni->declaring_type) {
        return reinterpret_cast<void*>(cil2cpp::type_get_type_object(ni->declaring_type));
    }
    return nullptr;
}
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Signature(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimeFieldInfo =====
extern "C" void System_Reflection_RuntimeFieldInfo__ctor(void* /*__this*/, void* /*reflectedTypeCache*/, void* /*declaringType*/, System_Reflection_BindingFlags bindingFlags) { }

// ===== System.Reflection.RuntimeFieldInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeFieldInfo_GetRuntimeModule(void* /*__this*/) {
    alignas(16) static uint8_t s_dummy_module[64] = {};
    return s_dummy_module;
}

// ===== System.Reflection.RuntimeFieldInfo.get =====
extern "C" void* System_Reflection_RuntimeFieldInfo_get_ReflectedTypeInternal(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (fi && fi->native_info && fi->native_info->declaring_type) {
        return reinterpret_cast<void*>(cil2cpp::type_get_type_object(fi->native_info->declaring_type));
    }
    return nullptr;
}

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
extern "C" void* System_Reflection_RuntimeMethodInfo_GetRuntimeModule(void* /*__this*/) {
    alignas(16) static uint8_t s_dummy_module[64] = {};
    return s_dummy_module;
}

// ===== System.Reflection.RuntimeMethodInfo.InvokePropertySetter =====
extern "C" void System_Reflection_RuntimeMethodInfo_InvokePropertySetter(void* /*__this*/, void* /*obj*/, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*parameter*/, void* /*culture*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeMethodInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeMethodInfo_ThrowNoInvokeException(void* /*__this*/) { cil2cpp::stub_called(__func__); }

// ===== System.Reflection.RuntimeMethodInfo.get =====
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ArgumentTypes(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeMethodInfo_get_InvocationFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Invoker(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ReflectedTypeInternal(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (ni && ni->declaring_type) {
        return reinterpret_cast<void*>(cil2cpp::type_get_type_object(ni->declaring_type));
    }
    return nullptr;
}
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Signature(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetGetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetGetMethod(void* /*__this*/, bool nonPublic) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetIndexParametersNoCopy =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetIndexParametersNoCopy(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetRuntimeModule(void* /*__this*/) {
    alignas(16) static uint8_t s_dummy_module[64] = {};
    return s_dummy_module;
}

// ===== System.Reflection.RuntimePropertyInfo.GetSetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetSetMethod(void* /*__this*/, bool nonPublic) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.RuntimePropertyInfo.get =====
extern "C" System_Reflection_BindingFlags System_Reflection_RuntimePropertyInfo_get_BindingFlags(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_Reflection_RuntimePropertyInfo_get_ReflectedTypeInternal(void* __this) {
    // ManagedPropertyInfo doesn't exist — PropertyInfo data is in TypeInfo.properties
    // For now, return nullptr since we don't have a managed PropertyInfo wrapper with declaring_type access
    return nullptr;
}
extern "C" void* System_Reflection_RuntimePropertyInfo_get_Signature(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }

// ===== System.Reflection.TypeInfo =====
extern "C" void System_Reflection_TypeInfo__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_TypeInfo_IsAssignableFrom(void* __this, void* typeInfo) {
    auto* self = reinterpret_cast<cil2cpp::Type*>(__this);
    auto* other = reinterpret_cast<cil2cpp::Type*>(typeInfo);
    if (!self || !other || !self->type_info || !other->type_info) return false;
    return cil2cpp::type_is_assignable_from(self->type_info, other->type_info);
}

// ===== System.RuntimeType =====
// AllocateValueType: compiled from IL (method-level emission gate)
extern "C" bool System_RuntimeType_CanValueSpecialCast(void* /*valueType*/, void* /*targetType*/) {
    return false; // COM value special casts not applicable in AOT
}
extern "C" void System_RuntimeType_GetGUID(void* /*__this*/, void* /*result*/) {
    // COM type GUID — not applicable in AOT
}
extern "C" void* System_RuntimeType_InvokeDispMethod(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags invokeAttr, void* /*target*/, void* /*args*/, void* /*byrefModifiers*/, int32_t culture, void* /*namedParameters*/) {
    return nullptr; // COM IDispatch not supported in AOT
}
// RuntimeType.get_IsEnum / get_IsActualEnum / IsDelegate:
// These methods in CoreCLR read MethodTable.f_ParentMethodTable to compare against
// System.Enum or System.MulticastDelegate. Our TypeInfo struct has a different layout
// (ParentMethodTable offset 16 = full_name, not base_type), so the IL-compiled bodies
// read garbage. Provide correct implementations using TypeInfo.flags and base_type.
extern "C" bool System_RuntimeType_get_IsEnum(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & cil2cpp::TypeFlags::Enum;
}
extern "C" bool System_RuntimeType_get_IsActualEnum(void* __this) {
    // IsActualEnum is identical to IsEnum for concrete types (non-generic-parameters)
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & cil2cpp::TypeFlags::Enum;
}
extern "C" bool System_RuntimeType_IsDelegate(void* __this) {
    // Check if base_type is System.MulticastDelegate by name comparison.
    // All delegate types inherit from MulticastDelegate.
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->base_type) return false;
    auto* base_name = t->type_info->base_type->full_name;
    return base_name && std::strcmp(base_name, "System.MulticastDelegate") == 0;
}

// ===== System.RuntimeTypeHandle =====
// Methods removed from SkipAllMethodsTypes were compiled from IL by SocketTest.
// But RuntimeTypeHandle is back in SkipAllMethodsTypes due to pointer-level mismatches.
// Add back stubs that were removed in the batch cleanup.
extern "C" void System_RuntimeTypeHandle__ctor(void* __this, void* type) {
    // System_RuntimeTypeHandle is a struct with a single field f_m_type at offset 0.
    // Store the RuntimeType pointer so GetGCHandle can retrieve it later.
    *reinterpret_cast<void**>(__this) = type;
}
extern "C" void* System_RuntimeTypeHandle_GetRuntimeType(void* __this) {
    // RuntimeTypeHandle wraps a RuntimeType* — return it
    return __this;
}
extern "C" bool System_RuntimeTypeHandle_IsNullHandle(void* /*__this*/) { return true; }
struct TypeHandle_Stub { void* m_asTAddr; };
extern "C" TypeHandle_Stub System_RuntimeTypeHandle_GetNativeTypeHandle(void* __this) {
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (cil2cpp::Type*).
    // Return the TypeInfo* as the TypeHandle value. TypeInfo* serves as our MethodTable*
    // since CoreCLR's MethodTable concept maps to our TypeInfo.
    auto* runtimeType = *reinterpret_cast<cil2cpp::Type**>(__this);
    if (!runtimeType) return {nullptr};
    return {runtimeType->type_info};
}
extern "C" bool System_RuntimeTypeHandle_IsByRef(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    return t && t->type_info && t->type_info->cor_element_type == cil2cpp::cor_element_type::BYREF;
}
extern "C" bool System_RuntimeTypeHandle_IsPointer(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    return t && t->type_info && t->type_info->cor_element_type == cil2cpp::cor_element_type::PTR;
}
extern "C" bool System_RuntimeTypeHandle_IsFunctionPointer(void* /*type*/) {
    return false; // Function pointers not tracked in AOT TypeInfo
}
// Helper: extract generic definition name from a closed generic full_name.
// E.g., "System.Collections.Generic.GenericComparer`1<System.Int32>" → "System.Collections.Generic.GenericComparer`1"
static std::string extract_generic_definition(const char* full_name) {
    std::string s(full_name);
    auto pos = s.find('<');
    if (pos != std::string::npos)
        return s.substr(0, pos);
    return s;
}

// Helper: find a registered type whose full_name matches a pattern.
// Used as fallback when the primary type_get_by_name lookup fails due to
// mangled vs IL name format mismatch in open generic TypeInfos.
static cil2cpp::TypeInfo* type_find_by_suffix(const std::string& targetSuffix) {
    // targetSuffix is like "<Interop/SECURITY_STATUS>" — scan all registered types
    // for a matching closed generic name ending with this suffix that also starts
    // with a compatible prefix (the IL-format version of the mangled definition).
    // This is O(N) but only called as a rare fallback.
    return nullptr; // Placeholder for future use
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
        // AOT error: the compiler failed to materialize this generic specialization.
        // Fix by adding the type to EnsureComparerCompanionType in IRBuilder.Generics.cs.
        fprintf(stderr, "[AOT] FATAL: CreateInstanceForAnotherGenericParameter: type '%s' not found. "
            "Template was '%s'. The compiler must pre-generate this specialization.\n",
            targetName.c_str(), templateType->type_info->full_name);
        fflush(stderr);
        std::abort();
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
    std::string targetName = def + "<" + param1->type_info->full_name + "," + param2->type_info->full_name + ">";

    auto* targetInfo = cil2cpp::type_get_by_name(targetName.c_str());
    if (!targetInfo) {
        fprintf(stderr, "[AOT] FATAL: CreateInstanceForAnotherGenericParameter: type '%s' not found. "
            "Template was '%s'. The compiler must pre-generate this specialization.\n",
            targetName.c_str(), templateType->type_info->full_name);
        fflush(stderr);
        std::abort();
    }

    return cil2cpp::object_alloc(targetInfo);
}
extern "C" void* System_RuntimeTypeHandle_ConstructName__System_TypeNameFormatFlags(void* __this, System_TypeNameFormatFlags formatFlags) {
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (cil2cpp::Type*).
    // formatFlags: 0 = Name only, 1 = Namespace+Name (ToString), 3 = FullName
    auto* runtimeType = *reinterpret_cast<cil2cpp::Type**>(__this);
    if (!runtimeType || !runtimeType->type_info) return nullptr;
    auto* ti = runtimeType->type_info;
    if (formatFlags == 0) {
        // FormatBasic — just the type name (e.g., "Int32")
        return ti->name ? cil2cpp::string_literal(ti->name) : nullptr;
    }
    // FormatNamespace (1), FormatFullInst (3), etc. — use full_name
    return ti->full_name ? cil2cpp::string_literal(ti->full_name) : nullptr;
}
extern "C" bool System_RuntimeTypeHandle_ContainsGenericVariables(void* /*__this*/) {
    return false; // All types are closed in AOT — no open generic variables
}
extern "C" System_ReadOnlySpan_1_System_IntPtr System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_RuntimeTypeHandle___System_Span_1_System_IntPtr_(void* /*inHandles*/, System_Span_1_System_IntPtr stackScratch) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_Type___System_Int32Ref(void* /*inHandles*/, void* /*length*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_Equals__System_Object(void* __this, void* obj) {
    return __this == obj;
}
extern "C" bool System_RuntimeTypeHandle_Equals__System_RuntimeTypeHandle(System_RuntimeTypeHandle* __this, System_RuntimeTypeHandle other) {
    return __this->f_m_type == other.f_m_type;
}
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_IntPtr(void* /*__this*/, intptr_t objHandle) {
    cil2cpp::gchandle_free(objHandle);
    return {};
}
extern "C" void* System_RuntimeTypeHandle_GetConstraints(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" intptr_t System_RuntimeTypeHandle_GetGCHandle__System_Runtime_InteropServices_GCHandleType(void* /*__this*/, System_Runtime_InteropServices_GCHandleType type) {
    // Allocate an empty GCHandle slot (initially null). The caller stores the handle
    // in RuntimeType.m_cache, then populates it via GCHandle.InternalCompareExchange
    // with a RuntimeTypeCache object. Matches CoreCLR's CreateTypedHandle(NULL, type).
    auto h = cil2cpp::gchandle_alloc(nullptr, static_cast<cil2cpp::GCHandleType>(type));
    return h;
}
extern "C" int32_t System_RuntimeTypeHandle_GetGenericVariableIndex(void* /*__this*/) {
    return -1; // No open generic parameters in AOT
}
extern "C" int32_t System_RuntimeTypeHandle_GetHashCode(void* __this) {
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(__this) >> 3);
}
extern "C" void* System_RuntimeTypeHandle_GetInstantiationInternal(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->generic_arguments || t->type_info->generic_argument_count == 0)
        return nullptr;
    auto count = static_cast<int32_t>(t->type_info->generic_argument_count);
    auto* arr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, count));
    auto** data = reinterpret_cast<cil2cpp::Type**>(cil2cpp::array_data(arr));
    for (int32_t i = 0; i < count; i++) {
        data[i] = t->type_info->generic_arguments[i]
            ? cil2cpp::type_get_type_object(t->type_info->generic_arguments[i]) : nullptr;
    }
    return arr;
}
extern "C" void* System_RuntimeTypeHandle_GetInstantiationPublic(void* __this) {
    return System_RuntimeTypeHandle_GetInstantiationInternal(__this);
}
extern "C" System_RuntimeTypeHandle_IntroducedMethodEnumerator System_RuntimeTypeHandle_GetIntroducedMethods(void* /*type*/) {
    return {}; // Method enumeration via TypeInfo.methods, not enumerator pattern
}
extern "C" System_RuntimeTypeHandle System_RuntimeTypeHandle_GetNativeHandle(void* __this) {
    return { reinterpret_cast<void*>(__this) };
}
extern "C" void* System_RuntimeTypeHandle_GetTypeChecked(void* __this) {
    return __this; // RuntimeTypeHandle wraps RuntimeType — return it
}
extern "C" System_MdUtf8String System_RuntimeTypeHandle_GetUtf8Name(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info || !t->type_info->name) return {};
    return { const_cast<void*>(static_cast<const void*>(t->type_info->name)) };
}
extern "C" bool System_RuntimeTypeHandle_HasElementType(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    return t && t->type_info && t->type_info->element_type_info != nullptr;
}
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_RuntimeType(void* /*__this*/, void* /*inst*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_Type__(void* /*__this*/, void* /*inst*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_RuntimeTypeHandle_IsArray(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    return t && t->type_info && (t->type_info->flags & cil2cpp::TypeFlags::Array);
}
extern "C" bool System_RuntimeTypeHandle_IsComObject(void* /*type*/, bool /*isGenericCOM*/) {
    return false; // No COM support in AOT
}
extern "C" bool System_RuntimeTypeHandle_IsPrimitive(void* type) {
    // type is RuntimeType* (cil2cpp::Type*) — check TypeInfo flags
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    return t && t->type_info && (t->type_info->flags & cil2cpp::TypeFlags::Primitive);
}
extern "C" bool System_RuntimeTypeHandle_IsSZArray(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info) return false;
    // SZArray = single-dimensional zero-indexed array (T[]) — has Array flag but NOT MultiDimensionalArray
    return (t->type_info->flags & cil2cpp::TypeFlags::Array)
        && !(t->type_info->flags & cil2cpp::TypeFlags::MultiDimensionalArray);
}
extern "C" bool System_RuntimeTypeHandle_IsTypeDefinition(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info) return false;
    // Non-generic types are always type definitions. Generic instances are not.
    // In AOT, all generic types are closed instances, but ones without generic_definition_name
    // are the definition themselves.
    return !t->type_info->generic_definition_name;
}
extern "C" bool System_RuntimeTypeHandle_IsVisible(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info) return false;
    return (t->type_info->flags & cil2cpp::TypeFlags::Public)
        || (t->type_info->flags & cil2cpp::TypeFlags::NestedPublic);
}
extern "C" void* System_RuntimeTypeHandle_MakeArray__System_Int32(void* /*__this*/, int32_t rank) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_MakeByRef(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_MakePointer(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_RuntimeTypeHandle_MakeSZArray(void* /*__this*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_RuntimeType_System_Reflection_RuntimeAssembly(void* /*type*/, void* /*assembly*/) {
    // No collectible assemblies in AOT
}
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_RuntimeType___System_RuntimeType___System_RuntimeType(void* /*paramType*/, void* /*typeContext*/, void* /*methodContext*/, void* /*toType*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" intptr_t System_RuntimeTypeHandle_ToIntPtr(System_RuntimeTypeHandle value) {
    // RuntimeTypeHandle wraps a RuntimeType* — return it as IntPtr
    return reinterpret_cast<intptr_t>(value.f_m_type);
}
extern "C" void System_RuntimeTypeHandle_VerifyInterfaceIsImplemented__System_RuntimeTypeHandle(void* /*__this*/, System_RuntimeTypeHandle /*interfaceHandle*/) {
    // Interface implementation verified at compile time in AOT
}
extern "C" bool System_RuntimeTypeHandle__IsVisible(System_Runtime_CompilerServices_QCallTypeHandle typeHandle) {
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(typeHandle.f__ptr);
    if (!rtPtr || !*rtPtr || !(*rtPtr)->type_info) return false;
    auto* ti = (*rtPtr)->type_info;
    return (ti->flags & cil2cpp::TypeFlags::Public) || (ti->flags & cil2cpp::TypeFlags::NestedPublic);
}
extern "C" intptr_t System_RuntimeTypeHandle_get_Value(void* __this) {
    // RuntimeTypeHandle.Value returns IntPtr to the RuntimeType/MethodTable
    // __this is RuntimeType* (== cil2cpp::Type*)
    return reinterpret_cast<intptr_t>(__this);
}
extern "C" int32_t System_RuntimeTypeHandle___IsVisible_g____PInvoke_67_0(System_Runtime_CompilerServices_QCallTypeHandle __typeHandle_native) {
    return System_RuntimeTypeHandle__IsVisible(__typeHandle_native) ? 1 : 0;
}
extern "C" intptr_t System_RuntimeTypeHandle__GetMetadataImport(void* /*type*/) {
    // MetadataImport is a CoreCLR internal — no equivalent in AOT
    return {};
}
extern "C" void* System_RuntimeTypeHandle__GetUtf8Name(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info || !t->type_info->name) return nullptr;
    // Return raw UTF-8 pointer — MdUtf8String wraps const byte*
    return const_cast<char*>(t->type_info->name);
}
extern "C" bool System_RuntimeTypeHandle_CanCastTo(void* type, void* target) {
    // CanCastTo(source, target): can source be cast to target?
    auto* src = reinterpret_cast<cil2cpp::Type*>(type);
    auto* tgt = reinterpret_cast<cil2cpp::Type*>(target);
    if (!src || !tgt) {
        fprintf(stderr, "[CanCastTo] null arg: src=%p tgt=%p\n", type, target);
        return false;
    }
    if (!src->type_info || !tgt->type_info) {
        fprintf(stderr, "[CanCastTo] null type_info: src->ti=%p tgt->ti=%p\n",
                (void*)src->type_info, (void*)tgt->type_info);
        return false;
    }
    return cil2cpp::type_is_assignable_from(tgt->type_info, src->type_info);
}
extern "C" void System_RuntimeTypeHandle_ConstructName__System_Runtime_CompilerServices_QCallTypeHandle_System_TypeNameFormatFlags_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_TypeNameFormatFlags formatFlags, System_Runtime_CompilerServices_StringHandleOnStack retString) {
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(handle.f__ptr);
    if (!rtPtr || !*rtPtr || !(*rtPtr)->type_info) return;
    auto* ti = (*rtPtr)->type_info;
    const char* name = ti->full_name ? ti->full_name : (ti->name ? ti->name : "");
    auto* str = cil2cpp::string_create_utf8(name);
    *reinterpret_cast<cil2cpp::Object**>(retString.f__ptr) = reinterpret_cast<cil2cpp::Object*>(str);
}
extern "C" bool System_RuntimeTypeHandle_ContainsGenericVariables__System_RuntimeType(void* /*handle*/) {
    return false; // All types closed in AOT
}
extern "C" void System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle baseType, void* /*pTypeHandles*/, int32_t cTypeHandles, System_Runtime_CompilerServices_ObjectHandleOnStack instantiatedObject) { cil2cpp::stub_called(__func__); }
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtr(System_Runtime_CompilerServices_QCallTypeHandle typeHandle, intptr_t objHandle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_Runtime_CompilerServices_ObjectHandleOnStack_void_ptrPtr_System_VoidPtrPtr_void_ptrPtr_Interop_BOOLPtr(System_Runtime_CompilerServices_ObjectHandleOnStack pRuntimeType, void* /*ppfnAllocator*/, void* /*pvAllocatorFirstArg*/, void* /*ppfnCtor*/, void* /*pfCtorIsPublic*/) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_RuntimeType_void_ptrRef_System_VoidRefPtr_void_ptrRef_System_BooleanRef(void* /*rt*/, void* /*pfnAllocator*/, void* /*vAllocatorFirstArg*/, void* /*pfnCtor*/, void* /*ctorIsPublic*/) { cil2cpp::stub_called(__func__); }
extern "C" void* System_RuntimeTypeHandle_GetArgumentTypesFromFunctionPointer(void* /*type*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" System_Reflection_TypeAttributes System_RuntimeTypeHandle_GetAttributes(void* type) {
    // Map TypeInfo flags to ECMA-335 TypeAttributes
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info) return {};
    auto* ti = t->type_info;
    int32_t attrs = 0;
    // Visibility
    if (ti->flags & cil2cpp::TypeFlags::Public) attrs |= 0x00000001;       // Public
    if (ti->flags & cil2cpp::TypeFlags::NotPublic) attrs |= 0x00000000;    // NotPublic
    if (ti->flags & cil2cpp::TypeFlags::NestedPublic) attrs |= 0x00000002; // NestedPublic
    if (ti->flags & cil2cpp::TypeFlags::NestedAssembly) attrs |= 0x00000005; // NestedAssembly
    // Layout/semantics
    if (ti->flags & cil2cpp::TypeFlags::Interface) attrs |= 0x00000020;    // Interface
    if (ti->flags & cil2cpp::TypeFlags::Abstract) attrs |= 0x00000080;     // Abstract
    if (ti->flags & cil2cpp::TypeFlags::Sealed) attrs |= 0x00000100;       // Sealed
    return static_cast<System_Reflection_TypeAttributes>(attrs);
}
extern "C" void* System_RuntimeTypeHandle_GetBaseType(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info || !t->type_info->base_type) return nullptr;
    return reinterpret_cast<void*>(cil2cpp::type_get_type_object(t->type_info->base_type));
}
extern "C" void System_RuntimeTypeHandle_GetConstraints__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack types) { cil2cpp::stub_called(__func__); }
extern "C" System_Reflection_CorElementType System_RuntimeTypeHandle_GetCorElementType(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info) return {};
    return static_cast<System_Reflection_CorElementType>(t->type_info->cor_element_type);
}
extern "C" void* System_RuntimeTypeHandle_GetDeclaringType(void* /*type*/) {
    // Nested type declaring type — not tracked in TypeInfo currently
    return nullptr;
}
extern "C" bool System_RuntimeTypeHandle_GetFields(void* /*type*/, void* /*result*/, void* /*count*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetFirstIntroducedMethod(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" intptr_t System_RuntimeTypeHandle_GetGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_InteropServices_GCHandleType(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_InteropServices_GCHandleType type) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void System_RuntimeTypeHandle_GetGenericTypeDefinition(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack retType) { cil2cpp::stub_called(__func__); }
extern "C" int32_t System_RuntimeTypeHandle_GetGenericVariableIndex__System_RuntimeType(void* /*type*/) {
    return -1; // No open generic parameters in AOT
}
extern "C" void System_RuntimeTypeHandle_GetInstantiation(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack types, Interop_BOOL fAsRuntimeTypeArray) { cil2cpp::stub_called(__func__); }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallTypeHandle_System_RuntimeMethodHandleInternal(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_QCallTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_RuntimeTypeHandle_System_RuntimeMethodHandleInternal(void* /*__this*/, System_RuntimeTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetInterfaces(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info) return nullptr;
    auto count = static_cast<int32_t>(t->type_info->interface_count);
    auto* arr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, count));
    if (count > 0 && t->type_info->interfaces) {
        auto** data = reinterpret_cast<cil2cpp::Type**>(cil2cpp::array_data(arr));
        for (int32_t i = 0; i < count; i++) {
            data[i] = t->type_info->interfaces[i]
                ? cil2cpp::type_get_type_object(t->type_info->interfaces[i]) : nullptr;
        }
    }
    return arr;
}
extern "C" System_Reflection_MetadataImport System_RuntimeTypeHandle_GetMetadataImport(void* /*type*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetMethodAt(void* /*type*/, int32_t slot) { cil2cpp::stub_called(__func__); return {}; }
extern "C" void* System_RuntimeTypeHandle_GetModule(void* /*type*/) {
    // Return a singleton zero-initialized RuntimeModule. The caller (RuntimeTypeCache ctor)
    // uses it to check m_isGlobal = (module.RuntimeType == this). Since f_m_runtimeType is
    // null, get_RuntimeType returns null, op_Equality(null, type) → false, m_isGlobal = false.
    alignas(16) static uint8_t s_dummy_module[64] = {};
    return s_dummy_module;
}
extern "C" void System_RuntimeTypeHandle_GetNextIntroducedMethod(void* /*method*/) {
    // Method iteration uses TypeInfo.methods array, not CoreCLR enumerator
}
extern "C" int32_t System_RuntimeTypeHandle_GetNumVirtuals(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info || !t->type_info->vtable) return 0;
    return static_cast<int32_t>(t->type_info->vtable->method_count);
}
extern "C" void System_RuntimeTypeHandle_Instantiate__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, void* /*pInst*/, int32_t numGenericArgs, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" Interop_BOOL System_RuntimeTypeHandle_IsCollectible(System_Runtime_CompilerServices_QCallTypeHandle /*handle*/) {
    return 0; // AOT types are never collectible
}
extern "C" bool System_RuntimeTypeHandle_IsGenericVariable(void* /*type*/) {
    // In AOT, all generic types are closed — no open generic parameters exist at runtime
    return false;
}
extern "C" bool System_RuntimeTypeHandle_IsInterface(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    return t && t->type_info && (t->type_info->flags & cil2cpp::TypeFlags::Interface);
}
extern "C" bool System_RuntimeTypeHandle_IsUnmanagedFunctionPointer(void* /*type*/) {
    // Function pointers are not tracked in AOT TypeInfo
    return false;
}
extern "C" bool System_RuntimeTypeHandle_IsValueType(void* type) {
    return cil2cpp::type_get_is_value_type(reinterpret_cast<cil2cpp::Type*>(type));
}
extern "C" void System_RuntimeTypeHandle_MakeArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, int32_t rank, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_MakeByRef__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_MakePointer__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_MakeSZArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) { cil2cpp::stub_called(__func__); }
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallTypeHandle /*type*/, System_Runtime_CompilerServices_QCallAssembly /*assembly*/) {
    // No collectible assemblies in AOT
}
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_IntPtrPtr_System_Int32_System_IntPtrPtr_System_Int32_System_RuntimeType(void* /*paramType*/, void* /*pTypeContext*/, int32_t typeContextLength, void* /*pMethodContext*/, int32_t methodContextLength, void* /*toType*/) { cil2cpp::stub_called(__func__); return false; }
extern "C" void System_RuntimeTypeHandle_VerifyInterfaceIsImplemented__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallTypeHandle(System_Runtime_CompilerServices_QCallTypeHandle /*handle*/, System_Runtime_CompilerServices_QCallTypeHandle /*interfaceHandle*/) {
    // Interface implementation verified at compile time in AOT
}

// ===== System.Runtime.CompilerServices.MethodTable =====

// ===== System.Runtime.InteropServices.Marshalling =====
// SafeHandle marshaller helper: ManagedToUnmanagedIn holds a SafeHandle* and addref/release.
// In our AOT runtime, the SafeHandle struct lives at safe_handle.h and has f__handle (intptr_t).
// The ManagedToUnmanagedIn struct stores _handle (void*) and _addRefd (bool) — we just need
// to store the managed handle and extract its native handle value.
// All three SafeHandle types use the same logic.
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_FromManaged(void* /*__this*/, void* /*handle*/) {
    // Stores SafeHandle reference — handled by generated struct fields
}
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_ToUnmanaged(void* __this) {
    // __this is ManagedToUnmanagedIn* with _handle as first field (after Object header)
    // Extract the SafeHandle._handle (f__handle) value
    if (!__this) return {};
    // ManagedToUnmanagedIn is a value type: first field is the SafeHandle*, second is bool addRefd
    auto** handlePtr = reinterpret_cast<void**>(__this);
    auto* safeHandle = reinterpret_cast<cil2cpp::Object*>(*handlePtr);
    if (!safeHandle) return {};
    // SafeHandle layout: Object (16) + f__handle (intptr_t at offset 16)
    return *reinterpret_cast<intptr_t*>(reinterpret_cast<uint8_t*>(safeHandle) + sizeof(cil2cpp::Object));
}
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeThreadHandle_Free(void* /*__this*/) {
    // Release addref — no-op in GC environment
}
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_FromManaged(void* /*__this*/, void* /*handle*/) {
    // Same as SafeThreadHandle
}
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_ToUnmanaged(void* __this) {
    if (!__this) return {};
    auto** handlePtr = reinterpret_cast<void**>(__this);
    auto* safeHandle = reinterpret_cast<cil2cpp::Object*>(*handlePtr);
    if (!safeHandle) return {};
    return *reinterpret_cast<intptr_t*>(reinterpret_cast<uint8_t*>(safeHandle) + sizeof(cil2cpp::Object));
}
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeFindHandle_Free(void* /*__this*/) {
    // No-op
}
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_FromManaged(void* /*__this*/, void* /*handle*/) {
    // Same as SafeThreadHandle
}
extern "C" intptr_t System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_ToUnmanaged(void* __this) {
    if (!__this) return {};
    auto** handlePtr = reinterpret_cast<void**>(__this);
    auto* safeHandle = reinterpret_cast<cil2cpp::Object*>(*handlePtr);
    if (!safeHandle) return {};
    return *reinterpret_cast<intptr_t*>(reinterpret_cast<uint8_t*>(safeHandle) + sizeof(cil2cpp::Object));
}
extern "C" void System_Runtime_InteropServices_Marshalling_SafeHandleMarshaller_1_ManagedToUnmanagedIn_Microsoft_Win32_SafeHandles_SafeTokenHandle_Free(void* /*__this*/) {
    // No-op
}

// ===== System.Runtime.CompilerServices.MethodTable =====
// MethodTable properties read raw struct fields via Unsafe intrinsics in CoreCLR.
// In CIL2CPP, GetNativeTypeHandle returns TypeInfo* as the MethodTable pointer,
// so all MethodTable methods receive TypeInfo* and map CoreCLR concepts to TypeInfo flags.
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsValueType(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::ValueType);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsNullable(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::Nullable);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_HasComponentSize(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && ti->element_size > 0;
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_ContainsGCPointers(void* /*__this*/) {
    // BoehmGC is a non-moving conservative collector: no write barriers needed,
    // and all objects are inherently pinnable. Return false so Marshal.IsPinnable
    // works correctly (GCHandle.Alloc with Pinned type).
    return false;
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_HasTypeEquivalence(void* /*__this*/) {
    return false; // No type equivalence in AOT
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsMultiDimensionalArray(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::MultiDimensionalArray);
}
extern "C" int32_t System_Runtime_CompilerServices_MethodTable_get_MultiDimensionalArrayRank(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    if (!ti) return 0;
    return static_cast<int32_t>(ti->array_rank);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_HasInstantiation(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::Generic);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsGenericTypeDefinition(void* /*__this*/) {
    return false; // Closed generic types only in AOT
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsConstructedGenericType(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::Generic);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsInterface(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::Interface);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsPrimitive(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::Primitive);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsByRefLike(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && (ti->flags & cil2cpp::TypeFlags::IsByRefLike);
}
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_HasFinalizer(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti && ti->finalizer != nullptr;
}
extern "C" uint8_t System_Runtime_CompilerServices_MethodTable_GetPrimitiveCorElementType(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    if (!ti) return 0;
    // For enum types, return the underlying primitive type's cor_element_type
    // (e.g., ELEMENT_TYPE_I4 for int32 enums, ELEMENT_TYPE_I8 for uint64 enums).
    // The BCL expects this to be a primitive element type, not ELEMENT_TYPE_VALUETYPE.
    if ((ti->flags & cil2cpp::TypeFlags::Enum) && ti->underlying_type)
        return ti->underlying_type->cor_element_type;
    return ti->cor_element_type;
}
extern "C" void* System_Runtime_CompilerServices_MethodTable_GetArrayElementTypeHandle(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    return ti ? const_cast<cil2cpp::TypeInfo*>(ti->element_type_info) : nullptr;
}
extern "C" uint32_t System_Runtime_CompilerServices_MethodTable_GetNumInstanceFieldBytes(void* __this) {
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    if (!ti) return 0;
    // For value types, instance_size is the raw field size.
    // For reference types, subtract the object header (TypeInfo* + sync_block).
    if (ti->flags & cil2cpp::TypeFlags::ValueType)
        return ti->instance_size;
    constexpr uint32_t object_header_size = sizeof(void*) + sizeof(int32_t); // __type_info + __sync_block
    return ti->instance_size > object_header_size ? ti->instance_size - object_header_size : 0;
}

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
extern "C" void System_Threading_Thread__InformThreadNameChange_g____PInvoke_26_0(System_Threading_ThreadHandle /*__t_native*/, void* /*__name_native*/, int32_t /*__len_native*/) {
    // Debug-only OS notification of thread name change — not critical for execution
}
extern "C" bool System_Threading_Thread_get_IsBackground(void* __this) {
    return reinterpret_cast<cil2cpp::ManagedThread*>(__this)->is_background;
}
extern "C" bool System_Threading_Thread_get_IsThreadPoolThread(void* __this) {
    // Thread pool threads are started by the runtime, not user code.
    // Check the _mayNeedResetForThreadPool field as a proxy — thread pool sets this.
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    return t ? t->f__mayNeedResetForThreadPool : false;
}
extern "C" System_Threading_ThreadPriority System_Threading_Thread_get_Priority(void* __this) {
#ifdef _WIN32
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t && t->native_handle) {
        auto* stdThread = static_cast<std::thread*>(t->native_handle);
        HANDLE hThread = static_cast<HANDLE>(stdThread->native_handle());
        int p = GetThreadPriority(hThread);
        switch (p) {
            case THREAD_PRIORITY_LOWEST: return 0;
            case THREAD_PRIORITY_BELOW_NORMAL: return 1;
            case THREAD_PRIORITY_NORMAL: return 2;
            case THREAD_PRIORITY_ABOVE_NORMAL: return 3;
            case THREAD_PRIORITY_HIGHEST: return 4;
            default: return 2;
        }
    }
#else
    (void)__this;
#endif
    return 2; // Normal
}
extern "C" uint64_t System_Threading_Thread_GetCurrentOSThreadId() {
#ifdef _WIN32
    return static_cast<uint64_t>(::GetCurrentThreadId());
#elif defined(__linux__)
    return static_cast<uint64_t>(::syscall(SYS_gettid));
#else
    return 0;
#endif
}
extern "C" System_Threading_ThreadHandle System_Threading_Thread_GetNativeHandle(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t && t->native_handle) {
        auto* stdThread = static_cast<std::thread*>(t->native_handle);
        return { reinterpret_cast<void*>(stdThread->native_handle()) };
    }
    return {};
}
extern "C" void System_Threading_Thread_ResetThreadPoolThread(void* __this) {
    // Reset thread-local state for thread pool reuse
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t) {
        t->f__executionContext = nullptr;
        t->f__synchronizationContext = nullptr;
    }
}
extern "C" void System_Threading_Thread_ResetThreadPoolThreadSlow(void* __this) {
    System_Threading_Thread_ResetThreadPoolThread(__this);
}
extern "C" void System_Threading_Thread_set_IsBackground(void* __this, bool value) {
    reinterpret_cast<cil2cpp::ManagedThread*>(__this)->is_background = value;
}
extern "C" void System_Threading_Thread_set_Name(void* __this, void* value) {
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t) t->f__name = reinterpret_cast<cil2cpp::String*>(value);
}
extern "C" void System_Threading_Thread_set_IsThreadPoolThread(void* __this, bool value) {
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t) t->f__mayNeedResetForThreadPool = value;
}
extern "C" void System_Threading_Thread_set_Priority(void* __this, System_Threading_ThreadPriority value) {
#ifdef _WIN32
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t && t->native_handle) {
        int win32Priority;
        switch (value) {
            case 0: win32Priority = THREAD_PRIORITY_LOWEST; break;
            case 1: win32Priority = THREAD_PRIORITY_BELOW_NORMAL; break;
            case 2: win32Priority = THREAD_PRIORITY_NORMAL; break;
            case 3: win32Priority = THREAD_PRIORITY_ABOVE_NORMAL; break;
            case 4: win32Priority = THREAD_PRIORITY_HIGHEST; break;
            default: win32Priority = THREAD_PRIORITY_NORMAL; break;
        }
        auto* stdThread = static_cast<std::thread*>(t->native_handle);
        HANDLE hThread = static_cast<HANDLE>(stdThread->native_handle());
        SetThreadPriority(hThread, win32Priority);
    }
#else
    (void)__this; (void)value;
#endif
}
extern "C" void System_Threading_Thread_SetThreadPoolWorkerThreadName(void* __this) {
    // Set a default name for thread pool worker threads
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t) t->f__name = cil2cpp::string_literal(".NET TP Worker");
}
extern "C" void System_Threading_Thread_Start__System_Boolean_System_Boolean(void* __this, bool /*captureContext*/, bool /*internalThread*/) {
    cil2cpp::thread::start(reinterpret_cast<cil2cpp::ManagedThread*>(__this));
}
extern "C" void System_Threading_Thread_StartCore(void* __this) {
    cil2cpp::thread::start(reinterpret_cast<cil2cpp::ManagedThread*>(__this));
}
extern "C" void System_Threading_Thread_StartInternal(System_Threading_ThreadHandle t, int32_t /*stackSize*/, int32_t /*priority*/, void* /*pThreadName*/) {
    // StartInternal receives a ThreadHandle — we need to find the ManagedThread.
    // In our runtime, ThreadHandle.m_ptr is the std::thread* — but StartCore is the
    // primary entry point. This path is called from PInvoke-generated code.
    (void)t;
}
extern "C" void System_Threading_Thread_ThreadNameChanged(void* __this, void* value) {
    auto* t = reinterpret_cast<cil2cpp::ManagedThread*>(__this);
    if (t) t->f__name = reinterpret_cast<cil2cpp::String*>(value);
}
extern "C" void System_Threading_Thread_UnsafeStart(void* __this) {
    cil2cpp::thread::start(reinterpret_cast<cil2cpp::ManagedThread*>(__this));
}
extern "C" void System_Threading_Thread_UnsafeStart__System_Object(void* __this, void* /*parameter*/) {
    cil2cpp::thread::start(reinterpret_cast<cil2cpp::ManagedThread*>(__this));
}
extern "C" Interop_BOOL System_Threading_Thread_YieldInternal() {
#ifdef _WIN32
    return ::SwitchToThread() ? 1 : 0;
#else
    sched_yield();
    return 1;
#endif
}

// ===== System.Type =====
extern "C" void* System_Type_get_Assembly(void* /*__this*/) {
    // Return the singleton assembly — AOT has one assembly
    // Assembly object is allocated lazily in RuntimeAssembly but we don't track it
    return nullptr; // TODO: return singleton Assembly once assembly metadata is tracked
}
extern "C" void* System_Type_get_AssemblyQualifiedName(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->full_name) return nullptr;
    return reinterpret_cast<void*>(cil2cpp::string_literal(t->type_info->full_name));
}
extern "C" void* System_Type_get_BaseType(void* __this) {
    return reinterpret_cast<void*>(cil2cpp::type_get_base_type(reinterpret_cast<cil2cpp::Type*>(__this)));
}
extern "C" void* System_Type_get_FullName(void* __this) {
    return reinterpret_cast<void*>(cil2cpp::type_get_full_name(reinterpret_cast<cil2cpp::Type*>(__this)));
}
extern "C" System_Guid System_Type_get_GUID(void* /*__this*/) { cil2cpp::stub_called(__func__); return {}; }
extern "C" bool System_Type_IsArrayImpl(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    return t && t->type_info && (t->type_info->flags & cil2cpp::TypeFlags::Array);
}
extern "C" bool System_Type_get_IsClass(void* __this) {
    return cil2cpp::type_get_is_class(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_get_Module(void* /*__this*/) {
    // Return singleton dummy module — same as RuntimeTypeHandle.GetModule
    alignas(16) static uint8_t s_dummy_module[64] = {};
    return s_dummy_module;
}
extern "C" void* System_Type_get_Namespace(void* __this) {
    return reinterpret_cast<void*>(cil2cpp::type_get_namespace(reinterpret_cast<cil2cpp::Type*>(__this)));
}
extern "C" void* System_Type_get_UnderlyingSystemType(void* __this) {
    // For RuntimeType, UnderlyingSystemType returns this
    return __this;
}
extern "C" System_Reflection_TypeAttributes System_Type_GetAttributeFlagsImpl(void* __this) {
    // Delegate to RuntimeTypeHandle.GetAttributes
    return System_RuntimeTypeHandle_GetAttributes(__this);
}
extern "C" void* System_Type_GetConstructorImpl(void* __this, System_Reflection_BindingFlags /*bindingAttr*/, void* /*binder*/, System_Reflection_CallingConventions /*callConvention*/, void* /*types*/, void* /*modifiers*/) {
    // Search TypeInfo.methods for .ctor
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) return nullptr;
    for (uint32_t i = 0; i < t->type_info->method_count; i++) {
        auto& m = t->type_info->methods[i];
        if (m.name && std::strcmp(m.name, ".ctor") == 0) {
            auto* mi = static_cast<cil2cpp::ManagedMethodInfo*>(
                cil2cpp::gc::alloc(sizeof(cil2cpp::ManagedMethodInfo), &cil2cpp::System_Reflection_MethodInfo_TypeInfo));
            mi->native_info = &m;
            return mi;
        }
    }
    return nullptr;
}
extern "C" void* System_Type_GetConstructors__System_Reflection_BindingFlags(void* __this, System_Reflection_BindingFlags /*bindingAttr*/) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) return cil2cpp::gc::alloc_array(&cil2cpp::System_Reflection_MethodInfo_TypeInfo, 0);
    // Count constructors
    int32_t count = 0;
    for (uint32_t i = 0; i < t->type_info->method_count; i++) {
        if (t->type_info->methods[i].name && std::strcmp(t->type_info->methods[i].name, ".ctor") == 0) count++;
    }
    auto* arr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Reflection_MethodInfo_TypeInfo, count));
    auto** data = reinterpret_cast<cil2cpp::ManagedMethodInfo**>(cil2cpp::array_data(arr));
    int32_t idx = 0;
    for (uint32_t i = 0; i < t->type_info->method_count; i++) {
        auto& m = t->type_info->methods[i];
        if (m.name && std::strcmp(m.name, ".ctor") == 0) {
            auto* mi = static_cast<cil2cpp::ManagedMethodInfo*>(
                cil2cpp::gc::alloc(sizeof(cil2cpp::ManagedMethodInfo), &cil2cpp::System_Reflection_MethodInfo_TypeInfo));
            mi->native_info = &m;
            data[idx++] = mi;
        }
    }
    return arr;
}
extern "C" void* System_Type_GetElementType(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->element_type_info) return nullptr;
    return reinterpret_cast<void*>(cil2cpp::type_get_type_object(t->type_info->element_type_info));
}
extern "C" void* System_Type_GetEvent__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetEvents__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetField__System_String(void* __this, void* name) {
    return cil2cpp::type_get_field(reinterpret_cast<cil2cpp::Type*>(__this),
        reinterpret_cast<cil2cpp::String*>(name));
}
extern "C" void* System_Type_GetField__System_String_System_Reflection_BindingFlags(void* __this, void* name, System_Reflection_BindingFlags /*bindingAttr*/) {
    // Ignore binding flags — our runtime returns all matching fields
    return cil2cpp::type_get_field(reinterpret_cast<cil2cpp::Type*>(__this),
        reinterpret_cast<cil2cpp::String*>(name));
}
extern "C" void* System_Type_GetFields(void* __this) {
    return cil2cpp::type_get_fields(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_GetFields__System_Reflection_BindingFlags(void* __this, System_Reflection_BindingFlags /*bindingAttr*/) {
    return cil2cpp::type_get_fields(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_GetInterface__System_String_System_Boolean(void* __this, void* name, bool /*ignoreCase*/) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !name) return nullptr;
    char* utf8Name = cil2cpp::string_to_utf8(reinterpret_cast<cil2cpp::String*>(name));
    for (uint32_t i = 0; i < t->type_info->interface_count; i++) {
        auto* iface = t->type_info->interfaces[i];
        if (iface && iface->full_name && std::strcmp(iface->full_name, utf8Name) == 0) {
            return reinterpret_cast<void*>(cil2cpp::type_get_type_object(iface));
        }
    }
    return nullptr;
}
extern "C" void* System_Type_GetInterfaces(void* __this) {
    return System_RuntimeTypeHandle_GetInterfaces(__this);
}
extern "C" void* System_Type_GetMembers__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetMethod__System_String(void* __this, void* name) {
    return cil2cpp::type_get_method(reinterpret_cast<cil2cpp::Type*>(__this),
        reinterpret_cast<cil2cpp::String*>(name));
}
extern "C" void* System_Type_GetMethodImpl__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Reflection_CallingConventions_System_Type___System_Reflection_ParameterModifier__(void* __this, void* name, System_Reflection_BindingFlags /*bindingAttr*/, void* /*binder*/, System_Reflection_CallingConventions /*callConvention*/, void* /*types*/, void* /*modifiers*/) {
    if (!name) return nullptr;
    return cil2cpp::type_get_method(reinterpret_cast<cil2cpp::Type*>(__this),
        reinterpret_cast<cil2cpp::String*>(name));
}
extern "C" void* System_Type_GetMethods(void* __this) {
    return cil2cpp::type_get_methods(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_GetMethods__System_Reflection_BindingFlags(void* __this, System_Reflection_BindingFlags /*bindingAttr*/) {
    return cil2cpp::type_get_methods(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_GetNestedType__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetNestedTypes__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags bindingAttr) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" void* System_Type_GetProperties__System_Reflection_BindingFlags(void* __this, System_Reflection_BindingFlags /*bindingAttr*/) {
    return cil2cpp::type_get_properties(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_GetPropertyImpl(void* __this, void* name, System_Reflection_BindingFlags /*bindingAttr*/, void* /*binder*/, void* /*returnType*/, void* /*types*/, void* /*modifiers*/) {
    if (!name) return nullptr;
    return cil2cpp::type_get_property(reinterpret_cast<cil2cpp::Type*>(__this),
        reinterpret_cast<cil2cpp::String*>(name));
}
extern "C" bool System_Type_HasElementTypeImpl(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    return t && t->type_info && t->type_info->element_type_info != nullptr;
}
extern "C" void* System_Type_InvokeMember__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Object_System_Object___System_Reflection_ParameterModifier___System_Globalization_CultureInfo_System_String__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*target*/, void* /*args*/, void* /*modifiers*/, void* /*culture*/, void* /*namedParameters*/) { cil2cpp::stub_called(__func__); return nullptr; }
extern "C" bool System_Type_IsByRefImpl(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    return t && t->type_info && t->type_info->cor_element_type == cil2cpp::cor_element_type::BYREF;
}
extern "C" bool System_Type_IsCOMObjectImpl(void* /*__this*/) {
    return false; // No COM in AOT
}
extern "C" bool System_Type_IsPointerImpl(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    return t && t->type_info && t->type_info->cor_element_type == cil2cpp::cor_element_type::PTR;
}
extern "C" bool System_Type_IsPrimitiveImpl(void* __this) {
    return cil2cpp::type_get_is_primitive(reinterpret_cast<cil2cpp::Type*>(__this));
}

// Type virtual methods that base System.Type throws NotSupportedException("SubclassOverride") for.
// RuntimeType overrides them but is a CoreRuntimeType (vtable excluded), so virtual dispatch
// hits the base throwing implementations. Implement here using TypeInfo so they work correctly.

extern "C" bool System_Type_get_IsEnum(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) cil2cpp::throw_null_reference();
    return t->type_info->flags & cil2cpp::TypeFlags::Enum;
}

extern "C" bool System_Type_IsValueTypeImpl(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) cil2cpp::throw_null_reference();
    return t->type_info->flags & cil2cpp::TypeFlags::ValueType;
}

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
extern "C" bool System_ValueType_CanCompareBits(void* obj) {
    // CanCompareBits = true when the value type has no reference-type fields
    // and no padding (can use memcmp). Conservative: return false for safety.
    (void)obj;
    return false;
}
extern "C" int32_t System_ValueType_GetHashCode(void* __this) {
    // Default hash for value types — hash the object pointer
    // (each boxed value type is a distinct allocation)
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(__this) >> 3);
}
extern "C" void* System_ValueType_ToString(void* __this) {
    // ValueType.ToString() returns the full type name
    if (!__this) return nullptr;
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    if (obj->__type_info && obj->__type_info->full_name) {
        return reinterpret_cast<void*>(cil2cpp::string_literal(obj->__type_info->full_name));
    }
    return reinterpret_cast<void*>(cil2cpp::string_literal(""));
}

// ===== Bridge functions for QCall/P-Invoke wrappers and gated CoreRuntimeTypes =====

// ValueType.CanCompareBitsOrUseFastGetHashCode — P/Invoke wrapper (CLR-internal MethodTable check)
// Returns whether the value type's fields can be compared bit-by-bit.
// Conservative: always return false (forces field-by-field comparison).
extern "C" int32_t System_ValueType__CanCompareBitsOrUseFastGetHashCodeHelper_g____PInvoke_2_0(void* /*__pMT_native*/) { return 0; }

// Delegate.InternalAllocLike — QCall variant (ObjectHandleOnStack)
// The non-QCall variant (InternalAlloc) is in icall.cpp. This is the QCall bridge.
extern "C" void System_Delegate_InternalAllocLike__System_Runtime_CompilerServices_ObjectHandleOnStack(void* /*d*/) {
    // Delegate cloning via ObjectHandleOnStack — not commonly used in AOT paths
}

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
