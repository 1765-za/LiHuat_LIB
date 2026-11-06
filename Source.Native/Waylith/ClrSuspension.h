#pragma once
#include <cstdint>

namespace lihuat {
bool BeginCodeWrite();
bool CodeWriteActive();
bool ProfilerMethodIdentity(void* method, uintptr_t& module, uint32_t& token);
void EndCodeWrite();
class CodeWriteScope {
    bool entered;
public:
    CodeWriteScope() : entered(BeginCodeWrite()) {}
    ~CodeWriteScope() { if (entered) EndCodeWrite(); }
    explicit operator bool() const { return entered; }
    CodeWriteScope(const CodeWriteScope&) = delete;
    CodeWriteScope& operator=(const CodeWriteScope&) = delete;
};
}
