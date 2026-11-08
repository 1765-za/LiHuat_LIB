#pragma once
#include <cstddef>
#include <cstdint>

#if !defined(__linux__) || !defined(__x86_64__)
#error This platform implementation requires Linux x86_64.
#endif

namespace lihuat::linux_platform {
struct Region {
    uintptr_t begin = 0, end = 0;
    int protection = 0;
    bool privateMapping = false;
};
size_t PageSize() noexcept;
bool Query(const void* address, Region& region) noexcept;
bool Readable(const void* address, size_t bytes, bool executable = false) noexcept;
// Unlike memcpy, invalid/unmapped remote or local pointers report failure instead of SIGSEGV.
bool TryCopy(const void* address, void* destination, size_t bytes) noexcept;
bool RandomBytes(void* destination, size_t bytes) noexcept;

// ONLY for code not visible to another thread. This does not suspend CoreCLR or
// authorize a write. A live-code caller must first provide real runtime suspension.
// This first port accepts only a single private mapping (executable == writable);
// CoreCLR shared/double-mapped code heaps require a separate validated implementation.
// Caller owns mapping lifetime and must exclude concurrent mprotect/munmap.
bool ExchangeUnpublished(uint8_t* executable, uint8_t* writable,
                         int64_t expected, int64_t desired) noexcept;
// Process-lifetime slots, never reused: an in-flight jump may still hold an old slot.
void** AllocateRelay(void* destination) noexcept;
bool Branch(void** relay, int64_t& encoding) noexcept;

struct Forwarders { uint8_t* code = nullptr; size_t bytes = 0, slots = 0; };
// Receiver layout is { vtable, originalReceiver }. Only validated signatures whose
// receiver is RDI: no varargs and no hidden structure-return buffer before the receiver.
// JIT vtable slot layout/version must be checked separately by the runtime backend.
bool CreateRdiReceiverForwarders(size_t slots, Forwarders& result) noexcept;
// Only call before publication, or after proving no caller can retain a thunk.
bool ReleaseUnpublishedForwarders(Forwarders& forwarders) noexcept;
bool ImageContains(const void* anchor, const void* address, size_t bytes) noexcept;
// Resolve only an already-loaded runtime module, then pin it for callback lifetime.
void* FindLoadedSymbol(const char* module, const char* symbol) noexcept;
}
