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
#include <mutex>
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
#include <cil2cpp/assembly.h>
#include <cil2cpp/mdarray.h>
#include <cil2cpp/boxing.h>

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

// ===== Singleton helpers for Module and Assembly =====
// AOT has a single module and assembly. These are lazily initialized, thread-safe.
static cil2cpp::Object* s_singleton_module = nullptr;
static std::once_flag s_module_once;

static cil2cpp::Object* get_singleton_module() {
    std::call_once(s_module_once, []() {
        // Use RuntimeModule TypeInfo's instance_size for correct allocation.
        // If RuntimeModule hasn't been registered yet, fall back to Assembly TypeInfo's size.
        auto* module_ti = cil2cpp::type_get_by_name("System.Reflection.RuntimeModule");
        auto* ti = module_ti ? module_ti : &cil2cpp::System_Reflection_Assembly_TypeInfo;
        size_t alloc_size = ti->instance_size;
        if (alloc_size == 0) {
            cil2cpp::stub_called("get_singleton_module: TypeInfo has zero instance_size");
            alloc_size = sizeof(cil2cpp::Object) * 4;
        }
        s_singleton_module = static_cast<cil2cpp::Object*>(
            cil2cpp::gc::alloc(alloc_size, ti));
    });
    return s_singleton_module;
}

static cil2cpp::ManagedAssembly* s_singleton_assembly = nullptr;
static std::once_flag s_assembly_once;

// RuntimeAssembly TypeInfo is defined in generated code (base_type = Assembly_TypeInfo).
// BCL code casts Assembly → RuntimeAssembly, so the singleton must use this TypeInfo.
static cil2cpp::TypeInfo* s_runtime_assembly_ti = nullptr;

static cil2cpp::ManagedAssembly* get_singleton_assembly() {
    std::call_once(s_assembly_once, []() {
        auto* ti = s_runtime_assembly_ti ? s_runtime_assembly_ti
                                         : &cil2cpp::System_Reflection_Assembly_TypeInfo;
        s_singleton_assembly = static_cast<cil2cpp::ManagedAssembly*>(
            cil2cpp::gc::alloc(sizeof(cil2cpp::ManagedAssembly), ti));
        s_singleton_assembly->name = cil2cpp::string_create_utf8("AOTAssembly");
    });
    return s_singleton_assembly;
}

extern "C" void cil2cpp_set_runtime_assembly_type_info(cil2cpp::TypeInfo* ti) {
    s_runtime_assembly_ti = ti;
}

// SafeHandle field layout — struct access instead of raw pointer arithmetic
struct SafeHandleLayout : cil2cpp::Object {
    intptr_t _handle;       // f__handle — first field after Object header
    int32_t  _state;        // Combined state bits
    bool     _ownsHandle;
    bool     _fullyInitialized;
};

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

extern "C" int32_t System_Array_GetUpperBound(void* __this, int32_t dimension) {
    auto* arr = reinterpret_cast<cil2cpp::Array*>(__this);
    if (!arr) cil2cpp::throw_null_reference();
    if (dimension != 0) cil2cpp::throw_index_out_of_range(); // SZArray: only dimension 0
    return static_cast<int32_t>(cil2cpp::array_length(arr)) - 1;
}
extern "C" void* System_Array_GetEnumerator(void* __this) {
    // Array.GetEnumerator() — create SZGenericArrayEnumerator for element type
    auto* arr = reinterpret_cast<cil2cpp::Array*>(__this);
    if (!arr) cil2cpp::throw_null_reference();
    const char* elem_name = arr->element_type ? arr->element_type->full_name : nullptr;
    cil2cpp::TypeInfo* enum_type = nullptr;
    if (elem_name) {
        std::string type_name = "System.SZGenericArrayEnumerator`1<";
        type_name += elem_name;
        type_name += ">";
        enum_type = cil2cpp::type_get_by_name(type_name.c_str());
    }
    if (!enum_type) {
        enum_type = cil2cpp::type_get_by_name("System.SZGenericArrayEnumerator`1<System.Object>");
    }
    if (!enum_type) cil2cpp::throw_not_supported();
    // SZGenericArrayEnumerator layout: [Object header] _index(int32), _endIndex(int32), _array(Array*)
    auto* obj = cil2cpp::object_alloc(enum_type);
    auto* base = reinterpret_cast<uint8_t*>(obj);
    size_t off = sizeof(cil2cpp::Object); // after header
    *reinterpret_cast<int32_t*>(base + off) = -1;           // _index
    *reinterpret_cast<int32_t*>(base + off + 4) = static_cast<int32_t>(arr->length); // _endIndex
    *reinterpret_cast<cil2cpp::Array**>(base + off + 8) = arr; // _array
    return obj;
}

// ===== System.DefaultBinder =====
extern "C" void System_DefaultBinder__ctor(void* /*__this*/) { }
extern "C" void* System_DefaultBinder_BindToField(void* /*__this*/, System_Reflection_BindingFlags /*bindingAttr*/, void* /*match*/, void* /*value*/, void* /*cultureInfo*/) { cil2cpp::throw_not_supported(); }
extern "C" void* System_DefaultBinder_BindToMethod(void* /*__this*/, System_Reflection_BindingFlags /*bindingAttr*/, void* /*match*/, void* /*args*/, void* /*modifiers*/, void* /*cultureInfo*/, void* /*names*/, void* /*state*/) { cil2cpp::throw_not_supported(); }
extern "C" void* System_DefaultBinder_ChangeType(void* /*__this*/, void* /*value*/, void* /*type*/, void* /*cultureInfo*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_DefaultBinder_ReorderArgumentArray(void* /*__this*/, void* /*args*/, void* /*state*/) { }
extern "C" void* System_DefaultBinder_SelectMethod(void* /*__this*/, System_Reflection_BindingFlags /*bindingAttr*/, void* /*match*/, void* /*types*/, void* /*modifiers*/) { cil2cpp::throw_not_supported(); }
extern "C" void* System_DefaultBinder_SelectProperty(void* /*__this*/, System_Reflection_BindingFlags /*bindingAttr*/, void* /*match*/, void* /*returnType*/, void* /*indexes*/, void* /*modifiers*/) { cil2cpp::throw_not_supported(); }

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
extern "C" System_Exception_DispatchState System_Exception_CaptureDispatchState(void* __this) {
    // Capture exception dispatch state from Exception fields
    if (!__this) return {};
    auto* ex = reinterpret_cast<cil2cpp::Exception*>(__this);
    return {
        reinterpret_cast<void*>(ex->f__stackTrace),
        reinterpret_cast<void*>(ex->f__remoteStackTraceString),
        reinterpret_cast<void*>(ex->f__source)
    };
}
extern "C" void* System_Exception_GetClassName(void* __this) {
    if (!__this) return nullptr;
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* ti = obj->__type_info;
    if (ti && ti->full_name) return reinterpret_cast<void*>(cil2cpp::string_literal(ti->full_name));
    return nullptr;
}
extern "C" void System_Exception_GetMessageFromNativeResources__System_Exception_ExceptionMessageKind_System_Runtime_CompilerServices_StringHandleOnStack(System_Exception_ExceptionMessageKind kind, System_Runtime_CompilerServices_StringHandleOnStack retMesg) {
    // Native resource strings are not available in AOT. Return a generic message based on kind.
    const char* msg = "";
    switch (kind) {
        case 1: msg = "Out of memory."; break;           // OutOfMemory
        case 2: msg = "Arithmetic operation resulted in an overflow."; break; // Arithmetic
        case 3: msg = "An error occurred during execution."; break; // General
        default: msg = "An exception occurred."; break;
    }
    if (retMesg.f__ptr) {
        *reinterpret_cast<cil2cpp::Object**>(retMesg.f__ptr) =
            reinterpret_cast<cil2cpp::Object*>(cil2cpp::string_create_utf8(msg));
    }
}
extern "C" void System_Exception_GetStackTracesDeepCopy(void* /*exception*/, void* /*currentStackTrace*/, void* /*dynamicMethodArray*/) {
    // Stack trace deep copy for async exception chains — no-op is architecturally correct
}
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
extern "C" void* System_Reflection_Assembly_get_FullName(void* /*__this*/) {
    return reinterpret_cast<void*>(cil2cpp::string_literal("AOTAssembly, Version=1.0.0.0"));
}
extern "C" uint32_t System_Reflection_Assembly_GetAssemblyCount() { return 1; }
extern "C" void* System_Reflection_Assembly_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* attributeType, bool /*inherit*/) {
    auto* typeObj = reinterpret_cast<cil2cpp::Type*>(attributeType);
    auto* elemTypeInfo = (typeObj && typeObj->type_info) ? typeObj->type_info : &cil2cpp::System_Object_TypeInfo;
    return cil2cpp::gc::alloc_array(elemTypeInfo, 0);
}
extern "C" void System_Reflection_Assembly_GetEntryAssemblyNative(System_Runtime_CompilerServices_ObjectHandleOnStack retAssembly) {
    if (retAssembly.f__ptr) *reinterpret_cast<void**>(retAssembly.f__ptr) = get_singleton_assembly();
}
extern "C" void System_Reflection_Assembly_GetExecutingAssemblyNative(System_Runtime_CompilerServices_StackCrawlMarkHandle /*stackMark*/, System_Runtime_CompilerServices_ObjectHandleOnStack retAssembly) {
    if (retAssembly.f__ptr) *reinterpret_cast<void**>(retAssembly.f__ptr) = get_singleton_assembly();
}
extern "C" void* System_Reflection_Assembly_GetManifestResourceNames(void* /*__this*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_String_TypeInfo, 0);
}
extern "C" void* System_Reflection_Assembly_GetManifestResourceStream__System_String(void* /*__this*/, void* /*name*/) {
    cil2cpp::throw_not_supported(); // No embedded resources in AOT
}
extern "C" void* System_Reflection_Assembly_get_Location(void* /*__this*/) {
    return reinterpret_cast<void*>(cil2cpp::string_literal(""));
}
extern "C" void* System_Reflection_Assembly_GetName(void* /*__this*/) {
    cil2cpp::throw_not_supported(); // AssemblyName requires complex struct — not tracked in AOT
}
extern "C" void* System_Reflection_Assembly_GetName__System_Boolean(void* /*__this*/, bool /*copiedName*/) {
    cil2cpp::throw_not_supported(); // AssemblyName requires complex struct — not tracked in AOT
}
extern "C" void* System_Reflection_Assembly_GetType__System_String_System_Boolean_System_Boolean(void* /*__this*/, void* name, bool throwOnError, bool /*ignoreCase*/) {
    if (!name) return nullptr;
    auto* nameStr = reinterpret_cast<cil2cpp::String*>(name);
    char* utf8 = cil2cpp::string_to_utf8(nameStr);
    auto* ti = cil2cpp::type_get_by_name(utf8);
    if (!ti && throwOnError) cil2cpp::throw_argument();
    return ti ? reinterpret_cast<void*>(cil2cpp::type_get_type_object(ti)) : nullptr;
}

