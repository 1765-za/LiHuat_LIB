#include "GenerationAdmission.h"
#include "NativeAuthority.h"
#include "NativeWriteScope.h"
#include "NativeWriteSnapshot.h"
#include <windows.h>
#include <cstdint>
#include <memory>
#include <unordered_map>
#include <vector>
#include <algorithm>
#include <atomic>

namespace {
struct Permit { uint64_t ticket, subject; DWORD thread; };
struct Session {
    uint64_t revision = 0;
    bool blocked = false;
    std::vector<uint8_t> admitted;
    std::vector<Permit> permits;
};
SRWLOCK gate = SRWLOCK_INIT;
std::unordered_map<uint64_t, std::unique_ptr<Session>> sessions;
std::atomic<uint64_t> unrestrictedSession{0};
uint64_t nextSession = 0, nextTicket = 0;
lihuat::NativeWriteSnapshot writeSnapshot;
struct Shared { Shared() { AcquireSRWLockShared(&gate); } ~Shared() { ReleaseSRWLockShared(&gate); } };
struct Exclusive { Exclusive() { AcquireSRWLockExclusive(&gate); } ~Exclusive() { ReleaseSRWLockExclusive(&gate); } };
Session* Find(uint64_t id) {
    auto found = sessions.find(id);
    return found == sessions.end() ? nullptr : found->second.get();
}
bool Recovering(const Session& state, uint64_t subject) {
    if (!subject || state.permits.empty()) return false;
    DWORD thread = GetCurrentThreadId();
    return std::any_of(state.permits.begin(), state.permits.end(), [=](const Permit& permit) {
        return permit.subject == subject && permit.thread == thread;
    });
}
bool Known(const Session& state, uint64_t subject) {
    return subject && subject < state.admitted.size() && state.admitted[static_cast<size_t>(subject)] != 0;
}
void PublishWrites() {
    lihuat::EntryWriteIndex ranges;
    ranges.Reserve(sessions.size() * 4);
    for (const auto& entry : sessions) {
        const Session& state = *entry.second;
        ranges.Add(&entry, sizeof(entry));
        ranges.Add(&state, sizeof(state));
        ranges.Add(state.admitted.data(), state.admitted.capacity());
        ranges.Add(state.permits.data(), state.permits.capacity() * sizeof(Permit));
    }
    ranges.Seal();
    writeSnapshot.Publish(ranges, false);
}
}

#define API extern "C" __declspec(dllexport)
API uint64_t __cdecl LihuatNative_AdmissionOpen(uint64_t authority) {
    if (!lihuat::Authorized(authority)) return 0;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return 0;
    Exclusive lock;
    try {
        auto value = std::make_unique<Session>();
        uint64_t id = ++nextSession;
        sessions.emplace(id, std::move(value));
        PublishWrites();
        unrestrictedSession.store(id, std::memory_order_release);
        return id;
    } catch (...) { return 0; }
}
API int __cdecl LihuatNative_AdmissionClose(uint64_t authority, uint64_t session) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    Exclusive lock;
    if (unrestrictedSession.load(std::memory_order_relaxed) == session)
        unrestrictedSession.store(0, std::memory_order_release);
    if (!sessions.erase(session)) return 0;
    PublishWrites();
    return 1;
}
API int __cdecl LihuatNative_AdmissionPublish(uint64_t authority, uint64_t session, uint64_t revision, int blocked) {
    if (!lihuat::Authorized(authority)) return -10;
    Exclusive lock;
    Session* state = Find(session);
    if (!state || revision != state->revision + 1 || !revision) return 0;
    // Invalidate the unrestricted read before restricting or releasing its session.
    if (blocked && unrestrictedSession.load(std::memory_order_relaxed) == session)
        unrestrictedSession.store(0, std::memory_order_release);
    state->blocked = blocked != 0;
    state->revision = revision;
    if (!state->blocked) unrestrictedSession.store(session, std::memory_order_release);
    return 1;
}
API int __cdecl LihuatNative_AdmissionCheck(uint64_t session, uint64_t subject, int operation) {
    if (session && operation >= 0 && operation <= 2
        && unrestrictedSession.load(std::memory_order_acquire) == session) return 1;
    Shared lock;
    Session* state = Find(session);
    if (!state) return 0;
    if (operation == 3) return Known(*state, subject) ? 1 : 0;
    if (operation < 0 || operation > 2) return 0;
    if (!state->blocked) return 1;
    if (operation == 0) return 0;
    return (operation == 1 && Known(*state, subject)) || Recovering(*state, subject) ? 1 : 0;
}
API int __cdecl LihuatNative_AdmissionRemember(uint64_t authority, uint64_t session, uint64_t subject, int present) {
    if (!lihuat::Authorized(authority)) return -10;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return -11;
    Exclusive lock;
    Session* state = Find(session);
    if (!state || !subject || subject > 16777216) return 0;
    if (present && state->blocked && !Known(*state, subject) && !Recovering(*state, subject)) return 0;
    try {
        if (subject >= state->admitted.size()) {
            if (!present) return 1;
            state->admitted.resize(static_cast<size_t>(subject) + 1);
            PublishWrites();
        }
        state->admitted[static_cast<size_t>(subject)] = present ? 1 : 0;
        return 1;
    } catch (...) { return 0; }
}
API uint64_t __cdecl LihuatNative_AdmissionRecover(uint64_t authority, uint64_t session, uint64_t subject) {
    if (!lihuat::Authorized(authority) || !subject) return 0;
    lihuat::NativeWriteScope nativeWrite;
    if (!nativeWrite) return 0;
    Exclusive lock;
    Session* state = Find(session);
    if (!state) return 0;
    try {
        uint64_t ticket = ++nextTicket;
        size_t capacity = state->permits.capacity();
        state->permits.push_back({ticket, subject, GetCurrentThreadId()});
        if (capacity != state->permits.capacity()) PublishWrites();
        return ticket;
    } catch (...) { return 0; }
}
API int __cdecl LihuatNative_AdmissionEndRecovery(uint64_t authority, uint64_t session, uint64_t ticket) {
    if (!lihuat::Authorized(authority)) return -10;
    Exclusive lock;
    Session* state = Find(session);
    if (!state || state->permits.empty()) return 0;
    DWORD thread = GetCurrentThreadId();
    auto permit = std::find_if(state->permits.rbegin(), state->permits.rend(), [=](const Permit& value) { return value.thread == thread; });
    if (permit == state->permits.rend() || permit->ticket != ticket) return 0;
    state->permits.erase(std::next(permit).base());
    return 1;
}

bool lihuat::GenerationAdmissionOverlaps(const void* address, size_t length) {
    return writeSnapshot.Overlaps(address, length);
}
