# Linux port: implemented foundation, incomplete runtime

Baseline: main commit `8bc0dbde8572e50316557992102f001c1ce975ba`.
The `linux` branch preserves main and introduces an explicit development stage.
Provisional target: Linux x86_64 / glibc with the intended RimWorld 1.7 CoreCLR
host. The exact host runtime and Harmony version have **not** been verified.
This is not a Mono port, ARM64 port, or Wine DLL build.

## What is implemented

### Native platform foundation

`Source.Native/Linux` contains real implementations, not success stubs:

- `/proc/self/maps` mapping/protection queries with checked ranges.
- `process_vm_readv` self reads that return failure rather than dereferencing
  inaccessible pointers. A denied syscall is not replaced with unsafe `memcpy`.
- Kernel random bytes through `getrandom`.
- An aligned eight-byte compare/exchange with original permission restoration,
  only for **unpublished** code in the same private mapping. It deliberately
  rejects unrelated executable/write addresses. It does not suspend CoreCLR,
  authorize a caller, or support shared/double-mapped CoreCLR code heaps yet.
- Low-2-GiB, process-lifetime relay slots and the actual eight-byte x86-64
  indirect branch encoding. Slots are synchronized and never reused.
- RX forwarding thunks for explicitly verified SysV signatures with the
  receiver in RDI. Integer, floating-point and stack arguments are preserved.
  These thunks do **not** support hidden-structure-return/RSI receivers, varargs,
  or an unvalidated entire JIT vtable. Per-slot ABI verification is still needed.
- ELF PT_LOAD segment ownership and resolution/pinning of already-loaded
  runtime modules. No PE header assumptions or automatic runtime loading.

The Makefile builds `build/linux-x64/libLihuatPlatform.so`, with a distinct
support ABI (`LihuatPlatform_*` only). The full runtime name and ABI 11 exports
are intentionally absent. `make runtime` explicitly fails with this status.
Do not rename this library to `libLihuatNative.so` or install it as the backend.
The small public C API currently exposes mapping queries, safe reads, image
membership and support-ABI identification. The other C++ helpers are internal
and tested directly for later integration.

### Managed integration groundwork

- Windows retains its original `LoadLibraryExW` dependency-search flags;
  Linux uses `NativeLibrary` with `Native/libLihuatNative.so`.
- ABI 11, authority ownership and live startup checks are preserved.
- Startup checks use an actual exported code address for ELF ownership, not
  the opaque `dlopen` handle.
- A platform helper reads pointers through the OS and changes/restores only
  one private readable, non-executable Linux JIT data-slot page.
- Harmony setup uses platform-default unmanaged calling convention (x64 only).
- Profiler attachment has Linux Unix-domain socket transport with the exact
  PID/start-ticks endpoint, preserving request framing and readiness checks.
  It does not scan for an arbitrary socket. Custom diagnostic endpoints and
  changed-after-start TMPDIR configurations are not implemented.

These paths still require the real ABI-11 backend, the correct Harmony build,
and host integration tests. The managed smoke project isolates plumbing and
cannot declare the protection engine ready. Its fake Unix listener also checks
request framing, fragmented responses, remote errors, truncation and cancellation.

### Recovered build input

`ClrCallbackBase.h` is generated from a hash-pinned official .NET 10 header,
with all 95 callback methods and a reproducible generator. See
[profiler-build-inputs.md](profiler-build-inputs.md). Initialization defaults to
failure until implemented by the concrete profiler; it is not a fake profiler.

## Build and validation

Requirements: Linux x86_64, GCC with C++17, GNU make, Python 3, binutils and
libc/libstdc++ development files. No downloaded dependency is needed for the
native platform foundation.

```sh
make platform
make test
make sanitize
# Managed plumbing only, with a separately installed .NET 8+ SDK:
dotnet run --project Tests/ManagedPlatform/ManagedPlatform.csproj
```

A branch CI workflow runs the strict native checks and the managed smoke project
on Ubuntu 24.04 with .NET 8. A queued/pending job is not a successful result.

The strict native test is the default. On the original restricted cloud host,
`process_vm_readv` returned EPERM even for the same process, and LSan could not
run under the host tracing arrangement. The following explicitly limited run
was used, without changing production failure behavior:

```sh
LIHUAT_ALLOW_RESTRICTED_TESTS=1 ASAN_OPTIONS=detect_leaks=0 make verify
```

The override reports the positive self-read tests as **SKIP**. It does not make
a denied read succeed. ASan and UBSan run, but disabling LSan means there is no
leak-check result. Neither override should be used to claim a full strict pass.

Verified on Debian 13, GCC 14.2, Linux x86_64:

- Successful strict-warning native build and fully resolved shared-library link.
- ELF64 x86-64 DYN artifact and no Windows DLL imports.
- Actual execution through generated branch relays and SysV thunks, preserving
  register/stack integer and floating-point arguments for supported signatures.
- Wrong-expected-value rejection, page-permission restoration, unrelated-alias
  rejection, concurrent relay allocation, range/overflow/no-access failures.
- ELF membership and already-loaded-module lookup.
- ctypes load, support ABI, invalid-read rejection, and absence of ABI-11 exports.
- ASan and UBSan for the above native tests, with positive self-read skipped and
  LSan disabled for the stated host limitations.
- Generated callback base compiled/executed against official .NET 10 PAL headers;
  deterministic generation and hash rejection verified.
- `git diff --check`.

Not verified locally: successful process_vm_readv, LSan, managed compilation
(no .NET SDK installed), Windows build, full profiler attachment/suspension,
Harmony integration, full game behavior, or protection equivalence.
The local support binary imports GLIBC up to 2.38 and GLIBCXX up to 3.4.11;
rebuild on the oldest supported distribution before distributing a binary.
No universal Linux binary compatibility is claimed.

## Remaining work before a usable libLihuatNative.so

1. Supply/confirm the full managed build project, host CoreCLR/JIT/Harmony
   versions and reference assemblies. The old root `LihuatNative.cpp` is Mono
   ABI 1 and is not a substitute for Waylith ABI 11.
2. Integrate official PAL/CLR profiler interfaces consistently, resolve the old
   Windows SDK handwritten-interface duplication, and implement/test Linux
   profiler lifecycle, suspension and resumption with real callbacks.
3. Port registry/authority/write-scope and JIT publication to the Linux platform
   layer; validate precode/call-counting formats, JIT GUID, every vtable slot and
   SysV return conventions against the exact target runtime.
4. Implement shared/double-mapped executable heaps without assuming two virtual
   addresses alias the same storage; validate write authorization and rollback.
5. Replace the Windows MinHook/ntdll/kernelbase interception design with a real
   Linux protection backend. No no-op gate implementation is supplied, and no
   equivalent threat coverage has been established.
6. Run real runtime/game integration and stress tests: concurrency, runtime
   suspension failures, JIT/tier changes, rollback, removal, stale callbacks,
   corruption/denied writes and exact capability reporting.

A build-only success cannot establish protection equivalence. Until these steps
are complete, this branch is useful source groundwork, not a release artifact.
