#include <windows.h>
#include <cor.h>
#include <atomic>
#include <new>
#include "ClrProfilerInterfaces.h"
#pragma warning(push)
#pragma warning(disable:4100)
#include "ClrCallbackBase.h"
#pragma warning(pop)
#include "ClrSuspension.h"
#include "ClrProfilerRequests.h"
#include "NativeMemoryOwnership.h"

namespace {
const CLSID id = {0x4bafc2d1, 0x9ceb, 0x4bf7, {0x9d, 0x53, 0x9b, 0x48, 0x2d, 0x9e, 0xed, 0x11}};
const CLSID notificationId = {0x70d8330e, 0x2177, 0x46c0, {0xb4, 0x0a, 0x7c, 0x49, 0x66, 0x25, 0xa8, 0xf2}};
std::atomic<ICorProfilerInfo10*> information{nullptr};
std::atomic<HRESULT> lastResult{E_PENDING};
std::atomic<uint64_t> windows{0}, failed{0};
std::atomic<uint32_t> attachments{0};
std::atomic<uint32_t> role{0};
SRWLOCK writers = SRWLOCK_INIT;
thread_local unsigned depth = 0;
thread_local bool requesting = false;

class Profiler final : public ClrCallbackBase {
    std::atomic<ULONG> references{1};
    const bool notificationOnly;
    ICorProfilerInfo10* owned = nullptr;
    void Drop() {
        AcquireSRWLockExclusive(&writers);
        if (owned) {
            ICorProfilerInfo10* expected = owned;
            if (information.compare_exchange_strong(expected, nullptr, std::memory_order_acq_rel)) role.store(0);
            owned->Release(); owned = nullptr;
        }
        ReleaseSRWLockExclusive(&writers);
    }
public:
    explicit Profiler(bool notification) : notificationOnly(notification) { }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid != IID_IUnknown && iid != __uuidof(ICorProfilerCallback) && iid != __uuidof(ICorProfilerCallback2)
            && iid != __uuidof(ICorProfilerCallback3) && iid != __uuidof(ICorProfilerCallback4)
            && iid != __uuidof(ICorProfilerCallback5) && iid != __uuidof(ICorProfilerCallback6)
            && iid != __uuidof(ICorProfilerCallback7) && iid != __uuidof(ICorProfilerCallback8)
            && iid != __uuidof(ICorProfilerCallback9) && iid != __uuidof(ICorProfilerCallback10)
            && iid != __uuidof(ICorProfilerCallback11)) return E_NOINTERFACE;
        *value = static_cast<ICorProfilerCallback11*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG value = --references; if (!value) delete this; return value; }
    HRESULT STDMETHODCALLTYPE Initialize(IUnknown* runtime) override {
        if (owned) return S_OK;
        ICorProfilerInfo10* info = nullptr;
        HRESULT hr = runtime->QueryInterface(__uuidof(ICorProfilerInfo10), reinterpret_cast<void**>(&info));
        if (SUCCEEDED(hr) && !lihuat::InstallProfilerRequests(info)) { info->Release(); info = nullptr; hr = E_FAIL; }
        if (SUCCEEDED(hr)) {
            ICorProfilerInfo10* expected = nullptr;
            if (information.compare_exchange_strong(expected, info, std::memory_order_acq_rel)) {
                owned = info; role.store(notificationOnly ? 2u : 1u, std::memory_order_release);
            }
            else info->Release();
            attachments.fetch_add(1, std::memory_order_relaxed);
        }
        lastResult.store(hr); return hr;
    }
    HRESULT STDMETHODCALLTYPE InitializeForAttach(IUnknown* runtime, void*, UINT) override { return Initialize(runtime); }
    HRESULT STDMETHODCALLTYPE Shutdown() override { Drop(); return S_OK; }
    HRESULT STDMETHODCALLTYPE ProfilerDetachSucceeded() override { Drop(); return S_OK; }
    HRESULT STDMETHODCALLTYPE LoadAsNotificationOnly(BOOL* result) override {
        if (!result) return E_POINTER;
        *result = notificationOnly ? TRUE : FALSE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE EventPipeEventDelivered(UINT_PTR, DWORD, DWORD, ULONG, LPCBYTE, ULONG, LPCBYTE,
        LPCGUID, LPCGUID, ThreadID, ULONG, UINT_PTR[]) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE EventPipeProviderCreated(UINT_PTR) override { return S_OK; }
};
class Factory final : public IClassFactory {
    std::atomic<ULONG> references{1};
    const bool notificationOnly;
public:
    explicit Factory(bool notification) : notificationOnly(notification) { }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
        *value = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG value = --references; if (!value) delete this; return value; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** value) override {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto instance = new (std::nothrow) Profiler(notificationOnly); if (!instance) return E_OUTOFMEMORY;
        HRESULT hr = instance->QueryInterface(iid, value); instance->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** value) {
    if (clsid != id && clsid != notificationId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) Factory(clsid == notificationId); if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid, value); factory->Release(); return hr;
}
STDAPI DllCanUnloadNow() { return S_FALSE; }

