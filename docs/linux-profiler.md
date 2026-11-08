# Linux CoreCLR profiler support (development only)

`libLihuatProfilerSupport.so` is a separate Linux x86-64 support module, not
`libLihuatNative.so`, ABI 11, a protection engine or a game-ready backend.
Do not substitute it for the full backend. It does not patch code, block ReJIT,
install JIT hooks, enforce authority ownership or validate Harmony/game ABIs.

## Verified interface source

`tools/fetch_clr_headers.sh [destination]` obtains a sparse official
[dotnet/runtime v10.0.0](https://github.com/dotnet/runtime/tree/60629d14374c56f1cb51819049ad1fa529307f8d)
checkout and verifies commit `60629d14374c56f1cb51819049ad1fa529307f8d`.
The build uses its actual PAL, COM, profiler declarations and GUID definitions,
plus the recovered callback base. No hand-written vtable approximation is used.
The upstream files retain the .NET Foundation MIT license in the checkout.
Downloaded dependencies are build inputs, not vendored source.

## Lifecycle and API

Profiler CLSID: `{8B93831B-5C4D-4B8C-9E55-9ABA9118E482}` (distinct from the
full backend). `DllGetClassObject` supplies a real reference-counted factory and
callback object with callback interfaces 1 through 11. Unsupported interfaces
and aggregation are rejected; failed initialization never sets readiness.
Initialization obtains and owns `ICorProfilerInfo10` through `QueryInterface`.
Attach initialization remains unready until `ProfilerAttachComplete`.
Shutdown/detach and final release clear readiness and release the info pointer.
Only one active profiler instance is accepted. No event masks are enabled.

The C support ABI exposes:

- `LihuatProfiler_GetSupportAbi()` returns **1**, not the engine ABI.
- `LihuatProfiler_IsReady()` returns 1 only with initialized profiler info.
- `LihuatProfiler_SuspendRuntime()` / `ResumeRuntime()` return signed 32-bit
  HRESULTs from real runtime operations or explicit local validation failures.

Suspension is allowed only from a native OS thread not registered as a managed
thread. Managed/PInvoke callers are rejected before suspension using the exact
`CORPROF_E_NOT_MANAGED_THREAD` result from `GetCurrentThreadID` as the gate.
Do not use suppressed-GC-transition PInvokes. A native owner can nest scopes;
other threads cannot nest or resume them. Failed runtime calls do not advance
the nesting state. No runtime APIs are invoked while a managed callback is
waiting for managed execution. Callers must not execute managed code or wait
for managed work while holding suspension.

A native thread exiting with an outstanding scope resumes via TLS cleanup.
If that resume fails, or runtime shutdown/detach occurs with an active scope,
the process terminates rather than silently leaving the runtime suspended.
This is an explicit experimental fail-closed contract, not graceful recovery.
Always balance scopes before shutdown. The library must remain loaded for the
process lifetime (`DllCanUnloadNow` always returns `S_FALSE`); calling `dlclose`
in violation of that contract is unsupported.

## Validation

The build needs C++17, pthreads and pinned CLR headers. `contracts.cpp` validates
factory/callback COM identity, refcounts, null arguments, unavailable profiler
info and failure before readiness. `harness.cpp` builds a test-only native DSO.
It creates an unmanaged `std::thread`, performs real suspension and nested
resume, checks managed heartbeat quiescence, rejects another native owner and
checks thread-exit cleanup. A PInvoke caller merely starts and joins that native
thread, using normal preemptive transitions, never suspending on itself.

With the libraries and harness in `build/linux-x64` and a .NET 10 SDK installed:

```sh
bash tests/profiler/run.sh build/linux-x64
```

This builds the managed smoke project outside profiling, tests disabled-profiler
failure, then launches a fresh .NET process with profiling enabled. Each process
has a 30-second external timeout to expose suspension deadlocks. Output records
the actual runtime version and architecture. The integration checks startup
loading; dynamic attachment is implemented but **not integration-tested** here.

Local verification: strict GCC build and native COM contracts pass. There was
no .NET SDK in the implementation environment, so real CLR loading/suspension
must be validated in CI before claiming those integration tests pass. Passing
on a .NET 10 patch runtime does not establish RimWorld host compatibility.
