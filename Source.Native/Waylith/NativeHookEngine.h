#pragma once
#include <cstddef>

namespace lihuat {
struct NativeHookLayout { void* patch; size_t patchBytes; void* trampoline; void* destination; bool enabled; };
int InitializeNativeHooks();
int CreateNativeHook(void* target, void* callback, void** original);
int SetNativeHooks(bool enabled);
int ReleaseUnusedNativeHooks();
bool DescribeNativeHook(void* target, NativeHookLayout& layout);
void NativeHookStorage(const void*& address, size_t& bytes);
}
