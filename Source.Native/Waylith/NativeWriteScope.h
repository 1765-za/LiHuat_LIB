#pragma once
#include <cstdint>

namespace lihuat {
class NativeWriteScope final {
    uintptr_t previous = 0;
    bool entered = false;
public:
    NativeWriteScope() noexcept;
    ~NativeWriteScope();
    NativeWriteScope(const NativeWriteScope&) = delete;
    NativeWriteScope& operator=(const NativeWriteScope&) = delete;
    explicit operator bool() const noexcept { return entered; }
};
bool InitializeNativeWriteScope();
bool NativeWriteAuthorized() noexcept;
}