bool lihuat::BeginCodeWrite() {
    if (depth) { ++depth; return true; }
    if (requesting) return false;
    requesting = true;
    AcquireSRWLockExclusive(&writers);
    auto info = information.load(std::memory_order_acquire);
    HRESULT hr = E_NOINTERFACE;
    if (info) {
        for (unsigned attempt = 0; attempt != 250; ++attempt) {
            hr = info->SuspendRuntime();
            if (hr != static_cast<HRESULT>(0x80131388)) break;
            Sleep(1);
        }
    }
    lastResult.store(hr);
    requesting = false;
    if (FAILED(hr)) { failed.fetch_add(1); ReleaseSRWLockExclusive(&writers); return false; }
    depth = 1; windows.fetch_add(1); return true;
}
void lihuat::EndCodeWrite() {
    if (--depth) return;
    HRESULT hr = information.load(std::memory_order_acquire)->ResumeRuntime();
    lastResult.store(hr);
    if (FAILED(hr)) failed.fetch_add(1);
    ReleaseSRWLockExclusive(&writers);
}
bool lihuat::CodeWriteActive() { return depth != 0; }

bool lihuat::ProfilerMethodIdentity(void* method, uintptr_t& module, uint32_t& token) {
    if (!depth) AcquireSRWLockShared(&writers);
    auto info = information.load(std::memory_order_acquire);
    ClassID owner = 0; ModuleID moduleId = 0; mdToken methodToken = 0;
    HRESULT hr = info ? info->GetFunctionInfo(reinterpret_cast<FunctionID>(method), &owner, &moduleId, &methodToken) : E_UNEXPECTED;
    if (!depth) ReleaseSRWLockShared(&writers);
    module = moduleId; token = methodToken;
    return SUCCEEDED(hr) && moduleId && TypeFromToken(methodToken) == mdtMethodDef;
}

extern "C" __declspec(dllexport) int __cdecl LihuatNative_SuspensionReady() {
    return information.load(std::memory_order_acquire) != nullptr;
}
extern "C" __declspec(dllexport) uint32_t __cdecl LihuatNative_ProfilerAttachments() {
    return attachments.load(std::memory_order_relaxed);
}
extern "C" __declspec(dllexport) uint32_t __cdecl LihuatNative_ProfilerRole() {
    return role.load(std::memory_order_acquire);
}
struct SuspensionStatus { uint64_t windows, failures; int32_t result; uint32_t ready; };
extern "C" __declspec(dllexport) int __cdecl LihuatNative_SuspensionStatus(SuspensionStatus* status, uint32_t bytes) {
    if (bytes != sizeof(SuspensionStatus) || !lihuat::NativeOutputAllowed(status, bytes)) return 0;
    *status = {windows.load(), failed.load(), lastResult.load(), information.load() ? 1u : 0u};
    return 1;
}
