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

This builds the managed test projects outside profiling and runs four cases:

1. Disabled-profiler rejection in a fresh process.
2. Startup loading with profiling enabled, followed by the native harness above.
3. External dynamic attachment to a disposable child created by `AttachDriver`.
   The child first verifies support ABI 1, no profiler readiness and rejection of
   suspension. Only after that handshake does the driver call Microsoft's
   `DiagnosticsClient.AttachProfiler` with the child's PID, the support CLSID
   above and the absolute support-library path. The child requires readiness
   from `ProfilerAttachComplete` and repeats the real suspension/resume tests.
   The driver requires both a zero child exit code and its final PASS marker.
4. Managed self-transport in another disposable process. The
   `ProfilerSelfTransport` project links the unchanged production
   `Source/Mechanisms/ClrProfilerAttach.cs` and invokes its private `Request`
   using reflection, as the existing mock tests do. This time its `Connect` and
   `LinuxDiagnosticEndpoint` reach the real process's default diagnostic socket,
   with the support CLSID and DSO path. The test requires initial unreadiness,
   HRESULT `S_OK`, subsequent support readiness and the native suspension
   harness, including heartbeat recovery. It does not accept another PID or
   replace the runtime's socket. A throwing `NativeModule` compile boundary
   makes accidental entry into full `Attach`/`Send`/`Bind` fail explicitly.

The driver never accepts an arbitrary PID. It removes inherited startup-profiler
and diagnostic-port settings for its child, has a 25-second deadline and kills
the child on failure. `run.sh` also clears profiling before SDK/build processes.
Startup/disabled runs have 30-second external timeouts; the entire attach driver
and standalone self-transport process each have a 35-second external timeout.
The self-transport watchdog additionally sends SIGKILL after a five-second
termination grace period; its request has a 10-second cancellation deadline.
Output records the actual runtime and
architecture. No timeout or attach failure is converted to a skip or success.

The driver uses the official NuGet package
[`Microsoft.Diagnostics.NETCore.Client` 0.2.661903](https://www.nuget.org/packages/Microsoft.Diagnostics.NETCore.Client/0.2.661903),
under Microsoft's [MIT license](https://licenses.nuget.org/MIT). Its exact version
and transitive dependency content hashes are locked in
`tests/profiler-attach/packages.lock.json`; restore uses only NuGet.org in locked
mode. This dependency is exclusive to the test driver, not the mod or native
library. No game binaries are needed or executed by this test.

Local verification on Debian 13 x86-64, SDK 10.0.100/runtime 10.0.0: strict native
COM contracts, disabled rejection, startup loading and real suspension/resume
pass. Local dynamic attachment is blocked before profiler loading because this
cloud sandbox denies AF_UNIX socket creation with `EPERM`; no target diagnostic
socket is created. The strict CI runtime-contracts job runs all four cases on
Ubuntu and must succeed for its exact commit before claiming either attach
integration passed. No sandbox security settings are changed.

**Scope:** these support-module attach tests are not validation of the full
production `Attach`/`Send` path. In particular, the self-transport test does not
execute `NativeModule.Bind`, `LihuatNative_SuspensionReady`, the production
primary/notification CLSIDs, or the `AlreadyActive` fallback. The full backend
uses a different CLSID/export contract and still requires integration with
mutation authority and lifetime rules. These tests do not establish detachment
behavior, game/JIT/Harmony
compatibility, private-runtime API validity, protection coverage or a usable
ABI-11 Linux backend. Passing on a stock .NET 10 patch runtime does not establish
RimWorld host compatibility.
