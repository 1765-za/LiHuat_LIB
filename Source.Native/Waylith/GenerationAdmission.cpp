#include "GenerationAdmission.h"
#include "NativeAuthority.h"
#include <windows.h>
#include <cstdint>
#include <memory>
#include <unordered_map>
#include <vector>
#include <algorithm>

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
uint64_t nextSession = 0, nextTicket = 0;
struct Shared { Shared() { AcquireSRWLockShared(&gate); } ~Shared() { ReleaseSRWLockShared(&gate); } };
struct Exclusive { Exclusive() { AcquireSRWLockExclusive(&gate); } ~Exclusive() { ReleaseSRWLockExclusive(&gate); } };
Session* Find(uint64_t id) {
    auto found = sessions.find(id);
    return found == sessions.end() ? nullptr : found->second.get();
}
bool Recovering(const Session& state, uint64_t subject) {
    if (!subject) return false;
    DWORD thread = GetCurrentThreadId();
    return std::any_of(state.permits.begin(), state.permits.end(), [=](const Permit& permit) {
        return permit.subject == subject && permit.thread == thread;
    });
}
bool Known(const Session& state, uint64_t subject) {
    return subject && subject < state.admitted.size() && state.admitted[static_cast<size_t>(subject)] != 0;
}
bool Overlaps(const void* start, size_t length, const void* owned, size_t size) {
    auto a = reinterpret_cast<uintptr_t>(start), b = reinterpret_cast<uintptr_t>(owned);
    return a && b && length && size && a <= UINTPTR_MAX - length && b <= UINTPTR_MAX - size
        && a < b + size && b < a + length;
}
}

#define API extern "C" __declspec(dllexport)
API uint64_t __cdecl LihuatNative_AdmissionOpen(uint64_t authority) {
    if (!lihuat::Authorized(authority)) return 0;
    Exclusive lock;
    try {
        auto value = std::make_unique<Session>();
        uint64_t id = ++nextSession;
        sessions.emplace(id, std::move(value));
        return id;
    } catch (...) { return 0; }
}
API int __cdecl LihuatNative_AdmissionClose(uint64_t authority, uint64_t session) {
    if (!lihuat::Authorized(authority)) return -10;
    Exclusive lock;
    return sessions.erase(session) ? 1 : 0;
}
API int __cdecl LihuatNative_AdmissionPublish(uint64_t authority, uint64_t session, uint64_t revision, int blocked) {
    if (!lihuat::Authorized(authority)) return -10;
    Exclusive lock;
    Session* state = Find(session);
    if (!state || revision != state->revision + 1 || !revision) return 0;
    state->blocked = blocked != 0;
    state->revision = revision;
    return 1;
}
API int __cdecl LihuatNative_AdmissionCheck(uint64_t session, uint64_t subject, int operation) {
    Shared lock;
    Session* state = Find(session);
    if (!state) return 0;
    if (operation == 3) return Known(*state, subject) ? 1 : 0;
    if (operation < 0 || operation > 2) return 0;
    if (!state->blocked) return 1;
    if (operation == 0) return 0;
    return Recovering(*state, subject) || (operation == 1 && Known(*state, subject)) ? 1 : 0;
}
API int __cdecl LihuatNative_AdmissionRemember(uint64_t authority, uint64_t session, uint64_t subject, int present) {
    if (!lihuat::Authorized(authority)) return -10;
    Exclusive lock;
    Session* state = Find(session);
    if (!state || !subject || subject > 16777216) return 0;
    if (present && state->blocked && !Known(*state, subject) && !Recovering(*state, subject)) return 0;
    try {
        if (subject >= state->admitted.size()) {
            if (!present) return 1;
            state->admitted.resize(static_cast<size_t>(subject) + 1);
        }
        state->admitted[static_cast<size_t>(subject)] = present ? 1 : 0;
        return 1;
    } catch (...) { return 0; }
}
API uint64_t __cdecl LihuatNative_AdmissionRecover(uint64_t authority, uint64_t session, uint64_t subject) {
    if (!lihuat::Authorized(authority) || !subject) return 0;
    Exclusive lock;
    Session* state = Find(session);
    if (!state) return 0;
    try {
        uint64_t ticket = ++nextTicket;
        state->permits.push_back({ticket, subject, GetCurrentThreadId()});
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
    Shared lock;
    for (const auto& entry : sessions) {
        const Session& state = *entry.second;
        if (Overlaps(address, length, &entry, sizeof(entry)) || Overlaps(address, length, &state, sizeof(state))
            || Overlaps(address, length, state.admitted.data(), state.admitted.capacity())
            || Overlaps(address, length, state.permits.data(), state.permits.capacity() * sizeof(Permit))) return true;
    }
    return false;
}