extern "C" bool System_Reflection_Assembly_get_IsDynamic(void* /*__this*/) {
    return false; // AOT assemblies are never dynamic
}
extern "C" void* System_Reflection_Assembly_GetType__System_String(void* /*__this*/, void* name) {
    if (!name) return nullptr;
    auto* nameStr = reinterpret_cast<cil2cpp::String*>(name);
    char* utf8 = cil2cpp::string_to_utf8(nameStr);
    auto* ti = cil2cpp::type_get_by_name(utf8);
    return ti ? reinterpret_cast<void*>(cil2cpp::type_get_type_object(ti)) : nullptr;
}
extern "C" void* System_Reflection_Assembly_LoadFile(void* /*path*/) {
    cil2cpp::throw_not_supported(); // AOT: cannot load assemblies at runtime
}
extern "C" void* System_Reflection_Assembly_GetModules(void* __this) {
    // Return a single-element Module[] with the singleton module
    auto* moduleArr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Reflection_Assembly_TypeInfo, 1));
    auto** data = reinterpret_cast<void**>(cil2cpp::array_data(moduleArr));
    data[0] = get_singleton_module();
    return moduleArr;
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
extern "C" void* System_Reflection_FieldInfo_GetRawConstantValue(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (!fi || !fi->native_info) return nullptr;
    auto* native = fi->native_info;
    bool is_literal = cil2cpp::metadata::field_is_literal(native->flags);
    if (is_literal && native->field_type) {
        auto elem_size = native->field_type->element_size;
        if (elem_size == 0) elem_size = native->field_type->instance_size;
        return cil2cpp::box_raw(&native->constant_value, elem_size, native->field_type);
    }
    return nullptr;
}
extern "C" void* System_Reflection_FieldInfo_GetValue(void* __this, void* obj) {
    return cil2cpp::fieldinfo_get_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj));
}
extern "C" void System_Reflection_FieldInfo_SetValue__System_Object_System_Object_System_Reflection_BindingFlags_System_Reflection_Binder_System_Globalization_CultureInfo(void* __this, void* obj, void* value, System_Reflection_BindingFlags invokeAttr, void* /*binder*/, void* /*culture*/) {
    cil2cpp::fieldinfo_set_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Object*>(value));
}
extern "C" void System_Reflection_FieldInfo_SetValue__System_Object_System_Object(void* __this, void* obj, void* value) {
    cil2cpp::fieldinfo_set_value(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this), reinterpret_cast<cil2cpp::Object*>(obj), reinterpret_cast<cil2cpp::Object*>(value));
}
extern "C" System_RuntimeFieldHandle System_Reflection_FieldInfo_get_FieldHandle(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (fi && fi->native_info) return { { fi->native_info } };
    return {};
}
extern "C" bool System_Reflection_FieldInfo_get_IsStatic(void* __this) {
    return cil2cpp::fieldinfo_get_is_static(reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this));
}
extern "C" bool System_Reflection_FieldInfo_get_IsPublic(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (!fi || !fi->native_info) return false;
    return cil2cpp::metadata::field_is_public(fi->native_info->flags);
}
extern "C" bool System_Reflection_FieldInfo_get_IsPrivate(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (!fi || !fi->native_info) return false;
    return cil2cpp::metadata::field_is_private(fi->native_info->flags);
}
extern "C" bool System_Reflection_FieldInfo_get_IsInitOnly(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (!fi || !fi->native_info) return false;
    return cil2cpp::metadata::field_is_init_only(fi->native_info->flags);
}
extern "C" bool System_Reflection_FieldInfo_get_IsLiteral(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (!fi || !fi->native_info) return false;
    return cil2cpp::metadata::field_is_literal(fi->native_info->flags);
}
extern "C" bool System_Reflection_FieldInfo_get_IsSpecialName(void* __this) {
    auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
    if (!fi || !fi->native_info) return false;
    return cil2cpp::metadata::field_is_special_name(fi->native_info->flags);
}

// ===== System.Reflection.MemberInfo =====
extern "C" void System_Reflection_MemberInfo__ctor(void* /*__this*/) { }
extern "C" bool System_Reflection_MemberInfo_CacheEquals(void* __this, void* o) {
    return __this == o; // Reference equality for cached member info objects
}
extern "C" bool System_Reflection_MemberInfo_Equals(void* __this, void* obj) {
    if (__this == obj) return true;
    if (!__this || !obj) return false;
    // Compare by underlying native_info pointer — different managed wrappers
    // for the same native PropertyInfo/MethodInfo/FieldInfo should be equal.
    // native_info is the first field after Object header in all ManagedXxxInfo types.
    auto* a = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* b = reinterpret_cast<cil2cpp::Object*>(obj);
    if (a->__type_info != b->__type_info) {
        return false;
    }
    auto* a_native = *reinterpret_cast<void**>(reinterpret_cast<char*>(a) + sizeof(cil2cpp::Object));
    auto* b_native = *reinterpret_cast<void**>(reinterpret_cast<char*>(b) + sizeof(cil2cpp::Object));
    return a_native == b_native;
}
extern "C" void* System_Reflection_MemberInfo_get_DeclaringType(void* __this) {
    return cil2cpp::memberinfo_get_declaring_type(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" bool System_Reflection_MemberInfo_get_IsCollectible(void* /*__this*/) {
    return false; // AOT types are never collectible
}
// MemberTypes enum constants (ECMA-335)
namespace member_types {
    constexpr int32_t kConstructor = 1;
    constexpr int32_t kEvent       = 2;
    constexpr int32_t kField       = 4;
    constexpr int32_t kMethod      = 8;
    constexpr int32_t kProperty    = 16;
    constexpr int32_t kTypeInfo    = 32;
}

// Check if ti's full_name matches any of the given names.
// Used instead of TypeInfo pointer comparison because runtime-defined externs and
// generated TypeInfos can be different symbols for the same logical type.
static bool ti_name_matches(cil2cpp::TypeInfo* ti, std::initializer_list<const char*> names) {
    if (!ti || !ti->full_name) return false;
    for (auto* name : names) {
        if (std::strcmp(ti->full_name, name) == 0) return true;
    }
    return false;
}

static bool ti_is_field_info(cil2cpp::TypeInfo* ti) {
    return ti_name_matches(ti, {"System.Reflection.FieldInfo", "System.Reflection.RuntimeFieldInfo"});
}
static bool ti_is_constructor_info(cil2cpp::TypeInfo* ti) {
    return ti_name_matches(ti, {"System.Reflection.ConstructorInfo", "System.Reflection.RuntimeConstructorInfo"});
}
static bool ti_is_method_info(cil2cpp::TypeInfo* ti) {
    return ti_name_matches(ti, {"System.Reflection.MethodInfo", "System.Reflection.RuntimeMethodInfo",
                                "System.Reflection.MethodBase"});
}
static bool ti_is_event_info(cil2cpp::TypeInfo* ti) {
    return ti_name_matches(ti, {"System.Reflection.EventInfo", "System.Reflection.RuntimeEventInfo"});
}
static bool ti_is_property_info(cil2cpp::TypeInfo* ti) {
    return ti_name_matches(ti, {"System.Reflection.PropertyInfo", "System.Reflection.RuntimePropertyInfo"});
}
static bool ti_is_type(cil2cpp::TypeInfo* ti) {
    return ti_name_matches(ti, {"System.Type", "System.RuntimeType"});
}

extern "C" System_Reflection_MemberTypes System_Reflection_MemberInfo_get_MemberType(void* __this) {
    if (!__this) return {};
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* ti = obj->__type_info;
    if (!ti) return {};
    // Dispatch by full_name exact match (not fragile substring matching).
    // ConstructorInfo must be checked before MethodInfo (both share MethodBase ancestry).
    if (ti_is_field_info(ti)) return member_types::kField;
    if (ti_is_constructor_info(ti)) return member_types::kConstructor;
    if (ti_is_method_info(ti)) return member_types::kMethod;
    if (ti_is_property_info(ti)) return member_types::kProperty;
    if (ti_is_event_info(ti)) return member_types::kEvent;
    if (ti_is_type(ti)) return member_types::kTypeInfo;
    return 0;
}
extern "C" int32_t System_Reflection_MemberInfo_get_MetadataToken(void* __this) {
    if (!__this) return 0;
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* ti = obj->__type_info;
    if (!ti) return 0;
    // For Type objects, return the type's metadata token
    if (ti_is_type(ti)) {
        auto* type = reinterpret_cast<cil2cpp::Type*>(__this);
        if (type->type_info) return static_cast<int32_t>(type->type_info->metadata_token);
    }
    return 0;
}
extern "C" void* System_Reflection_MemberInfo_get_Module(void* /*__this*/) {
    return get_singleton_module();
}
extern "C" void* System_Reflection_MemberInfo_get_Name(void* __this) {
    return cil2cpp::memberinfo_get_name(reinterpret_cast<cil2cpp::Object*>(__this));
}
extern "C" void* System_Reflection_MemberInfo_get_ReflectedType(void* __this) {
    // ReflectedType == DeclaringType for our purposes
    return cil2cpp::memberinfo_get_declaring_type(reinterpret_cast<cil2cpp::Object*>(__this));
}
// Helper: extract custom_attributes/count from a member based on its runtime type
static bool _get_member_custom_attrs(void* __this, cil2cpp::CustomAttributeInfo*& attrs, uint32_t& count) {
    attrs = nullptr;
    count = 0;
    if (!__this) return false;
    auto* obj = reinterpret_cast<cil2cpp::Object*>(__this);
    auto* ti = obj->__type_info;
    if (!ti) return false;
    if (ti_is_property_info(ti)) {
        auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
        if (pi->native_info) {
            attrs = pi->native_info->custom_attributes;
            count = pi->native_info->custom_attribute_count;
        }
    } else if (ti_is_field_info(ti)) {
        auto* fi = reinterpret_cast<cil2cpp::ManagedFieldInfo*>(__this);
        if (fi->native_info) { attrs = fi->native_info->custom_attributes; count = fi->native_info->custom_attribute_count; }
    } else if (ti_is_method_info(ti)) {
        auto* mi = reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this);
        if (mi->native_info) { attrs = mi->native_info->custom_attributes; count = mi->native_info->custom_attribute_count; }
    } else if (ti_is_type(ti)) {
        auto* type = reinterpret_cast<cil2cpp::Type*>(__this);
        if (type->type_info) { attrs = type->type_info->custom_attributes; count = type->type_info->custom_attribute_count; }
    }
    return count > 0 && attrs != nullptr;
}

