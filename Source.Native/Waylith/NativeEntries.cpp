#include "EntryMemory.h"
#include "EntryWriteIndex.h"
#include "ClrCompilation.h"
#include "ClrInlining.h"
#include "ClrJitSlot.h"
#include "ClrSuspension.h"
#include "NativeAuthority.h"
#include "NativeMemoryOwnership.h"
#include "GenericBodies.h"
#include "ClrProfilerRequests.h"
#include <memory>
#include <unordered_map>
#include <vector>
#include <algorithm>
#include <atomic>

namespace {
struct Site { uint8_t* address; int64_t original; };
struct Record {
    uint64_t generation;
    void* precode;
    uint8_t* control;
    int64_t controlBytes, installed;
    void** relay;
    uint32_t publications = 0;
    uint32_t failure = 0;
    std::vector<Site> sites;
    // The managed registry retains each metadata method and its loader allocator until removal.
    lihuat::EntryRoute route;
};
struct Snapshot { uint32_t state, publications, sites, intact; void* current; void* control; };
constexpr size_t MaxRecords = 4096, InitialSites = 16;
SRWLOCK registryLock = SRWLOCK_INIT;
std::unordered_map<void*, std::unique_ptr<Record>> registry;
std::atomic<uint32_t> activeEntries{0};
std::atomic<uint64_t> staleCompilations{0};
std::atomic<uint64_t> nextGeneration{0};
lihuat::EntryWriteIndex writeEntries;
thread_local void* publishing = nullptr;
struct Exclusive {
    Exclusive() { AcquireSRWLockExclusive(&registryLock); }
    ~Exclusive() { ReleaseSRWLockExclusive(&registryLock); }
};
struct Shared {
    Shared() { AcquireSRWLockShared(&registryLock); }
    ~Shared() { ReleaseSRWLockShared(&registryLock); }
};

void IndexWrites() {
    writeEntries.Clear();
    for (const auto& pair : registry) {
        const Record& record = *pair.second;
        writeEntries.Add(&record, sizeof(record));
        writeEntries.Add(record.sites.data(), record.sites.capacity() * sizeof(Site));
        writeEntries.Add(record.precode, 16);
        writeEntries.Add(record.control, 16);
        writeEntries.Add(record.relay, sizeof(void*));
        for (size_t i = 0; i < record.route.count; ++i) {
            const auto& observation = record.route.observations[i];
            writeEntries.Add(observation.address, observation.writeSize);
        }
        for (const Site& site : record.sites) writeEntries.Add(site.address, 16);
    }
    writeEntries.Seal();
}

uint32_t State(const Record& record, uint8_t*& current) {
    void** slot;
    bool resolved = lihuat::CodeEntry(record.precode, slot, current);
    if (record.failure) return record.failure;
    if (!resolved) return 6;
    bool owned = current == record.control;
    int64_t value;
    if (!lihuat::Readable(record.control, 8, true) || !lihuat::TryRead(record.control, value) || value != record.controlBytes
        || *reinterpret_cast<void* volatile*>(record.relay) != record.control) return 5;
    for (const Site& site : record.sites) {
        if (!lihuat::Readable(site.address, 8, true) || !lihuat::TryRead(site.address, value) || value != record.installed) return 4;
        owned |= current == site.address;
    }
    return owned ? 2 : 3;
}
bool Intact(const Record& record) { uint8_t* current = nullptr; return State(record, current) == 2; }

bool Restore(const Record& record) {
    size_t changed = 0;
    for (; changed < record.sites.size(); ++changed) {
        const Site& site = record.sites[changed];
        if (!lihuat::Exchange(site.address, record.installed, site.original)) break;
    }
    if (changed == record.sites.size()) return true;
    while (changed) {
        const Site& site = record.sites[--changed];
        lihuat::Exchange(site.address, site.original, record.installed);
    }
    return false;
}
}

#define API extern "C" __declspec(dllexport)
API int __cdecl LihuatNative_Publication(uint64_t authority, void* key, void** prior) {
    if (!lihuat::Authorized(authority)) return -10;
    if (!prior) return 0;
    *prior = publishing; publishing = key; return 1;
}
API int __cdecl LihuatNative_ObserveCompilations(uint64_t authority, void* continuation) {
    if (!lihuat::Authorized(authority)) return -10;
    return lihuat::InstallCompilationObserver(continuation) ? 1 : 0;
}
API uint64_t __cdecl LihuatNative_CompilationNotifications() { return lihuat::CompilationNotifications(); }
API void* __cdecl LihuatNative_Code(void* precode) {
    void** slot; uint8_t* code;
    return lihuat::CodeEntry(precode, slot, code) ? code : nullptr;
}
API int __cdecl LihuatNative_Initialize(uint32_t version) {
    if (version != 10 || sizeof(void*) != 8 || GetModuleHandleW(L"coreclr.dll") == nullptr) return 0;
    if (!lihuat::InitializeNativeImage()) return 0;
    Exclusive lock;
    try { writeEntries.Reserve(MaxRecords * (InitialSites + 5 + lihuat::EntryRoute::MaxObservations)); }
    catch (...) { return 0; }
    return 1;
}

