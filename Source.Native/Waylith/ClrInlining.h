#pragma once
#include <cstdint>

namespace lihuat {
struct InliningContext {
    void** table;
    void* original;
    uint64_t flags = 0;
    bool flagsRead = false;
    uint8_t* hot = nullptr;
    uint8_t* writable = nullptr;
    uint32_t capacity = 0;
};
bool InitializeInlining();
void** InliningTable();
uint64_t BlockedInlines();
bool PreventInlining(void* method);
bool HasProtectedEntries();
uint32_t ProtectedEntryCount();
}
