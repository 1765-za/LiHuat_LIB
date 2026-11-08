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
  only for **unpublished** code. It accepts the same private mapping or shared
  executable/write views with matching device, inode and exact file offsets.
  Unrelated mappings, different offsets and private-COW aliases are rejected.
  Mapping/backing-file lifetime must be held stable by the caller. This helper
  itself does not suspend CoreCLR or authorize a write.
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

### Real profiler support and restored managed project

A separate `libLihuatProfilerSupport.so` now supplies a real Linux CoreCLR
profiler factory and Info10 lifecycle, plus strictly unmanaged-thread suspension
with ownership/nesting/cleanup. It is not wired into the complete ABI-11 engine.
See [linux-profiler.md](linux-profiler.md) for contracts and tests.

The managed net10.0 project and a portable cache fingerprint target are restored
from the older Star Company build metadata, adapted to current sources without
copying ABI-10 code or Windows scripts. All four real game/Harmony references are
required for actual compilation. See [managed-build.md](managed-build.md).

A compiler-based JIT contract audit compares the current source GUID, six vtable
slots and structure offsets against official .NET 10 headers. It does not verify
every method signature, an installed game JIT, or full hook behavior.

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
# Optional .NET 10 profiler/source-contract tests:
bash tools/fetch_clr_headers.sh
make jit-contracts profiler-contracts
make profiler-integration
python3 Tests/BuildMetadata/test_build_metadata.py
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

## Later provisional managed compile

A bounded Linux compile using SDK 10.0.100, Krafs.Rimworld.Ref
1.7.4984-unstable and Lib.Harmony 3.0.0-rc.3 successfully compiled all 599 source
items at commit `3d35a5d`, with no compiler errors. The earlier local note about
unavailable managed compilation describes the initial foundation environment.
This newer result does not validate the target game, Ludeon's custom runtime,
reflection/private-member contracts or native defenses. See the
[reproducible probe and exact limits](provisional-reference-build.md).

The later .NET 10 profiler test also verifies startup loading and real
suspension/resume locally. Strict CI also runs external attachment to a
disposable child and a linked-production `Request`/`Connect` self-transport
test against a real .NET process using the separate support profiler. Local
AF_UNIX socket creation is denied by the cloud sandbox before attachment. See
[profiler validation and limits](linux-profiler.md). This does not validate the
full engine's self-attach path or mutation authority.

## Remaining work before a usable libLihuatNative.so

1. Confirm host CoreCLR/JIT/Harmony versions and supply the four real reference
   assemblies to the restored managed project. The old root `LihuatNative.cpp` is Mono
   ABI 1 and is not a substitute for Waylith ABI 11.
2. Integrate the separate official-interface Linux profiler support into the
   full engine and validate production self-attachment and lifecycle alongside its
   mutation authority. The old Windows SDK wrappers cannot be included with
   modern duplicate interface declarations.
3. Port registry/authority/write-scope and JIT publication to the Linux platform
   layer; validate precode/call-counting formats, JIT GUID, every vtable slot and
   SysV return conventions against the exact target runtime.
4. Integrate the tested shared-alias primitive with actual CoreCLR code heaps;
   validate allocation lifetime, suspension, write authorization and rollback
   in the full engine rather than relying on the standalone mapping test.
5. Replace the Windows MinHook/ntdll/kernelbase interception design with a real
   Linux protection backend. No no-op gate implementation is supplied, and no
   equivalent threat coverage has been established.
6. Run real runtime/game integration and stress tests: concurrency, runtime
   suspension failures, JIT/tier changes, rollback, removal, stale callbacks,
   corruption/denied writes and exact capability reporting.

A build-only success cannot establish protection equivalence. Until these steps
are complete, this branch is useful source groundwork, not a release artifact.
