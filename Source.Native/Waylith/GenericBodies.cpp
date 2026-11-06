#include "GenericBodies.h"
#include "NativeAuthority.h"
#include "NativeMemoryOwnership.h"
#include "EntryWriteIndex.h"
#include "ClrProfilerRequests.h"
#include "HighMechanisms.h"
#include <windows.h>
#include <unordered_map>
#include <vector>
#include <atomic>
#include <algorithm>
#include <cstring>

namespace {
struct CodeRange { const void* address; uint32_t length; };
struct Body {
    void* owner;
    std::vector<uint8_t> current, previous;
    std::vector<CodeRange> code;
    uint32_t publications = 0, compilations = 0, rejected = 0;
};
SRWLOCK gate = SRWLOCK_INIT;
std::unordered_map<void*, Body> bodies;
std::unordered_map<void*, std::vector<void*>> owners;
std::atomic<bool> active{false};
lihuat::EntryWriteIndex writes;
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
    writes.Clear();
    for (const auto& item : bodies) {
        const Body& body = item.second;
        writes.Add(&body, sizeof(body));
        writes.Add(body.current.data(), body.current.capacity());
        writes.Add(body.previous.data(), body.previous.capacity());
        writes.Add(body.code.data(), body.code.capacity() * sizeof(CodeRange));
        for (const auto& code : body.code) writes.Add(code.address, code.length);
    }
    for (const auto& owner : owners) writes.Add(owner.second.data(), owner.second.capacity() * sizeof(void*));
    writes.Seal();
}
}

#define API extern "C" __declspec(dllexport)
API int __cdecl LihuatNative_GenericBody(uint64_t authority, void* owner, void* method,
    const uint8_t* current, uint32_t length, const uint8_t* previous, uint32_t previousLength) {
    if (!lihuat::Authorized(authority)) return -10;
    if (!owner || !method || !current || !length || length > 1048576 || previousLength > 1048576
        || (previousLength && !previous)) return -1;
    try {
        Body next{};
        next.owner = owner;
        next.current.assign(current, current + length);
        if (previousLength) next.previous.assign(previous, previous + previousLength);
        Exclusive lock;
        writes.Reserve(bodies.size() * 6 + codeCount + 8);
        auto old = bodies.find(method);
        if (old != bodies.end()) {
            if (old->second.owner != owner) return -2;
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
    Exclusive lock;
    auto found = bodies.find(method);
    if (found == bodies.end()) return 0;
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
    Shared lock;
    return Find(context, method) != nullptr;
}
bool GenericBodyAllowed(void* context, const MethodBodyInput& method) {
    if (!HasGenericBodies()) return true;
    Exclusive lock;
    Body* body = Find(context, method.method);
    if (!body) return true;
    bool matches = method.il && ((body->current.size() == method.length && !std::memcmp(body->current.data(), method.il, method.length))
        || (!body->previous.empty() && body->previous.size() == method.length && !std::memcmp(body->previous.data(), method.il, method.length)));
    if (!matches) ++body->rejected;
    return matches;
}
bool GenericBodyCompiled(void* context, void* method, void* code, uint32_t size) {
    if (!HasGenericBodies()) return true;
    Exclusive lock;
    Body* body = Find(context, method);
    if (!body) return true;
    try {
        if (std::none_of(body->code.begin(), body->code.end(), [code](const CodeRange& item) { return item.address == code; })) {
            writes.Reserve(bodies.size() * 6 + codeCount + 2);
            const CodeRange* old = body->code.data();
            body->code.push_back({code, size});
            ++codeCount;
            if (old != body->code.data()) IndexWrites();
            else writes.Include(code, size);
        }
        ++body->compilations;
        return true;
    } catch (const std::bad_alloc&) { ++body->rejected; return false; }
}
bool GenericBodyOverlaps(const void* address, size_t length) {
    if (!HasGenericBodies()) return false;
    Shared lock;
    return writes.OwnsStorage(address, length) || writes.Overlaps(address, length);
}
}
