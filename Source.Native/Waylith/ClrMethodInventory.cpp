#include <windows.h>
#include <cor.h>
#include "ClrProfilerInterfaces.h"
#include "ClrMethodInventory.h"
#include "NativeWriteScope.h"
#include <unordered_map>
#include <memory>

namespace lihuat { ICorProfilerInfo10* AcquireProfilerInformation(); }

namespace {
template<class T> struct Release { void operator()(T* value) const { if (value) value->Release(); } };
struct MetadataKey {
    ModuleID module;
    mdToken token;
    bool operator==(const MetadataKey& other) const { return module == other.module && token == other.token; }
};
struct MetadataHash {
    size_t operator()(const MetadataKey& key) const { return std::hash<uintptr_t>{}(key.module) ^ (size_t{key.token} << 1); }
};

HRESULT ReadCode(ICorProfilerInfo10* info, FunctionID function, ReJITID revision, void* key,
    std::vector<lihuat::CompiledMethodRegion>& regions) {
    ULONG32 count = 0;
    HRESULT result = info->GetNativeCodeStartAddresses(function, revision, 0, &count, nullptr);
    if (FAILED(result)) return result;
    if (!count) return S_OK;
    std::vector<UINT_PTR> addresses;
    do {
        addresses.resize(count);
        result = info->GetNativeCodeStartAddresses(function, revision, count, &count, addresses.data());
    } while (result == HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER));
    if (FAILED(result)) return result;
    addresses.resize(count);
    for (UINT_PTR address : addresses) {
        COR_PRF_CODE_INFO code[2]{};
        ULONG32 pieces = 0;
        result = info->GetCodeInfo4(address, 2, &pieces, code);
        if (FAILED(result) || pieces > 2) return FAILED(result) ? result : E_UNEXPECTED;
        for (ULONG32 i = 0; i < pieces; ++i)
            if (code[i].startAddress && code[i].size)
                regions.push_back({key, reinterpret_cast<const void*>(code[i].startAddress), code[i].size});
    }
    return S_OK;
}

struct Request {
    const std::vector<void*>& methods;
    std::vector<lihuat::CompiledMethodRegion>& regions;
    HRESULT result = E_PENDING;
};

DWORD WINAPI Snapshot(void* state) {
    auto& request = *static_cast<Request*>(state);
    lihuat::NativeWriteScope writing;
    if (!writing) { request.result = E_ACCESSDENIED; return 0; }
    std::unique_ptr<ICorProfilerInfo10, Release<ICorProfilerInfo10>> info(lihuat::AcquireProfilerInformation());
    if (!info) { request.result = E_NOINTERFACE; return 0; }
    try {
        std::unordered_map<MetadataKey, void*, MetadataHash> identities;
        identities.reserve(request.methods.size());
        for (void* method : request.methods) {
            ModuleID module = 0;
            mdToken token = 0;
            request.result = info->GetFunctionInfo(reinterpret_cast<FunctionID>(method), nullptr, &module, &token);
            if (FAILED(request.result) || !module || TypeFromToken(token) != mdtMethodDef) {
                if (SUCCEEDED(request.result)) request.result = E_INVALIDARG;
                return 0;
            }
            identities.emplace(MetadataKey{module, token}, method);
        }
        ICorProfilerFunctionEnum* enumeration = nullptr;
        request.result = info->EnumJITedFunctions2(&enumeration);
        if (FAILED(request.result)) return 0;
        std::unique_ptr<ICorProfilerFunctionEnum, Release<ICorProfilerFunctionEnum>> functions(enumeration);
        COR_PRF_FUNCTION batch[64]{};
        ULONG count = 0;
        do {
            request.result = functions->Next(64, batch, &count);
            if (FAILED(request.result)) return 0;
            for (ULONG i = 0; i < count; ++i) {
                ModuleID module = 0;
                mdToken token = 0;
                if (FAILED(info->GetFunctionInfo(batch[i].functionId, nullptr, &module, &token))) continue;
                auto found = identities.find({module, token});
                if (found == identities.end()) continue;
                request.result = ReadCode(info.get(), batch[i].functionId, batch[i].reJitId, found->second, request.regions);
                if (FAILED(request.result)) return 0;
            }
        } while (count == 64);
        request.result = S_OK;
    } catch (const std::bad_alloc&) { request.result = E_OUTOFMEMORY; }
    return 0;
}
}

int32_t lihuat::SnapshotMethodCode(const std::vector<void*>& methods, std::vector<CompiledMethodRegion>& regions) {
    Request request{methods, regions};
    HANDLE thread = CreateThread(nullptr, 0, Snapshot, &request, 0, nullptr);
    if (!thread) return HRESULT_FROM_WIN32(GetLastError());
    DWORD result = WaitForSingleObject(thread, INFINITE);
    CloseHandle(thread);
    return result == WAIT_OBJECT_0 ? request.result : E_FAIL;
}
