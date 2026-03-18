/**
 * CIL2CPP Runtime - EventSource No-op ICalls
 *
 * ETW/EventSource tracing is disabled in AOT compilation.
 * These no-op implementations allow TplEventSource, ArrayPoolEventSource,
 * DependencyInjectionEventSource, and other EventSource-derived types to
 * compile from BCL IL without errors.
 *
 * IsEnabled() returns false, causing all derived type methods to early-return
 * before calling WriteEvent(). This makes tracing a zero-cost no-op at runtime.
 */

#pragma once

#include "types.h"

namespace cil2cpp {

// EventSource..ctor() — base constructor, no-op
inline void eventsource_ctor(void*) {}

// EventSource..ctor(string name) — named EventSource constructor, no-op
// Uses template to handle resolved parameter types (String* etc.)
template<typename T>
inline void eventsource_ctor_name(void*, T) {}

// EventSource..ctor(string name, EventSourceSettings settings) — no-op
// Uses template: internal ctors have varying signatures (Guid+String, String+Settings, etc.)
template<typename T1, typename T2>
inline void eventsource_ctor_name_settings(void*, T1, T2) {}

// EventSource.IsEnabled() — parameterless overload, always false
inline bool eventsource_is_enabled(void*) { return false; }

// EventSource.IsEnabled(EventLevel, EventKeywords) — always false
inline bool eventsource_is_enabled_level(void*, int32_t /*level*/, int64_t /*keywords*/) { return false; }

// EventSource.get_IsSupported — static property, always false
inline bool eventsource_get_is_supported() { return false; }

// EventSource.SetCurrentThreadActivityId(Guid, out Guid) — no-op
template<typename T1, typename T2>
inline void eventsource_set_activity_id_2(T1, T2) {}

// EventCommandEventArgs.get_Command — returns 0 (EventCommand.Update)
inline int32_t eventsource_get_command(void*) { return 0; }

// Variadic no-op — used for WriteEvent, WriteEventCore, WriteEventWithRelatedActivityIdCore,
// EventData property setters, EventSourcePrimitive.op_Implicit, SetCurrentThreadActivityId(1-param),
// FrameworkEventSource tracing methods, ActivityTracker.Enable.
template<typename... Args>
inline void eventsource_write_event(Args...) {}

} // namespace cil2cpp
