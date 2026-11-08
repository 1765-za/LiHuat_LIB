#include "LinuxPlatform.h"
#include <cstring>
#include <mutex>
#include <sys/mman.h>

namespace lihuat::linux_platform {
void** AllocateRelay(void* destination) noexcept {
    constexpr size_t arenaBytes = 0x10000;
    static std::mutex allocation;
    static void** arena = nullptr;
    static size_t used = 0;
    std::lock_guard<std::mutex> lock(allocation);
    if (!arena) {
        void* memory = mmap(nullptr, arenaBytes, PROT_READ | PROT_WRITE,
                            MAP_PRIVATE | MAP_ANONYMOUS | MAP_32BIT, -1, 0);
        if (memory == MAP_FAILED) return nullptr;
        uintptr_t start = reinterpret_cast<uintptr_t>(memory);
        if (!start || start >= 0x80000000u || arenaBytes > 0x80000000u - start) {
            munmap(memory, arenaBytes); return nullptr;
        }
        arena = static_cast<void**>(memory);
    }
    if (used == arenaBytes / sizeof(void*)) return nullptr;
    void** slot = arena + used++;
    __atomic_store_n(slot, destination, __ATOMIC_RELEASE);
    return slot;
}
bool Branch(void** relay, int64_t& encoding) noexcept {
    uintptr_t address = reinterpret_cast<uintptr_t>(relay);
    if (!address || address >= 0x80000000u || (address & 7) || !Readable(relay, sizeof(void*))) return false;
    // FF /4 SIB absolute disp32 sign-extends in 64-bit mode: require the low 2 GiB.
    uint8_t bytes[8] = {0xff, 0x24, 0x25, 0, 0, 0, 0, 0x90};
    uint32_t absolute = static_cast<uint32_t>(address);
    std::memcpy(bytes + 3, &absolute, sizeof(absolute));
    std::memcpy(&encoding, bytes, sizeof(encoding));
    return true;
}
}