// Marshal a CustomAttributeArg to a void*-sized value for constructor invocation.
// String args → managed String*; integer/enum/bool → reinterpret to pointer-sized.
static void* _marshal_attr_arg(const cil2cpp::CustomAttributeArg& arg) {
    if (arg.type_name && std::strcmp(arg.type_name, "System.String") == 0)
        return cil2cpp::string_literal(arg.string_val ? arg.string_val : "");
    return reinterpret_cast<void*>(static_cast<intptr_t>(arg.int_val));
}

// Helper: construct a managed attribute instance from CustomAttributeInfo.
// Supports constructors with 0-8 parameters (covers all standard .NET attributes).
static void* _construct_attribute(cil2cpp::CustomAttributeInfo& ai) {
    if (!ai.attribute_type) return nullptr;
    auto* attrTi = ai.attribute_type;
    if (!attrTi->instance_size) return nullptr;

    // Allocate instance
    auto* instance = cil2cpp::gc::alloc(attrTi->instance_size, attrTi);
    if (!instance) return nullptr;

    // Find matching .ctor
    auto* ctor = cil2cpp::find_method_info(attrTi, ".ctor", ai.arg_count);
    if (!ctor || !ctor->method_pointer) {
        // No matching constructor — try parameterless .ctor as fallback
        if (ai.arg_count > 0)
            ctor = cil2cpp::find_method_info(attrTi, ".ctor", 0);
        if (!ctor || !ctor->method_pointer)
            return instance; // Return uninitialized instance (best effort)
    }

    // Marshal all arguments to pointer-sized values.
    // args[] is zero-initialized — unused trailing slots are nullptr.
    constexpr uint32_t kMaxAttrArgs = 8;
    void* args[kMaxAttrArgs] = {};
    uint32_t argc = ctor->parameter_count < kMaxAttrArgs ? ctor->parameter_count : kMaxAttrArgs;
    if (ctor->parameter_count > kMaxAttrArgs) {
        cil2cpp::stub_called("_construct_attribute: constructor has >8 parameters (unsupported)");
    }
    for (uint32_t i = 0; i < argc && i < ai.arg_count; i++) {
        args[i] = _marshal_attr_arg(ai.args[i]);
    }

    // On x64, all parameter types (int32, int64, pointer) fit in 8-byte register/stack slots.
    // Calling with extra void* args beyond what the callee declares is safe on x64 ABI —
    // the callee only reads its declared parameters, ignoring extras on the stack/in registers.
    // This avoids a parameter-count-specific switch and handles any argc <= kMaxAttrArgs.
    auto fn = reinterpret_cast<void(*)(void*, void*, void*, void*, void*, void*, void*, void*, void*)>(
        ctor->method_pointer);
    fn(instance, args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
    return instance;
}

extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Boolean(void* __this, bool /*inherit*/) {
    cil2cpp::CustomAttributeInfo* attrs;
    uint32_t count;
    if (!_get_member_custom_attrs(__this, attrs, count)) {
        return cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, 0);
    }
    // Construct all attributes
    auto* arr = cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, count);
    auto** data = reinterpret_cast<void**>(reinterpret_cast<char*>(arr) + sizeof(cil2cpp::Array));
    uint32_t filled = 0;
    for (uint32_t i = 0; i < count; i++) {
        auto* obj = _construct_attribute(attrs[i]);
        if (obj) data[filled++] = obj;
    }
    // If some failed, return shorter array
    if (filled < count) {
        auto* result = cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, filled);
        auto** rdata = reinterpret_cast<void**>(reinterpret_cast<char*>(result) + sizeof(cil2cpp::Array));
        std::memcpy(rdata, data, filled * sizeof(void*));
        return result;
    }
    return arr;
}
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributes__System_Type_System_Boolean(void* __this, void* attributeType, bool /*inherit*/) {
    auto* typeObj = reinterpret_cast<cil2cpp::Type*>(attributeType);
    auto* elemTypeInfo = (typeObj && typeObj->type_info) ? typeObj->type_info : &cil2cpp::System_Object_TypeInfo;

    cil2cpp::CustomAttributeInfo* attrs;
    uint32_t count;
    if (!_get_member_custom_attrs(__this, attrs, count)) {
        return cil2cpp::gc::alloc_array(elemTypeInfo, 0);
    }

    // Count matching attributes
    const char* targetName = elemTypeInfo->full_name;
    uint32_t matchCount = 0;
    for (uint32_t i = 0; i < count; i++) {
        if (attrs[i].attribute_type_name && targetName &&
            std::strcmp(attrs[i].attribute_type_name, targetName) == 0)
            matchCount++;
    }
    if (matchCount == 0)
        return cil2cpp::gc::alloc_array(elemTypeInfo, 0);

    // Construct matching attribute instances
    auto* arr = cil2cpp::gc::alloc_array(elemTypeInfo, matchCount);
    auto** data = reinterpret_cast<void**>(reinterpret_cast<char*>(arr) + sizeof(cil2cpp::Array));
    uint32_t idx = 0;
    for (uint32_t i = 0; i < count && idx < matchCount; i++) {
        if (attrs[i].attribute_type_name && targetName &&
            std::strcmp(attrs[i].attribute_type_name, targetName) == 0) {
            auto* obj = _construct_attribute(attrs[i]);
            data[idx++] = obj ? obj : cil2cpp::gc::alloc(elemTypeInfo->instance_size, elemTypeInfo);
        }
    }
    return arr;
}
extern "C" void* System_Reflection_MemberInfo_GetCustomAttributesData(void* /*__this*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, 0);
}
extern "C" int32_t System_Reflection_MemberInfo_GetHashCode(void* __this) {
    // Hash by native_info pointer for consistency with Equals
    if (!__this) return 0;
    auto* native = *reinterpret_cast<void**>(reinterpret_cast<char*>(__this) + sizeof(cil2cpp::Object));
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(native) >> 3);
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
extern "C" bool System_Reflection_MemberInfo_IsDefined(void* __this, void* attributeType, bool /*inherit*/) {
    if (!__this || !attributeType) return false;
    auto* attrType = reinterpret_cast<cil2cpp::Type*>(attributeType);
    if (!attrType || !attrType->type_info || !attrType->type_info->full_name) return false;
    const char* targetName = attrType->type_info->full_name;

    cil2cpp::CustomAttributeInfo* attrs = nullptr;
    uint32_t count = 0;
    if (!_get_member_custom_attrs(__this, attrs, count)) return false;

    for (uint32_t i = 0; i < count; i++) {
        if (attrs[i].attribute_type_name && std::strcmp(attrs[i].attribute_type_name, targetName) == 0)
            return true;
    }
    return false;
}

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
    if (ni && cil2cpp::metadata::method_is_static(ni->flags)) return 1; // Static → Standard
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
    return ni && cil2cpp::metadata::method_is_public(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsStatic(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_static(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsFinal(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_final(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsSpecialName(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_special_name(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsHideBySig(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_hide_by_sig(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsPrivate(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_private(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsFamily(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_family(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsFamilyOrAssembly(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_family_or_assembly(ni->flags);
}
extern "C" bool System_Reflection_MethodBase_get_IsFamilyAndAssembly(void* __this) {
    auto* ni = _get_native_mi(__this);
    return ni && cil2cpp::metadata::method_is_family_and_assembly(ni->flags);
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
extern "C" void System_Reflection_MethodInfo__ctor(void* /*__this*/) { }
extern "C" void* System_Reflection_MethodInfo_CreateDelegate__System_Type_System_Object(void* /*__this*/, void* /*delegateType*/, void* /*target*/) {
    // Runtime delegate creation from MethodInfo is not supported in AOT
    cil2cpp::throw_not_supported();
}
extern "C" void* System_Reflection_MethodInfo_CreateDelegate_System_Text_RegularExpressions_CompiledRegexRunner_ScanDelegate(void* /*__this*/) {
    // CompiledRegex delegate creation not supported in AOT
    cil2cpp::throw_not_supported();
}
extern "C" int32_t System_Reflection_MethodInfo_get_GenericParameterCount(void* /*__this*/) {
    return 0; // All methods are closed (fully specialized) in AOT
}
extern "C" void* System_Reflection_MethodInfo_get_ReturnType(void* __this) {
    return cil2cpp::methodinfo_get_return_type(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}
extern "C" void* System_Reflection_MethodInfo_GetGenericMethodDefinition(void* /*__this*/) {
    // Open generic method definitions do not exist in AOT — all methods are closed specializations
    cil2cpp::throw_not_supported();
}
extern "C" void* System_Reflection_MethodInfo_MakeGenericMethod(void* /*__this*/, void* /*typeArguments*/) {
    // Runtime generic method instantiation is not supported in AOT compilation
    cil2cpp::throw_not_supported();
}
extern "C" void* System_Reflection_MethodInfo_get_ReturnTypeCustomAttributes(void* /*__this*/) {
    // Return type custom attributes not tracked in AOT
    cil2cpp::throw_not_supported();
}
extern "C" void* System_Reflection_MethodInfo_GetBaseDefinition(void* __this) {
    return __this; // In AOT, return self — base definition walking via GetParentDefinition
}

extern "C" void* System_Reflection_MethodInfo_get_ReturnParameter(void* __this) {
    // Create a ParameterInfo wrapping the return type with position -1
    auto* mi = reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this);
    if (!mi || !mi->native_info) return nullptr;
    auto* pi = static_cast<cil2cpp::ManagedParameterInfo*>(
        cil2cpp::object_alloc(&cil2cpp::System_Reflection_ParameterInfo_TypeInfo));
    pi->name = nullptr; // Return parameter has no name
    pi->param_type = mi->native_info->return_type;
    pi->position = -1;  // ECMA-335: return parameter has position -1
    return pi;
}

// ===== System.Reflection.ParameterInfo =====
extern "C" void System_Reflection_ParameterInfo__ctor(void* /*__this*/) { }
extern "C" System_Reflection_ParameterAttributes System_Reflection_ParameterInfo_get_Attributes(void* /*__this*/) { return 0; /* ParameterAttributes.None */ }
extern "C" void* System_Reflection_ParameterInfo_get_DefaultValue(void* /*__this*/) { return nullptr; /* No default values tracked */ }
extern "C" bool System_Reflection_ParameterInfo_get_HasDefaultValue(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsIn(void* /*__this*/) { return false; /* No flags tracked */ }
extern "C" bool System_Reflection_ParameterInfo_get_IsOptional(void* /*__this*/) { return false; }
extern "C" bool System_Reflection_ParameterInfo_get_IsOut(void* /*__this*/) { return false; }
extern "C" void* System_Reflection_ParameterInfo_get_Member(void* /*__this*/) { return nullptr; /* Declaring method not tracked */ }
extern "C" int32_t System_Reflection_ParameterInfo_get_MetadataToken(void* /*__this*/) { return 0; }
extern "C" void* System_Reflection_ParameterInfo_get_Name(void* __this) {
    return cil2cpp::parameterinfo_get_name(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" void* System_Reflection_ParameterInfo_get_ParameterType(void* __this) {
    return cil2cpp::parameterinfo_get_parameter_type(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" int32_t System_Reflection_ParameterInfo_get_Position(void* __this) {
    return cil2cpp::parameterinfo_get_position(reinterpret_cast<cil2cpp::ManagedParameterInfo*>(__this));
}
extern "C" void* System_Reflection_ParameterInfo_GetCustomAttributes__System_Boolean(void* /*__this*/, bool /*inherit*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, 0);
}
extern "C" void* System_Reflection_ParameterInfo_GetCustomAttributes__System_Type_System_Boolean(void* /*__this*/, void* attributeType, bool /*inherit*/) {
    auto* typeObj = reinterpret_cast<cil2cpp::Type*>(attributeType);
    auto* elemTypeInfo = (typeObj && typeObj->type_info) ? typeObj->type_info : &cil2cpp::System_Object_TypeInfo;
    return cil2cpp::gc::alloc_array(elemTypeInfo, 0);
}
extern "C" void* System_Reflection_ParameterInfo_GetCustomAttributesData(void* /*__this*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, 0);
}
extern "C" bool System_Reflection_ParameterInfo_IsDefined(void* /*__this*/, void* /*attributeType*/, bool /*inherit*/) { return false; }

// ===== System.Reflection.RuntimeAssembly. =====
extern "C" int32_t System_Reflection_RuntimeAssembly__GetCodeBase_g____PInvoke_14_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, System_Runtime_CompilerServices_StringHandleOnStack /*__retString_native*/) { cil2cpp::throw_not_supported(); }
extern "C" int32_t System_Reflection_RuntimeAssembly__GetManifestResourceInfo_g____PInvoke_60_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, void* /*__resourceName_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__assemblyRef_native*/, System_Runtime_CompilerServices_StringHandleOnStack /*__retFileName_native*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly__GetModule_g____PInvoke_52_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, void* /*__name_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__retModule_native*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly__GetModules_g____PInvoke_90_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, int32_t /*__loadIfNotFound_native*/, int32_t /*__getResourceModules_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__retModuleHandles_native*/) { cil2cpp::throw_not_supported(); }
extern "C" void* System_Reflection_RuntimeAssembly__GetResource_g____PInvoke_37_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, void* /*__resourceName_native*/, void* /*__length_native*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly__GetTypeCore_g____PInvoke_26_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, void* /*__typeName_native*/, void* /*__nestedTypeNames_native*/, int32_t /*__nestedTypeNamesLength_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__retType_native*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly__GetTypeCoreIgnoreCase_g____PInvoke_27_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, void* /*__typeName_native*/, void* /*__nestedTypeNames_native*/, int32_t /*__nestedTypeNamesLength_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__retType_native*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly__GetVersion_g____PInvoke_72_0(System_Runtime_CompilerServices_QCallAssembly /*__assembly_native*/, void* /*__majVer_native*/, void* /*__minVer_native*/, void* /*__buildNum_native*/, void* /*__revNum_native*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly__InternalLoad_g____PInvoke_49_0(void* /*__pAssemblyNameParts_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__requestingAssembly_native*/, System_Runtime_CompilerServices_StackCrawlMarkHandle /*__stackMark_native*/, int32_t /*__throwOnFileNotFound_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__assemblyLoadContext_native*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*__retAssembly_native*/) {
    // Runtime assembly loading is not supported in AOT compilation
    cil2cpp::throw_not_supported();
}

// ===== System.Reflection.RuntimeAssembly.GetEntryPoint =====
extern "C" void System_Reflection_RuntimeAssembly_GetEntryPoint(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*retMethod*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetExportedTypes =====
extern "C" void System_Reflection_RuntimeAssembly_GetExportedTypes__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*retTypes*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetFlags =====
extern "C" System_Reflection_AssemblyNameFlags System_Reflection_RuntimeAssembly_GetFlags(void* /*__this*/) { return 0; }
extern "C" System_Reflection_AssemblyNameFlags System_Reflection_RuntimeAssembly_GetFlags__System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallAssembly /*assembly*/) { return 0; }

// ===== System.Reflection.RuntimeAssembly.GetForwardedType =====
extern "C" void System_Reflection_RuntimeAssembly_GetForwardedType(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Reflection_MetadataToken /*mdtExternalType*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*type*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetFullName =====
extern "C" void System_Reflection_RuntimeAssembly_GetFullName(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_StringHandleOnStack /*retString*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetHashAlgorithm =====
extern "C" System_Configuration_Assemblies_AssemblyHashAlgorithm System_Reflection_RuntimeAssembly_GetHashAlgorithm__System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallAssembly /*assembly*/) { return 0; }

// ===== System.Reflection.RuntimeAssembly.GetImageRuntimeVersion =====
extern "C" void System_Reflection_RuntimeAssembly_GetImageRuntimeVersion(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_StringHandleOnStack /*retString*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetIsCollectible =====
extern "C" Interop_BOOL System_Reflection_RuntimeAssembly_GetIsCollectible(System_Runtime_CompilerServices_QCallAssembly /*assembly*/) {
    return 0; // AOT assemblies are never collectible
}

// ===== System.Reflection.RuntimeAssembly.GetLocale =====
extern "C" void System_Reflection_RuntimeAssembly_GetLocale__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_StringHandleOnStack /*retString*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetLocation =====
extern "C" void System_Reflection_RuntimeAssembly_GetLocation(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_StringHandleOnStack /*retString*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetPublicKey =====
extern "C" void* System_Reflection_RuntimeAssembly_GetPublicKey(void* /*__this*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly_GetPublicKey__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*retPublicKey*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetSimpleName =====
extern "C" void* System_Reflection_RuntimeAssembly_GetSimpleName(void* /*__this*/) { cil2cpp::throw_not_supported(); }
extern "C" void System_Reflection_RuntimeAssembly_GetSimpleName__System_Runtime_CompilerServices_QCallAssembly_System_Runtime_CompilerServices_StringHandleOnStack(System_Runtime_CompilerServices_QCallAssembly /*assembly*/, System_Runtime_CompilerServices_StringHandleOnStack /*retSimpleName*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetTypeCore =====
extern "C" void* System_Reflection_RuntimeAssembly_GetTypeCore__System_String_System_ReadOnlySpan_1_System_String__System_Boolean_System_Boolean(void* /*__this*/, void* /*typeName*/, System_ReadOnlySpan_1_System_String /*nestedTypeNames*/, bool /*throwOnError*/, bool /*ignoreCase*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.GetVersion =====
extern "C" void* System_Reflection_RuntimeAssembly_GetVersion(void* /*__this*/) { cil2cpp::throw_not_supported(); }

// ===== System.Reflection.RuntimeAssembly.InternalGetSatelliteAssembly =====
// (provided by generated code: compiled from IL or linker stub)

// ===== System.Reflection.RuntimeConstructorInfo. =====
extern "C" void* System_Reflection_RuntimeConstructorInfo__get_Signature_g__LazyCreateSignature_21_0(void* /*__this*/) {
    // Signature is a CoreCLR JIT internal — not applicable in AOT
    cil2cpp::throw_not_supported();
}

// ===== System.Reflection.RuntimeConstructorInfo.ComputeAndUpdateInvocationFlags =====
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeConstructorInfo_ComputeAndUpdateInvocationFlags(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (!ni) return {};
    int32_t flags = 1; // INVOCATION_FLAGS_INITIALIZED
    if (!cil2cpp::metadata::method_is_public(ni->flags)) flags |= 2; // Non-public
    return static_cast<System_Reflection_InvocationFlags>(flags);
}

// ===== System.Reflection.RuntimeConstructorInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeConstructorInfo_GetRuntimeModule(void* /*__this*/) {
    return get_singleton_module();
}

// ===== System.Reflection.RuntimeConstructorInfo.InvokeClassConstructor =====
extern "C" void System_Reflection_RuntimeConstructorInfo_InvokeClassConstructor(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (!ni || !ni->declaring_type) return;
    auto* ti = ni->declaring_type;
    for (uint32_t i = 0; i < ti->method_count; i++) {
        if (ti->methods[i].name && std::strcmp(ti->methods[i].name, ".cctor") == 0 && ti->methods[i].method_pointer) {
            auto fn = reinterpret_cast<void(*)()>(ti->methods[i].method_pointer);
            fn();
            return;
        }
    }
}

// ===== System.Reflection.RuntimeConstructorInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeConstructorInfo_ThrowNoInvokeException(void* /*__this*/) {
    cil2cpp::throw_not_supported();
}

// ===== System.Reflection.RuntimeConstructorInfo.get =====
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ArgumentTypes(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (!ni) return cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, 0);
    auto count = static_cast<int32_t>(ni->parameter_count);
    auto* arr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, count));
    if (count > 0 && ni->parameter_types) {
        auto** data = reinterpret_cast<cil2cpp::Type**>(cil2cpp::array_data(arr));
        for (int32_t i = 0; i < count; i++) {
            data[i] = ni->parameter_types[i] ? cil2cpp::type_get_type_object(ni->parameter_types[i]) : nullptr;
        }
    }
    return arr;
}
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeConstructorInfo_get_InvocationFlags(void* __this) {
    return System_Reflection_RuntimeConstructorInfo_ComputeAndUpdateInvocationFlags(__this);
}
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Invoker(void* /*__this*/) {
    // MethodInvoker is a CoreCLR JIT internal — not applicable in AOT
    cil2cpp::throw_not_supported();
}
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_ReflectedTypeInternal(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (ni && ni->declaring_type) {
        return reinterpret_cast<void*>(cil2cpp::type_get_type_object(ni->declaring_type));
    }
    return nullptr;
}
extern "C" void* System_Reflection_RuntimeConstructorInfo_get_Signature(void* /*__this*/) {
    // Signature is a CoreCLR JIT internal — not applicable in AOT
    cil2cpp::throw_not_supported();
}

// ===== System.Reflection.RuntimeFieldInfo =====
extern "C" void System_Reflection_RuntimeFieldInfo__ctor(void* /*__this*/, void* /*reflectedTypeCache*/, void* /*declaringType*/, System_Reflection_BindingFlags bindingFlags) { }

// ===== System.Reflection.RuntimeFieldInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeFieldInfo_GetRuntimeModule(void* /*__this*/) {
    return get_singleton_module();
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
extern "C" void* System_Reflection_RuntimeMethodInfo__get_Signature_g__LazyCreateSignature_25_0(void* /*__this*/) {
    // Signature is a CoreCLR JIT internal — not applicable in AOT
    cil2cpp::throw_not_supported();
}

// ===== System.Reflection.RuntimeMethodInfo.ComputeAndUpdateInvocationFlags =====
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeMethodInfo_ComputeAndUpdateInvocationFlags(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (!ni) return {};
    int32_t flags = 1; // INVOCATION_FLAGS_INITIALIZED
    if (!cil2cpp::metadata::method_is_public(ni->flags)) flags |= 2; // Non-public
    return static_cast<System_Reflection_InvocationFlags>(flags);
}

// ===== System.Reflection.RuntimeMethodInfo.CreateDelegateInternal =====
extern "C" void* System_Reflection_RuntimeMethodInfo_CreateDelegateInternal(void* /*__this*/, void* /*delegateType*/, void* /*firstArgument*/, System_DelegateBindingFlags /*bindingFlags*/) {
    return nullptr; // Runtime delegate creation not commonly used in AOT
}

// ===== System.Reflection.RuntimeMethodInfo.FetchNonReturnParameters =====
extern "C" void* System_Reflection_RuntimeMethodInfo_FetchNonReturnParameters(void* __this) {
    // Same as GetParameters — returns ParameterInfo[] for non-return parameters
    return cil2cpp::methodinfo_get_parameters(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this));
}

// ===== System.Reflection.RuntimeMethodInfo.GetParentDefinition =====
extern "C" void* System_Reflection_RuntimeMethodInfo_GetParentDefinition(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (!ni || !ni->declaring_type || !ni->declaring_type->base_type) return nullptr;
    auto* baseTi = ni->declaring_type->base_type;
    for (uint32_t i = 0; i < baseTi->method_count; i++) {
        auto& bm = baseTi->methods[i];
        if (bm.name && ni->name && std::strcmp(bm.name, ni->name) == 0 && bm.parameter_count == ni->parameter_count) {
            auto* mi = static_cast<cil2cpp::ManagedMethodInfo*>(
                cil2cpp::gc::alloc(sizeof(cil2cpp::ManagedMethodInfo), &cil2cpp::System_Reflection_MethodInfo_TypeInfo));
            mi->native_info = &bm;
            return mi;
        }
    }
    return nullptr;
}

// ===== System.Reflection.RuntimeMethodInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimeMethodInfo_GetRuntimeModule(void* /*__this*/) {
    return get_singleton_module();
}

// ===== System.Reflection.RuntimeMethodInfo.InvokePropertySetter =====
extern "C" void System_Reflection_RuntimeMethodInfo_InvokePropertySetter(void* __this, void* obj, System_Reflection_BindingFlags /*invokeAttr*/, void* /*binder*/, void* parameter, void* /*culture*/) {
    // parameter is a single Object, wrap in a single-element array and invoke
    auto* ni = _get_native_mi(__this);
    if (!ni || !ni->method_pointer) return;
    auto* paramArr = static_cast<cil2cpp::Array*>(cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, 1));
    auto** data = reinterpret_cast<void**>(cil2cpp::array_data(paramArr));
    data[0] = parameter;
    cil2cpp::methodinfo_invoke(reinterpret_cast<cil2cpp::ManagedMethodInfo*>(__this),
        reinterpret_cast<cil2cpp::Object*>(obj), paramArr);
}

// ===== System.Reflection.RuntimeMethodInfo.ThrowNoInvokeException =====
extern "C" void System_Reflection_RuntimeMethodInfo_ThrowNoInvokeException(void* /*__this*/) {
    cil2cpp::throw_not_supported();
}

// ===== System.Reflection.RuntimeMethodInfo.get =====
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ArgumentTypes(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (!ni) return cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, 0);
    auto count = static_cast<int32_t>(ni->parameter_count);
    auto* arr = static_cast<cil2cpp::Array*>(
        cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, count));
    if (count > 0 && ni->parameter_types) {
        auto** data = reinterpret_cast<cil2cpp::Type**>(cil2cpp::array_data(arr));
        for (int32_t i = 0; i < count; i++) {
            data[i] = ni->parameter_types[i] ? cil2cpp::type_get_type_object(ni->parameter_types[i]) : nullptr;
        }
    }
    return arr;
}
extern "C" System_Reflection_InvocationFlags System_Reflection_RuntimeMethodInfo_get_InvocationFlags(void* __this) {
    return System_Reflection_RuntimeMethodInfo_ComputeAndUpdateInvocationFlags(__this);
}
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Invoker(void* /*__this*/) {
    // MethodInvoker is a CoreCLR JIT internal — not applicable in AOT
    cil2cpp::throw_not_supported();
}
extern "C" void* System_Reflection_RuntimeMethodInfo_get_ReflectedTypeInternal(void* __this) {
    auto* ni = _get_native_mi(__this);
    if (ni && ni->declaring_type) {
        return reinterpret_cast<void*>(cil2cpp::type_get_type_object(ni->declaring_type));
    }
    return nullptr;
}
extern "C" void* System_Reflection_RuntimeMethodInfo_get_Signature(void* /*__this*/) {
    // Signature is a CoreCLR JIT internal — not applicable in AOT
    cil2cpp::throw_not_supported();
}

// ===== System.Reflection.RuntimePropertyInfo.get_CanRead =====
extern "C" bool System_Reflection_RuntimePropertyInfo_get_CanRead(void* __this) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    return cil2cpp::propertyinfo_can_read(pi);
}

// ===== System.Reflection.RuntimePropertyInfo.get_CanWrite =====
extern "C" bool System_Reflection_RuntimePropertyInfo_get_CanWrite(void* __this) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    return cil2cpp::propertyinfo_can_write(pi);
}

// ===== System.Reflection.RuntimePropertyInfo.GetValue (5-param) =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetValue_5(void* __this, void* obj,
    int32_t /*invokeAttr*/, void* /*binder*/, void* /*index*/, void* /*culture*/) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    return cil2cpp::propertyinfo_get_value(pi, reinterpret_cast<cil2cpp::Object*>(obj), nullptr);
}

