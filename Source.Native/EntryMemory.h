#pragma once
#include <windows.h>
#include <stdint.h>

namespace lihuat {
struct EntrySite {
    uint8_t* address;
    int64_t initial;
    int64_t installed;
};

bool EntryBranch(uint8_t* address, void* destination, int64_t& branch);
bool ExchangeEntry(uint8_t* address, int64_t expected, int64_t desired);
int64_t ReadEntry(uint8_t* address);
}
