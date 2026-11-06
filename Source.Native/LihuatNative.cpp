#include "EntryMemory.h"
#include <memory>
#include <string.h>
#include <unordered_map>
#include <vector>

namespace {
struct MonoProfiler;
struct MonoMethod;
struct MonoClass;
struct MonoImage;
struct MonoMethodHeader;
struct MonoJitInfo;
struct MonoDomain;
using JitBegin = void (__cdecl *)(MonoProfiler*, MonoMethod*);
using JitDone = void (__cdecl *)(MonoProfiler*, MonoMethod*, MonoJitInfo*);

struct MonoApi {
    void* (__cdecl *createProfiler)(MonoProfiler*);
    void (__cdecl *setJitBegin)(void*, JitBegin);
    void (__cdecl *setJitDone)(void*, JitDone);
    void (__cdecl *setJitFailed)(void*, JitBegin);
    MonoClass* (__cdecl *methodGetClass)(MonoMethod*);
    MonoImage* (__cdecl *classGetImage)(MonoClass*);
    const char* (__cdecl *imageGetGuid)(MonoImage*);
    uint32_t (__cdecl *methodGetToken)(MonoMethod*);
    uint32_t (__cdecl *methodGetFlags)(MonoMethod*, uint32_t*);
    MonoMethodHeader* (__cdecl *methodGetHeader)(MonoMethod*);
    const uint8_t* (__cdecl *headerGetCode)(MonoMethodHeader*, uint32_t*, uint32_t*);
    void (__cdecl *freeHeader)(MonoMethodHeader*);
    MonoDomain* (__cdecl *domainGet)();
    MonoJitInfo* (__cdecl *jitFind)(MonoDomain*, void*);
    void* (__cdecl *jitCode)(MonoJitInfo*);
    int (__cdecl *jitSize)(MonoJitInfo*);
    MonoMethod* (__cdecl *jitMethod)(MonoJitInfo*);
};

enum class Mode : uint32_t { Guard = 1, Rewrite = 2 };
enum class State : uint32_t { Pending = 1, Applied = 2, SourceMismatch = 3, WriteFailed = 4, EntryFailed = 5, JitFailed = 6 };
struct Record {
    Mode mode;
    State state = State::Pending;
    std::vector<uint8_t> expected;
    std::vector<uint8_t> replacement;
    std::vector<lihuat::EntrySite> sites;
    void* destination = nullptr;
    uint32_t starts = 0, finishes = 0, repairs = 0, publications = 0;
};
struct Snapshot {
    uint32_t mode, state, starts, finishes, repairs, publications, sites, intact;
};
constexpr uint32_t MaxCil = 65536;
constexpr size_t MaxRecords = 512;
SRWLOCK registryLock = SRWLOCK_INIT;
std::unordered_map<MonoMethod*, std::unique_ptr<Record>> registry;
MonoApi api = {};
bool initialized = false;
int identity;

struct SharedLock {
    SharedLock() { AcquireSRWLockShared(&registryLock); }
    ~SharedLock() { ReleaseSRWLockShared(&registryLock); }
};
struct ExclusiveLock {
    ExclusiveLock() { AcquireSRWLockExclusive(&registryLock); }
    ~ExclusiveLock() { ReleaseSRWLockExclusive(&registryLock); }
};
struct Header {
    MonoMethodHeader* value;
    const uint8_t* code;
    uint32_t length = 0;
    explicit Header(MonoMethod* method) : value(api.methodGetHeader(method)) {
        code = value == nullptr ? nullptr : api.headerGetCode(value, &length, nullptr);
    }
    ~Header() { if (value != nullptr) api.freeHeader(value); }
    bool Equals(const std::vector<uint8_t>& bytes) const {
        return code != nullptr && length == bytes.size() && memcmp(code, bytes.data(), length) == 0;
    }
};

bool NormalizeMvid(const char* source, char destination[33]) {
    if (source == nullptr) return false;
    int used = 0;
    for (const char* p = source; *p != '\0'; ++p) {
        char c = *p;
        if (c == '-' || c == '{' || c == '}') continue;
        if (used == 32) return false;
        if (c >= 'A' && c <= 'F') c = static_cast<char>(c - 'A' + 'a');
        if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
        destination[used++] = c;
    }
    destination[used] = '\0';
    return used == 32;
}

bool MatchMethod(MonoMethod* method, const char* mvid, uint32_t token) {
    if (method == nullptr || (token & 0xff000000u) != 0x06000000u || api.methodGetToken(method) != token) return false;
    MonoClass* klass = api.methodGetClass(method);
    MonoImage* image = klass == nullptr ? nullptr : api.classGetImage(klass);
    char expected[33] = {}, actual[33] = {};
    return image != nullptr && NormalizeMvid(mvid, expected) && NormalizeMvid(api.imageGetGuid(image), actual)
        && strcmp(expected, actual) == 0;
}

bool PreventInlining(MonoMethod* method, uint32_t managedFlags) {
    uint32_t implementation = 0;
    uint32_t flags = api.methodGetFlags(method, &implementation);
    if ((reinterpret_cast<uintptr_t>(method) & 3) != 0 || implementation != managedFlags) return false;
    volatile LONG* word = reinterpret_cast<volatile LONG*>(method);
    LONG current = InterlockedCompareExchange(word, 0, 0);
    if (static_cast<uint16_t>(current) != static_cast<uint16_t>(flags)
        || (static_cast<uint32_t>(current) >> 16) != implementation) return false;
    constexpr LONG noInlining = 8 << 16;
    for (;;) {
        LONG observed = InterlockedCompareExchange(word, current | noInlining, current);
        if (observed == current) break;
        current = observed;
    }
    api.methodGetFlags(method, &implementation);
    return (implementation & 8) != 0;
}

bool WriteCil(const Header& header, const std::vector<uint8_t>& bytes) {
    if (header.code == nullptr || header.length != bytes.size()) return false;
    uint8_t* code = const_cast<uint8_t*>(header.code);
    MEMORY_BASIC_INFORMATION memory = {};
    if (VirtualQuery(code, &memory, sizeof(memory)) != sizeof(memory) || memory.State != MEM_COMMIT
        || reinterpret_cast<uintptr_t>(code) + header.length > reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize) return false;
    DWORD writable = 0;
    if (memory.Type == MEM_PRIVATE) writable = (memory.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0
        ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE;
    else if (memory.Type == MEM_IMAGE || memory.Type == MEM_MAPPED)
        writable = (memory.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0
            ? PAGE_EXECUTE_WRITECOPY : PAGE_WRITECOPY;
    std::vector<uint8_t> before(code, code + header.length);
    DWORD previous = 0, ignored = 0;
    if (writable == 0 || !VirtualProtect(code, header.length, writable, &previous)) return false;
    memcpy(code, bytes.data(), header.length);
    bool verified = header.Equals(bytes);
    if (!verified) memcpy(code, before.data(), header.length);
    if (VirtualProtect(code, header.length, previous, &ignored)) return verified;
    memcpy(code, before.data(), header.length);
    VirtualProtect(code, header.length, previous, &ignored);
    return false;
}

bool HasRecord(MonoMethod* method) {
    SharedLock lock;
    return registry.find(method) != registry.end();
}

bool PublishSite(Record& record, MonoMethod* method, MonoJitInfo* info) {
    if (info == nullptr || api.jitMethod(info) != method || api.jitSize(info) < 8) return false;
    uint8_t* address = static_cast<uint8_t*>(api.jitCode(info));
    if (address == nullptr || (reinterpret_cast<uintptr_t>(address) & 7) != 0) return false;
    for (lihuat::EntrySite& site : record.sites) {
        if (site.address != address) continue;
        if (lihuat::ReadEntry(address) == site.installed) return true;
        if (!lihuat::ExchangeEntry(address, lihuat::ReadEntry(address), site.installed)) return false;
        record.publications++;
        return true;
    }
    int64_t branch = 0;
    if (!lihuat::EntryBranch(address, record.destination, branch)) return false;
    int64_t before = lihuat::ReadEntry(address);
    record.sites.push_back({address, before, branch});
    if (!lihuat::ExchangeEntry(address, before, branch)) { record.sites.pop_back(); return false; }
    record.publications++;
    return true;
}

void __cdecl OnJitBegin(MonoProfiler*, MonoMethod* method) {
    if (!HasRecord(method)) return;
    ExclusiveLock lock;
    auto found = registry.find(method);
    if (found == registry.end()) return;
    Record& record = *found->second;
    record.starts++;
    try {
        Header header(method);
        if (record.mode == Mode::Rewrite && record.state == State::Pending) {
            if (!header.Equals(record.expected)) record.state = State::SourceMismatch;
            else record.state = WriteCil(header, record.replacement) ? State::Applied : State::WriteFailed;
        } else if (record.mode == Mode::Guard || record.state == State::Applied) {
            if (!header.Equals(record.replacement)) {
                if (WriteCil(header, record.replacement)) record.repairs++;
                else record.state = State::WriteFailed;
            }
        }
    } catch (...) { record.state = State::WriteFailed; }
}

void __cdecl OnJitDone(MonoProfiler*, MonoMethod* method, MonoJitInfo* info) {
    if (!HasRecord(method)) return;
    ExclusiveLock lock;
    auto found = registry.find(method);
    if (found == registry.end()) return;
    Record& record = *found->second;
    record.finishes++;
    if (record.mode != Mode::Guard) return;
    try {
        if (!PublishSite(record, method, info)) record.state = State::EntryFailed;
        else if (record.state != State::WriteFailed) record.state = State::Applied;
    } catch (...) { record.state = State::EntryFailed; }
}

void __cdecl OnJitFailed(MonoProfiler*, MonoMethod* method) {
    if (!HasRecord(method)) return;
    ExclusiveLock lock;
    auto found = registry.find(method);
    if (found != registry.end()) found->second->state = State::JitFailed;
}

bool Resolve() {
    HMODULE module = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
    if (module == nullptr) return false;
#define LOAD(field, symbol) api.field = reinterpret_cast<decltype(api.field)>(GetProcAddress(module, symbol)); if (api.field == nullptr) return false
    LOAD(createProfiler, "mono_profiler_create");
    LOAD(setJitBegin, "mono_profiler_set_jit_begin_callback");
    LOAD(setJitDone, "mono_profiler_set_jit_done_callback");
    LOAD(setJitFailed, "mono_profiler_set_jit_failed_callback");
    LOAD(methodGetClass, "mono_method_get_class");
    LOAD(classGetImage, "mono_class_get_image");
    LOAD(imageGetGuid, "mono_image_get_guid");
    LOAD(methodGetToken, "mono_method_get_token");
    LOAD(methodGetFlags, "mono_method_get_flags");
    LOAD(methodGetHeader, "mono_method_get_header");
    LOAD(headerGetCode, "mono_method_header_get_code");
    LOAD(freeHeader, "mono_metadata_free_mh");
    LOAD(domainGet, "mono_domain_get");
    LOAD(jitFind, "mono_jit_info_table_find");
    LOAD(jitCode, "mono_jit_info_get_code_start");
    LOAD(jitSize, "mono_jit_info_get_code_size");
    LOAD(jitMethod, "mono_jit_info_get_method");
#undef LOAD
    return true;
}
}

#define API extern "C" __declspec(dllexport)
API int __cdecl LihuatNative_Initialize(uint32_t version) {
    ExclusiveLock lock;
    if (version != 1) return 0;
    if (initialized) return 1;
    if (!Resolve()) return 0;
    void* profiler = api.createProfiler(reinterpret_cast<MonoProfiler*>(&identity));
    if (profiler == nullptr) return 0;
    api.setJitBegin(profiler, OnJitBegin);
    api.setJitDone(profiler, OnJitDone);
    api.setJitFailed(profiler, OnJitFailed);
    initialized = true;
    return 1;
}

API int __cdecl LihuatNative_Register(MonoMethod* method, const char* mvid, uint32_t token,
    const uint8_t* expected, const uint8_t* replacement, uint32_t length, void* destination, uint32_t flags) {
    ExclusiveLock lock;
    if (!initialized || length == 0 || length > MaxCil || expected == nullptr || replacement == nullptr
        || registry.size() >= MaxRecords || registry.find(method) != registry.end() || !MatchMethod(method, mvid, token)) return 0;
    try {
        auto record = std::make_unique<Record>();
        record->mode = destination == nullptr ? Mode::Rewrite : Mode::Guard;
        if (destination == nullptr && memcmp(expected, replacement, length) == 0) return 0;
        record->expected.assign(expected, expected + length);
        record->replacement.assign(replacement, replacement + length);
        record->destination = destination;
        Header header(method);
        if (!header.Equals(record->expected) || !PreventInlining(method, flags)) return 0;
        registry.emplace(method, std::move(record));
        return 1;
    } catch (...) { return 0; }
}

API int __cdecl LihuatNative_Publish(MonoMethod* method, void* address, void* destination) {
    ExclusiveLock lock;
    auto found = registry.find(method);
    if (found == registry.end() || found->second->mode != Mode::Guard || address == nullptr || destination == nullptr) return 0;
    Record& record = *found->second;
    try {
        MonoJitInfo* info = api.jitFind(api.domainGet(), address);
        if (info == nullptr || api.jitCode(info) != address || !PublishSite(record, method, info)) {
            record.state = State::EntryFailed;
            return 0;
        }
        if (record.state == State::WriteFailed) return 0;
        record.state = State::Applied;
        if (record.destination == destination) return record.state == State::Applied ? 1 : 0;
        std::vector<int64_t> branches;
        for (const lihuat::EntrySite& site : record.sites) {
            int64_t branch = 0;
            if (!lihuat::EntryBranch(site.address, destination, branch)) {
                record.state = State::EntryFailed;
                return 0;
            }
            branches.push_back(branch);
        }
        size_t changed = 0;
        for (; changed < record.sites.size(); ++changed) {
            auto& site = record.sites[changed];
            if (!lihuat::ExchangeEntry(site.address, site.installed, branches[changed])) break;
        }
        if (changed != record.sites.size()) {
            while (changed != 0) {
                --changed;
                lihuat::ExchangeEntry(record.sites[changed].address, branches[changed], record.sites[changed].installed);
            }
            record.state = State::EntryFailed;
            return 0;
        }
        for (size_t i = 0; i < record.sites.size(); ++i) record.sites[i].installed = branches[i];
        record.destination = destination;
        record.publications++;
        record.state = State::Applied;
        return 1;
    } catch (...) { record.state = State::EntryFailed; return 0; }
}

API int __cdecl LihuatNative_Status(MonoMethod* method, Snapshot* snapshot, uint32_t size) {
    if (snapshot == nullptr || size != sizeof(Snapshot)) return 0;
    SharedLock lock;
    auto found = registry.find(method);
    if (found == registry.end()) return 0;
    const Record& record = *found->second;
    bool intact = record.state == State::Applied;
    for (const lihuat::EntrySite& site : record.sites) intact = intact && lihuat::ReadEntry(site.address) == site.installed;
    *snapshot = {static_cast<uint32_t>(record.mode), static_cast<uint32_t>(record.state), record.starts, record.finishes,
        record.repairs, record.publications, static_cast<uint32_t>(record.sites.size()), intact ? 1u : 0u};
    return 1;
}

API int __cdecl LihuatNative_Remove(MonoMethod* method) {
    ExclusiveLock lock;
    auto found = registry.find(method);
    if (found == registry.end()) return 0;
    Record& record = *found->second;
    if (record.mode == Mode::Rewrite && record.state != State::Pending) return 0;
    for (const lihuat::EntrySite& site : record.sites)
        if (lihuat::ReadEntry(site.address) != site.installed) return 0;
    size_t restored = 0;
    for (; restored < record.sites.size(); ++restored) {
        const auto& site = record.sites[restored];
        if (!lihuat::ExchangeEntry(site.address, site.installed, site.initial)) break;
    }
    if (restored != record.sites.size()) {
        while (restored != 0) {
            const auto& site = record.sites[--restored];
            lihuat::ExchangeEntry(site.address, site.initial, site.installed);
        }
        record.state = State::EntryFailed;
        return 0;
    }
    registry.erase(found);
    return 1;
}
