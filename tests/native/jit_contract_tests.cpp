#include <cstddef>
#include <cstdint>
#include <cstring>
#include <cassert>
#include <iostream>
#include <corjit.h>
#include <jiteeversionguid.h>
#include "../../Source.Native/Waylith/GenericBodies.h"
#include "../../Source.Native/Waylith/ClrInlining.h"
#include "SourceContract.h"

// GCC/Clang x86-64 SysV use the Itanium C++ ABI: virtual member-pointer value
// is vtable byte offset + 1. This test deliberately does not pretend portability.
#if !defined(__linux__) || !defined(__x86_64__) || !defined(__GXX_ABI_VERSION)
#error JIT slot audit requires Linux x86_64 with the Itanium C++ ABI.
#endif

template<class T> size_t Slot(T pointer) {
    struct MemberPointer { ptrdiff_t offset, adjustment; };
    static_assert(sizeof(T) == sizeof(MemberPointer));
    MemberPointer value{};
    std::memcpy(&value, &pointer, sizeof(value));
    assert(value.offset > 0 && (value.offset & 1) && value.adjustment == 0);
    assert((value.offset - 1) % sizeof(void*) == 0);
    return static_cast<size_t>(value.offset - 1) / sizeof(void*);
}
static_assert(sizeof(void*) == 8);
static_assert(sizeof(source_contract::Allocation) == sizeof(AllocMemArgs));
static_assert(offsetof(source_contract::Allocation, hotSize) == offsetof(AllocMemArgs, hotCodeSize));
static_assert(offsetof(source_contract::Allocation, coldSize) == offsetof(AllocMemArgs, coldCodeSize));
static_assert(offsetof(source_contract::Allocation, dataSize) == offsetof(AllocMemArgs, roDataSize));
static_assert(offsetof(source_contract::Allocation, exceptions) == offsetof(AllocMemArgs, xcptnsCount));
static_assert(offsetof(source_contract::Allocation, flags) == offsetof(AllocMemArgs, flag));
static_assert(offsetof(source_contract::Allocation, hot) == offsetof(AllocMemArgs, hotCodeBlock));
static_assert(offsetof(source_contract::Allocation, hotWritable) == offsetof(AllocMemArgs, hotCodeBlockRW));
static_assert(offsetof(source_contract::Allocation, cold) == offsetof(AllocMemArgs, coldCodeBlock));
static_assert(offsetof(source_contract::Allocation, coldWritable) == offsetof(AllocMemArgs, coldCodeBlockRW));
static_assert(offsetof(source_contract::Allocation, data) == offsetof(AllocMemArgs, roDataBlock));
static_assert(offsetof(source_contract::Allocation, dataWritable) == offsetof(AllocMemArgs, roDataBlockRW));
static_assert(offsetof(lihuat::MethodBodyInput, method) == offsetof(CORINFO_METHOD_INFO, ftn));
static_assert(offsetof(lihuat::MethodBodyInput, scope) == offsetof(CORINFO_METHOD_INFO, scope));
static_assert(offsetof(lihuat::MethodBodyInput, il) == offsetof(CORINFO_METHOD_INFO, ILCode));
static_assert(offsetof(lihuat::MethodBodyInput, length) == offsetof(CORINFO_METHOD_INFO, ILCodeSize));
static_assert(offsetof(lihuat::InliningContext, original) == 8);
int main() {
    assert(std::memcmp(&source_contract::expected, &JITEEVersionIdentifier, sizeof(GUID)) == 0);
    assert(Slot(&ICorJitCompiler::compileMethod) == 0);
    assert(Slot(&ICorJitCompiler::getVersionIdentifier) == 2);
    assert(Slot(&ICorJitInfo::canInline) == source_contract::CanInlineSlot);
    assert(Slot(&ICorJitInfo::reportInliningDecision) == source_contract::ReportInlineSlot);
    assert(Slot(&ICorJitInfo::allocMem) == source_contract::AllocMemSlot);
    assert(Slot(&ICorJitInfo::getJitFlags) == source_contract::JitFlagsSlot);
    assert(source_contract::JitFlagsSlot + 1 == source_contract::InfoSlots);
    std::cout << "PASS current-source GUID, six JIT slots, allocation and method-prefix layouts against .NET 10 headers.\n"
                 "Not a check of the installed game JIT, all signatures, or a working JIT hook.\n";
}
