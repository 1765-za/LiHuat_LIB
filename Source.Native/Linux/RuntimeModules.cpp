#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif
#include "LinuxPlatform.h"
#include <dlfcn.h>
#include <link.h>

namespace lihuat::linux_platform {
namespace {
struct Search { uintptr_t anchor, start; size_t bytes; bool found = false; };
bool Contains(uintptr_t first, uintptr_t last, uintptr_t start, size_t bytes) {
    return first <= start && start < last && bytes <= last - start;
}
int FindImage(dl_phdr_info* info, size_t, void* state) {
    auto& search = *static_cast<Search*>(state);
    bool ownsAnchor = false;
    for (size_t i = 0; i < info->dlpi_phnum; ++i) {
        const auto& header = info->dlpi_phdr[i];
        if (header.p_type != PT_LOAD || header.p_vaddr > UINTPTR_MAX - info->dlpi_addr) continue;
        uintptr_t start = info->dlpi_addr + header.p_vaddr;
        if (header.p_memsz > UINTPTR_MAX - start) continue;
        if (Contains(start, start + header.p_memsz, search.anchor, 1)) { ownsAnchor = true; break; }
    }
    if (!ownsAnchor) return 0;
    for (size_t i = 0; i < info->dlpi_phnum; ++i) {
        const auto& header = info->dlpi_phdr[i];
        if (header.p_type != PT_LOAD || header.p_vaddr > UINTPTR_MAX - info->dlpi_addr) continue;
        uintptr_t start = info->dlpi_addr + header.p_vaddr;
        if (header.p_memsz <= UINTPTR_MAX - start && Contains(start, start + header.p_memsz, search.start, search.bytes)) {
            search.found = true; break;
        }
    }
    return 1;
}
}
bool ImageContains(const void* anchor, const void* address, size_t bytes) noexcept {
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    if (!anchor || !start || !bytes || bytes > UINTPTR_MAX - start) return false;
    Search search{reinterpret_cast<uintptr_t>(anchor), start, bytes};
    dl_iterate_phdr(FindImage, &search);
    return search.found;
}
void* FindLoadedSymbol(const char* module, const char* symbol) noexcept {
    if (!module || !symbol) return nullptr;
    void* handle = dlopen(module, RTLD_NOW | RTLD_NOLOAD | RTLD_NODELETE);
    if (!handle) return nullptr;
    void* entry = dlsym(handle, symbol);
    dlclose(handle); // RTLD_NODELETE deliberately pins runtime callbacks until exit.
    return entry;
}
}
