#include <windows.h>
#include <atomic>
#include "ClrCompilation.h"
#include "ClrInlining.h"
#include "ClrJitSlot.h"
#include "NativeMemoryOwnership.h"
#include "GenericBodies.h"

namespace {
using MethodInfoPrefix = lihuat::MethodBodyInput;
using CompileMethod = int(__stdcall*)(void*, void*, MethodInfoPrefix*, unsigned, uint8_t**, uint32_t*);
CompileMethod previous = nullptr;
std::atomic<uint64_t> notifications{0};
std::atomic<uint64_t> guarded{0};
std::atomic<uint64_t> osr{0}, rejected{0};
std::atomic<uint64_t> missingFlags{0}, missingAllocation{0}, writeRejected{0};
bool prepared = false;
SRWLOCK installationLock = SRWLOCK_INIT;

int __stdcall Compile(void* compiler, void* context, MethodInfoPrefix* method, unsigned flags,
    uint8_t** entry, uint32_t* size) {
    if (context && method && !lihuat::GenericBodyAllowed(context, *method)) {
        if (entry) *entry = nullptr;
        if (size) *size = 0;
        return static_cast<int>(0x80000001);
    }
    uint64_t revision = lihuat::RegistrationRevision();
    uint64_t generation = method ? lihuat::CompilationGeneration(method->method) : 0;
    lihuat::InliningContext proxy{lihuat::InliningTable(), context};
    bool protect = context && lihuat::HasProtectedEntries();
    if (protect) guarded.fetch_add(1, std::memory_order_relaxed);
    int result = previous(compiler, context ? &proxy : context, method, flags, entry, size);
    DWORD error = GetLastError();
    if (result == 0 && method && entry && *entry && size && *size) {
        notifications.fetch_add(1, std::memory_order_relaxed);
        if (context && !lihuat::GenericBodyCompiled(context, method->method, *entry, *size)) {
            *entry = nullptr; *size = 0; SetLastError(error);
            return static_cast<int>(0x80000003);
        }
        if ((generation || revision != lihuat::RegistrationRevision()) && lihuat::CompilationGeneration(method->method)) {
            constexpr uint64_t Osr = uint64_t{1} << 7;
            if (proxy.flagsRead && (proxy.flags & Osr)) osr.fetch_add(1, std::memory_order_relaxed);
            else {
                uintptr_t address = reinterpret_cast<uintptr_t>(*entry), start = reinterpret_cast<uintptr_t>(proxy.hot);
                bool allocation = start && proxy.writable && address >= start && address - start <= proxy.capacity && proxy.capacity - (address - start) >= 8;
                if (!proxy.flagsRead || !allocation || !lihuat::Compiled(method->method, generation, *entry,
                    proxy.capacity - (address - start), proxy.writable + (address - start))) {
                    if (!proxy.flagsRead) missingFlags.fetch_add(1, std::memory_order_relaxed);
                    else if (!allocation) missingAllocation.fetch_add(1, std::memory_order_relaxed);
                    else writeRejected.fetch_add(1, std::memory_order_relaxed);
                    rejected.fetch_add(1, std::memory_order_relaxed);
                    *entry = nullptr; *size = 0;
                    result = static_cast<int>(0x80000003);
                }
            }
        }
    }
    SetLastError(error);
    return result;
}
}

struct CompilationAdmission { uint64_t osr, rejected, missingFlags, missingAllocation, writeRejected; };
extern "C" __declspec(dllexport) int __cdecl LihuatNative_CompilationAdmission(CompilationAdmission* status, uint32_t bytes) {
    if (bytes != sizeof(CompilationAdmission) || !lihuat::NativeOutputAllowed(status, bytes)) return 0;
    *status = {osr.load(std::memory_order_relaxed), rejected.load(std::memory_order_relaxed),
        missingFlags.load(std::memory_order_relaxed), missingAllocation.load(std::memory_order_relaxed), writeRejected.load(std::memory_order_relaxed)};
    return 1;
}

namespace lihuat {
bool InstallCompilationObserver(void* continuation) {
    AcquireSRWLockExclusive(&installationLock);
    if (prepared) {
        bool result = (!continuation || reinterpret_cast<void*>(previous) == continuation) && RepairCompilationSlot();
        ReleaseSRWLockExclusive(&installationLock); return result;
    }
    HMODULE jitModule = GetModuleHandleW(L"clrjit.dll");
    auto getJit = jitModule ? reinterpret_cast<void*(__stdcall*)()>(GetProcAddress(jitModule, "getJit")) : nullptr;
    if (!getJit) { ReleaseSRWLockExclusive(&installationLock); return false; }
    void* jit = getJit();
    void** table = *static_cast<void***>(jit);
    GUID version{};
    reinterpret_cast<void(__stdcall*)(void*, GUID*)>(table[2])(jit, &version);
    const GUID expected = {0x7a8cbc56, 0x9e19, 0x4321, {0x80, 0xb9, 0xa0, 0xd2, 0xc5, 0x78, 0xc9, 0x45}};
    if (version != expected) { ReleaseSRWLockExclusive(&installationLock); return false; }
    if (!InitializeInlining()) { ReleaseSRWLockExclusive(&installationLock); return false; }
    HMODULE ownModule;
    // The CLR can retain this callback until process shutdown.
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&Compile), &ownModule)) { ReleaseSRWLockExclusive(&installationLock); return false; }
    previous = reinterpret_cast<CompileMethod>(continuation ? continuation : table[0]);
    prepared = true;
    bool changed = OwnCompilationSlot(jit, table, reinterpret_cast<void*>(&Compile), reinterpret_cast<void*>(previous));
    ReleaseSRWLockExclusive(&installationLock);
    return changed;
}
bool CompilationObserverActive() { return CompilationSlotActive(); }
uint64_t CompilationNotifications() { return notifications.load(std::memory_order_relaxed); }
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_CompilationStatus(lihuat::CompilationStatus* status, uint32_t bytes) {
    if (bytes != sizeof(lihuat::CompilationStatus) || !lihuat::NativeOutputAllowed(status, bytes)) return 0;
    *status = {lihuat::CompilationNotifications(), guarded.load(std::memory_order_relaxed),
        lihuat::BlockedInlines(), lihuat::StaleCompilations(), lihuat::ProtectedEntryCount(), lihuat::CompilationObserverActive() ? 1u : 0u};
    return 1;
}
