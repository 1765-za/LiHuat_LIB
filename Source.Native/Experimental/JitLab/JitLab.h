#pragma once
#include <windows.h>
#include <cstdint>
#include <cstddef>

#define LAB_API extern "C" __declspec(dllexport)
namespace jitlab {
constexpr uint32_t Abi = 1, MaxRules = 64, MaxIl = 512;
constexpr size_t InfoSlots = 176, AllocMemSlot = 160;
constexpr size_t CanInlineSlot = 8, ReportInlineSlot = 10;
constexpr uint32_t MaxCallers = 32, MaxEvents = 256, MaxQueries = 256;
struct MethodInfo {
    void* method;
    void* scope;
    const uint8_t* il;
    uint32_t ilSize, maxStack, ehCount;
    int32_t options, regionKind;
};
static_assert(offsetof(MethodInfo, ilSize) == 24 && offsetof(MethodInfo, ehCount) == 32);
using Compile = int(__stdcall*)(void*, void*, MethodInfo*, unsigned, uint8_t**, uint32_t*);
enum class Mode : uint32_t { Observe, ReturnInt32, SkipVoid, PatchInt32Constant };
struct Rule {
    void* method;
    uint64_t generation;
    Mode mode;
    int32_t value;
    uint32_t ilSize;
    uint8_t expected[MaxIl];
};
struct Sample {
    uint64_t generation, calls, rewrites, mismatches;
    void* method;
    uint8_t* executable;
    uint8_t* writable;
    uint32_t nativeSize, allocations, matched;
    int32_t result;
    uint64_t executableAtReturn, writableAtReturn, executableNow;
    uint32_t readMask, originalIlSize, submittedIlSize, ehCount;
};
struct AllocArgs {
    uint32_t hotSize, coldSize, dataSize, exceptionCount;
    int32_t flags;
    uint8_t* hot;
    uint8_t* hotWritable;
    uint8_t* cold;
    uint8_t* coldWritable;
    uint8_t* data;
    uint8_t* dataWritable;
};
static_assert(offsetof(AllocArgs, hot) == 24);
struct InfoProxy {
    void** vtable;
    void* original;
    void* rootMethod;
    AllocArgs allocation{};
    uint32_t allocations = 0;
};
struct PolicyQuery { void* method; uint64_t generation; };
struct Policy { uint64_t generation; uint32_t mode; int32_t value; uint32_t found, current; };
enum class EventKind : uint32_t { Registered = 1, Released, Compiled, InlineConsidered, InlineBlocked, Evaluated };
struct Event {
    uint64_t sequence, generation;
    void* method;
    void* caller;
    void* root;
    uint32_t kind, detail;
};
struct AssistStatus { uint64_t emitted, dropped, drained; uint32_t queued, capacity; };
struct CallerStatus { uint32_t count, overflow; uint64_t inlineChecks, inlineBlocks; };
bool HasRules();
bool FindRule(void* method, Rule& rule);
bool QueryPolicy(void* method, uint64_t generation, Policy& policy);
void RecordInline(void* method, uint64_t generation, void* caller, void* root, bool blocked);
void Emit(EventKind kind, void* method, uint64_t generation, void* caller = nullptr, void* root = nullptr, uint32_t detail = 0);
void Record(const Rule& rule, Sample sample);
bool InitializeProxy();
void** ProxyTable();
void CaptureCode(const InfoProxy& proxy, Sample& sample);
bool PatchConstant(const Rule& rule, Sample& sample);
bool ReadWord(const void* address, uint32_t length, uint64_t& word);
}
