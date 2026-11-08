#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif
#include "LinuxPlatform.h"
#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <limits>
#include <mutex>
#include <sys/mman.h>
#include <sys/random.h>
#include <sys/uio.h>
#include <unistd.h>

namespace lihuat::linux_platform {
namespace {
bool Fits(const void* address, size_t bytes, const Region& region) noexcept {
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    return start && bytes && start >= region.begin && start < region.end && bytes <= region.end - start;
}
std::mutex entryWrites;
}
size_t PageSize() noexcept {
    static const size_t size = [] { long value = sysconf(_SC_PAGESIZE); return value > 0 ? static_cast<size_t>(value) : size_t{0}; }();
    return size;
}
bool Query(const void* address, Region& result) noexcept {
    result = {};
    if (!address) return false;
    FILE* maps = fopen("/proc/self/maps", "re");
    if (!maps) return false;
    char* line = nullptr;
    size_t capacity = 0;
    bool found = false;
    uintptr_t target = reinterpret_cast<uintptr_t>(address);
    while (getline(&line, &capacity, maps) >= 0) {
        unsigned long long begin = 0, end = 0;
        char mode[5]{};
        if (sscanf(line, "%llx-%llx %4s", &begin, &end, mode) != 3) continue;
        if (begin > UINTPTR_MAX || end > UINTPTR_MAX || begin >= end) continue;
        if (target < begin || target >= end) continue;
        result = {static_cast<uintptr_t>(begin), static_cast<uintptr_t>(end),
            (mode[0] == 'r' ? PROT_READ : 0) | (mode[1] == 'w' ? PROT_WRITE : 0) | (mode[2] == 'x' ? PROT_EXEC : 0),
            mode[3] == 'p'};
        found = true;
        break;
    }
    free(line);
    fclose(maps);
    return found;
}
bool Readable(const void* address, size_t bytes, bool executable) noexcept {
    Region region;
    return Query(address, region) && Fits(address, bytes, region) && (region.protection & PROT_READ)
        && (!executable || (region.protection & PROT_EXEC));
}
bool TryCopy(const void* address, void* destination, size_t bytes) noexcept {
    if (!address || !destination || !bytes || bytes > static_cast<size_t>(std::numeric_limits<ssize_t>::max())
        || bytes > UINTPTR_MAX - reinterpret_cast<uintptr_t>(address)
        || bytes > UINTPTR_MAX - reinterpret_cast<uintptr_t>(destination)) return false;
    iovec local{destination, bytes}, remote{const_cast<void*>(address), bytes};
    ssize_t copied;
    do { copied = process_vm_readv(getpid(), &local, 1, &remote, 1, 0); } while (copied < 0 && errno == EINTR);
    return copied >= 0 && static_cast<size_t>(copied) == bytes;
}
bool RandomBytes(void* destination, size_t bytes) noexcept {
    if (!destination || !bytes || bytes > UINTPTR_MAX - reinterpret_cast<uintptr_t>(destination)) return false;
    auto output = static_cast<uint8_t*>(destination);
    while (bytes) {
        // Small reads also avoid the getrandom maximum-request limit.
        size_t chunk = bytes < 256 ? bytes : 256;
        ssize_t count = getrandom(output, chunk, 0);
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) return false;
        output += count; bytes -= static_cast<size_t>(count);
    }
    return true;
}
bool ExchangeUnpublished(uint8_t* executable, uint8_t* writable, int64_t expected, int64_t desired) noexcept {
    if (executable != writable || (reinterpret_cast<uintptr_t>(executable) & 7) || (reinterpret_cast<uintptr_t>(writable) & 7)
        || !Readable(executable, sizeof(int64_t), true)) return false;
    std::lock_guard<std::mutex> lock(entryWrites);
    Region region;
    if (!Query(writable, region) || !Fits(writable, sizeof(int64_t), region)
        || !(region.protection & PROT_READ) || !region.privateMapping) return false;
    size_t page = PageSize();
    if (!page) return false;
    uintptr_t start = reinterpret_cast<uintptr_t>(writable);
    void* base = reinterpret_cast<void*>(start - start % page);
    // An aligned 8-byte word cannot straddle a normal x86_64 Linux page.
    if (start % page > page - sizeof(int64_t)) return false;
    int temporary = region.protection | PROT_WRITE;
    if (mprotect(base, page, temporary) != 0) return false;
    int64_t observed = expected;
    bool changed = __atomic_compare_exchange_n(reinterpret_cast<int64_t*>(writable), &observed, desired,
                                               false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);
    if (changed) __builtin___clear_cache(reinterpret_cast<char*>(executable), reinterpret_cast<char*>(executable + 8));
    if (mprotect(base, page, region.protection) == 0) return changed;
    // Never report success if page permissions could not be restored.
    if (changed) {
        observed = desired;
        __atomic_compare_exchange_n(reinterpret_cast<int64_t*>(writable), &observed, expected,
                                    false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);
        __builtin___clear_cache(reinterpret_cast<char*>(executable), reinterpret_cast<char*>(executable + 8));
    }
    mprotect(base, page, region.protection);
    return false;
}
}
