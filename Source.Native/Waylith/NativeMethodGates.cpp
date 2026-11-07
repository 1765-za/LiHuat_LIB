#include "NativeMethodGates.h"
#include "NativeMemoryOwnership.h"
#include "NativeWriteScope.h"
#include "NativeHookEngine.h"
#include "NativeAuthority.h"
#include "HighMechanisms.h"
#include "EntryMemory.h"
#include "EntryWriteIndex.h"
#include "ClrSuspension.h"
#include <windows.h>
#include <atomic>
#include <cstring>

namespace {
constexpr uint32_t Count = static_cast<uint32_t>(lihuat::NativeGate::Count);
constexpr uint32_t Complete = (1u << Count) - 1;
constexpr size_t TrampolineBytes = 64;
struct Entry {
    void* target = nullptr;
    void* continuation = nullptr;
    lihuat::NativeHookLayout layout{};
    uint8_t original[8]{}, expected[8]{}, trampoline[TrampolineBytes]{};
    std::atomic<uint64_t> allowed{0}, rejected{0};
};
Entry entries[Count];
lihuat::EntryWriteIndex methodWrites, retainedWrites;
SRWLOCK installation = SRWLOCK_INIT;
std::atomic<uint32_t> phase{0}, enabled{0}, failure{UINT32_MAX};
std::atomic<bool> prepared{false}, protectTargets{false};
struct Exclusive {
    Exclusive() { AcquireSRWLockExclusive(&installation); }
    ~Exclusive() { ReleaseSRWLockExclusive(&installation); }
};
struct Shared {
    Shared() { AcquireSRWLockShared(&installation); }
    ~Shared() { ReleaseSRWLockShared(&installation); }
};
const lihuat::NativeGateBinding& Binding(uint32_t index) {
    constexpr uint32_t split = static_cast<uint32_t>(lihuat::NativeGate::WinProtect);
    return index < split ? lihuat::NativeSystemBindings()[index] : lihuat::NativeWin32Bindings()[index - split];
}
bool Matches(const void* address, const uint8_t* expected, size_t bytes) {
    uint8_t value[TrampolineBytes];
    return bytes <= sizeof(value) && lihuat::TryCopy(address, value, bytes) && memcmp(value, expected, bytes) == 0;
}
uint32_t EnabledMask() {
    uint32_t mask = 0;
    for (uint32_t i = 0; i != Count; ++i) {
        lihuat::NativeHookLayout layout{};
        if (entries[i].target && lihuat::DescribeNativeHook(entries[i].target, layout) && layout.enabled) mask |= 1u << i;
    }
    return mask;
}
bool Intact() {
    for (const Entry& entry : entries) {
        lihuat::NativeHookLayout layout{};
        if (!lihuat::DescribeNativeHook(entry.target, layout) || !layout.enabled
            || layout.patch != entry.layout.patch || layout.patchBytes != entry.layout.patchBytes
            || layout.trampoline != entry.layout.trampoline || layout.destination != entry.layout.destination
            || !Matches(entry.layout.patch, entry.expected, entry.layout.patchBytes)
            || !Matches(entry.continuation, entry.trampoline, sizeof(entry.trampoline))) return false;
    }
    return true;
}

int Prepare() {
    if (prepared.load(std::memory_order_acquire)) return 0;
    int result = lihuat::InitializeNativeHooks();
    if (result) return result;
    try {
        methodWrites.Reserve(Count * 2);
        retainedWrites.Reserve(Count + 2);
        for (uint32_t i = 0; i != Count; ++i) {
            const auto& binding = Binding(i);
            Entry& entry = entries[i];
            HMODULE module = GetModuleHandleW(binding.module);
            entry.target = module ? reinterpret_cast<void*>(GetProcAddress(module, binding.name)) : nullptr;
            if (!entry.target) { failure.store(i); result = 100; break; }
            result = lihuat::CreateNativeHook(entry.target, binding.callback, &entry.continuation);
            if (result || !lihuat::DescribeNativeHook(entry.target, entry.layout)
                || entry.layout.patchBytes > sizeof(entry.original)) { failure.store(i); if (!result) result = 101; break; }
            if (!lihuat::TryCopy(entry.layout.patch, entry.original, entry.layout.patchBytes)
                || !lihuat::TryCopy(entry.continuation, entry.trampoline, sizeof(entry.trampoline))) {
                failure.store(i); result = 102; break;
            }
            entry.expected[0] = 0xe9;
            intptr_t displacement = reinterpret_cast<intptr_t>(entry.layout.destination)
                - (reinterpret_cast<intptr_t>(entry.layout.patch) + 5);
            if (displacement < INT32_MIN || displacement > INT32_MAX) { failure.store(i); result = 103; break; }
            int32_t relative = static_cast<int32_t>(displacement);
            memcpy(entry.expected + 1, &relative, sizeof(relative));
            if (entry.layout.patchBytes == 7) { entry.expected[5] = 0xeb; entry.expected[6] = 0xf9; }
            methodWrites.Add(entry.layout.patch, entry.layout.patchBytes);
            methodWrites.Add(entry.target, 32);
            MEMORY_BASIC_INFORMATION memory{};
            if (VirtualQuery(entry.continuation, &memory, sizeof(memory)) != sizeof(memory)) {
                failure.store(i); result = 104; break;
            }
            retainedWrites.Add(memory.BaseAddress, memory.RegionSize);
        }
        if (!result) {
            const void* storage = nullptr; size_t bytes = 0;
            lihuat::NativeHookStorage(storage, bytes);
            retainedWrites.Add(storage, bytes);
            methodWrites.Seal(); retainedWrites.Seal();
            prepared.store(true, std::memory_order_release);
            return 0;
        }
    } catch (const std::bad_alloc&) { result = 105; }
    int released = lihuat::ReleaseUnusedNativeHooks();
    methodWrites.Clear(); retainedWrites.Clear();
    for (Entry& entry : entries) { entry.target = nullptr; entry.continuation = nullptr; entry.layout = {}; }
    return released ? 200 + released : result;
}
}

