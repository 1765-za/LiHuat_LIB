// CoreCLR interfaces come exclusively from the pinned official PAL headers.
#include <exception>
#include <atomic>
#include <climits>
#include <cstring>
#include <mutex>
#include <new>
#include <thread>
#include <cor.h>
#include <corprof.h>
#include <corerror.h>
#define _MIDL_USE_GUIDDEF_
#include <corprof_i.cpp>
#include "../Waylith/ClrCallbackBase.h"
#include "ProfilerSupport.h"

namespace {
const GUID unknownId = {0,0,0,{0xc0,0,0,0,0,0,0,0x46}};
const GUID factoryId = {1,0,0,{0xc0,0,0,0,0,0,0,0x46}};
// Deliberately distinct from the full Windows protection engine's CLSID.
const GUID profilerId = {0x8b93831b,0x5c4d,0x4b8c,{0x9e,0x55,0x9a,0xba,0x91,0x18,0xe4,0x82}};
bool equal(REFGUID a, REFGUID b) { return std::memcmp(&a,&b,sizeof(GUID)) == 0; }
struct State {
    std::mutex mutex;
    ICorProfilerInfo10* info = nullptr;
    void* profiler = nullptr;
    bool ready = false;
    std::thread::id owner;
    unsigned depth = 0;
};
State state;
// A thread that exits without balancing its scope must resume before disappearing.
// A failed resume cannot safely be ignored: the managed runtime may be stranded.
struct ThreadCleanup {
    ~ThreadCleanup() {
        std::lock_guard<std::mutex> lock(state.mutex);
        if (state.depth && state.owner == std::this_thread::get_id()) {
            if (!state.info || FAILED(state.info->ResumeRuntime())) std::terminate();
            state.depth = 0;
            state.owner = {};
        }
    }
};
thread_local ThreadCleanup cleanup;

class Profiler final : public ClrCallbackBase {
    std::atomic<ULONG> refs{1};
    HRESULT initialize(IUnknown* source, bool ready) {
        if (!source) return E_POINTER;
        std::lock_guard<std::mutex> lock(state.mutex);
        if (state.profiler) return E_FAIL;
        ICorProfilerInfo10* info = nullptr;
        HRESULT hr = source->QueryInterface(IID_ICorProfilerInfo10, reinterpret_cast<void**>(&info));
        if (FAILED(hr)) return hr;
        if (!info) return E_NOINTERFACE;
        // No event masks, instrumentation, JIT hooks or writable runtime data.
        state.info = info;
        state.profiler = this;
        state.ready = ready;
        return S_OK;
    }
    void clear() {
        std::lock_guard<std::mutex> lock(state.mutex);
        if (state.profiler != this) return;
        state.ready = false;
        // Shutdown/detach must not strand a suspended runtime. The public API
        // does not request detach; active callers must balance before shutdown.
        if (state.depth) std::terminate();
        auto* info = state.info;
        state.info = nullptr;
        state.profiler = nullptr;
        if (info) info->Release();
    }
public:
    ~Profiler() { clear(); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        const GUID* ids[] = {&unknownId, &IID_ICorProfilerCallback, &IID_ICorProfilerCallback2,
            &IID_ICorProfilerCallback3, &IID_ICorProfilerCallback4, &IID_ICorProfilerCallback5,
            &IID_ICorProfilerCallback6, &IID_ICorProfilerCallback7, &IID_ICorProfilerCallback8,
            &IID_ICorProfilerCallback9, &IID_ICorProfilerCallback10, &IID_ICorProfilerCallback11};
        for (const auto* supported : ids) if (equal(id,*supported)) {
            *result = static_cast<ICorProfilerCallback11*>(this); AddRef(); return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE Initialize(IUnknown* source) override { return initialize(source,true); }
    HRESULT STDMETHODCALLTYPE InitializeForAttach(IUnknown* source, void*, UINT) override { return initialize(source,false); }
    HRESULT STDMETHODCALLTYPE ProfilerAttachComplete() override {
        std::lock_guard<std::mutex> lock(state.mutex);
        if (state.profiler != this || !state.info) return E_FAIL;
        state.ready = true; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Shutdown() override { clear(); return S_OK; }
    HRESULT STDMETHODCALLTYPE ProfilerDetachSucceeded() override { clear(); return S_OK; }
};
class Factory final : public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (!equal(id,unknownId) && !equal(id,factoryId)) return E_NOINTERFACE;
        *result = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID id, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* profiler = new (std::nothrow) Profiler;
        if (!profiler) return E_OUTOFMEMORY;
        HRESULT hr = profiler->QueryInterface(id,result);
        profiler->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return E_NOTIMPL; }
};
}
extern "C" LIHUAT_PROFILER_EXPORT HRESULT STDMETHODCALLTYPE DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    if (!result) return E_POINTER;
    *result = nullptr;
    if (!equal(clsid,profilerId)) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) Factory;
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid,result);
    factory->Release(); return hr;
}
// Runtime and TLS callbacks keep this library process-resident; never claim unloadability.
extern "C" LIHUAT_PROFILER_EXPORT HRESULT STDMETHODCALLTYPE DllCanUnloadNow() { return S_FALSE; }
uint32_t LihuatProfiler_GetSupportAbi() { return 1; }
int32_t LihuatProfiler_IsReady() {
    std::lock_guard<std::mutex> lock(state.mutex);
    return state.ready && state.info ? 1 : 0;
}
int32_t LihuatProfiler_SuspendRuntime() {
    (void)cleanup;
    std::lock_guard<std::mutex> lock(state.mutex);
    if (!state.ready || !state.info) return E_FAIL;
    if (state.depth) {
        if (state.owner != std::this_thread::get_id()) return E_ACCESSDENIED;
        if (state.depth == UINT_MAX) return E_FAIL;
        ++state.depth; return S_OK;
    }
    ThreadID managedThread = 0;
    HRESULT hr = state.info->GetCurrentThreadID(&managedThread);
    // Only the precise documented unmanaged-thread result is acceptable.
    if (hr != CORPROF_E_NOT_MANAGED_THREAD) return FAILED(hr) ? hr : E_ACCESSDENIED;
    hr = state.info->SuspendRuntime();
    if (SUCCEEDED(hr)) { state.owner = std::this_thread::get_id(); state.depth = 1; }
    return hr;
}
int32_t LihuatProfiler_ResumeRuntime() {
    std::lock_guard<std::mutex> lock(state.mutex);
    if (!state.ready || !state.info || !state.depth) return E_FAIL;
    if (state.owner != std::this_thread::get_id()) return E_ACCESSDENIED;
    if (state.depth > 1) { --state.depth; return S_OK; }
    HRESULT hr = state.info->ResumeRuntime();
    if (SUCCEEDED(hr)) { state.depth = 0; state.owner = {}; }
    return hr;
}
