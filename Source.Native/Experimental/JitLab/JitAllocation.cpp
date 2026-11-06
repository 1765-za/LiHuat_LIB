#include "JitLab.h"
#include <algorithm>
#include <cstring>

namespace {
void* table[jitlab::InfoSlots]{};
uint8_t* forwarding = nullptr;
int __stdcall CanInline(jitlab::InfoProxy* proxy, void* caller, void* callee) {
    auto original = *static_cast<void***>(proxy->original);
    int result = reinterpret_cast<int(__stdcall*)(void*, void*, void*)>(original[jitlab::CanInlineSlot])(proxy->original, caller, callee);
    jitlab::Policy policy{};
    if (result < 0 || !jitlab::QueryPolicy(callee, 0, policy)) return result;
    bool blocked = policy.mode != static_cast<uint32_t>(jitlab::Mode::Observe);
    jitlab::RecordInline(callee, policy.generation, caller, proxy->rootMethod, blocked);
    if (!blocked) return result;
    constexpr int inlineFail = -1;
    reinterpret_cast<void(__stdcall*)(void*, void*, void*, int, const char*)>(original[jitlab::ReportInlineSlot])(
        proxy->original, caller, callee, inlineFail, "Lihuat experimental method rule");
    return inlineFail;
}
void __stdcall Allocate(jitlab::InfoProxy* proxy, jitlab::AllocArgs* args) {
    auto original = *static_cast<void***>(proxy->original);
    reinterpret_cast<void(__stdcall*)(void*, jitlab::AllocArgs*)>(original[jitlab::AllocMemSlot])(proxy->original, args);
    proxy->allocation = *args;
    ++proxy->allocations;
}
}
namespace jitlab {
bool InitializeProxy() {
    if (forwarding) return true;
    constexpr size_t stride = 16, bytes = InfoSlots * stride;
    auto memory = static_cast<uint8_t*>(VirtualAlloc(nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (!memory) return false;
    for (size_t i = 0; i < InfoSlots; ++i) {
        // Forward the original ICorJitInfo receiver, leaving every other ABI argument in place.
        uint8_t stub[stride] = {0x48, 0x8b, 0x49, 0x08, 0x48, 0x8b, 0x01, 0xff, 0xa0, 0, 0, 0, 0, 0xcc, 0xcc, 0xcc};
        uint32_t offset = static_cast<uint32_t>(i * sizeof(void*));
        std::memcpy(stub + 9, &offset, sizeof(offset));
        std::memcpy(memory + i * stride, stub, stride);
    }
    DWORD ignored;
    if (!VirtualProtect(memory, bytes, PAGE_EXECUTE_READ, &ignored) || !FlushInstructionCache(GetCurrentProcess(), memory, bytes)) {
        VirtualFree(memory, 0, MEM_RELEASE); return false;
    }
    for (size_t i = 0; i < InfoSlots; ++i) table[i] = memory + i * stride;
    table[AllocMemSlot] = reinterpret_cast<void*>(&Allocate);
    table[CanInlineSlot] = reinterpret_cast<void*>(&CanInline);
    forwarding = memory;
    return true;
}
void** ProxyTable() { return table; }
bool ReadWord(const void* address, uint32_t length, uint64_t& word) {
    word = 0;
    if (!address || !length) return false;
    SIZE_T bytes = (std::min)(static_cast<size_t>(length), sizeof(word)), read = 0;
    return ReadProcessMemory(GetCurrentProcess(), address, &word, bytes, &read) != 0 && read == bytes;
}
void CaptureCode(const InfoProxy& proxy, Sample& sample) {
    sample.allocations = proxy.allocations;
    uintptr_t begin = reinterpret_cast<uintptr_t>(proxy.allocation.hot), entry = reinterpret_cast<uintptr_t>(sample.executable);
    if (begin && entry >= begin && entry - begin < proxy.allocation.hotSize && proxy.allocation.hotWritable)
        sample.writable = proxy.allocation.hotWritable + (entry - begin);
    if (ReadWord(sample.executable, sample.nativeSize, sample.executableAtReturn)) sample.readMask |= 1;
    if (ReadWord(sample.writable, sample.nativeSize, sample.writableAtReturn)) sample.readMask |= 2;
}
}
