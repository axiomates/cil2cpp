/**
 * CIL2CPP Runtime - System.Object Implementation
 */

#include <cil2cpp/bcl/System.Object.h>
#include <cil2cpp/string.h>
#include <cil2cpp/type_info.h>
#include <cstdio>
#include <cstring>

namespace cil2cpp {
namespace System {

// System.Object type info
TypeInfo Object_TypeInfo = {
    .name = "Object",
    .namespace_name = "System",
    .full_name = "System.Object",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .properties = nullptr,
    .property_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

} // namespace System

// Object method implementations
String* object_to_string(Object* obj) {
    if (!obj) {
        return string_literal("null");
    }

    auto* ti = obj->__type_info;
    if (!ti) return string_literal("System.Object");

    // Enum types: look up the enum member name by value
    if ((static_cast<uint32_t>(ti->flags) & static_cast<uint32_t>(TypeFlags::Enum)) != 0
        && ti->enum_names && ti->enum_count > 0)
    {
        // Read the raw underlying value from the boxed object's data area
        // (first field after Object header, at offset == sizeof(Object))
        auto* rawData = reinterpret_cast<const uint8_t*>(obj) + sizeof(Object);
        int64_t val = 0;
        int elemSize = 4;
        if (ti->underlying_type) {
            switch (ti->underlying_type->cor_element_type) {
                case 0x04: val = *reinterpret_cast<const int8_t*>(rawData); elemSize = 1; break;
                case 0x05: val = *reinterpret_cast<const uint8_t*>(rawData); elemSize = 1; break;
                case 0x06: val = *reinterpret_cast<const int16_t*>(rawData); elemSize = 2; break;
                case 0x07: val = *reinterpret_cast<const uint16_t*>(rawData); elemSize = 2; break;
                case 0x08: val = *reinterpret_cast<const int32_t*>(rawData); elemSize = 4; break;
                case 0x09: val = *reinterpret_cast<const uint32_t*>(rawData); elemSize = 4; break;
                case 0x0A: val = *reinterpret_cast<const int64_t*>(rawData); elemSize = 8; break;
                case 0x0B: val = static_cast<int64_t>(*reinterpret_cast<const uint64_t*>(rawData)); elemSize = 8; break;
                default: val = *reinterpret_cast<const int32_t*>(rawData); break;
            }
        } else {
            val = *reinterpret_cast<const int32_t*>(rawData);
        }
        for (uint32_t i = 0; i < ti->enum_count; i++) {
            if (ti->enum_values[i] == val)
                return string_literal(ti->enum_names[i]);
        }
        // Fallback: return numeric value as string
        char buf[32];
        std::snprintf(buf, sizeof(buf), "%lld", (long long)val);
        return string_literal(buf);
    }

    if (ti->full_name) {
        return string_literal(ti->full_name);
    }

    return string_literal("System.Object");
}

} // namespace cil2cpp
