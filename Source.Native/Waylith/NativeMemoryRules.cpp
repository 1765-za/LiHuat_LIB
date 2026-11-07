#include "NativeMemoryRules.h"
#include "NativeMemoryOwnership.h"
#include "NativeWriteScope.h"
#include <cstdint>

namespace {
bool CurrentProcess(HANDLE process) {
    return process == GetCurrentProcess() || (process && GetProcessId(process) == GetCurrentProcessId());
}
}

bool lihuat::NativeWriteAllowed(const void* address, size_t length, bool pages) {
    if (NativeWriteAuthorized()) return true;
    uintptr_t begin = reinterpret_cast<uintptr_t>(address);
    if (!begin || !length || length > UINTPTR_MAX - begin) return true;
    uintptr_t end = begin + length;
    auto stack = reinterpret_cast<const NT_TIB*>(NtCurrentTeb());
    if (begin >= reinterpret_cast<uintptr_t>(stack->StackLimit)
        && end <= reinterpret_cast<uintptr_t>(stack->StackBase)) return true;
    if (pages) {
        constexpr uintptr_t mask = 0xfff;
        begin &= ~mask;
        if (end > UINTPTR_MAX - mask) return false;
        end = (end + mask) & ~mask;
    }
    return !NativeWritesOverlap(reinterpret_cast<const void*>(begin), end - begin);
}

bool lihuat::NativeProcessWriteAllowed(HANDLE process, const void* address, size_t length, bool pages) {
    return NativeWriteAuthorized() || !CurrentProcess(process) || NativeWriteAllowed(address, length, pages);
}

bool lihuat::NativeAllocationReleaseAllowed(HANDLE process, const void* address) {
    if (NativeWriteAuthorized() || !address || !CurrentProcess(process)) return true;
    MEMORY_BASIC_INFORMATION memory{};
    if (VirtualQuery(address, &memory, sizeof(memory)) != sizeof(memory) || !memory.AllocationBase) return true;
    const void* allocation = memory.AllocationBase;
    uintptr_t cursor = reinterpret_cast<uintptr_t>(allocation);
    if (memory.BaseAddress != allocation && VirtualQuery(allocation, &memory, sizeof(memory)) != sizeof(memory)) return false;
    do {
        if (NativeWritesOverlap(memory.BaseAddress, memory.RegionSize)) return false;
        uintptr_t next = reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize;
        if (next <= cursor) return false;
        cursor = next;
    } while (VirtualQuery(reinterpret_cast<const void*>(cursor), &memory, sizeof(memory)) == sizeof(memory)
        && memory.AllocationBase == allocation);
    return true;
}

bool lihuat::NativeMemoryInput(void* const* address, const size_t* length, const void*& value, size_t& bytes) {
    __try {
        if (!address || !length) return false;
        value = *address; bytes = *length; return true;
    } __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION || GetExceptionCode() == EXCEPTION_IN_PAGE_ERROR
        ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) { return false; }
}