API int __cdecl LihuatNative_Overlaps(void* address, uint32_t length) {
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    if (!start || !length || start > UINTPTR_MAX - length) return 0;
    if (lihuat::NativeMemoryOverlaps(address, length) || lihuat::CompilationSlotOverlaps(address, length)
        || lihuat::GenericBodyOverlaps(address, length) || lihuat::ProfilerRequestsOverlaps(address, length)) return 1;
    Shared lock;
    return writeEntries.OwnsStorage(address, length) || writeEntries.Overlaps(address, length) ? 1 : 0;
}

bool lihuat::NativeOutputAllowed(const void* address, size_t length) {
    uintptr_t start = reinterpret_cast<uintptr_t>(address);
    return start && length && length <= UINT32_MAX && length <= UINTPTR_MAX - start
        && !LihuatNative_Overlaps(const_cast<void*>(address), static_cast<uint32_t>(length));
}

API int __cdecl LihuatNative_Register(uint64_t authority, void* key, void* precode, uint8_t* previous, int64_t expected, uint8_t* control) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::CodeWriteScope writing;
    if (!writing) return -9;
    Exclusive lock;
    void** slot; uint8_t* current;
    if (!key || registry.count(key) || registry.size() == MaxRecords) return -1;
    if (!lihuat::CodeEntry(precode, slot, current) || current != control || previous == control) return -2;
    if ((reinterpret_cast<uintptr_t>(previous) & 7) || !lihuat::Readable(previous, 8, true)) return -3;
    int64_t observed;
    if (!lihuat::TryRead(previous, observed) || observed != expected) return -4;
    try {
        auto record = std::make_unique<Record>();
        if (!lihuat::CodeEntry(precode, slot, current, &record->route) || current != control) return -2;
        record->generation = nextGeneration.fetch_add(1, std::memory_order_release) + 1;
        record->precode = precode; record->control = control;
        if (!lihuat::TryRead(control, record->controlBytes)) return -4;
        record->sites.reserve(InitialSites);
        record->sites.push_back({previous, expected});
        writeEntries.ReserveAdditional(6 + lihuat::EntryRoute::MaxObservations);
        record->relay = lihuat::AllocateRelay(control);
        if (!record->relay) return 0;
        record->installed = lihuat::Branch(record->relay);
        Record* value = record.get();
        registry.emplace(key, std::move(record));
        if (!lihuat::Exchange(previous, expected, value->installed)) { registry.erase(key); return 0; }
        activeEntries.store(static_cast<uint32_t>(registry.size()), std::memory_order_release);
        value->publications = 1;
        IndexWrites();
        return 1;
    } catch (...) { return 0; }
}

API int __cdecl LihuatNative_Publish(uint64_t authority, void* key, uint8_t* previous, int64_t expected, uint8_t* control) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::CodeWriteScope writing;
    if (!writing) return -9;
    Exclusive lock;
    auto found = registry.find(key);
    if (found == registry.end()) return 0;
    Record& record = *found->second;
    try {
        record.sites.reserve(record.sites.size() + 2);
        writeEntries.ReserveAdditional(2 + lihuat::EntryRoute::MaxObservations);
    } catch (const std::bad_alloc&) { return 0; }
    void** slot; uint8_t* current;
    uint64_t revision = record.route.revision;
    if (!lihuat::CodeEntry(record.precode, slot, current, &record.route) || current != control) return 0;
    if (control == record.control) {
        if (revision != record.route.revision) IndexWrites();
        return Intact(record) ? 1 : 0;
    }
    int64_t observed, newControl;
    for (const Site& site : record.sites)
        if (site.address == control || !lihuat::Readable(site.address, 8, true)
            || !lihuat::TryRead(site.address, observed) || observed != record.installed) return 0;
    if (!lihuat::Readable(record.control, 8, true) || !lihuat::TryRead(record.control, observed) || observed != record.controlBytes) return 0;
    if (!lihuat::TryRead(control, newControl)) return 0;
    bool includePrevious = previous != record.control;
    if (includePrevious && ((reinterpret_cast<uintptr_t>(previous) & 7)
        || !lihuat::Readable(previous, 8, true) || !lihuat::TryRead(previous, observed) || observed != expected)) return 0;
    uint8_t* oldControl = record.control;
    InterlockedExchangePointer(record.relay, control);
    if (!lihuat::Exchange(oldControl, record.controlBytes, record.installed)) {
        InterlockedExchangePointer(record.relay, oldControl); return 0;
    }
    if (includePrevious && !lihuat::Exchange(previous, expected, record.installed)) {
        lihuat::Exchange(oldControl, record.installed, record.controlBytes);
        InterlockedExchangePointer(record.relay, oldControl); return 0;
    }
    record.sites.push_back({oldControl, record.controlBytes});
    if (includePrevious) record.sites.push_back({previous, expected});
    record.control = control; record.controlBytes = newControl; record.publications++;
    IndexWrites();
    return Intact(record) ? 1 : 0;
}

