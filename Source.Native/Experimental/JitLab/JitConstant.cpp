#include "JitLab.h"
#include <cstring>

namespace jitlab {
bool PatchConstant(const Rule& rule, Sample& sample) {
    int32_t original;
    if (rule.ilSize == 2 && rule.expected[0] >= 0x15 && rule.expected[0] <= 0x1e && rule.expected[1] == 0x2a)
        original = static_cast<int32_t>(rule.expected[0]) - 0x16;
    else if (rule.ilSize == 3 && rule.expected[0] == 0x1f && rule.expected[2] == 0x2a)
        original = static_cast<int8_t>(rule.expected[1]);
    else if (rule.ilSize == 6 && rule.expected[0] == 0x20 && rule.expected[5] == 0x2a)
        std::memcpy(&original, rule.expected + 1, sizeof(original));
    else return false;
    if (!sample.writable || sample.nativeSize > 16) return false;
    uint8_t bytes[16]{};
    SIZE_T read = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), sample.writable, bytes, sample.nativeSize, &read) || read != sample.nativeSize) return false;
    size_t offset;
    if (sample.nativeSize == 6 && bytes[0] == 0xb8 && bytes[5] == 0xc3) offset = 1;
    else if (sample.nativeSize == 11 && bytes[0] == 0x55 && bytes[1] == 0x48 && bytes[2] == 0x8b
        && bytes[3] == 0xec && bytes[4] == 0xb8 && bytes[9] == 0x5d && bytes[10] == 0xc3) offset = 5;
    else return false;
    int32_t actual;
    std::memcpy(&actual, bytes + offset, sizeof(actual));
    if (actual != original) return false;
    // Only the immediate changes; stack-frame and unwind instructions stay compiler-owned.
    std::memcpy(sample.writable + offset, &rule.value, sizeof(rule.value));
    return true;
}
}
