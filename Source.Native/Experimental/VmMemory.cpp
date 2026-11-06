#include <windows.h>
#include <stdint.h>

extern "C" __declspec(dllexport) int __cdecl LihuatVm_Sample(const void* address, uint32_t length,
    const uint8_t* previous, uint8_t* current, uint32_t* offsets, uint32_t capacity, uint32_t* changed) {
    if (!address || !current || !changed || !length || length > 65536 || capacity > 1024 || (capacity && !offsets)) return 0;
    *changed = 0;
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    if (start > UINTPTR_MAX - length) return 0;
    uintptr_t end = start + length;
    for (uintptr_t cursor = start; cursor < end;) {
        MEMORY_BASIC_INFORMATION region{};
        if (VirtualQuery(reinterpret_cast<void*>(cursor), &region, sizeof(region)) != sizeof(region)
            || region.State != MEM_COMMIT || (region.Protect & (PAGE_NOACCESS | PAGE_GUARD))) return 0;
        uintptr_t next = reinterpret_cast<uintptr_t>(region.BaseAddress) + region.RegionSize;
        if (next <= cursor) return 0;
        cursor = next;
    }
    SIZE_T read = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), address, current, length, &read) || read != length) return 0;
    if (previous) for (uint32_t i = 0; i < length; ++i) {
        if (previous[i] == current[i]) continue;
        if (*changed < capacity) offsets[*changed] = i;
        ++*changed;
    }
    return 1;
}
