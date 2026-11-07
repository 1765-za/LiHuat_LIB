#pragma once
#include <windows.h>
#include <stdint.h>

namespace lihuat {
struct EntryRoute {
    static constexpr size_t MaxHops = 8;
    static constexpr size_t MaxObservations = 2 + MaxHops * 2 + 2;
    struct Observation { const uint8_t* address; uint8_t bytes[24]; uint8_t size, writeSize; };
    Observation observations[MaxObservations]{};
    size_t count = 0;
    uint64_t revision = 0;
    void* entry = nullptr;
    void** slot = nullptr;
    uint8_t* body = nullptr;
    bool Matches(void* precode) const;
    const uint8_t* Observe(const void* address, size_t size, size_t writeSize = 0);
};
bool Readable(const void* address, size_t size, bool executable = false);
bool TryCopy(const void* address, void* destination, size_t size);
bool TryRead(const uint8_t* address, int64_t& value);
bool CodeEntry(void* precode, void**& slot, uint8_t*& body, EntryRoute* route = nullptr, bool* pending = nullptr);
bool Exchange(uint8_t* address, int64_t expected, int64_t desired);
bool ExchangeUnpublished(uint8_t* address, uint8_t* writable, int64_t expected, int64_t desired);
void** AllocateRelay(void* destination);
int64_t Branch(void** relay);
}
