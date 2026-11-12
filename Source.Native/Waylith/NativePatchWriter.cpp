#include <windows.h>
#include <cstdint>
#include <cstring>
#include <vector>
#include "NativeAuthority.h"
#include "NativeMemoryOwnership.h"
#include "NativeWriteScope.h"

namespace {
struct Region { void* address; size_t length; DWORD protection; bool changed = false; };
bool Copy(void* destination, const void* source, size_t length) {
    __try { memmove(destination, source, length); return true; }
    __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION || GetExceptionCode() == EXCEPTION_IN_PAGE_ERROR
        ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) { return false; }
}
bool Restore(std::vector<Region>& regions) {
    bool success = true;
    for (auto entry = regions.rbegin(); entry != regions.rend(); ++entry) {
        if (!entry->changed) continue;
        DWORD previous = 0;
        if (VirtualProtect(entry->address, entry->length, entry->protection, &previous)) entry->changed = false;
        else success = false;
    }
    return success;
}
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_PatchBytes(uint64_t authority, void* target,
    const void* source, size_t length, void* backup, size_t backupLength, int executable) {
    if (!lihuat::Authorized(authority)) return -10;
    if (!length) return 1;
    uintptr_t begin = reinterpret_cast<uintptr_t>(target);
    if (!begin || !source || length > UINTPTR_MAX - begin) return -11;
    bool save = backup && backupLength >= length;
    if (!lihuat::NativeWriteAuthorized() && (lihuat::NativeWritesOverlap(target, length)
        || (save && lihuat::NativeWritesOverlap(backup, length)))) return -12;
    lihuat::NativeWriteScope writing;
    if (!writing) return -13;
    try {
        std::vector<Region> regions;
        uintptr_t cursor = begin, end = begin + length;
        while (cursor < end) {
            MEMORY_BASIC_INFORMATION memory{};
            if (VirtualQuery(reinterpret_cast<void*>(cursor), &memory, sizeof(memory)) != sizeof(memory)
                || memory.State != MEM_COMMIT || (memory.Protect & (PAGE_NOACCESS | PAGE_GUARD))) return -14;
            uintptr_t limit = reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize;
            if (limit <= cursor) return -14;
            size_t bytes = (limit < end ? limit : end) - cursor;
            regions.push_back({reinterpret_cast<void*>(cursor), bytes, memory.Protect});
            cursor += bytes;
        }
        std::vector<uint8_t> original(length);
        if (!Copy(original.data(), target, length)) return -14;
        if (save && !Copy(backup, original.data(), length)) return -15;
        for (auto& region : regions) {
            DWORD previous = 0;
            bool code = executable || (region.protection & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY));
            if (!VirtualProtect(region.address, region.length, code ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE, &previous)) {
                Restore(regions); return -16;
            }
            region.changed = true;
        }
        bool copied = Copy(target, source, length);
        bool flushed = !executable || FlushInstructionCache(GetCurrentProcess(), target, length);
        if (!copied || !flushed) {
            Copy(target, original.data(), length);
            if (executable) FlushInstructionCache(GetCurrentProcess(), target, length);
            Restore(regions); return -17;
        }
        return Restore(regions) ? 1 : -18;
    } catch (...) { return -19; }
}
