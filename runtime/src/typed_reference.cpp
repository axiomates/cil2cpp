#include "cil2cpp/typed_reference.h"
#include "cil2cpp/exception.h"

namespace cil2cpp {

void argiterator_init(ArgIterator* self, intptr_t handle) {
    auto* h = reinterpret_cast<VarArgHandle*>(handle);
    if (h) {
        self->entries = h->entries;
        self->count = h->count;
    } else {
        self->entries = nullptr;
        self->count = 0;
    }
    self->index = 0;
}

int32_t argiterator_get_remaining_count(ArgIterator* self) {
    return self->count - self->index;
}

TypedReference argiterator_get_next_arg(ArgIterator* self) {
    if (self->index >= self->count) {
        throw_invalid_operation();  // .NET throws InvalidOperationException
    }
    auto& entry = self->entries[self->index++];
    return TypedReference{entry.ptr, entry.type};
}

void argiterator_end(ArgIterator* /*self*/) {
    // No-op — stack-allocated VarArgHandle is cleaned up automatically
}

} // namespace cil2cpp
