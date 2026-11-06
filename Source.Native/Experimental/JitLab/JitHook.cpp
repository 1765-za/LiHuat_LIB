#include "JitLab.h"
#include <atomic>
#include <cstring>

namespace {
jitlab::Compile previous = nullptr;
void** installedTable = nullptr;
std::atomic<bool> enabled{false};
std::atomic<uint64_t> callbacks{0};
bool installedOnce = false;
SRWLOCK installation = SRWLOCK_INIT;

struct RestoreIl {
    jitlab::MethodInfo* info;
    const uint8_t* code;
    uint32_t size, stack;
    explicit RestoreIl(jitlab::MethodInfo* value) : info(value), code(value->il), size(value->ilSize), stack(value->maxStack) {}
    ~RestoreIl() { info->il = code; info->ilSize = size; info->maxStack = stack; }
};
int __stdcall Compile(void* compiler, void* context, jitlab::MethodInfo* info, unsigned flags, uint8_t** entry, uint32_t* size) {
    DWORD incoming = GetLastError();
    if (!enabled.load(std::memory_order_acquire) || !info || !jitlab::HasRules()) {
        SetLastError(incoming);
        return previous(compiler, context, info, flags, entry, size);
    }
    jitlab::InfoProxy proxy{jitlab::ProxyTable(), context, info->method};
    jitlab::Rule rule;
    if (!jitlab::FindRule(info->method, rule)) {
        SetLastError(incoming);
        return previous(compiler, &proxy, info, flags, entry, size);
    }
    callbacks.fetch_add(1, std::memory_order_relaxed);
    jitlab::Sample sample{};
    sample.generation = rule.generation; sample.method = rule.method; sample.calls = 1;
    sample.originalIlSize = info->ilSize; sample.ehCount = info->ehCount;
    bool matches = info->il && info->ilSize == rule.ilSize && std::memcmp(info->il, rule.expected, rule.ilSize) == 0;
    sample.matched = matches ? 1u : 0u;
    RestoreIl restore(info);
    uint8_t replacement[6] = {0x20, 0, 0, 0, 0, 0x2a};
    if (rule.mode == jitlab::Mode::ReturnInt32 || rule.mode == jitlab::Mode::SkipVoid) {
        if (!matches || info->ehCount) sample.mismatches = 1;
        else {
            if (rule.mode == jitlab::Mode::ReturnInt32) {
                std::memcpy(replacement + 1, &rule.value, sizeof(rule.value));
                info->il = replacement; info->ilSize = 6; info->maxStack = 1;
            } else { info->il = replacement + 5; info->ilSize = 1; info->maxStack = 0; }
            sample.rewrites = 1;
        }
    }
    sample.submittedIlSize = info->ilSize;
    SetLastError(incoming);
    int result = previous(compiler, &proxy, info, flags, entry, size);
    DWORD lastError = GetLastError();
    sample.result = result;
    if (result == 0 && entry && size) {
        sample.executable = *entry; sample.nativeSize = *size;
        jitlab::CaptureCode(proxy, sample);
        if (rule.mode == jitlab::Mode::PatchInt32Constant) {
            if (matches && !info->ehCount && jitlab::PatchConstant(rule, sample)) {
                sample.rewrites = 1;
                jitlab::CaptureCode(proxy, sample);
            } else sample.mismatches = 1;
        }
    }
    jitlab::Record(rule, sample);
    SetLastError(lastError);
    return result;
}
bool Exchange(void** slot, void* expected, void* replacement) {
    DWORD protection, ignored;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &protection)) return false;
    bool changed = InterlockedCompareExchangePointer(slot, replacement, expected) == expected;
    bool restored = VirtualProtect(slot, sizeof(void*), protection, &ignored) != 0;
    if (changed && !restored) {
        InterlockedCompareExchangePointer(slot, expected, replacement);
        VirtualProtect(slot, sizeof(void*), protection, &ignored);
        return false;
    }
    return changed;
}
}
LAB_API int __cdecl JitLab_Install(uint32_t abi) {
    if (abi != jitlab::Abi || sizeof(void*) != 8) return -1;
    AcquireSRWLockExclusive(&installation);
    if (installedOnce) { ReleaseSRWLockExclusive(&installation); return 0; }
    HMODULE jit = GetModuleHandleW(L"clrjit.dll");
    auto getJit = jit ? reinterpret_cast<void*(__stdcall*)()>(GetProcAddress(jit, "getJit")) : nullptr;
    if (!getJit) { ReleaseSRWLockExclusive(&installation); return -2; }
    void* compiler = getJit();
    void** table = *static_cast<void***>(compiler);
    GUID version{};
    reinterpret_cast<void(__stdcall*)(void*, GUID*)>(table[2])(compiler, &version);
    const GUID expected = {0x7a8cbc56, 0x9e19, 0x4321, {0x80, 0xb9, 0xa0, 0xd2, 0xc5, 0x78, 0xc9, 0x45}};
    if (version != expected || !jitlab::InitializeProxy()) { ReleaseSRWLockExclusive(&installation); return -3; }
    HMODULE own;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&Compile), &own)) { ReleaseSRWLockExclusive(&installation); return -4; }
    previous = reinterpret_cast<jitlab::Compile>(table[0]);
    enabled.store(true, std::memory_order_release);
    bool installed = Exchange(table, reinterpret_cast<void*>(previous), reinterpret_cast<void*>(&Compile));
    if (installed) { installedTable = table; installedOnce = true; }
    else enabled.store(false, std::memory_order_release);
    ReleaseSRWLockExclusive(&installation);
    return installed ? 1 : -5;
}
LAB_API int __cdecl JitLab_Remove() {
    AcquireSRWLockExclusive(&installation);
    enabled.store(false, std::memory_order_release);
    bool removed = installedTable && Exchange(installedTable, reinterpret_cast<void*>(&Compile), reinterpret_cast<void*>(previous));
    if (removed) installedTable = nullptr;
    ReleaseSRWLockExclusive(&installation);
    return removed ? 1 : 0;
}
struct LabStatus { uint32_t enabled, topOwned; uint64_t callbacks; void* top; void* prior; };
LAB_API int __cdecl JitLab_Status(LabStatus* value, uint32_t size) {
    if (!value || size != sizeof(LabStatus)) return 0;
    AcquireSRWLockShared(&installation);
    void* top = installedTable ? *reinterpret_cast<void* volatile*>(installedTable) : nullptr;
    *value = {enabled.load() ? 1u : 0u, top == reinterpret_cast<void*>(&Compile) ? 1u : 0u,
        callbacks.load(std::memory_order_relaxed), top, reinterpret_cast<void*>(previous)};
    ReleaseSRWLockShared(&installation);
    return 1;
}
