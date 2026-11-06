#pragma once
#include <cstdint>

namespace lihuat {
bool InstallCompilationObserver(void* continuation);
bool CompilationObserverActive();
uint64_t CompilationNotifications();
uint64_t CompilationGeneration(void* key);
uint64_t RegistrationRevision();
uint64_t StaleCompilations();
bool Compiled(void* key, uint64_t generation, uint8_t* code, uint64_t size, uint8_t* writable = nullptr);
struct CompilationStatus {
    uint64_t notifications, guarded, blockedInlines, stale;
    uint32_t entries, installed;
};
}
