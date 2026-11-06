#pragma once
#include <corprof.h>

struct __declspec(uuid("CEC5B60E-C69C-495F-87F6-84D28EE16FFB")) ICorProfilerCallback10 : ICorProfilerCallback9 {
    virtual HRESULT STDMETHODCALLTYPE EventPipeEventDelivered(UINT_PTR, DWORD, DWORD, ULONG, LPCBYTE, ULONG, LPCBYTE,
        LPCGUID, LPCGUID, ThreadID, ULONG, UINT_PTR[]) = 0;
    virtual HRESULT STDMETHODCALLTYPE EventPipeProviderCreated(UINT_PTR) = 0;
};
struct __declspec(uuid("42350846-AAED-47F7-B128-FD0C98881CDE")) ICorProfilerCallback11 : ICorProfilerCallback10 {
    virtual HRESULT STDMETHODCALLTYPE LoadAsNotificationOnly(BOOL*) = 0;
};

// Interface order and identifiers are defined by dotnet/runtime v10.0.0 corprof.idl.
struct __declspec(uuid("008170DB-F8CC-4796-9A51-DC8AA0B47012")) ICorProfilerInfo9 : ICorProfilerInfo8 {
    virtual HRESULT STDMETHODCALLTYPE GetNativeCodeStartAddresses(FunctionID, ReJITID, ULONG32, ULONG32*, UINT_PTR[]) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetILToNativeMapping3(UINT_PTR, ULONG32, ULONG32*, COR_DEBUG_IL_TO_NATIVE_MAP[]) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCodeInfo4(UINT_PTR, ULONG32, ULONG32*, COR_PRF_CODE_INFO[]) = 0;
};
using ObjectReferenceCallback = BOOL STDMETHODCALLTYPE(ObjectID, ObjectID*, void*);
struct __declspec(uuid("2F1B5152-C869-40C9-AA5F-3ABE026BD720")) ICorProfilerInfo10 : ICorProfilerInfo9 {
    virtual HRESULT STDMETHODCALLTYPE EnumerateObjectReferences(ObjectID, ObjectReferenceCallback*, void*) = 0;
    virtual HRESULT STDMETHODCALLTYPE IsFrozenObject(ObjectID, BOOL*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetLOHObjectSizeThreshold(DWORD*) = 0;
    virtual HRESULT STDMETHODCALLTYPE RequestReJITWithInliners(DWORD, ULONG, ModuleID[], mdMethodDef[]) = 0;
    virtual HRESULT STDMETHODCALLTYPE SuspendRuntime() = 0;
    virtual HRESULT STDMETHODCALLTYPE ResumeRuntime() = 0;
};
