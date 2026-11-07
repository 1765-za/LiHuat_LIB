#include "NativeMethodGates.h"
#include "NativeMemoryRules.h"
#include "NativeWriteScope.h"
#include <windows.h>

namespace {
constexpr LONG Denied = static_cast<LONG>(0xc0000022u);
struct ErrorState {
    DWORD previous = GetLastError();
    ~ErrorState() { SetLastError(previous); }
};
template<class T> T Original(lihuat::NativeGate gate) {
    return reinterpret_cast<T>(lihuat::NativeGateContinuation(gate));
}
bool Output(const void* address, size_t bytes) { return lihuat::NativeWriteAllowed(address, bytes); }
bool ReferenceRange(HANDLE process, void** address, size_t* length, bool release = false) {
    if (!Output(address, sizeof(void*)) || !Output(length, sizeof(size_t))) return false;
    const void* value = nullptr; size_t bytes = 0;
    if (!lihuat::NativeMemoryInput(address, length, value, bytes)) return true;
    return release && !bytes ? lihuat::NativeAllocationReleaseAllowed(process, value)
        : lihuat::NativeProcessWriteAllowed(process, value, bytes ? bytes : 1, true);
}

LONG NTAPI Protect(HANDLE process, void** address, size_t* length, ULONG protection, ULONG* old) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, void**, size_t*, ULONG, ULONG*);
    constexpr auto gate = lihuat::NativeGate::Protect;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        Output(old, sizeof(ULONG)) && ReferenceRange(process, address, length))) return Denied;
    return Original<Call>(gate)(process, address, length, protection, old);
}
LONG NTAPI Write(HANDLE process, void* address, const void* buffer, size_t length, size_t* written) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, void*, const void*, size_t, size_t*);
    constexpr auto gate = lihuat::NativeGate::Write;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        Output(written, sizeof(size_t)) && lihuat::NativeProcessWriteAllowed(process, address, length))) return Denied;
    return Original<Call>(gate)(process, address, buffer, length, written);
}
LONG NTAPI Read(HANDLE process, const void* address, void* buffer, size_t length, size_t* read) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, const void*, void*, size_t, size_t*);
    constexpr auto gate = lihuat::NativeGate::Read;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        Output(read, sizeof(size_t)) && Output(buffer, length))) return Denied;
    return Original<Call>(gate)(process, address, buffer, length, read);
}
LONG NTAPI Free(HANDLE process, void** address, size_t* length, ULONG type) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, void**, size_t*, ULONG);
    constexpr auto gate = lihuat::NativeGate::Free;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        ReferenceRange(process, address, length, true))) return Denied;
    return Original<Call>(gate)(process, address, length, type);
}
LONG NTAPI Unmap(HANDLE process, const void* address) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, const void*);
    constexpr auto gate = lihuat::NativeGate::Unmap;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        lihuat::NativeAllocationReleaseAllowed(process, address))) return Denied;
    return Original<Call>(gate)(process, address);
}
LONG NTAPI UnmapEx(HANDLE process, const void* address, ULONG flags) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, const void*, ULONG);
    constexpr auto gate = lihuat::NativeGate::UnmapEx;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        lihuat::NativeAllocationReleaseAllowed(process, address))) return Denied;
    return Original<Call>(gate)(process, address, flags);
}
LONG NTAPI Allocate(HANDLE process, void** address, ULONG_PTR zeroBits, size_t* length, ULONG type, ULONG protection) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, void**, ULONG_PTR, size_t*, ULONG, ULONG);
    constexpr auto gate = lihuat::NativeGate::Allocate;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        ReferenceRange(process, address, length))) return Denied;
    return Original<Call>(gate)(process, address, zeroBits, length, type, protection);
}
LONG NTAPI AllocateEx(HANDLE process, void** address, size_t* length, ULONG type, ULONG protection,
    void* parameters, ULONG count) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, void**, size_t*, ULONG, ULONG, void*, ULONG);
    constexpr auto gate = lihuat::NativeGate::AllocateEx;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        ReferenceRange(process, address, length))) return Denied;
    return Original<Call>(gate)(process, address, length, type, protection, parameters, count);
}
LONG NTAPI Map(HANDLE section, HANDLE process, void** address, ULONG_PTR zeroBits, size_t commit,
    LARGE_INTEGER* offset, size_t* length, ULONG inherit, ULONG type, ULONG protection) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, HANDLE, void**, ULONG_PTR, size_t, LARGE_INTEGER*, size_t*, ULONG, ULONG, ULONG);
    constexpr auto gate = lihuat::NativeGate::Map;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        Output(offset, sizeof(LARGE_INTEGER)) && ReferenceRange(process, address, length))) return Denied;
    return Original<Call>(gate)(section, process, address, zeroBits, commit, offset, length, inherit, type, protection);
}
LONG NTAPI MapEx(HANDLE section, HANDLE process, void** address, LARGE_INTEGER* offset, size_t* length,
    ULONG type, ULONG protection, void* parameters, ULONG count) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, HANDLE, void**, LARGE_INTEGER*, size_t*, ULONG, ULONG, void*, ULONG);
    constexpr auto gate = lihuat::NativeGate::MapEx;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        Output(offset, sizeof(LARGE_INTEGER)) && ReferenceRange(process, address, length))) return Denied;
    return Original<Call>(gate)(section, process, address, offset, length, type, protection, parameters, count);
}
LONG NTAPI Query(HANDLE process, const void* address, ULONG kind, void* buffer, size_t length, size_t* returned) {
    ErrorState error;
    using Call = LONG(NTAPI*)(HANDLE, const void*, ULONG, void*, size_t, size_t*);
    constexpr auto gate = lihuat::NativeGate::Query;
    if (!lihuat::NativeWriteAuthorized() && !lihuat::AdmitNativeCall(gate,
        Output(returned, sizeof(size_t)) && Output(buffer, length))) return Denied;
    return Original<Call>(gate)(process, address, kind, buffer, length, returned);
}
const lihuat::NativeGateBinding bindings[] = {
    {L"ntdll.dll", "NtProtectVirtualMemory", reinterpret_cast<void*>(&Protect)},
    {L"ntdll.dll", "NtWriteVirtualMemory", reinterpret_cast<void*>(&Write)},
    {L"ntdll.dll", "NtReadVirtualMemory", reinterpret_cast<void*>(&Read)},
    {L"ntdll.dll", "NtFreeVirtualMemory", reinterpret_cast<void*>(&Free)},
    {L"ntdll.dll", "NtUnmapViewOfSection", reinterpret_cast<void*>(&Unmap)},
    {L"ntdll.dll", "NtUnmapViewOfSectionEx", reinterpret_cast<void*>(&UnmapEx)},
    {L"ntdll.dll", "NtAllocateVirtualMemory", reinterpret_cast<void*>(&Allocate)},
    {L"ntdll.dll", "NtAllocateVirtualMemoryEx", reinterpret_cast<void*>(&AllocateEx)},
    {L"ntdll.dll", "NtMapViewOfSection", reinterpret_cast<void*>(&Map)},
    {L"ntdll.dll", "NtMapViewOfSectionEx", reinterpret_cast<void*>(&MapEx)},
    {L"ntdll.dll", "NtQueryVirtualMemory", reinterpret_cast<void*>(&Query)}
};
static_assert(sizeof(bindings) / sizeof(bindings[0]) == static_cast<size_t>(lihuat::NativeGate::WinProtect));
}

const lihuat::NativeGateBinding* lihuat::NativeSystemBindings() { return bindings; }
