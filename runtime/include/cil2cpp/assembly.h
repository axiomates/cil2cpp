/**
 * CIL2CPP Runtime - Assembly & PropertyInfo Reflection Types
 *
 * Phase II.4: Minimal runtime structs for System.Reflection.Assembly,
 * System.Reflection.RuntimeAssembly, and System.Reflection.RuntimePropertyInfo.
 */

#pragma once

#include "object.h"
#include "type_info.h"

namespace cil2cpp {

// Forward declarations
struct String;

// TypeInfo for assembly/property reflection types
extern TypeInfo System_Reflection_Assembly_TypeInfo;
extern TypeInfo System_Reflection_PropertyInfo_TypeInfo;

/**
 * Managed System.Reflection.Assembly — minimal stub.
 * TODO: Populate with real assembly metadata when full reflection is needed.
 */
struct ManagedAssembly : Object {
    String* name;       // Assembly simple name
};

/**
 * Managed System.Reflection.PropertyInfo — minimal stub.
 * TODO: Populate with real property metadata when full reflection is needed.
 */
struct ManagedPropertyInfo : Object {
    const char* name;       // Property name
    TypeInfo* prop_type;    // Property type
};

} // namespace cil2cpp

// Type aliases used by generated code
using System_Reflection_Assembly = cil2cpp::ManagedAssembly;
using System_Reflection_RuntimeAssembly = cil2cpp::ManagedAssembly;
using System_Reflection_RuntimePropertyInfo = cil2cpp::ManagedPropertyInfo;