void* lihuat::NativeGateContinuation(NativeGate gate) { return entries[static_cast<uint32_t>(gate)].continuation; }
bool lihuat::AdmitNativeCall(NativeGate gate, bool allowed) {
    Entry& entry = entries[static_cast<uint32_t>(gate)];
    (allowed ? entry.allowed : entry.rejected).fetch_add(1, std::memory_order_relaxed);
    return allowed;
}
bool lihuat::NativeMethodGatesOverlap(const void* address, size_t length) {
    if (!prepared.load(std::memory_order_acquire)) return false;
    return retainedWrites.OwnsStorage(address, length) || retainedWrites.Overlaps(address, length)
        || methodWrites.OwnsStorage(address, length)
        || (protectTargets.load(std::memory_order_acquire) && methodWrites.Overlaps(address, length));
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_InstallMethodGates(uint64_t authority) {
    if (!lihuat::Authorized(authority)) return -10;
    if (!lihuat::NativeProtectionEnabled()) return -8;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    lihuat::CodeWriteScope codeWrite;
    if (!codeWrite) return -9;
    Exclusive lock;
    if (enabled.load(std::memory_order_acquire)) return enabled.load() == Complete && Intact() ? 1 : -4;
    phase.store(3, std::memory_order_release);
    failure.store(UINT32_MAX, std::memory_order_relaxed);
    int result = Prepare();
    if (result) { phase.store(0, std::memory_order_release); return -100 - result; }
    for (uint32_t i = 0; i != Count; ++i) {
        const Entry& entry = entries[i];
        if (!Matches(entry.layout.patch, entry.original, entry.layout.patchBytes)) {
            failure.store(i); phase.store(0, std::memory_order_release); return -4;
        }
    }
    protectTargets.store(true, std::memory_order_release);
    result = lihuat::SetNativeHooks(true);
    uint32_t mask = EnabledMask();
    enabled.store(mask, std::memory_order_release);
    if (!result && mask == Complete && Intact()) { phase.store(1, std::memory_order_release); return 1; }
    lihuat::SetNativeHooks(false);
    mask = EnabledMask();
    enabled.store(mask, std::memory_order_release);
    protectTargets.store(mask != 0, std::memory_order_release);
    phase.store(mask ? 2u : 0u, std::memory_order_release);
    return result ? -100 - result : -3;
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_RemoveMethodGates(uint64_t authority) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    lihuat::CodeWriteScope codeWrite;
    if (!codeWrite) return -9;
    Exclusive lock;
    if (!enabled.load(std::memory_order_acquire)) return 1;
    if (!Intact()) return -4;
    phase.store(3, std::memory_order_release);
    int result = lihuat::SetNativeHooks(false);
    uint32_t mask = EnabledMask();
    if (result || mask) {
        lihuat::SetNativeHooks(true);
        mask = EnabledMask();
        enabled.store(mask, std::memory_order_release);
        phase.store(mask == Complete && Intact() ? 1u : 2u, std::memory_order_release);
        return result ? -100 - result : -3;
    }
    enabled.store(0, std::memory_order_release);
    protectTargets.store(false, std::memory_order_release);
    phase.store(0, std::memory_order_release);
    return 1;
}

struct MethodGateStatus { uint32_t state, count, mask, failure; uint64_t allowed, rejected; };
extern "C" __declspec(dllexport) int __cdecl LihuatNative_MethodGateStatus(MethodGateStatus* status, uint32_t bytes) {
    if (bytes != sizeof(MethodGateStatus) || !lihuat::NativeOutputAllowed(status, bytes)) return 0;
    Shared lock;
    uint32_t state = phase.load(std::memory_order_acquire);
    if (state == 1 && !Intact()) state = 2;
    uint64_t allowed = 0, rejected = 0;
    for (const Entry& entry : entries) {
        allowed += entry.allowed.load(std::memory_order_relaxed);
        rejected += entry.rejected.load(std::memory_order_relaxed);
    }
    *status = {state, Count, enabled.load(std::memory_order_acquire), failure.load(std::memory_order_relaxed), allowed, rejected};
    return 1;
}
