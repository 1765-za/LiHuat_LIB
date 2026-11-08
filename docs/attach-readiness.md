# Attachment readiness: observed contract and deferred change

This is a bounded correctness assessment of `ClrProfilerAttach.Send`, not an
assertion of a game defect or protection loss. Production acceptance logic is
unchanged by these tests.

## What the full backend currently promises

`NativeModule.Bind` resolves `LihuatNative_SuspensionReady` from the prepared
backend module. It is not a generic check that any CLR profiler exists.
In `Source.Native/Waylith/ClrSuspension.cpp`, the export returns whether this
module's atomic `information` pointer is non-null. The pointer is published
after acquiring `ICorProfilerInfo10` and the optional profiler-request setup.

`InitializeForAttach` calls `Initialize` and may publish that pointer before
`ProfilerAttachComplete`. The full backend inherits the callback base's no-op
completion method. Therefore a nonzero probe does **not** itself prove completed
attachment, future suspension success, or continued availability. An unrelated
profiler alone does not set this module's pointer; incomplete attachment of this
module may. Repeating the same probe does not strengthen this contract.

The separate Linux support profiler gates readiness on `ProfilerAttachComplete`,
but has a different export/CLSID. Its successful integration tests do not supply
the missing guarantee to the full backend's `Send`.

## Confirmed control-flow inconsistency

With initial readiness zero, a primary request can return `AlreadyActive`
(`0x8013136A`). If the module-specific probe then becomes nonzero, `Send` skips
the notification fallback but retains the negative result and throws. This
differs from the initial nonzero-readiness shortcut, which returns immediately.

The method's semaphore serializes its own callers. To realize this transition,
readiness must change independently, for example through a native lifecycle
event while the request is in flight. Reachability in the actual game has not
been established.

Converting only this result to success and retaining the final probe would be
consistent with the existing weaker pointer-presence contract. It would **not**
establish that an incompletely attached profiler is rejected. Consequently this
branch retains the conservative failure instead of broadening acceptance on
that evidence alone. This is a documented deferred change, not a claim that the
inconsistency is fixed or that all existing readiness shortcuts are sufficient.

Before changing the acceptance contract, the full backend needs instance-owned
lifecycle readiness: pending attach remains unready until completion; failure,
shutdown and detach clear readiness with appropriate synchronization. Validate
all consumers and operation-level lifetime checks, not only the `Send` branch.
A last probe is not a lease and cannot prevent readiness loss just after return.

## Controlled decision tests

`Tests/AttachDecision` links the unchanged production `ClrProfilerAttach.cs`.
Only native binding/readiness is replaced by an explicitly managed test double.
A private-TMPDIR Unix peer verifies the actual primary/notification GUIDs,
payload and request count. It changes the probe state before replying, modeling
an independent lifecycle change rather than bypassing the semaphore. The real
runtime's socket and profiler are never used or overwritten.

Eleven cases characterize:

- Initial readiness with no request and primary success with readiness.
- Primary success without readiness and unrelated primary failure despite
  readiness, both rejected.
- The `AlreadyActive`/changed-readiness conservative failure above.
- Notification fallback when readiness stays zero, including success with
  readiness, success without readiness, ordinary failure despite readiness and
  `AlreadyActive` without readiness.
- Observed readiness loss before a final check.
- Two concurrent callers: the second waits before binding, then rechecks
  readiness after the first releases the semaphore instead of issuing a second
  request. Sequential cases after failures also exercise semaphore release.

The tests have bounded waits and an external kill watchdog. They do not validate
native export binding, callback-phase guarantees, pending-attach native states,
real fallback profiler loading, cancellation of semaphore waiters, Windows or
game behavior. Local execution remains blocked by the cloud sandbox's known
AF_UNIX restriction; strict hosted CI runs the suite without skip behavior.
