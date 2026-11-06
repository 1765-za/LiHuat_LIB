#include "JitLab.h"
#include <atomic>
#include <cstring>

namespace {
struct Entry {
    jitlab::Rule rule{};
    jitlab::Sample sample{};
    void* callers[jitlab::MaxCallers]{};
    jitlab::CallerStatus callerStatus{};
};
Entry entries[jitlab::MaxRules]{};
std::atomic<uint32_t> count{0};
uint64_t nextGeneration = 0;
SRWLOCK gate = SRWLOCK_INIT;
size_t Position(void* method, size_t size) {
    uintptr_t key = reinterpret_cast<uintptr_t>(method);
    size_t low = 0, high = size;
    while (low < high) {
        size_t middle = low + (high - low) / 2;
        if (reinterpret_cast<uintptr_t>(entries[middle].rule.method) < key) low = middle + 1;
        else high = middle;
    }
    return low;
}
bool ReadPolicy(void* method, uint64_t expected, jitlab::Policy& output) {
    output = {};
    size_t size = count.load(std::memory_order_relaxed), at = Position(method, size);
    if (at == size || entries[at].rule.method != method) return false;
    const auto& rule = entries[at].rule;
    bool current = expected == 0 || expected == rule.generation;
    output = {rule.generation, static_cast<uint32_t>(rule.mode), rule.value, 1, current ? 1u : 0u};
    return current;
}
bool ReleaseRule(void* method, uint64_t expected) {
    AcquireSRWLockExclusive(&gate);
    size_t size = count.load(std::memory_order_relaxed), at = Position(method, size);
    bool found = at < size && entries[at].rule.method == method
        && (expected == 0 || expected == entries[at].rule.generation);
    if (found) {
        jitlab::Emit(jitlab::EventKind::Released, method, entries[at].rule.generation);
        for (size_t i = at + 1; i < size; ++i) entries[i - 1] = entries[i];
        entries[size - 1] = {}; count.store(static_cast<uint32_t>(size - 1), std::memory_order_release);
    }
    ReleaseSRWLockExclusive(&gate);
    return found;
}
}
namespace jitlab {
bool HasRules() { return count.load(std::memory_order_acquire) != 0; }
bool QueryPolicy(void* method, uint64_t generation, Policy& policy) {
    AcquireSRWLockShared(&gate);
    bool current = ReadPolicy(method, generation, policy);
    ReleaseSRWLockShared(&gate);
    return current;
}
bool FindRule(void* method, Rule& rule) {
    if (!count.load(std::memory_order_acquire)) return false;
    AcquireSRWLockShared(&gate);
    size_t size = count.load(std::memory_order_relaxed), at = Position(method, size);
    bool found = at < size && entries[at].rule.method == method;
    if (found) rule = entries[at].rule;
    ReleaseSRWLockShared(&gate);
    return found;
}
void Record(const Rule& rule, Sample sample) {
    bool accepted = false;
    AcquireSRWLockExclusive(&gate);
    size_t size = count.load(std::memory_order_relaxed), at = Position(rule.method, size);
    if (at < size && entries[at].rule.method == rule.method && entries[at].rule.generation == rule.generation) {
        const Sample& old = entries[at].sample;
        sample.calls += old.calls; sample.rewrites += old.rewrites; sample.mismatches += old.mismatches;
        entries[at].sample = sample;
        accepted = true;
    }
    ReleaseSRWLockExclusive(&gate);
    if (accepted) Emit(EventKind::Compiled, rule.method, rule.generation, nullptr, nullptr, sample.result == 0 ? sample.matched : 0);
}
void RecordInline(void* method, uint64_t generation, void* caller, void* root, bool blocked) {
    AcquireSRWLockExclusive(&gate);
    size_t size = count.load(std::memory_order_relaxed), at = Position(method, size);
    bool current = at < size && entries[at].rule.method == method && entries[at].rule.generation == generation;
    if (current) {
        auto& entry = entries[at];
        ++entry.callerStatus.inlineChecks;
        if (blocked) ++entry.callerStatus.inlineBlocks;
        bool known = false;
        for (uint32_t i = 0; i < entry.callerStatus.count; ++i) known |= entry.callers[i] == root;
        if (!known) {
            if (entry.callerStatus.count < MaxCallers) entry.callers[entry.callerStatus.count++] = root;
            else ++entry.callerStatus.overflow;
        }
    }
    ReleaseSRWLockExclusive(&gate);
    if (current) Emit(blocked ? EventKind::InlineBlocked : EventKind::InlineConsidered, method, generation, caller, root);
}
}
LAB_API int __cdecl JitLab_Register(void* method, const uint8_t* expected, uint32_t length, uint32_t mode, int32_t value) {
    if (!method || !expected || !length || length > jitlab::MaxIl || mode > 3) return 0;
    AcquireSRWLockExclusive(&gate);
    size_t size = count.load(std::memory_order_relaxed), at = Position(method, size);
    bool found = at < size && entries[at].rule.method == method;
    if (!found && size == jitlab::MaxRules) { ReleaseSRWLockExclusive(&gate); return 0; }
    if (!found) {
        for (size_t i = size; i > at; --i) entries[i] = entries[i - 1];
        entries[at] = {};
        count.store(static_cast<uint32_t>(size + 1), std::memory_order_release);
    }
    entries[at].rule = {}; entries[at].sample = {};
    auto& rule = entries[at].rule;
    rule.method = method; rule.generation = ++nextGeneration; rule.mode = static_cast<jitlab::Mode>(mode);
    rule.value = value; rule.ilSize = length; std::memcpy(rule.expected, expected, length);
    entries[at].sample.generation = rule.generation; entries[at].sample.method = method;
    jitlab::Emit(jitlab::EventKind::Registered, method, rule.generation);
    ReleaseSRWLockExclusive(&gate);
    return 1;
}
LAB_API int __cdecl JitLab_Unregister(void* method) {
    return ReleaseRule(method, 0) ? 1 : 0;
}
LAB_API int __cdecl JitLab_ReleaseRule(void* method, uint64_t generation) {
    return generation && ReleaseRule(method, generation) ? 1 : 0;
}
LAB_API uint32_t __cdecl JitLab_QueryRules(const jitlab::PolicyQuery* queries, jitlab::Policy* output, uint32_t length) {
    if (!queries || !output || !length || length > jitlab::MaxQueries) return 0;
    AcquireSRWLockShared(&gate);
    for (uint32_t i = 0; i < length; ++i) ReadPolicy(queries[i].method, queries[i].generation, output[i]);
    ReleaseSRWLockShared(&gate);
    return length;
}
LAB_API int __cdecl JitLab_ReadCallers(void* method, uint64_t generation, void** callers, uint32_t capacity,
    jitlab::CallerStatus* status, uint32_t bytes) {
    if (!status || bytes != sizeof(jitlab::CallerStatus) || capacity > jitlab::MaxCallers || (capacity && !callers)) return 0;
    AcquireSRWLockShared(&gate);
    size_t size = count.load(std::memory_order_relaxed), at = Position(method, size);
    bool current = at < size && entries[at].rule.method == method && entries[at].rule.generation == generation;
    if (current) {
        *status = entries[at].callerStatus;
        uint32_t copied = capacity < status->count ? capacity : status->count;
        for (uint32_t i = 0; i < copied; ++i) callers[i] = entries[at].callers[i];
    }
    ReleaseSRWLockShared(&gate);
    return current ? 1 : 0;
}
LAB_API int __cdecl JitLab_Read(void* method, jitlab::Sample* output, uint32_t bytes) {
    if (!output || bytes != sizeof(jitlab::Sample)) return 0;
    AcquireSRWLockShared(&gate);
    size_t size = count.load(std::memory_order_relaxed), at = Position(method, size);
    bool found = at < size && entries[at].rule.method == method;
    if (found) *output = entries[at].sample;
    ReleaseSRWLockShared(&gate);
    if (!found) return 0;
    if (jitlab::ReadWord(output->executable, output->nativeSize, output->executableNow)) output->readMask |= 4;
    return 1;
}
