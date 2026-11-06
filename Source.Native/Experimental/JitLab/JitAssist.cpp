#include "JitLab.h"

namespace {
jitlab::Event events[jitlab::MaxEvents]{};
uint32_t head = 0, count = 0;
uint64_t emitted = 0, dropped = 0, drained = 0;
SRWLOCK gate = SRWLOCK_INIT;
}
namespace jitlab {
void Emit(EventKind kind, void* method, uint64_t generation, void* caller, void* root, uint32_t detail) {
    AcquireSRWLockExclusive(&gate);
    uint64_t sequence = ++emitted;
    if (count == MaxEvents) ++dropped;
    else {
        events[(head + count) % MaxEvents] = {sequence, generation, method, caller, root, static_cast<uint32_t>(kind), detail};
        ++count;
    }
    ReleaseSRWLockExclusive(&gate);
}
}
LAB_API uint32_t __cdecl JitLab_DrainEvents(jitlab::Event* output, uint32_t capacity) {
    if (!output || !capacity || capacity > jitlab::MaxEvents) return 0;
    AcquireSRWLockExclusive(&gate);
    uint32_t copied = count < capacity ? count : capacity;
    for (uint32_t i = 0; i < copied; ++i) output[i] = events[(head + i) % jitlab::MaxEvents];
    head = (head + copied) % jitlab::MaxEvents; count -= copied; drained += copied;
    ReleaseSRWLockExclusive(&gate);
    return copied;
}
LAB_API int __cdecl JitLab_AssistStatus(jitlab::AssistStatus* status, uint32_t bytes) {
    if (!status || bytes != sizeof(jitlab::AssistStatus)) return 0;
    AcquireSRWLockShared(&gate);
    *status = {emitted, dropped, drained, count, jitlab::MaxEvents};
    ReleaseSRWLockShared(&gate);
    return 1;
}
LAB_API int __cdecl JitLab_Evaluate(void* method, uint64_t generation, jitlab::Policy* output, uint32_t bytes) {
    if (!output || bytes != sizeof(jitlab::Policy)) return 0;
    bool current = jitlab::QueryPolicy(method, generation, *output);
    if (current) jitlab::Emit(jitlab::EventKind::Evaluated, method, output->generation, nullptr, nullptr, output->mode);
    return current ? 1 : 0;
}
