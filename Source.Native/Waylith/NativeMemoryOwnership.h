#pragma once
#include <cstddef>

namespace lihuat {
enum class NativeAllocation { Inlining, Relay };
bool InitializeNativeImage();
void OwnNativeAllocation(NativeAllocation kind, const void* address, size_t length);
bool NativeMemoryOverlaps(const void* address, size_t length);
bool NativeOutputAllowed(const void* address, size_t length);
}