// ===== System.Reflection.RuntimePropertyInfo.SetValue (6-param) =====
extern "C" void System_Reflection_RuntimePropertyInfo_SetValue_6(void* __this, void* obj,
    void* value, int32_t /*invokeAttr*/, void* /*binder*/, void* /*index*/, void* /*culture*/) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    cil2cpp::propertyinfo_set_value(pi, reinterpret_cast<cil2cpp::Object*>(obj),
        reinterpret_cast<cil2cpp::Object*>(value), nullptr);
}

// ===== System.Reflection.RuntimePropertyInfo.GetGetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetGetMethod(void* __this, bool /*nonPublic*/) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    return cil2cpp::propertyinfo_get_get_method(pi);
}

// ===== System.Reflection.RuntimePropertyInfo.get_PropertyType =====
extern "C" void* System_Reflection_RuntimePropertyInfo_get_PropertyType(void* __this) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    return cil2cpp::propertyinfo_get_property_type(pi);
}

// ===== System.Reflection.RuntimePropertyInfo.GetIndexParameters =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetIndexParameters(void* /*__this*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Reflection_ParameterInfo_TypeInfo, 0);
}

// ===== System.Reflection.RuntimePropertyInfo.GetIndexParametersNoCopy =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetIndexParametersNoCopy(void* /*__this*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Reflection_ParameterInfo_TypeInfo, 0);
}

