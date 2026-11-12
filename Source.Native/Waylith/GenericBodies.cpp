#include "GenericBodies.h"
#include "NativeAuthority.h"
#include "NativeMemoryOwnership.h"
#include "EntryWriteIndex.h"
#include "NativeWriteSnapshot.h"
#include "ClrProfilerRequests.h"
#include "HighMechanisms.h"
#include "NativeWriteScope.h"
#include "EntryMemory.h"
#include "ClrMethodInventory.h"
#include "ClrCompilation.h"
#include "../ThirdParty/MinHook/src/hde/hde64.h"
#include <windows.h>
#include <unordered_map>
#include <vector>
#include <atomic>
#include <algorithm>
#include <cstring>

namespace {
struct CodeRange { const void* address; uint32_t length; bool entry = false; };
struct Body {
    void* owner;
    std::vector<uint8_t> current, previous;
    std::vector<uint8_t> metadata;
    std::vector<CodeRange> code;
    uint32_t publications = 0, compilations = 0, rejected = 0;
};
SRWLOCK gate = SRWLOCK_INIT;
std::unordered_map<void*, Body> bodies;
std::unordered_map<void*, std::vector<void*>> owners;
std::atomic<bool> active{false};
lihuat::EntryWriteIndex writes;
lihuat::NativeWriteSnapshot writeSnapshot;
size_t codeCount = 0;
struct Shared { Shared() { AcquireSRWLockShared(&gate); } ~Shared() { ReleaseSRWLockShared(&gate); } };
struct Exclusive { Exclusive() { AcquireSRWLockExclusive(&gate); } ~Exclusive() { ReleaseSRWLockExclusive(&gate); } };

Body* Find(void* context, void* method) {
    auto exact = bodies.find(method);
    if (exact != bodies.end()) return &exact->second;
    auto table = *static_cast<void***>(context);
    auto type = reinterpret_cast<void*(__stdcall*)(void*, void*)>(table[14])(context, method);
    auto definition = reinterpret_cast<void*(__stdcall*)(void*, void*)>(table[7])(context, type);
    auto group = owners.find(definition);
    if (group == owners.end()) return nullptr;
    auto same = reinterpret_cast<bool(__stdcall*)(void*, void*, void*)>(table[6]);
    for (void* key : group->second) if (same(context, key, method)) return &bodies.at(key);
    return nullptr;
}

void IndexWrites() {
    auto previous = writes.Reserve(bodies.size() * 7 + codeCount + 8);
    writes.Clear();
    for (const auto& item : bodies) {
        const Body& body = item.second;
        writes.Add(&body, sizeof(body));
        writes.Add(body.current.data(), body.current.capacity());
        writes.Add(body.previous.data(), body.previous.capacity());
        writes.Add(body.metadata.data(), body.metadata.capacity());
        writes.Add(body.code.data(), body.code.capacity() * sizeof(CodeRange));
        for (const auto& code : body.code) writes.Add(code.address, code.length);
    }
    for (const auto& owner : owners) writes.Add(owner.second.data(), owner.second.capacity() * sizeof(void*));
    writes.Seal();
    writeSnapshot.Publish(writes);
}

uint32_t EntryLength(const uint8_t* address) {
    uint32_t length = 0;
    while (length < 128) {
        uint8_t bytes[16];
        if (!lihuat::TryCopy(address + length, bytes, sizeof(bytes))) return 0;
        hde64s instruction{};
        unsigned size = hde64_disasm(bytes, &instruction);
        if (!size || (instruction.flags & F_ERROR)) return 0;
        length += size;
        if (instruction.opcode == 0xe9 || instruction.opcode == 0xeb || instruction.opcode == 0xc3
            || instruction.opcode == 0xc2 || (instruction.opcode == 0xff && instruction.modrm_reg == 4)) return length;
    }
    return 0;
}
}

#define API extern "C" __declspec(dllexport)
API int __cdecl LihuatNative_GenericBody(uint64_t authority, void* owner, void* method,
    const uint8_t* current, uint32_t length, const uint8_t* previous, uint32_t previousLength) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    if (!owner || !method || !current || !length || length > 1048576 || previousLength > 1048576
        || (previousLength && !previous)) return -1;
    try {
        Body next{};
        next.owner = owner;
        next.current.assign(current, current + length);
        if (previousLength) next.previous.assign(previous, previous + previousLength);
        Exclusive lock;
        auto old = bodies.find(method);
        if (old != bodies.end()) {
            if (old->second.owner != owner) return -2;
            next.metadata = std::move(old->second.metadata);
            next.code = std::move(old->second.code);
            next.publications = old->second.publications;
            next.compilations = old->second.compilations;
            next.rejected = old->second.rejected;
        } else {
            auto& group = owners[owner];
            try {
                group.reserve(group.size() + 1);
                bodies.emplace(method, Body{});
            } catch (...) { if (group.empty()) owners.erase(owner); throw; }
            group.push_back(method);
        }
        ++next.publications;
        bodies.insert_or_assign(method, std::move(next));
        IndexWrites();
        active.store(true, std::memory_order_release);
        return 1;
    } catch (const std::bad_alloc&) { return 0; }
}

