#include "HighMechanisms.h"
#include <windows.h>
#include <atomic>
#include "ClrJitSlot.h"
#include "NativeMemoryOwnership.h"
#include "NativeWriteScope.h"

namespace {
SRWLOCK slotLock = SRWLOCK_INIT;
std::atomic<bool> prepared{false};
void* compilerObject = nullptr;
void** compilerSlot = nullptr;
void* control = nullptr;
void* downstream = nullptr;
std::atomic<void*> displaced{nullptr};
std::atomic<uint64_t> publications{0}, contentions{0}, failures{0};

struct Exclusive {
    Exclusive() { AcquireSRWLockExclusive(&slotLock); }
    ~Exclusive() { ReleaseSRWLockExclusive(&slotLock); }
};
void* Read(void* address) {
    __try { return *static_cast<void* volatile*>(address); }
    __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION || GetExceptionCode() == EXCEPTION_IN_PAGE_ERROR
        ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) { return nullptr; }
}

bool Publish() {
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return false;
    if (Read(compilerObject) != compilerSlot) { failures.fetch_add(1); return false; }
    if (Read(compilerSlot) == control) return true;
    DWORD protection = 0, ignored = 0;
    if (!VirtualProtect(compilerSlot, sizeof(void*), PAGE_READWRITE, &protection)) { failures.fetch_add(1); return false; }
    bool changed = false;
    void* previous = nullptr;
    for (int attempt = 0; attempt != 4; ++attempt) {
        previous = Read(compilerSlot);
        if (previous == control) break;
        if (InterlockedCompareExchangePointer(compilerSlot, control, previous) == previous) { changed = true; break; }
        contentions.fetch_add(1, std::memory_order_relaxed);
    }
    bool restored = VirtualProtect(compilerSlot, sizeof(void*), protection, &ignored) != 0;
    if (!restored) {
        if (changed) InterlockedCompareExchangePointer(compilerSlot, previous, control);
        VirtualProtect(compilerSlot, sizeof(void*), protection, &ignored);
        failures.fetch_add(1); return false;
    }
    if (changed) {
        displaced.store(previous, std::memory_order_relaxed);
        publications.fetch_add(1, std::memory_order_relaxed);
    }
    bool intact = Read(compilerObject) == compilerSlot && Read(compilerSlot) == control;
    if (!intact) failures.fetch_add(1);
    return intact;
}
}

bool lihuat::OwnCompilationSlot(void* compiler, void** slot, void* callback, void* continuation) {
    Exclusive lock;
    if (prepared.load(std::memory_order_relaxed)) {
        if (compilerObject != compiler || compilerSlot != slot || control != callback || downstream != continuation) return false;
    } else {
        compilerObject = compiler; compilerSlot = slot; control = callback; downstream = continuation;
        prepared.store(true, std::memory_order_release);
    }
    return Publish();
}

bool lihuat::CompilationSlotActive() {
    return prepared.load(std::memory_order_acquire) && Read(compilerObject) == compilerSlot && Read(compilerSlot) == control;
}

bool lihuat::RepairCompilationSlot() {
    if (CompilationSlotActive()) return true;
    if (!prepared.load(std::memory_order_acquire) || !NativeProtectionEnabled()) return false;
    Exclusive lock;
    // A later hook may call us. Rebasing the downstream pointer to that hook would make a cycle.
    return Publish();
}

bool lihuat::CompilationSlotOverlaps(const void* address, size_t length) {
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    if (!prepared.load(std::memory_order_acquire) || !start || !length || start > UINTPTR_MAX - length) return false;
    auto overlaps = [start, length](const void* target) {
        uintptr_t at = reinterpret_cast<uintptr_t>(target);
        return at < start + length && (at >= start || start - at < sizeof(void*));
    };
    return overlaps(compilerSlot) || overlaps(compilerObject);
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_JitSlotStatus(lihuat::JitSlotStatus* status, uint32_t bytes) {
    if (bytes != sizeof(lihuat::JitSlotStatus) || !lihuat::NativeOutputAllowed(status, bytes)) return 0;
    bool ready = prepared.load(std::memory_order_acquire);
    *status = {ready ? compilerSlot : nullptr, ready ? Read(compilerSlot) : nullptr,
        ready ? control : nullptr, ready ? downstream : nullptr, displaced.load(std::memory_order_relaxed),
        publications.load(std::memory_order_relaxed), contentions.load(std::memory_order_relaxed), failures.load(std::memory_order_relaxed),
        lihuat::CompilationSlotActive() ? 1u : 0u, 0u};
    return 1;
}