API int __cdecl LihuatNative_Retain(uint64_t authority, void* key, uint8_t* address, int64_t expected) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::CodeWriteScope writing;
    if (!writing) return -9;
    Exclusive lock;
    auto found = registry.find(key);
    if (found == registry.end()) return -1;
    if (!Intact(*found->second)) return -2;
    Record& record = *found->second;
    if (address == record.control) return 1;
    for (const Site& site : record.sites) if (site.address == address) return 1;
    if (reinterpret_cast<uintptr_t>(address) & 7) return -4;
    if (!lihuat::Readable(address, 8, true)) return -5;
    int64_t observed;
    if (!lihuat::TryRead(address, observed) || observed != expected) return -6;
    try {
        record.sites.reserve(record.sites.size() + 1);
        writeEntries.ReserveAdditional(2);
        if (!lihuat::Exchange(address, expected, record.installed)) return -7;
        record.sites.push_back({address, expected});
        IndexWrites();
        return 1;
    } catch (...) { return -8; }
}

API int __cdecl LihuatNative_Status(void* key, Snapshot* status, uint32_t size) {
    if (size != sizeof(Snapshot) || !lihuat::NativeOutputAllowed(status, size)) return 0;
    Shared lock;
    auto found = registry.find(key);
    if (found == registry.end()) return 0;
    const Record& record = *found->second;
    uint8_t* current = nullptr;
    uint32_t state = State(record, current);
    *status = {state, record.publications, static_cast<uint32_t>(record.sites.size() + 1), state == 2 ? 1u : 0u,
        current, record.control};
    return 1;
}

bool lihuat::HasProtectedEntries() { return activeEntries.load(std::memory_order_acquire) != 0; }
uint32_t lihuat::ProtectedEntryCount() { return activeEntries.load(std::memory_order_acquire); }
uint64_t lihuat::RegistrationRevision() { return nextGeneration.load(std::memory_order_acquire); }
uint64_t lihuat::StaleCompilations() { return staleCompilations.load(std::memory_order_relaxed); }
bool lihuat::PreventInlining(void* method) {
    if (!HasProtectedEntries()) return false;
    Shared lock;
    return registry.find(method) != registry.end();
}
uint64_t lihuat::CompilationGeneration(void* key) {
    if (key == publishing) return 0;
    Shared lock;
    auto found = registry.find(key);
    return found == registry.end() ? 0 : found->second->generation;
}
bool lihuat::Compiled(void* key, uint64_t generation, uint8_t* code, uint64_t size, uint8_t* writable) {
    if (key == publishing) return true;
    if (!generation) {
        Shared lock;
        if (registry.find(key) == registry.end()) return true;
    }
    Exclusive lock;
    auto found = registry.find(key);
    if (found == registry.end()) {
        if (generation) staleCompilations.fetch_add(1, std::memory_order_relaxed);
        return true;
    }
    Record& record = *found->second;
    if (generation && record.generation != generation) staleCompilations.fetch_add(1, std::memory_order_relaxed);
    if (code == record.control) return true;
    for (const Site& site : record.sites) if (site.address == code) return true;
    int64_t original;
    // The runtime can copy its writable buffer into the executable mapping after compileMethod returns.
    uint8_t* emitted = writable ? writable : code;
    if (size < 8 || !lihuat::TryRead(emitted, original)) { record.failure = 6; return false; }
    try {
        writeEntries.ReserveAdditional(2);
        const Site* storage = record.sites.data();
        record.sites.push_back({code, original});
        if (!lihuat::ExchangeUnpublished(code, emitted, original, record.installed)) {
            record.sites.pop_back(); record.failure = 4;
            if (storage != record.sites.data()) IndexWrites();
            return false;
        }
        ++record.publications;
        if (storage != record.sites.data()) IndexWrites();
        else writeEntries.Include(code, 16);
        return true;
    } catch (const std::bad_alloc&) { record.failure = 6; return false; }
}

API int __cdecl LihuatNative_Flush(uint64_t authority, void*) {
    if (!lihuat::Authorized(authority)) return -10;
    return lihuat::RepairCompilationSlot() ? 1 : -1;
}

API int __cdecl LihuatNative_Remove(uint64_t authority, void* key) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::CodeWriteScope writing;
    if (!writing) return 0;
    Exclusive lock;
    auto found = registry.find(key);
    if (found == registry.end()) return 0;
    if (!Intact(*found->second) || !Restore(*found->second)) return 0;
    registry.erase(found);
    activeEntries.store(static_cast<uint32_t>(registry.size()), std::memory_order_release);
    IndexWrites();
    return 1;
}
