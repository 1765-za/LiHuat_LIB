#include "NativeMethodGates.h"
#include "NativeMemoryRules.h"
#include "NativeWriteScope.h"
#include <windows.h>

namespace {
template<class T> T Original(lihuat::NativeGate gate) {
    return reinterpret_cast<T>(lihuat::NativeGateContinuation(gate));
}
template<class Check> bool Allowed(lihuat::NativeGate gate, Check check) {
    if (lihuat::NativeWriteAuthorized()) return true;
    DWORD error = GetLastError();
    bool allowed = lihuat::AdmitNativeCall(gate, check());
    SetLastError(allowed ? error : ERROR_ACCESS_DENIED);
    return allowed;
}
bool Output(const void* address, size_t length) { return lihuat::NativeWriteAllowed(address, length); }

BOOL WINAPI Protect(void* address, SIZE_T length, DWORD protection, DWORD* previous) {
    using Call = BOOL(WINAPI*)(void*, SIZE_T, DWORD, DWORD*);
    constexpr auto gate = lihuat::NativeGate::WinProtect;
    if (!Allowed(gate, [&] { return Output(previous, sizeof(DWORD)) && lihuat::NativeWriteAllowed(address, length, true); })) return FALSE;
    return Original<Call>(gate)(address, length, protection, previous);
}
BOOL WINAPI ProtectEx(HANDLE process, void* address, SIZE_T length, DWORD protection, DWORD* previous) {
    using Call = BOOL(WINAPI*)(HANDLE, void*, SIZE_T, DWORD, DWORD*);
    constexpr auto gate = lihuat::NativeGate::WinProtectEx;
    if (!Allowed(gate, [&] { return Output(previous, sizeof(DWORD))
        && lihuat::NativeProcessWriteAllowed(process, address, length, true); })) return FALSE;
    return Original<Call>(gate)(process, address, length, protection, previous);
}
BOOL WINAPI Write(HANDLE process, void* address, const void* buffer, SIZE_T length, SIZE_T* written) {
    using Call = BOOL(WINAPI*)(HANDLE, void*, const void*, SIZE_T, SIZE_T*);
    constexpr auto gate = lihuat::NativeGate::WinWrite;
    if (!Allowed(gate, [&] { return Output(written, sizeof(SIZE_T))
        && lihuat::NativeProcessWriteAllowed(process, address, length); })) return FALSE;
    return Original<Call>(gate)(process, address, buffer, length, written);
}
BOOL WINAPI Read(HANDLE process, const void* address, void* buffer, SIZE_T length, SIZE_T* read) {
    using Call = BOOL(WINAPI*)(HANDLE, const void*, void*, SIZE_T, SIZE_T*);
    constexpr auto gate = lihuat::NativeGate::WinRead;
    if (!Allowed(gate, [&] { return Output(read, sizeof(SIZE_T)) && Output(buffer, length); })) return FALSE;
    return Original<Call>(gate)(process, address, buffer, length, read);
}
SIZE_T WINAPI Query(const void* address, MEMORY_BASIC_INFORMATION* buffer, SIZE_T length) {
    using Call = SIZE_T(WINAPI*)(const void*, MEMORY_BASIC_INFORMATION*, SIZE_T);
    constexpr auto gate = lihuat::NativeGate::WinQuery;
    if (!Allowed(gate, [&] { return Output(buffer, length); })) return 0;
    return Original<Call>(gate)(address, buffer, length);
}
SIZE_T WINAPI QueryEx(HANDLE process, const void* address, MEMORY_BASIC_INFORMATION* buffer, SIZE_T length) {
    using Call = SIZE_T(WINAPI*)(HANDLE, const void*, MEMORY_BASIC_INFORMATION*, SIZE_T);
    constexpr auto gate = lihuat::NativeGate::WinQueryEx;
    if (!Allowed(gate, [&] { return Output(buffer, length); })) return 0;
    return Original<Call>(gate)(process, address, buffer, length);
}
const lihuat::NativeGateBinding bindings[] = {
    {L"kernelbase.dll", "VirtualProtect", reinterpret_cast<void*>(&Protect)},
    {L"kernelbase.dll", "VirtualProtectEx", reinterpret_cast<void*>(&ProtectEx)},
    {L"kernelbase.dll", "WriteProcessMemory", reinterpret_cast<void*>(&Write)},
    {L"kernelbase.dll", "ReadProcessMemory", reinterpret_cast<void*>(&Read)},
    {L"kernelbase.dll", "VirtualQuery", reinterpret_cast<void*>(&Query)},
    {L"kernelbase.dll", "VirtualQueryEx", reinterpret_cast<void*>(&QueryEx)}
};
static_assert(sizeof(bindings) / sizeof(bindings[0]) == static_cast<size_t>(lihuat::NativeGate::Count)
    - static_cast<size_t>(lihuat::NativeGate::WinProtect));
}

const lihuat::NativeGateBinding* lihuat::NativeWin32Bindings() { return bindings; }
