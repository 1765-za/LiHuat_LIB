#pragma once
#include <cstdint>
#include <cstddef>

namespace lihuat {
bool InstallProfilerRequests(void* information);
bool ProfilerRequestsIntact();
bool ProfilerRequestsOverlaps(const void* address, size_t length);
}
