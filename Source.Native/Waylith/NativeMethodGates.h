#pragma once
#include <cstddef>
#include <cstdint>

namespace lihuat {
enum class NativeGate : uint32_t {
    Protect, Write, Read, Free, Unmap, UnmapEx, Allocate, AllocateEx, Map, MapEx, Query,
    WinProtect, WinProtectEx, WinWrite, WinRead, WinQuery, WinQueryEx, Count
};
struct NativeGateBinding { const wchar_t* module; const char* name; void* callback; };
const NativeGateBinding* NativeSystemBindings();
const NativeGateBinding* NativeWin32Bindings();
void* NativeGateContinuation(NativeGate gate);
bool AdmitNativeCall(NativeGate gate, bool allowed);
bool NativeMethodGatesOverlap(const void* address, size_t length);
}
