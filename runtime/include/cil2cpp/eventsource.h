/**
 * CIL2CPP Runtime - EventSource No-op ICalls
 *
 * ETW/EventSource tracing is disabled in AOT compilation.
 * These no-op implementations allow TplEventSource, ArrayPoolEventSource, and
 * other EventSource-derived types to compile from BCL IL without errors.
 *
 * IsEnabled() returns false, causing all derived type methods to early-return
 * before calling WriteEvent(). This makes tracing a zero-cost no-op at runtime.
 */

#pragma once

#include "types.h"

namespace cil2cpp {

// EventSource..ctor() — base constructor, no-op
inline void eventsource_ctor(void*) {}

// EventSource.IsEnabled() — parameterless overload, always false
inline bool eventsource_is_enabled(void*) { return false; }

// EventSource.IsEnabled(EventLevel, EventKeywords) — always false
inline bool eventsource_is_enabled_level(void*, int32_t /*level*/, int64_t /*keywords*/) { return false; }

// EventSource.get_IsSupported — static property, always false
inline bool eventsource_get_is_supported() { return false; }

// EventSource.WriteEvent — variadic no-op (never reached when IsEnabled()=false)
// Template handles all overloads (1-8 params) with a single definition.
template<typename... Args>
inline void eventsource_write_event(Args...) {}

} // namespace cil2cpp
