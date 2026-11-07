#include "EntryMemory.h"
#include <algorithm>
#include <cstring>
#include "ClrSuspension.h"
#include "NativeMemoryOwnership.h"
#include "NativeWriteScope.h"

namespace lihuat {
bool Readable(const void* address, size_t size, bool executable) {
    NativeWriteScope nativeWrite;
    if (!nativeWrite) return false;
    MEMORY_BASIC_INFORMATION memory{};
    if (!address || !size || VirtualQuery(address, &memory, sizeof(memory)) != sizeof(memory)
        || memory.State != MEM_COMMIT || (memory.Protect & (PAGE_NOACCESS | PAGE_GUARD))) return false;
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    uintptr_t base = reinterpret_cast<uintptr_t>(memory.BaseAddress);
    if (start < base || size > memory.RegionSize || start - base > memory.RegionSize - size) return false;
    return !executable || (memory.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY));
}

bool TryCopy(const void* address, void* destination, size_t size) {
    NativeWriteScope nativeWrite;
    if (!nativeWrite) return false;
    SIZE_T copied = 0;
    return address && size && ReadProcessMemory(GetCurrentProcess(), address, destination, size, &copied) && copied == size;
}
bool TryRead(const uint8_t* address, int64_t& value) { return TryCopy(address, &value, sizeof(value)); }

bool EntryRoute::Matches(void* precode) const {
    if (entry != precode || !body) return false;
    for (size_t i = 0; i < count; ++i) {
        const Observation& value = observations[i];
        uint8_t bytes[sizeof(value.bytes)];
        if (!TryCopy(value.address, bytes, value.size) || memcmp(bytes, value.bytes, value.size) != 0) return false;
    }
    return true;
}

const uint8_t* EntryRoute::Observe(const void* address, size_t size, size_t writeSize) {
    if (count == MaxObservations || size > sizeof(observations[0].bytes)) return nullptr;
    Observation& value = observations[count];
    if (!TryCopy(address, value.bytes, size)) return nullptr;
    value.address = static_cast<const uint8_t*>(address);
    value.size = static_cast<uint8_t>(size);
    value.writeSize = static_cast<uint8_t>(writeSize ? writeSize : size);
    ++count;
    return value.bytes;
}

bool CodeEntry(void* precode, void**& slot, uint8_t*& body, EntryRoute* route, bool* pending) {
    if (pending) *pending = false;
    if (route && route->Matches(precode)) { slot = route->slot; body = route->body; return true; }
    if (route) { route->count = 0; route->body = nullptr; ++route->revision; }
    auto entry = static_cast<uint8_t*>(precode);
    if (!Readable(entry, 6, true)) return false;
    uint8_t direct[24];
    const uint8_t* entryBytes = route ? route->Observe(entry, 6) : TryCopy(entry, direct, 6) ? direct : nullptr;
    if (!entryBytes) return false;
    if (entryBytes[0] != 0xff || entryBytes[1] != 0x25) return false;
    int32_t displacement;
    memcpy(&displacement, entryBytes + 2, sizeof(displacement));
    slot = reinterpret_cast<void**>(entry + 6 + displacement);
    if ((reinterpret_cast<uintptr_t>(slot) & 7) || !Readable(slot, sizeof(void*))) return false;
    if (route) {
        const uint8_t* bytes = route->Observe(slot, sizeof(void*));
        if (!bytes) return false;
        memcpy(&body, bytes, sizeof(body));
    } else if (!TryCopy(slot, &body, sizeof(body))) return false;
    if (body == entry + 6) { if (pending) *pending = true; return false; }
    for (size_t hop = 0; Readable(body, 24, true); ++hop) {
        const uint8_t* bodyBytes = route ? route->Observe(body, 24, 16) : TryCopy(body, direct, 24) ? direct : nullptr;
        if (!bodyBytes) return false;
        const uint8_t prefix[] = {0x48, 0x8b, 0x05};
        const uint8_t branch[] = {0x66, 0xff, 0x08, 0x74, 0x06, 0xff, 0x25};
        bool precodeThunk = bodyBytes[0] == 0xff && bodyBytes[1] == 0x25 && bodyBytes[6] == 0x4c && bodyBytes[7] == 0x8b
            && bodyBytes[8] == 0x15 && bodyBytes[13] == 0xff && bodyBytes[14] == 0x25;
        bool countingThunk = memcmp(bodyBytes, prefix, 3) == 0 && memcmp(bodyBytes + 7, branch, 7) == 0
            && bodyBytes[18] == 0xff && bodyBytes[19] == 0x25;
        if (!precodeThunk && !countingThunk) break;
        if (route) route->observations[route->count - 1].writeSize = 24;
        if (hop == EntryRoute::MaxHops) return false;
        int32_t targetOffset;
        int instructionEnd = precodeThunk ? 6 : 18;
        memcpy(&targetOffset, bodyBytes + instructionEnd - 4, sizeof(targetOffset));
        auto target = reinterpret_cast<void**>(body + instructionEnd + targetOffset);
        if ((reinterpret_cast<uintptr_t>(target) & 7) || !Readable(target, sizeof(void*))) return false;
        uint8_t* prestub = precodeThunk ? body + 6 : nullptr;
        if (route) {
            const uint8_t* bytes = route->Observe(target, sizeof(void*));
            if (!bytes) return false;
            memcpy(&body, bytes, sizeof(body));
        } else if (!TryCopy(target, &body, sizeof(body))) return false;
        if (body == prestub && prestub) { if (pending) *pending = true; return false; }
    }
    if (body == precode || (reinterpret_cast<uintptr_t>(body) & 7) || !Readable(body, 8, true)) return false;
    if (route) {
        if (!route->Observe(body, 8)) return false;
        route->entry = precode; route->slot = slot; route->body = body;
    }
    return true;
}

