#include "LinuxPlatform.h"
#include <cstring>
#include <sys/mman.h>

namespace lihuat::linux_platform {
bool CreateRdiReceiverForwarders(size_t slots, Forwarders& result) noexcept {
    if (result.code || !slots || slots > 4096) return false;
    constexpr size_t stride = 16;
    size_t bytes = slots * stride;
    void* allocation = mmap(nullptr, bytes, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (allocation == MAP_FAILED) return false;
    auto code = static_cast<uint8_t*>(allocation);
    for (size_t i = 0; i < slots; ++i) {
        // For the explicitly supported signatures, SysV receiver is RDI, not RCX.
        // Hidden-structure-return/RSI receivers are not supported. Preserve other integer,
        // floating-point and stack arguments while replacing only the receiver.
        uint8_t stub[stride] = {0x48, 0x8b, 0x7f, 0x08, 0x48, 0x8b, 0x07, 0xff, 0xa0, 0, 0, 0, 0, 0xcc, 0xcc, 0xcc};
        uint32_t offset = static_cast<uint32_t>(i * sizeof(void*));
        std::memcpy(stub + 9, &offset, sizeof(offset));
        std::memcpy(code + i * stride, stub, stride);
    }
    if (mprotect(code, bytes, PROT_READ | PROT_EXEC) != 0) { munmap(code, bytes); return false; }
    __builtin___clear_cache(reinterpret_cast<char*>(code), reinterpret_cast<char*>(code + bytes));
    result = {code, bytes, slots};
    return true;
}
bool ReleaseUnpublishedForwarders(Forwarders& forwarders) noexcept {
    if (!forwarders.code || !forwarders.bytes || munmap(forwarders.code, forwarders.bytes) != 0) return false;
    forwarders = {};
    return true;
}
}
