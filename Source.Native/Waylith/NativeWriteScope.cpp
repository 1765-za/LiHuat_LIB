#include "NativeWriteScope.h"
#include "NativeAuthority.h"
#include <windows.h>

namespace {
DWORD slot = TLS_OUT_OF_INDEXES;
INIT_ONCE initialization = INIT_ONCE_STATIC_INIT;
BOOL CALLBACK Initialize(PINIT_ONCE, PVOID, PVOID*) {
    slot = TlsAlloc();
    return slot != TLS_OUT_OF_INDEXES;
}
uintptr_t Read() noexcept {
    if (slot == TLS_OUT_OF_INDEXES) return 0;
    DWORD error = GetLastError();
    uintptr_t value = reinterpret_cast<uintptr_t>(TlsGetValue(slot));
    SetLastError(error);
    return value;
}
bool Write(uintptr_t value) noexcept {
    DWORD error = GetLastError();
    bool changed = slot != TLS_OUT_OF_INDEXES && TlsSetValue(slot, reinterpret_cast<void*>(value)) != FALSE;
    SetLastError(error);
    return changed;
}
}

bool lihuat::InitializeNativeWriteScope() { return InitOnceExecuteOnce(&initialization, Initialize, nullptr, nullptr) != FALSE; }

// Memory entry hooks can run before the loader has installed this module's static TLS on a new thread.
lihuat::NativeWriteScope::NativeWriteScope() noexcept : previous(Read()), entered(Write(previous + 1)) { }
lihuat::NativeWriteScope::~NativeWriteScope() { if (entered) Write(previous); }
bool lihuat::NativeWriteAuthorized() noexcept { return Read() != 0; }

extern "C" __declspec(dllexport) int __cdecl LihuatNative_WriteOperation(uint64_t authority,
    int(__cdecl* operation)(void*), void* state) {
    if (!lihuat::Authorized(authority)) return -10;
    if (!operation) return -12;
    lihuat::NativeWriteScope scope;
    if (!scope) return -11;
    return operation(state);
}
