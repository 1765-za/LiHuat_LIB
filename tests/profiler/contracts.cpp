#include <cassert>
#include <cstdio>
#include <cor.h>
#include <corprof.h>
#define _MIDL_USE_GUIDDEF_
#include <corprof_i.cpp>
#include "ProfilerSupport.h"
extern "C" HRESULT DllGetClassObject(REFCLSID,REFIID,void**);
extern "C" HRESULT DllCanUnloadNow();
namespace {
const GUID unknownId = {0,0,0,{0xc0,0,0,0,0,0,0,0x46}};
const GUID factoryId = {1,0,0,{0xc0,0,0,0,0,0,0,0x46}};
const GUID profilerId = {0x8b93831b,0x5c4d,0x4b8c,{0x9e,0x55,0x9a,0xba,0x91,0x18,0xe4,0x82}};
struct NoInfo final : IUnknown {
    HRESULT QueryInterface(REFIID,void** out) override { if (!out) return E_POINTER; *out=nullptr; return E_NOINTERFACE; }
    ULONG AddRef() override { return 1; }
    ULONG Release() override { return 1; }
};
}
int main() {
    assert(LihuatProfiler_GetSupportAbi()==1);
    assert(LihuatProfiler_IsReady()==0);
    assert(LihuatProfiler_SuspendRuntime()<0);
    assert(LihuatProfiler_ResumeRuntime()<0);
    assert(DllCanUnloadNow()==S_FALSE);
    void* out = reinterpret_cast<void*>(1);
    assert(DllGetClassObject(unknownId,factoryId,&out)==CLASS_E_CLASSNOTAVAILABLE && !out);
    assert(DllGetClassObject(profilerId,factoryId,nullptr)==E_POINTER);
    assert(DllGetClassObject(profilerId,IID_ICorProfilerInfo10,&out)==E_NOINTERFACE && !out);
    assert(DllGetClassObject(profilerId,factoryId,&out)==S_OK && out);
    auto* factory=static_cast<IClassFactory*>(out);
    NoInfo unknown;
    assert(factory->CreateInstance(&unknown,IID_ICorProfilerCallback,&out)==CLASS_E_NOAGGREGATION && !out);
    assert(factory->CreateInstance(nullptr,IID_ICorProfilerInfo10,&out)==E_NOINTERFACE && !out);
    assert(factory->CreateInstance(nullptr,IID_ICorProfilerCallback11,&out)==S_OK && out);
    auto* profiler=static_cast<ICorProfilerCallback11*>(out);
    assert(factory->Release()==0);
    const GUID* callbacks[] = {&unknownId,&IID_ICorProfilerCallback,&IID_ICorProfilerCallback2,
        &IID_ICorProfilerCallback3,&IID_ICorProfilerCallback4,&IID_ICorProfilerCallback5,
        &IID_ICorProfilerCallback6,&IID_ICorProfilerCallback7,&IID_ICorProfilerCallback8,
        &IID_ICorProfilerCallback9,&IID_ICorProfilerCallback10,&IID_ICorProfilerCallback11};
    for (auto* id:callbacks) {
        assert(profiler->QueryInterface(*id,&out)==S_OK && out==profiler);
        assert(static_cast<IUnknown*>(out)->Release()==1);
    }
    assert(profiler->QueryInterface(IID_ICorProfilerInfo10,&out)==E_NOINTERFACE && !out);
    assert(profiler->Initialize(nullptr)==E_POINTER);
    assert(profiler->Initialize(&unknown)==E_NOINTERFACE);
    assert(profiler->InitializeForAttach(&unknown,nullptr,0)==E_NOINTERFACE);
    assert(profiler->ProfilerAttachComplete()<0);
    assert(LihuatProfiler_IsReady()==0);
    BOOL flag=TRUE;
    assert(profiler->LoadAsNotificationOnly(&flag)==S_OK && flag==FALSE);
    assert(profiler->LoadAsNotificationOnly(nullptr)==E_POINTER);
    assert(profiler->Shutdown()==S_OK);
    assert(profiler->Release()==0);
    std::puts("Profiler COM identity, ownership, unsupported info and fail-closed contracts PASS");
}
