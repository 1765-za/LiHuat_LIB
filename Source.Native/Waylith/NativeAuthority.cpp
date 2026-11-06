#include <windows.h>
#include <bcrypt.h>
#include <atomic>
#include "NativeAuthority.h"
#include "NativeMemoryOwnership.h"

namespace {
std::atomic<uint64_t> owner{0}, rejected{0};
}

bool lihuat::Authorized(uint64_t authority) {
    if (authority && authority == owner.load(std::memory_order_acquire)) return true;
    rejected.fetch_add(1, std::memory_order_relaxed);
    return false;
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_Claim(uint64_t* authority) {
    if (!lihuat::InitializeNativeImage() || !lihuat::NativeOutputAllowed(authority, sizeof(*authority))) return 0;
    if (owner.load(std::memory_order_acquire)) { *authority = 0; return 0; }
    uint64_t candidate = 0, empty = 0;
    if (BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(&candidate), sizeof(candidate), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0
        || !candidate || !owner.compare_exchange_strong(empty, candidate, std::memory_order_release)) {
        *authority = 0; return 0;
    }
    *authority = candidate;
    return 1;
}

extern "C" __declspec(dllexport) uint64_t __cdecl LihuatNative_RejectedMutations() {
    return rejected.load(std::memory_order_relaxed);
}