API int __cdecl LihuatNative_GenericRelease(uint64_t authority, void* method) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    Exclusive lock;
    auto found = bodies.find(method);
    if (found == bodies.end()) return 0;
    if (!found->second.metadata.empty()) {
        found->second.current = found->second.metadata;
        found->second.previous.clear();
        ++found->second.publications;
        IndexWrites();
        return 1;
    }
    auto group = owners.find(found->second.owner);
    auto& keys = group->second;
    keys.erase(std::remove(keys.begin(), keys.end(), method), keys.end());
    if (keys.empty()) owners.erase(group);
    codeCount -= found->second.code.size();
    bodies.erase(found);
    IndexWrites();
    active.store(!bodies.empty(), std::memory_order_release);
    return 1;
}

struct BodyStatus { uint32_t publications, compilations, rejected, registered, intact; };
API int __cdecl LihuatNative_GenericStatus(void* method, BodyStatus* output, uint32_t size) {
    if (size != sizeof(BodyStatus) || !lihuat::NativeOutputAllowed(output, size)) return 0;
    Shared lock;
    auto found = bodies.find(method);
    if (found == bodies.end()) { *output = {}; return 1; }
    const auto& body = found->second;
    *output = {body.publications, body.compilations, body.rejected, 1,
        !lihuat::NativeProtectionEnabled() || lihuat::ProfilerRequestsIntact() ? 1u : 0u};
    return 1;
}

namespace lihuat {
bool HasGenericBodies() { return active.load(std::memory_order_acquire); }
bool GenericInlineBlocked(void* context, void* method) {
    if (!HasGenericBodies()) return false;
    NativeWriteScope nativeWrite;
    if (!nativeWrite) return true;
    Shared lock;
    return Find(context, method) != nullptr;
}
bool GenericBodyAllowed(void* context, const MethodBodyInput& method) {
    if (!HasGenericBodies()) return true;
    NativeWriteScope nativeWrite;
    if (!nativeWrite) return false;
    Exclusive lock;
    Body* body = Find(context, method.method);
    if (!body) return true;
    bool matches = (!body->metadata.empty() && PublishedMethod(method.method))
        || (method.il && ((body->current.size() == method.length && !std::memcmp(body->current.data(), method.il, method.length))
        || (!body->previous.empty() && body->previous.size() == method.length && !std::memcmp(body->previous.data(), method.il, method.length))));
    if (!matches) ++body->rejected;
    return matches;
}

struct MetadataBodyInput { void* owner; void* method; const uint8_t* code; void* precode; uint32_t length; };
API int __cdecl LihuatNative_ProtectMetadata(uint64_t authority, const MetadataBodyInput* input, uint32_t count) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    if (!input || !count || count > 100000) return -1;
    try {
        std::vector<std::pair<void*, Body>> additions;
        std::vector<void*> keys;
        additions.reserve(count); keys.reserve(count);
        for (uint32_t i = 0; i < count; ++i) {
            const auto& value = input[i];
            if (!value.owner || !value.method || !value.code || !value.length || value.length > 1048576) return -1;
            Body body{};
            body.owner = value.owner;
            body.current.assign(value.code, value.code + value.length);
            body.metadata = body.current;
            if (value.precode) {
                lihuat::EntryRoute route;
                void** slot = nullptr;
                uint8_t* code = nullptr;
                bool pending = false;
                if (!lihuat::CodeEntry(value.precode, slot, code, &route, &pending) && !pending) return -2;
                for (size_t j = 0; j < route.count; ++j)
                    body.code.push_back({route.observations[j].address, route.observations[j].writeSize, true});
            }
            keys.push_back(value.method);
            additions.emplace_back(value.method, std::move(body));
        }
        {
            Exclusive lock;
            auto nextBodies = bodies;
            auto nextOwners = owners;
            for (auto& addition : additions) {
                auto found = nextBodies.find(addition.first);
                if (found == nextBodies.end()) {
                    nextOwners[addition.second.owner].push_back(addition.first);
                    nextBodies.emplace(addition.first, std::move(addition.second));
                } else {
                    if (found->second.owner != addition.second.owner || !found->second.metadata.empty()) return -3;
                    found->second.metadata = std::move(addition.second.metadata);
                    found->second.code.insert(found->second.code.end(), addition.second.code.begin(), addition.second.code.end());
                }
            }
            size_t nextCodeCount = 0;
            for (const auto& entry : nextBodies) nextCodeCount += entry.second.code.size();
            bodies.swap(nextBodies); owners.swap(nextOwners); codeCount = nextCodeCount;
            IndexWrites();
            active.store(true, std::memory_order_release);
        }
        std::vector<lihuat::CompiledMethodRegion> regions;
        int32_t result = lihuat::SnapshotMethodCode(keys, regions);
        if (result < 0) return result;
        Exclusive lock;
        for (const auto& region : regions) {
            auto& body = bodies.at(region.method);
            if (!region.length || region.length > UINT32_MAX) return -4;
            if (std::none_of(body.code.begin(), body.code.end(), [&](const CodeRange& code) {
                return code.address == region.address && code.length >= region.length;
            })) {
                body.code.push_back({region.address, static_cast<uint32_t>(region.length)});
                ++codeCount;
            }
        }
        IndexWrites();
        return 1;
    } catch (const std::bad_alloc&) { return 0; }
}

