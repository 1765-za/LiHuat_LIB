#pragma once
#include <windows.h>
#include <cstddef>

namespace lihuat {
bool NativeWriteAllowed(const void* address, size_t length, bool pages = false);
bool NativeProcessWriteAllowed(HANDLE process, const void* address, size_t length, bool pages = false);
bool NativeAllocationReleaseAllowed(HANDLE process, const void* address);
bool NativeMemoryInput(void* const* address, const size_t* length, const void*& value, size_t& bytes);
}
