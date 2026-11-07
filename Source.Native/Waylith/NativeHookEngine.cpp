#include "NativeHookEngine.h"
#include <cstdint>
// MinHook encodes its backward short jump in an unsigned byte.
#pragma warning(push)
#pragma warning(disable:4310)
#include "../ThirdParty/MinHook/src/hook.c"
#pragma warning(pop)

int lihuat::InitializeNativeHooks() { return MH_Initialize(); }
int lihuat::CreateNativeHook(void* target, void* callback, void** original) { return MH_CreateHook(target, callback, original); }
int lihuat::SetNativeHooks(bool enabled) { return enabled ? MH_EnableHook(MH_ALL_HOOKS) : MH_DisableHook(MH_ALL_HOOKS); }
int lihuat::ReleaseUnusedNativeHooks() { return MH_Uninitialize(); }

bool lihuat::DescribeNativeHook(void* target, NativeHookLayout& layout) {
    UINT index = FindHookEntry(target);
    if (index == INVALID_HOOK_POS) return false;
    const HOOK_ENTRY& entry = g_hooks.pItems[index];
    layout = {static_cast<uint8_t*>(entry.pTarget) - (entry.patchAbove ? sizeof(JMP_REL) : 0),
        entry.patchAbove ? sizeof(JMP_REL) + sizeof(JMP_REL_SHORT) : sizeof(JMP_REL),
        entry.pTrampoline, entry.pDetour, entry.isEnabled != 0};
    return true;
}

void lihuat::NativeHookStorage(const void*& address, size_t& bytes) {
    address = g_hooks.pItems;
    bytes = g_hooks.capacity * sizeof(HOOK_ENTRY);
}