API int __cdecl LihuatNative_ProtectEntry(uint64_t authority, void* method, void* precode) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    lihuat::EntryRoute route;
    void** slot = nullptr;
    uint8_t* code = nullptr;
    bool pending = false;
    if (!lihuat::CodeEntry(precode, slot, code, &route, &pending) && !pending) return -1;
    Exclusive lock;
    auto found = bodies.find(method);
    if (found == bodies.end() || found->second.metadata.empty()) return -2;
    Body& body = found->second;
    uint32_t extra = 0;
    if (code && !pending && std::none_of(body.code.begin(), body.code.end(), [code](const CodeRange& region) {
        return !region.entry && region.address == code;
    })) {
        extra = EntryLength(code);
        if (!extra) return -3;
    }
    try {
        bool changed = false;
        auto include = [&](const void* address, uint32_t length) {
            if (std::any_of(body.code.begin(), body.code.end(), [&](const CodeRange& region) {
                return region.address == address && region.length >= length;
            })) return;
            body.code.push_back({address, length, true});
            ++codeCount; changed = true;
        };
        for (size_t i = 0; i < route.count; ++i) include(route.observations[i].address, route.observations[i].writeSize);
        if (extra) include(code, extra);
        if (changed) IndexWrites();
        return 1;
    } catch (const std::bad_alloc&) { return 0; }
}

struct MetadataStatus { uint32_t methods, regions; uint64_t compilations, rejected; };
API int __cdecl LihuatNative_MetadataStatus(MetadataStatus* output, uint32_t bytes) {
    if (bytes != sizeof(MetadataStatus) || !lihuat::NativeOutputAllowed(output, bytes)) return 0;
    Shared lock;
    MetadataStatus value{};
    for (const auto& entry : bodies) {
        const Body& body = entry.second;
        if (body.metadata.empty()) continue;
        ++value.methods;
        value.regions += static_cast<uint32_t>(body.code.size());
        value.compilations += body.compilations;
        value.rejected += body.rejected;
    }
    *output = value;
    return 1;
}
bool GenericBodyCompiled(void* context, void* method, void* code, uint32_t size) {
    if (!HasGenericBodies()) return true;
    NativeWriteScope nativeWrite;
    if (!nativeWrite) return false;
    Exclusive lock;
    Body* body = Find(context, method);
    if (!body) return true;
    try {
        if (std::none_of(body->code.begin(), body->code.end(), [code](const CodeRange& item) { return item.address == code; })) {
            const CodeRange* old = body->code.data();
            body->code.push_back({code, size});
            ++codeCount;
            if (old != body->code.data()) IndexWrites();
            else { auto previous = writes.Include(code, size); writeSnapshot.Publish(writes); }
        }
        ++body->compilations;
        return true;
    } catch (const std::bad_alloc&) { ++body->rejected; return false; }
}
bool GenericBodyOverlaps(const void* address, size_t length) {
    if (!HasGenericBodies()) return false;
    return writeSnapshot.Overlaps(address, length);
}
}
