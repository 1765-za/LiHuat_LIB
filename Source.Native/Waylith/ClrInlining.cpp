#include "ClrInlining.h"
#include "NativeMemoryOwnership.h"
#include "GenericBodies.h"
#include <windows.h>
#include <atomic>
#include <cstring>
#include <cstddef>

namespace {
constexpr size_t InfoSlots = 176, CanInlineSlot = 8, ReportInlineSlot = 10, AllocMemSlot = 160, JitFlagsSlot = 175;
void* table[InfoSlots]{};
std::atomic<uint64_t> blocked{0};
static_assert(offsetof(lihuat::InliningContext, original) == 8);
struct Allocation {
    uint32_t hotSize, coldSize, dataSize, exceptions, flags;
    void* hot;
    void* hotWritable;
    void* cold;
    void* coldWritable;
    void* data;
    void* dataWritable;
};
static_assert(offsetof(Allocation, hot) == 24);

void __stdcall Allocate(lihuat::InliningContext* context, Allocation* args) {
    auto original = *static_cast<void***>(context->original);
    if (args->hotSize && args->hotSize < 8) args->hotSize = 8;
    reinterpret_cast<void(__stdcall*)(void*, Allocation*)>(original[AllocMemSlot])(context->original, args);
    context->hot = static_cast<uint8_t*>(args->hot);
    context->writable = static_cast<uint8_t*>(args->hotWritable);
    context->capacity = args->hotSize;
}

uint32_t __stdcall JitFlags(lihuat::InliningContext* context, void* flags, uint32_t bytes) {
    auto original = *static_cast<void***>(context->original);
    uint32_t written = reinterpret_cast<uint32_t(__stdcall*)(void*, void*, uint32_t)>(original[JitFlagsSlot])(context->original, flags, bytes);
    if (written >= sizeof(uint64_t) && bytes >= sizeof(uint64_t)) {
        std::memcpy(&context->flags, flags, sizeof(uint64_t)); context->flagsRead = true;
    }
    return written;
}

int __stdcall CanInline(lihuat::InliningContext* context, void* caller, void* callee) {
    auto original = *static_cast<void***>(context->original);
    int result = reinterpret_cast<int(__stdcall*)(void*, void*, void*)>(original[CanInlineSlot])(context->original, caller, callee);
    if (result < 0 || (!lihuat::PreventInlining(callee) && !lihuat::GenericInlineBlocked(context->original, callee))) return result;
    blocked.fetch_add(1, std::memory_order_relaxed);
    constexpr int inlineFail = -1;
    reinterpret_cast<void(__stdcall*)(void*, void*, void*, int, const char*)>(original[ReportInlineSlot])(
        context->original, caller, callee, inlineFail, "Protected method entry");
    return inlineFail;
}
}

namespace lihuat {
bool InitializeInlining() {
    if (table[0]) return true;
    constexpr size_t stride = 16, bytes = InfoSlots * stride;
    auto memory = static_cast<uint8_t*>(VirtualAlloc(nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (!memory) return false;
    for (size_t i = 0; i < InfoSlots; ++i) {
        // Restore the runtime receiver without changing the remaining ABI arguments.
        uint8_t stub[stride] = {0x48, 0x8b, 0x49, 0x08, 0x48, 0x8b, 0x01, 0xff, 0xa0, 0, 0, 0, 0, 0xcc, 0xcc, 0xcc};
        uint32_t offset = static_cast<uint32_t>(i * sizeof(void*));
        std::memcpy(stub + 9, &offset, sizeof(offset));
        std::memcpy(memory + i * stride, stub, stride);
    }
    DWORD ignored;
    if (!VirtualProtect(memory, bytes, PAGE_EXECUTE_READ, &ignored) || !FlushInstructionCache(GetCurrentProcess(), memory, bytes)) {
        VirtualFree(memory, 0, MEM_RELEASE); return false;
    }
    OwnNativeAllocation(NativeAllocation::Inlining, memory, bytes);
    for (size_t i = 0; i < InfoSlots; ++i) table[i] = memory + i * stride;
    table[CanInlineSlot] = reinterpret_cast<void*>(&CanInline);
    table[AllocMemSlot] = reinterpret_cast<void*>(&Allocate);
    table[JitFlagsSlot] = reinterpret_cast<void*>(&JitFlags);
    return true;
}
void** InliningTable() { return table; }
uint64_t BlockedInlines() { return blocked.load(std::memory_order_relaxed); }
}
