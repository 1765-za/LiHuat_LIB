#include <windows.h>
#include <atomic>
#include "HighMechanisms.h"
#include "NativeAuthority.h"
#include "ClrCompilation.h"

namespace {
std::atomic<uint32_t> selection{UINT32_MAX};
}

bool lihuat::NativeProtectionEnabled() {
    uint32_t disabled = selection.load(std::memory_order_acquire);
    return disabled == UINT32_MAX || (disabled & 9u) == 0;
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_ConfigureHighMechanisms(uint64_t authority, uint32_t disabled) {
    if (!lihuat::Authorized(authority) || (disabled & ~15u)) return 0;
    uint32_t expected = UINT32_MAX;
    if (selection.load(std::memory_order_acquire) == UINT32_MAX && lihuat::CompilationObserverActive()) return 0;
    return selection.compare_exchange_strong(expected, disabled, std::memory_order_release) || expected == disabled ? 1 : 0;
}
