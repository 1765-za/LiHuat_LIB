#include <windows.h>
#include <cor.h>
#define CINTERFACE
#include <corprof.h>
#include <atomic>
#include "ClrProfilerRequests.h"
#include "GenericBodies.h"

namespace {
using Rejit = HRESULT(STDMETHODCALLTYPE*)(void*, ULONG, ModuleID*, mdMethodDef*);
using Revert = HRESULT(STDMETHODCALLTYPE*)(void*, ULONG, ModuleID*, mdMethodDef*, HRESULT*);
using Inliners = HRESULT(STDMETHODCALLTYPE*)(void*, DWORD, ULONG, ModuleID*, mdMethodDef*);
using SetBody = HRESULT(STDMETHODCALLTYPE*)(void*, ModuleID, mdMethodDef, LPCBYTE);
struct ProfilerInfo10Layout {
    ICorProfilerInfo8Vtbl base;
    void *nativeAddresses, *mapping, *codeInfo, *references, *frozen, *loh, *rejitInliners;
};
struct Slot { void** address = nullptr; void* previous = nullptr; void* callback = nullptr; };
Slot slots[4];
SRWLOCK installation = SRWLOCK_INIT;
std::atomic<bool> installed{false};

bool Allowed(ULONG count, ModuleID* modules, mdMethodDef* methods) {
    return !count || !modules || !methods || lihuat::GenericProfilerMutationAllowed(count, modules, methods);
}
HRESULT STDMETHODCALLTYPE RequestRejit(void* self, ULONG count, ModuleID* modules, mdMethodDef* methods) {
    if (!Allowed(count, modules, methods)) return E_ACCESSDENIED;
    return reinterpret_cast<Rejit>(slots[0].previous)(self, count, modules, methods);
}
HRESULT STDMETHODCALLTYPE RequestRevert(void* self, ULONG count, ModuleID* modules, mdMethodDef* methods, HRESULT* status) {
    if (!Allowed(count, modules, methods)) return E_ACCESSDENIED;
    return reinterpret_cast<Revert>(slots[1].previous)(self, count, modules, methods, status);
}
HRESULT STDMETHODCALLTYPE RequestInliners(void* self, DWORD flags, ULONG count, ModuleID* modules, mdMethodDef* methods) {
    if (!Allowed(count, modules, methods)) return E_ACCESSDENIED;
    return reinterpret_cast<Inliners>(slots[2].previous)(self, flags, count, modules, methods);
}
HRESULT STDMETHODCALLTYPE SetIlBody(void* self, ModuleID module, mdMethodDef method, LPCBYTE body) {
    if (!Allowed(1, &module, &method)) return E_ACCESSDENIED;
    return reinterpret_cast<SetBody>(slots[3].previous)(self, module, method, body);
}
void* Read(const Slot& slot) { return *static_cast<void* volatile*>(slot.address); }
bool Exchange(const Slot& slot, void* expected, void* desired) {
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