// ===== System.Reflection.RuntimePropertyInfo.GetRuntimeModule =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetRuntimeModule(void* /*__this*/) {
    return get_singleton_module();
}

// ===== System.Reflection.RuntimePropertyInfo.GetSetMethod =====
extern "C" void* System_Reflection_RuntimePropertyInfo_GetSetMethod(void* __this, bool /*nonPublic*/) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    return cil2cpp::propertyinfo_get_set_method(pi);
}

// ===== System.Reflection.RuntimePropertyInfo.get =====
extern "C" System_Reflection_BindingFlags System_Reflection_RuntimePropertyInfo_get_BindingFlags(void* /*__this*/) {
    return cil2cpp::binding_flags::Default; // Public | Instance
}
extern "C" void* System_Reflection_RuntimePropertyInfo_get_ReflectedTypeInternal(void* __this) {
    auto* pi = reinterpret_cast<cil2cpp::ManagedPropertyInfo*>(__this);
    return cil2cpp::propertyinfo_get_declaring_type(pi);
}
extern "C" void* System_Reflection_RuntimePropertyInfo_get_Signature(void* /*__this*/) {
    // Signature is a CoreCLR JIT internal — not applicable in AOT
    cil2cpp::throw_not_supported();
}

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
extern "C" void* System_RuntimeType_InvokeDispMethod(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags /*invokeAttr*/, void* /*target*/, void* /*args*/, void* /*byrefModifiers*/, int32_t /*culture*/, void* /*namedParameters*/) {
    // COM IDispatch not supported in AOT
    cil2cpp::throw_not_supported();
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
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) return false;
    return t->type_info->flags & cil2cpp::TypeFlags::Delegate;
}