bool Exchange(uint8_t* address, int64_t expected, int64_t desired) {
    if (!CodeWriteActive()) return false;
    return ExchangeUnpublished(address, address, expected, desired);
}

bool ExchangeUnpublished(uint8_t* address, uint8_t* writable, int64_t expected, int64_t desired) {
    NativeWriteScope nativeWrite;
    if (!nativeWrite) return false;
    if ((reinterpret_cast<uintptr_t>(address) & 7) || !Readable(address, 8, true)) return false;
    if (!writable || (reinterpret_cast<uintptr_t>(writable) & 7) || !Readable(writable, 8)) return false;
    DWORD previous = 0, ignored = 0;
    if (!VirtualProtect(writable, 8, PAGE_EXECUTE_READWRITE, &previous)) return false;
    auto word = reinterpret_cast<volatile LONG64*>(writable);
    bool changed = InterlockedCompareExchange64(word, desired, expected) == expected;
    bool flushed = changed && FlushInstructionCache(GetCurrentProcess(), address, 8);
    if (flushed && VirtualProtect(writable, 8, previous, &ignored)) return true;
    if (changed) {
        InterlockedCompareExchange64(word, expected, desired);
        FlushInstructionCache(GetCurrentProcess(), address, 8);
    }
    VirtualProtect(writable, 8, previous, &ignored);
    return false;
}

void** AllocateRelay(void* destination) {
    constexpr uintptr_t arenaSize = 0x10000, limit = 0x80000000;
    static void** arena = nullptr;
    static size_t used = 0;
    if (!arena) {
        for (uintptr_t cursor = arenaSize; cursor < limit;) {
            MEMORY_BASIC_INFORMATION memory{};
            if (!VirtualQuery(reinterpret_cast<void*>(cursor), &memory, sizeof(memory))) return nullptr;
            uintptr_t end = reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize;
            if (end <= cursor) return nullptr;
            if (memory.State == MEM_FREE) {
                uintptr_t first = (cursor + arenaSize - 1) & ~(arenaSize - 1);
                if (first < limit && end - first >= arenaSize)
                    arena = static_cast<void**>(VirtualAlloc(reinterpret_cast<void*>(first), arenaSize,
                        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
                if (arena) {
                    OwnNativeAllocation(NativeAllocation::Relay, arena, arenaSize);
                    break;
                }
            }
            cursor = end;
        }
    }
    if (!arena || used == arenaSize / sizeof(void*)) return nullptr;
    // A caller can already have fetched the jump before removal. Slots are never reused.
    void** slot = arena + used++;
    InterlockedExchangePointer(slot, destination);
    return slot;
}

int64_t Branch(void** relay) {
    uint8_t code[8] = {0xff, 0x24, 0x25, 0, 0, 0, 0, 0x90};
    uint32_t address = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(relay));
    memcpy(code + 3, &address, sizeof(address));
    int64_t result;
    memcpy(&result, code, sizeof(result));
    return result;
}
}
