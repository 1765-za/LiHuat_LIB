#include <windows.h>
#include <atomic>
#include <cstdint>
#include "NativeMemoryOwnership.h"

namespace {
struct Range {
    std::atomic<uintptr_t> begin{0}, end{0};
    void Set(const void* address, size_t length) {
        uintptr_t start = reinterpret_cast<uintptr_t>(address);
        end.store(start + length, std::memory_order_relaxed);
        begin.store(start, std::memory_order_release);
    }
    bool Overlaps(uintptr_t start, uintptr_t limit) const {
        uintptr_t first = begin.load(std::memory_order_acquire);
        return first && first < limit && start < end.load(std::memory_order_relaxed);
    }
};
Range image, allocations[2];
INIT_ONCE initialization = INIT_ONCE_STATIC_INIT;
BOOL CALLBACK Initialize(PINIT_ONCE, PVOID, PVOID*) {
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&Initialize), &module)) return FALSE;
    auto base = reinterpret_cast<const uint8_t*>(module);
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    image.Set(base, nt->OptionalHeader.SizeOfImage);
    return TRUE;
}
}

bool lihuat::InitializeNativeImage() { return InitOnceExecuteOnce(&initialization, Initialize, nullptr, nullptr) != FALSE; }

void lihuat::OwnNativeAllocation(NativeAllocation kind, const void* address, size_t length) {
    allocations[static_cast<size_t>(kind)].Set(address, length);
}

bool lihuat::NativeMemoryOverlaps(const void* address, size_t length) {
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    if (!start || !length || length > UINTPTR_MAX - start) return false;
    uintptr_t end = start + length;
    return image.Overlaps(start, end) || allocations[0].Overlaps(start, end) || allocations[1].Overlaps(start, end);
}