extern "C" bool System_RuntimeType_get_IsNullableOfT(void* __this) {
    // Checks if this RuntimeType represents Nullable<T>.
    // CoreCLR reads MethodTable.Flags — we check the type name instead.
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->full_name) return false;
    static constexpr char kNullablePrefix[] = "System.Nullable`1";
    return std::strncmp(t->type_info->full_name, kNullablePrefix, sizeof(kNullablePrefix) - 1) == 0;
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
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (f_m_type).
    return *reinterpret_cast<void**>(__this);
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
extern "C" System_ReadOnlySpan_1_System_IntPtr System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_RuntimeTypeHandle___System_Span_1_System_IntPtr_(void* /*inHandles*/, System_Span_1_System_IntPtr /*stackScratch*/) { return {}; }
extern "C" void* System_RuntimeTypeHandle_CopyRuntimeTypeHandles__System_Type___System_Int32Ref(void* /*inHandles*/, void* /*length*/) { return nullptr; }
extern "C" bool System_RuntimeTypeHandle_Equals__System_Object(void* __this, void* obj) {
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (f_m_type).
    return *reinterpret_cast<void**>(__this) == obj;
}
extern "C" bool System_RuntimeTypeHandle_Equals__System_RuntimeTypeHandle(System_RuntimeTypeHandle* __this, System_RuntimeTypeHandle other) {
    return __this->f_m_type == other.f_m_type;
}
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_IntPtr(void* /*__this*/, intptr_t objHandle) {
    cil2cpp::gchandle_free(objHandle);
    return {};
}
extern "C" void* System_RuntimeTypeHandle_GetConstraints(void* /*__this*/) {
    // AOT compiler verifies all generic constraints at compile time.
    // Return empty Type[] — no runtime constraint checking needed.
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, 0);
}
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
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (f_m_type).
    auto* rt = *reinterpret_cast<void**>(__this);
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(rt) >> 3);
}
extern "C" void* System_RuntimeTypeHandle_GetInstantiationInternal(void* __this) {
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (f_m_type).
    auto* t = *reinterpret_cast<cil2cpp::Type**>(__this);
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
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (f_m_type).
    return *reinterpret_cast<void**>(__this);
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
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_RuntimeType(void* /*__this*/, void* /*inst*/) {
    // Runtime generic type instantiation is not supported in AOT compilation
    cil2cpp::throw_not_supported();
}
extern "C" void* System_RuntimeTypeHandle_Instantiate__System_Type__(void* /*__this*/, void* /*inst*/) {
    // Runtime generic type instantiation is not supported in AOT compilation
    cil2cpp::throw_not_supported();
}
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
extern "C" void* System_RuntimeTypeHandle_MakeArray__System_Int32(void* __this, int32_t rank) {
    // Look up pre-compiled array type by name (e.g., "System.Int32[,]")
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->full_name) return nullptr;
    std::string name = std::string(t->type_info->full_name) + "[";
    for (int32_t i = 1; i < rank; i++) name += ",";
    name += "]";
    auto* ti = cil2cpp::type_get_by_name(name.c_str());
    return ti ? cil2cpp::type_get_type_object(ti) : nullptr;
}
extern "C" void* System_RuntimeTypeHandle_MakeByRef(void* __this) {
    // ByRef types are not tracked as separate TypeInfo in AOT
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->full_name) return nullptr;
    std::string name = std::string(t->type_info->full_name) + "&";
    auto* ti = cil2cpp::type_get_by_name(name.c_str());
    return ti ? cil2cpp::type_get_type_object(ti) : nullptr;
}
extern "C" void* System_RuntimeTypeHandle_MakePointer(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->full_name) return nullptr;
    std::string name = std::string(t->type_info->full_name) + "*";
    auto* ti = cil2cpp::type_get_by_name(name.c_str());
    return ti ? cil2cpp::type_get_type_object(ti) : nullptr;
}
extern "C" void* System_RuntimeTypeHandle_MakeSZArray(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info || !t->type_info->full_name) return nullptr;
    std::string name = std::string(t->type_info->full_name) + "[]";
    auto* ti = cil2cpp::type_get_by_name(name.c_str());
    return ti ? cil2cpp::type_get_type_object(ti) : nullptr;
}
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_RuntimeType_System_Reflection_RuntimeAssembly(void* /*type*/, void* /*assembly*/) {
    // No collectible assemblies in AOT
}
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_RuntimeType___System_RuntimeType___System_RuntimeType(void* /*paramType*/, void* /*typeContext*/, void* /*methodContext*/, void* /*toType*/) {
    return true; // AOT compiler verifies all generic constraints at compile time
}
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
    // __this is RuntimeTypeHandle* — field 0 is RuntimeType* (f_m_type).
    // RuntimeTypeHandle.Value returns IntPtr to the RuntimeType.
    return reinterpret_cast<intptr_t>(*reinterpret_cast<void**>(__this));
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
extern "C" void System_RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle baseType, void* /*pTypeHandles*/, int32_t /*cTypeHandles*/, System_Runtime_CompilerServices_ObjectHandleOnStack instantiatedObject) {
    if (!instantiatedObject.f__ptr) return;
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(baseType.f__ptr);
    if (!rtPtr || !*rtPtr || !(*rtPtr)->type_info) return;
    auto* ti = (*rtPtr)->type_info;
    // Allocate default instance — caller handles initialization
    auto* obj = cil2cpp::object_alloc(ti);
    if (obj && ti->default_ctor) {
        // Call default constructor: void .ctor(this)
        reinterpret_cast<void(*)(void*)>(ti->default_ctor)(obj);
    }
    *reinterpret_cast<void**>(instantiatedObject.f__ptr) = obj;
}
extern "C" intptr_t System_RuntimeTypeHandle_FreeGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtr(System_Runtime_CompilerServices_QCallTypeHandle /*typeHandle*/, intptr_t objHandle) {
    cil2cpp::gchandle_free(objHandle);
    return {};
}
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_Runtime_CompilerServices_ObjectHandleOnStack_void_ptrPtr_System_VoidPtrPtr_void_ptrPtr_Interop_BOOLPtr(System_Runtime_CompilerServices_ObjectHandleOnStack pRuntimeType, void* ppfnAllocator, void* /*pvAllocatorFirstArg*/, void* ppfnCtor, void* pfCtorIsPublic) {
    // Return default_ctor as the constructor pointer; allocator not needed (GC handles it)
    if (!pRuntimeType.f__ptr) return;
    auto* rt = *reinterpret_cast<cil2cpp::Type**>(pRuntimeType.f__ptr);
    if (!rt || !rt->type_info) return;
    auto* ti = rt->type_info;
    if (ppfnCtor) *reinterpret_cast<void**>(ppfnCtor) = ti->default_ctor;
    if (pfCtorIsPublic) *reinterpret_cast<int32_t*>(pfCtorIsPublic) = 1; // Assume public
}
extern "C" void System_RuntimeTypeHandle_GetActivationInfo__System_RuntimeType_void_ptrRef_System_VoidRefPtr_void_ptrRef_System_BooleanRef(void* rt, void* /*pfnAllocator*/, void* /*vAllocatorFirstArg*/, void* pfnCtor, void* ctorIsPublic) {
    auto* type = reinterpret_cast<cil2cpp::Type*>(rt);
    if (!type || !type->type_info) return;
    if (pfnCtor) *reinterpret_cast<void**>(pfnCtor) = type->type_info->default_ctor;
    if (ctorIsPublic) *reinterpret_cast<bool*>(ctorIsPublic) = true;
}
extern "C" void* System_RuntimeTypeHandle_GetArgumentTypesFromFunctionPointer(void* /*type*/) {
    // Function pointer type metadata is not tracked in AOT TypeInfo
    cil2cpp::throw_not_supported();
}
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
extern "C" void System_RuntimeTypeHandle_GetConstraints__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack types) {
    // AOT verifies constraints at compile time — return empty array
    if (types.f__ptr) {
        *reinterpret_cast<void**>(types.f__ptr) = cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, 0);
    }
}
extern "C" System_Reflection_CorElementType System_RuntimeTypeHandle_GetCorElementType(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info) return {};
    return static_cast<System_Reflection_CorElementType>(t->type_info->cor_element_type);
}
extern "C" void* System_RuntimeTypeHandle_GetDeclaringType(void* /*type*/) {
    // Nested type declaring type — not tracked in TypeInfo currently
    return nullptr;
}
extern "C" bool System_RuntimeTypeHandle_GetFields(void* /*type*/, void* /*result*/, void* /*count*/) {
    return false; // Field enumeration uses TypeInfo.fields directly
}
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetFirstIntroducedMethod(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info || t->type_info->method_count == 0) return {};
    return { t->type_info->methods[0].method_pointer };
}
extern "C" intptr_t System_RuntimeTypeHandle_GetGCHandle__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_InteropServices_GCHandleType(System_Runtime_CompilerServices_QCallTypeHandle /*handle*/, System_Runtime_InteropServices_GCHandleType type) {
    auto h = cil2cpp::gchandle_alloc(nullptr, static_cast<cil2cpp::GCHandleType>(type));
    return h;
}
extern "C" void System_RuntimeTypeHandle_GetGenericTypeDefinition(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack retType) {
    if (!retType.f__ptr) return;
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(type.f__ptr);
    if (!rtPtr || !*rtPtr || !(*rtPtr)->type_info) return;
    auto* ti = (*rtPtr)->type_info;
    // If this type has a generic definition name, look it up
    if (ti->generic_definition_name) {
        auto* defTi = cil2cpp::type_get_by_name(ti->generic_definition_name);
        if (defTi) {
            *reinterpret_cast<void**>(retType.f__ptr) = cil2cpp::type_get_type_object(defTi);
            return;
        }
    }
    // Non-generic or definition itself — return self
    *reinterpret_cast<void**>(retType.f__ptr) = *rtPtr;
}
extern "C" int32_t System_RuntimeTypeHandle_GetGenericVariableIndex__System_RuntimeType(void* /*type*/) {
    return -1; // No open generic parameters in AOT
}
extern "C" void System_RuntimeTypeHandle_GetInstantiation(System_Runtime_CompilerServices_QCallTypeHandle type, System_Runtime_CompilerServices_ObjectHandleOnStack types, Interop_BOOL fAsRuntimeTypeArray) {
    if (!types.f__ptr) return;
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(type.f__ptr);
    if (!rtPtr || !*rtPtr) { *reinterpret_cast<void**>(types.f__ptr) = nullptr; return; }
    // Delegate to existing GetInstantiationInternal which builds Type[] from generic_arguments
    *reinterpret_cast<void**>(types.f__ptr) = System_RuntimeTypeHandle_GetInstantiationInternal(*rtPtr);
}
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallTypeHandle_System_RuntimeMethodHandleInternal(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_QCallTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) {
    // Look up the interface vtable and find the method by slot
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(handle.f__ptr);
    auto** ifPtr = reinterpret_cast<cil2cpp::Type**>(interfaceHandle.f__ptr);
    if (!rtPtr || !*rtPtr || !(*rtPtr)->type_info || !ifPtr || !*ifPtr || !(*ifPtr)->type_info)
        return {};
    auto* ivt = cil2cpp::type_get_interface_vtable((*rtPtr)->type_info, (*ifPtr)->type_info);
    if (!ivt || !ivt->methods) return {};
    // Find which slot the interface method occupies by matching method_pointer
    auto* ifTi = (*ifPtr)->type_info;
    for (uint32_t i = 0; i < ifTi->method_count && i < ivt->method_count; i++) {
        if (ifTi->methods[i].method_pointer == interfaceMethodHandle.f_value) {
            return { ivt->methods[i] };
        }
    }
    return {};
}
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetInterfaceMethodImplementation__System_RuntimeTypeHandle_System_RuntimeMethodHandleInternal(void* __this, System_RuntimeTypeHandle interfaceHandle, System_RuntimeMethodHandleInternal interfaceMethodHandle) {
    auto* type = reinterpret_cast<cil2cpp::Type*>(__this);
    auto* ifType = reinterpret_cast<cil2cpp::Type*>(interfaceHandle.f_m_type);
    if (!type || !type->type_info || !ifType || !ifType->type_info) return {};
    auto* ivt = cil2cpp::type_get_interface_vtable(type->type_info, ifType->type_info);
    if (!ivt || !ivt->methods) return {};
    auto* ifTi = ifType->type_info;
    for (uint32_t i = 0; i < ifTi->method_count && i < ivt->method_count; i++) {
        if (ifTi->methods[i].method_pointer == interfaceMethodHandle.f_value) {
            return { ivt->methods[i] };
        }
    }
    return {};
}
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
extern "C" System_Reflection_MetadataImport System_RuntimeTypeHandle_GetMetadataImport(void* /*type*/) {
    return {}; // MetadataImport is a CoreCLR internal — not applicable in AOT
}
extern "C" System_RuntimeMethodHandleInternal System_RuntimeTypeHandle_GetMethodAt(void* type, int32_t slot) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info || !t->type_info->vtable ||
        slot < 0 || static_cast<uint32_t>(slot) >= t->type_info->vtable->method_count)
        return {};
    return { t->type_info->vtable->methods[slot] };
}
extern "C" void* System_RuntimeTypeHandle_GetModule(void* /*type*/) {
    // Caller (RuntimeTypeCache ctor) checks module.RuntimeType == this.
    // Singleton module has null RuntimeType → m_isGlobal = false (correct).
    return get_singleton_module();
}
extern "C" void System_RuntimeTypeHandle_GetNextIntroducedMethod(void* /*method*/) {
    // Method iteration uses TypeInfo.methods array, not CoreCLR enumerator
}
extern "C" int32_t System_RuntimeTypeHandle_GetNumVirtuals(void* type) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(type);
    if (!t || !t->type_info || !t->type_info->vtable) return 0;
    return static_cast<int32_t>(t->type_info->vtable->method_count);
}
extern "C" void System_RuntimeTypeHandle_Instantiate__System_Runtime_CompilerServices_QCallTypeHandle_System_IntPtrPtr_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle /*handle*/, void* /*pInst*/, int32_t /*numGenericArgs*/, System_Runtime_CompilerServices_ObjectHandleOnStack /*type*/) {
    // Runtime generic type instantiation is not supported in AOT compilation
    cil2cpp::throw_not_supported();
}
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
extern "C" void System_RuntimeTypeHandle_MakeArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Int32_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, int32_t rank, System_Runtime_CompilerServices_ObjectHandleOnStack type) {
    if (!type.f__ptr) return;
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(handle.f__ptr);
    if (!rtPtr || !*rtPtr) { *reinterpret_cast<void**>(type.f__ptr) = nullptr; return; }
    *reinterpret_cast<void**>(type.f__ptr) = System_RuntimeTypeHandle_MakeArray__System_Int32(*rtPtr, rank);
}
extern "C" void System_RuntimeTypeHandle_MakeByRef__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) {
    if (!type.f__ptr) return;
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(handle.f__ptr);
    if (!rtPtr || !*rtPtr) { *reinterpret_cast<void**>(type.f__ptr) = nullptr; return; }
    *reinterpret_cast<void**>(type.f__ptr) = System_RuntimeTypeHandle_MakeByRef(*rtPtr);
}
extern "C" void System_RuntimeTypeHandle_MakePointer__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) {
    if (!type.f__ptr) return;
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(handle.f__ptr);
    if (!rtPtr || !*rtPtr) { *reinterpret_cast<void**>(type.f__ptr) = nullptr; return; }
    *reinterpret_cast<void**>(type.f__ptr) = System_RuntimeTypeHandle_MakePointer(*rtPtr);
}
extern "C" void System_RuntimeTypeHandle_MakeSZArray__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_ObjectHandleOnStack(System_Runtime_CompilerServices_QCallTypeHandle handle, System_Runtime_CompilerServices_ObjectHandleOnStack type) {
    if (!type.f__ptr) return;
    auto** rtPtr = reinterpret_cast<cil2cpp::Type**>(handle.f__ptr);
    if (!rtPtr || !*rtPtr) { *reinterpret_cast<void**>(type.f__ptr) = nullptr; return; }
    *reinterpret_cast<void**>(type.f__ptr) = System_RuntimeTypeHandle_MakeSZArray(*rtPtr);
}
extern "C" void System_RuntimeTypeHandle_RegisterCollectibleTypeDependency__System_Runtime_CompilerServices_QCallTypeHandle_System_Runtime_CompilerServices_QCallAssembly(System_Runtime_CompilerServices_QCallTypeHandle /*type*/, System_Runtime_CompilerServices_QCallAssembly /*assembly*/) {
    // No collectible assemblies in AOT
}
extern "C" bool System_RuntimeTypeHandle_SatisfiesConstraints__System_RuntimeType_System_IntPtrPtr_System_Int32_System_IntPtrPtr_System_Int32_System_RuntimeType(void* /*paramType*/, void* /*pTypeContext*/, int32_t /*typeContextLength*/, void* /*pMethodContext*/, int32_t /*methodContextLength*/, void* /*toType*/) {
    return true; // AOT compiler verifies all generic constraints at compile time
}
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
    return static_cast<SafeHandleLayout*>(safeHandle)->_handle;
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
extern "C" bool System_Runtime_CompilerServices_MethodTable_get_IsGenericTypeDefinition(void* __this) {
    // Open generic type definition: full_name contains backtick (e.g. "System.Collections.Generic.IDictionary`2")
    // but NOT angle brackets (closed generics have "<...>" in full_name).
    auto* ti = reinterpret_cast<cil2cpp::TypeInfo*>(__this);
    if (!ti || !ti->full_name) return false;
    const char* backtick = std::strchr(ti->full_name, '`');
    if (!backtick) return false;
    return std::strchr(ti->full_name, '<') == nullptr;
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
    return get_singleton_assembly();
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
extern "C" System_Guid System_Type_get_GUID(void* /*__this*/) {
    return {}; // COM GUIDs not supported in AOT
}
extern "C" bool System_Type_IsArrayImpl(void* __this) {
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    return t && t->type_info && (t->type_info->flags & cil2cpp::TypeFlags::Array);
}
extern "C" bool System_Type_get_IsClass(void* __this) {
    return cil2cpp::type_get_is_class(reinterpret_cast<cil2cpp::Type*>(__this));
}
extern "C" void* System_Type_get_Module(void* /*__this*/) {
    return get_singleton_module();
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
extern "C" void* System_Type_GetEvent__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags /*bindingAttr*/) {
    return nullptr; // Events not tracked in TypeInfo
}
extern "C" void* System_Type_GetEvents__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags /*bindingAttr*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, 0);
}
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
extern "C" void* System_Type_GetMembers__System_Reflection_BindingFlags(void* __this, System_Reflection_BindingFlags /*bindingAttr*/) {
    // Combine methods + fields + properties into a single MemberInfo[] array
    auto* t = reinterpret_cast<cil2cpp::Type*>(__this);
    if (!t || !t->type_info) return cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, 0);
    auto* methods = static_cast<cil2cpp::Array*>(cil2cpp::type_get_methods(t));
    auto* fields = static_cast<cil2cpp::Array*>(cil2cpp::type_get_fields(t));
    auto* props = static_cast<cil2cpp::Array*>(cil2cpp::type_get_properties(t));
    auto mc = methods ? cil2cpp::array_length(methods) : 0u;
    auto fc = fields ? cil2cpp::array_length(fields) : 0u;
    auto pc = props ? cil2cpp::array_length(props) : 0u;
    auto total = static_cast<int32_t>(mc + fc + pc);
    auto* result = static_cast<cil2cpp::Array*>(cil2cpp::gc::alloc_array(&cil2cpp::System_Object_TypeInfo, total));
    auto** dest = reinterpret_cast<void**>(cil2cpp::array_data(result));
    int32_t idx = 0;
    if (mc > 0) { auto** src = reinterpret_cast<void**>(cil2cpp::array_data(methods)); for (uint32_t i = 0; i < mc; i++) dest[idx++] = src[i]; }
    if (fc > 0) { auto** src = reinterpret_cast<void**>(cil2cpp::array_data(fields)); for (uint32_t i = 0; i < fc; i++) dest[idx++] = src[i]; }
    if (pc > 0) { auto** src = reinterpret_cast<void**>(cil2cpp::array_data(props)); for (uint32_t i = 0; i < pc; i++) dest[idx++] = src[i]; }
    return result;
}
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
extern "C" void* System_Type_GetNestedType__System_String_System_Reflection_BindingFlags(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags /*bindingAttr*/) {
    return nullptr; // Nested types not tracked as children in TypeInfo
}
extern "C" void* System_Type_GetNestedTypes__System_Reflection_BindingFlags(void* /*__this*/, System_Reflection_BindingFlags /*bindingAttr*/) {
    return cil2cpp::gc::alloc_array(&cil2cpp::System_Type_TypeInfo, 0);
}
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
extern "C" void* System_Type_InvokeMember__System_String_System_Reflection_BindingFlags_System_Reflection_Binder_System_Object_System_Object___System_Reflection_ParameterModifier___System_Globalization_CultureInfo_System_String__(void* /*__this*/, void* /*name*/, System_Reflection_BindingFlags /*invokeAttr*/, void* /*binder*/, void* /*target*/, void* /*args*/, void* /*modifiers*/, void* /*culture*/, void* /*namedParameters*/) {
    cil2cpp::throw_not_supported(); // InvokeMember is not supported in AOT
}
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

    // Determine if full_name is in C++ format (underscores) or IL format (dots with backtick).
    // The compiler emits the open generic definition's TypeInfo for ldtoken typeof(IList<>),
    // so full_name is already the open type name (e.g., "System.Collections.Generic.IList`1").
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
            // Type argument not available at runtime — can't construct this generic type.
            // Return nullptr instead of throwing; callers check for null (TryMakeGenericType, etc.)
            return nullptr;
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
    // Delegate cloning via ObjectHandleOnStack — not supported in AOT
    cil2cpp::throw_not_supported();
}

