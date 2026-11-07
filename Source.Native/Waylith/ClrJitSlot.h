#pragma once
#include <cstdint>
#include <cstddef>

namespace lihuat {
bool OwnCompilationSlot(void* compiler, void** slot, void* callback, void* continuation);
bool RepairCompilationSlot();
bool CompilationSlotActive();
bool CompilationSlotOverlaps(const void* address, size_t length);
struct JitSlotStatus {
    void* slot;
    void* current;
    void* control;
    void* continuation;
    void* displaced;
    uint64_t publications, contentions, failures;
    uint32_t active, reserved;
};
}
