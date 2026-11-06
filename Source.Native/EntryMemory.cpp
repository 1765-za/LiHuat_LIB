#include "EntryMemory.h"
#include <algorithm>

namespace lihuat {
namespace {
constexpr uintptr_t ArenaSize = 0x10000;
constexpr uintptr_t SlotLimit = 0x80000000;
uint8_t* nextSlot = nullptr;
uint8_t* endSlots = nullptr;

bool Reachable(intptr_t origin, intptr_t target) {
    const intptr_t distance = target - origin - 5;
    return distance >= INT32_MIN && distance <= INT32_MAX;
}

uint8_t* AllocateSlots() {
    for (uintptr_t cursor = ArenaSize; cursor < SlotLimit;) {
        MEMORY_BASIC_INFORMATION info = {};
        if (VirtualQuery(reinterpret_cast<void*>(cursor), &info, sizeof(info)) != sizeof(info)) return nullptr;
        const uintptr_t start = reinterpret_cast<uintptr_t>(info.BaseAddress);
        const uintptr_t end = start + (std::min)(info.RegionSize, SlotLimit - start);
        if (info.State == MEM_FREE && info.RegionSize >= ArenaSize) {
            const uintptr_t first = ((std::max)(start, cursor) + ArenaSize - 1) & ~(ArenaSize - 1);
            for (uintptr_t candidate = first; candidate + ArenaSize <= end; candidate += ArenaSize) {
                auto memory = static_cast<uint8_t*>(VirtualAlloc(reinterpret_cast<void*>(candidate), ArenaSize,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
                if (memory != nullptr) return memory;
            }
        }
        if (end <= cursor) return nullptr;
        cursor = end;
    }
    return nullptr;
}

// The registry lock serializes allocation. Published slots are immutable and live as long as Mono code.
uint8_t* StoreDestination(void* destination) {
    if (nextSlot == endSlots) {
        uint8_t* arena = AllocateSlots();
        if (arena == nullptr) return nullptr;
        nextSlot = arena;
        endSlots = arena + ArenaSize;
    }
    uint8_t* slot = nextSlot;
    nextSlot += sizeof(void*);
    InterlockedExchangePointer(reinterpret_cast<void* volatile*>(slot), destination);
    return slot;
}
}

int64_t ReadEntry(uint8_t* address) {
    return *reinterpret_cast<const volatile LONG64*>(address);
}

bool EntryBranch(uint8_t* address, void* destination, int64_t& branch) {
    const intptr_t target = reinterpret_cast<intptr_t>(destination);
    const intptr_t origin = reinterpret_cast<intptr_t>(address);
    uint8_t bytes[] = {0xe9, 0, 0, 0, 0, 0x90, 0x90, 0x90};
    if (!Reachable(origin, target)) {
        uint8_t* slot = StoreDestination(destination);
        if (slot == nullptr) return false;
        // JMP [disp32] uses an absolute SIB address, preserving registers, stack and the atomic eight-byte entry.
        bytes[0] = 0xff; bytes[1] = 0x24; bytes[2] = 0x25;
        const uint32_t absolute = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(slot));
        memcpy(bytes + 3, &absolute, sizeof(absolute));
    } else {
        const int32_t relative = static_cast<int32_t>(target - origin - 5);
        memcpy(bytes + 1, &relative, sizeof(relative));
    }
    memcpy(&branch, bytes, sizeof(branch));
    return true;
}

bool ExchangeEntry(uint8_t* address, int64_t expected, int64_t desired) {
    DWORD protection = 0;
    if (!VirtualProtect(address, 8, PAGE_EXECUTE_READWRITE, &protection)) return false;
    volatile LONG64* word = reinterpret_cast<volatile LONG64*>(address);
    bool changed = InterlockedCompareExchange64(word, desired, expected) == expected;
    bool flushed = changed && FlushInstructionCache(GetCurrentProcess(), address, 8) != 0;
    DWORD ignored = 0;
    if (!flushed && changed) {
        InterlockedCompareExchange64(word, expected, desired);
        FlushInstructionCache(GetCurrentProcess(), address, 8);
    }
    if (VirtualProtect(address, 8, protection, &ignored)) return flushed;
    if (changed) {
        InterlockedCompareExchange64(word, expected, desired);
        FlushInstructionCache(GetCurrentProcess(), address, 8);
    }
    VirtualProtect(address, 8, protection, &ignored);
    return false;
}
}
