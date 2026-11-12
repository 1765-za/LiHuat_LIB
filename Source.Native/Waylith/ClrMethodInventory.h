#pragma once
#include <cstddef>
#include <cstdint>
#include <vector>

namespace lihuat {
struct CompiledMethodRegion { void* method; const void* address; size_t length; };
int32_t SnapshotMethodCode(const std::vector<void*>& methods, std::vector<CompiledMethodRegion>& regions);
}
