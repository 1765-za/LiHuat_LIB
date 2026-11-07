#include <windows.h>
#include <cor.h>
#define CINTERFACE
#include <corprof.h>
#include <atomic>
#include "ClrProfilerRequests.h"
#include "NativeWriteScope.h"

namespace {
struct ProfilerInfo10Layout {
    ICorProfilerInfo8Vtbl base;
    void *nativeAddresses, *mapping, *codeInfo, *references, *frozen, *loh, *rejitInliners;
};
struct Slot { void** address = nullptr; void* previous = nullptr; void* callback = nullptr; };
Slot slots[4];
SRWLOCK installation = SRWLOCK_INIT;
std::atomic<bool> installed{false};

HRESULT STDMETHODCALLTYPE RequestRejit(void*, ULONG, ModuleID*, mdMethodDef*) { return E_ACCESSDENIED; }
HRESULT STDMETHODCALLTYPE RequestRevert(void*, ULONG, ModuleID*, mdMethodDef*, HRESULT*) { return E_ACCESSDENIED; }
HRESULT STDMETHODCALLTYPE RequestInliners(void*, DWORD, ULONG, ModuleID*, mdMethodDef*) { return E_ACCESSDENIED; }
HRESULT STDMETHODCALLTYPE SetIlBody(void*, ModuleID, mdMethodDef, LPCBYTE) { return E_ACCESSDENIED; }
void* Read(const Slot& slot) { return *static_cast<void* volatile*>(slot.address); }
bool Exchange(const Slot& slot, void* expected, void* desired) {
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return false;
    DWORD previous = 0, ignored = 0;
    if (!VirtualProtect(slot.address, sizeof(void*), PAGE_READWRITE, &previous)) return false;
    bool changed = InterlockedCompareExchangePointer(slot.address, desired, expected) == expected;
    if (VirtualProtect(slot.address, sizeof(void*), previous, &ignored)) return changed;
    if (changed) InterlockedCompareExchangePointer(slot.address, expected, desired);
    VirtualProtect(slot.address, sizeof(void*), previous, &ignored);
    return false;
}
}

bool lihuat::InstallProfilerRequests(void* information) {
    AcquireSRWLockExclusive(&installation);
    if (installed.load(std::memory_order_acquire)) {
        bool intact = ProfilerRequestsIntact();
        ReleaseSRWLockExclusive(&installation); return intact;
    }
    auto table = *static_cast<void***>(information);
    const size_t indices[] = {
        offsetof(ICorProfilerInfo4Vtbl, RequestReJIT) / sizeof(void*),
        offsetof(ICorProfilerInfo4Vtbl, RequestRevert) / sizeof(void*),
        offsetof(ProfilerInfo10Layout, rejitInliners) / sizeof(void*),
        offsetof(ICorProfilerInfo4Vtbl, SetILFunctionBody) / sizeof(void*)
    };
    void* callbacks[] = {reinterpret_cast<void*>(&RequestRejit), reinterpret_cast<void*>(&RequestRevert),
        reinterpret_cast<void*>(&RequestInliners), reinterpret_cast<void*>(&SetIlBody)};
    for (size_t i = 0; i != 4; ++i) slots[i] = {table + indices[i], table[indices[i]], callbacks[i]};
    size_t changed = 0;
    while (changed != 4 && Exchange(slots[changed], slots[changed].previous, slots[changed].callback)) ++changed;
    bool success = changed == 4;
    if (!success) {
        while (changed) { --changed; Exchange(slots[changed], slots[changed].callback, slots[changed].previous); }
    }
    installed.store(success, std::memory_order_release);
    ReleaseSRWLockExclusive(&installation);
    return success;
}

bool lihuat::ProfilerRequestsIntact() {
    if (!installed.load(std::memory_order_acquire)) return false;
    for (const Slot& slot : slots) if (Read(slot) != slot.callback) return false;
    return true;
}

bool lihuat::ProfilerRequestsOverlaps(const void* address, size_t length) {
    if (!installed.load(std::memory_order_acquire)) return false;
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    if (!start || !length || length > UINTPTR_MAX - start) return false;
    for (const Slot& slot : slots) {
        uintptr_t entry = reinterpret_cast<uintptr_t>(slot.address);
        if (entry < start + length && (entry >= start || start - entry < sizeof(void*))) return true;
    }
    return false;
}
