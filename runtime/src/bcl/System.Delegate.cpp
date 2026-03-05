/**
 * CIL2CPP Runtime - System.Delegate implementation
 */

#include <cil2cpp/delegate.h>
#include <cil2cpp/array.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/type_info.h>
#include <cstring>

namespace cil2cpp {

// Internal TypeInfo for Delegate (used as element type for invocation list arrays)
// element_size = sizeof(pointer) since arrays store Delegate* pointers
TypeInfo Delegate_TypeInfo = {
    .name = "Delegate",
    .namespace_name = "System",
    .full_name = "System.Delegate",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Delegate),
    .element_size = static_cast<UInt32>(sizeof(Delegate*)),
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

Delegate* delegate_create(TypeInfo* type, Object* target, void* method_ptr) {
    auto* del = static_cast<Delegate*>(gc::alloc(sizeof(Delegate), type));
    del->target = target;
    del->method_ptr = method_ptr;
    del->invocation_list = nullptr;
    del->invocation_count = 0;
    return del;
}

static bool delegate_equals(Delegate* a, Delegate* b) {
    if (a == b) return true;
    if (!a || !b) return false;
    return a->method_ptr == b->method_ptr && a->target == b->target;
}

Object* delegate_combine(Object* a, Object* b) {
    if (!a) return b;
    if (!b) return a;

    auto* da = static_cast<Delegate*>(a);
    auto* db = static_cast<Delegate*>(b);

    // Collect delegate counts
    Int32 a_count = da->invocation_count > 0 ? da->invocation_count : 1;
    Int32 b_count = db->invocation_count > 0 ? db->invocation_count : 1;
    Int32 total = a_count + b_count;

    // Create invocation list array (Array of Delegate* pointers)
    auto* list = array_create(&Delegate_TypeInfo, total);
    auto** items = static_cast<Delegate**>(array_data(list));

    // Copy delegates from a
    if (da->invocation_count > 0) {
        auto** a_items = static_cast<Delegate**>(array_data(da->invocation_list));
        for (Int32 i = 0; i < da->invocation_count; i++)
            items[i] = a_items[i];
    } else {
        items[0] = da;
    }

    // Copy delegates from b
    if (db->invocation_count > 0) {
        auto** b_items = static_cast<Delegate**>(array_data(db->invocation_list));
        for (Int32 i = 0; i < db->invocation_count; i++)
            items[a_count + i] = b_items[i];
    } else {
        items[a_count] = db;
    }

    // Create multicast delegate — use type of first delegate
    auto* result = static_cast<Delegate*>(gc::alloc(sizeof(Delegate), da->__type_info));
    // The method_ptr and target point to the LAST delegate (invocation semantics)
    auto* last = items[total - 1];
    result->target = last->target;
    result->method_ptr = last->method_ptr;
    result->invocation_list = list;
    result->invocation_count = total;
    return result;
}

Object* delegate_remove(Object* source, Object* value) {
    if (!source) return nullptr;
    if (!value) return source;

    auto* src = static_cast<Delegate*>(source);
    auto* val = static_cast<Delegate*>(value);

    // Single-cast: if matches, return nullptr
    if (src->invocation_count == 0) {
        if (delegate_equals(src, val))
            return nullptr;
        return source;
    }

    // Multicast: find and remove the last matching delegate
    auto** items = static_cast<Delegate**>(array_data(src->invocation_list));
    Int32 remove_idx = -1;
    for (Int32 i = src->invocation_count - 1; i >= 0; i--) {
        if (delegate_equals(items[i], val)) {
            remove_idx = i;
            break;
        }
    }

    if (remove_idx < 0) return source;

    Int32 new_count = src->invocation_count - 1;
    if (new_count == 0) return nullptr;

    if (new_count == 1) {
        // Return the remaining single delegate directly
        return remove_idx == 0
            ? static_cast<Object*>(items[1])
            : static_cast<Object*>(items[0]);
    }

    // Create new multicast with one fewer
    auto* list = array_create(&Delegate_TypeInfo, new_count);
    auto** new_items = static_cast<Delegate**>(array_data(list));
    Int32 j = 0;
    for (Int32 i = 0; i < src->invocation_count; i++) {
        if (i != remove_idx)
            new_items[j++] = items[i];
    }

    auto* result = static_cast<Delegate*>(gc::alloc(sizeof(Delegate), src->__type_info));
    auto* last = new_items[new_count - 1];
    result->target = last->target;
    result->method_ptr = last->method_ptr;
    result->invocation_list = list;
    result->invocation_count = new_count;
    return result;
}

Int32 delegate_get_invocation_count(Delegate* del) {
    if (!del) return 0;
    return del->invocation_count > 0 ? del->invocation_count : 1;
}

Delegate* delegate_get_invocation_item(Delegate* del, Int32 index) {
    if (!del) return nullptr;
    if (del->invocation_count == 0) return del;
    auto** items = static_cast<Delegate**>(array_data(del->invocation_list));
    return items[index];
}

} // namespace cil2cpp