// RuntimeTypeHandle.GetNumVirtualsAndStaticVirtuals — CoreCLR vtable enumeration API, not used in AOT
extern "C" int32_t System_RuntimeTypeHandle_GetNumVirtualsAndStaticVirtuals__System_Runtime_CompilerServices_QCallTypeHandle(void* /*type*/) { cil2cpp::throw_not_supported(); }

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
    // Multi-dimensional array: use mdarray helpers for proper bounds checking
    if (cil2cpp::is_mdarray(reinterpret_cast<cil2cpp::Object*>(__this))) {
        auto* md = reinterpret_cast<cil2cpp::MdArray*>(__this);
        void* elem = cil2cpp::mdarray_get_element_ptr(md, idx);
        if (!elem) return nullptr;
        // Box the element: read value from element pointer and wrap as Object*
        auto* elemTi = md->element_type;
        if (!elemTi) return nullptr;
        if (elemTi->flags & cil2cpp::TypeFlags::ValueType) {
            // Box: allocate object, copy value
            auto* boxed = static_cast<cil2cpp::Object*>(
                cil2cpp::gc::alloc(sizeof(cil2cpp::Object) + elemTi->instance_size, elemTi));
            std::memcpy(reinterpret_cast<uint8_t*>(boxed) + sizeof(cil2cpp::Object),
                        elem, elemTi->instance_size);
            return boxed;
        }
        // Reference type: element is an Object*
        return *reinterpret_cast<cil2cpp::Object**>(elem);
    }
    // 1D array fallback: compute flattened index
    intptr_t flat = 0;
    intptr_t stride = 1;
    auto totalLen = cil2cpp::array_length(__this);
    for (intptr_t i = static_cast<intptr_t>(len) - 1; i >= 0; --i) {
        flat += idx[i] * stride;
        stride *= totalLen;
    }
    return static_cast<cil2cpp::Object*>(cil2cpp::array_get_value(__this, static_cast<int32_t>(flat)));
}

// Reflection.MethodBase.GetParametersAsSpan — return empty span (no parameter metadata via this path)
extern "C" void* System_Reflection_MethodBase_GetParametersAsSpan(void* /*__this*/) { return {}; }

// Reflection.TypeInfo.GetDeclaredField — field lookup not implemented via this path
extern "C" void* System_Reflection_TypeInfo_GetDeclaredField(void* /*__this*/, void* /*name*/) { cil2cpp::throw_not_supported(); }

// Reflection.TypeInfo.GetDeclaredProperty — property lookup not implemented via this path
extern "C" void* System_Reflection_TypeInfo_GetDeclaredProperty(void* /*__this*/, void* /*name*/) { cil2cpp::throw_not_supported(); }
