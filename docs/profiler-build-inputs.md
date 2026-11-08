# CLR profiler build inputs (Linux work in progress)

## Recovered input and provenance

The previously missing `Source.Native/Waylith/ClrCallbackBase.h` is now generated
from the official .NET runtime C++ interface declarations. It supplies all 95
callbacks from `ICorProfilerCallback` through `ICorProfilerCallback11`, without
redeclaring any COM interfaces or guessing vtable layout. `IUnknown` remains
abstract: the concrete profiler must implement identity and reference counting.
Initialize/attach default to `E_NOTIMPL`; the existing `Profiler` overrides them.
Decision callbacks initialize their BOOL outputs (cache/inline allowed,
notification-only false), and reject null output pointers.

Pinned upstream:

- Repository: https://github.com/dotnet/runtime
- Release: `v10.0.0`
- Verified tag commit: `60629d14374c56f1cb51819049ad1fa529307f8d`
- [Generated corprof.h](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/prebuilt/inc/corprof.h)
- [Source corprof.idl](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl)
- Header SHA-256: `6892e8f1c8492290d19cd9c9b3f73d840f02b05cdcee8ba344ab3614ad66406a`
- [Official native profiler CMake example](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/tests/profiler/native/CMakeLists.txt)

Acquire build inputs separately (not vendored into this repository):

```sh
git clone --depth 1 --filter=blob:none --sparse --branch v10.0.0 \
  https://github.com/dotnet/runtime.git /tmp/lihuat-dotnet-runtime
git -C /tmp/lihuat-dotnet-runtime sparse-checkout set \
  src/coreclr/inc src/coreclr/pal/prebuilt/inc src/coreclr/pal/inc \
  src/native/minipal
# Must print 60629d14374c56f1cb51819049ad1fa529307f8d:
git -C /tmp/lihuat-dotnet-runtime rev-parse HEAD
python3 tools/generate_clr_callback_base.py \
  /tmp/lihuat-dotnet-runtime/src/coreclr/pal/prebuilt/inc/corprof.h
python3 tools/generate_clr_callback_base.py \
  /tmp/lihuat-dotnet-runtime/src/coreclr/pal/prebuilt/inc/corprof.h --check
```

The generator requires Python 3, performs no download, verifies the entire input
hash, checks every inheritance edge and per-interface method count, and emits
stable UTF-8/LF bytes. `--check` fails without modifying a missing/stale output.
An unexpected header revision is rejected instead of silently changing the ABI.

## Verified scope

The generated class was compiled and a small executable run with GCC C++17 on
Linux x86-64 against the pinned official PAL headers. The test supplied only the
three `IUnknown` methods, statically checked that the result was not abstract,
and exercised the three BOOL decisions, null handling and initialization result.
This confirms the callback signatures match those headers. It is **not** a test
of runtime profiler loading, suspension, instrumentation or protection.

Reproduce the compile/run check from the repository root:

```sh
cat >/tmp/check-clr-callback.cpp <<'CPP'
#include <cor.h>
#include <corprof.h>
#include "ClrCallbackBase.h"
#include <type_traits>
#include <cassert>
class TestProfiler final : public ClrCallbackBase {
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void**) override { return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
};
static_assert(!std::is_abstract<TestProfiler>::value, "Missing callback");
int main() {
    TestProfiler profiler;
    BOOL value = FALSE;
    assert(profiler.JITInlining(0, 0, &value) == S_OK && value == TRUE);
    value = FALSE;
    assert(profiler.JITCachedFunctionSearchStarted(0, &value) == S_OK && value == TRUE);
    assert(profiler.LoadAsNotificationOnly(&value) == S_OK && value == FALSE);
    assert(profiler.LoadAsNotificationOnly(nullptr) == E_POINTER);
    assert(profiler.Initialize(nullptr) == E_NOTIMPL);
}
CPP
D=/tmp/lihuat-dotnet-runtime/src/coreclr
g++ -std=c++17 -DSOS_INCLUDE -DHOST_AMD64 -DHOST_64BIT -DPAL_STDCPP_COMPAT \
  -I/tmp/lihuat-dotnet-runtime/src/native \
  -I"$D/pal/prebuilt/inc" -I"$D/pal/inc/rt" -I"$D/pal/inc" -I"$D/inc" \
  -ISource.Native/Waylith /tmp/check-clr-callback.cpp -o /tmp/check-clr-callback
/tmp/check-clr-callback
```

Those architecture defines are for this x86-64 verification only, not an ARM64
build recipe. The installed managed .NET SDK alone is not this native PAL input
set. `src/native` is also needed because PAL types include `minipal/guid.h`.

## Remaining integration blockers

1. `ClrProfilerInterfaces.h` currently extends an older Windows `corprof.h`
   manually. Modern official headers already define Callback10/11 and Info9/10;
   including both produces duplicate definitions. A Linux build must use the
   official declarations directly, with consistent PAL type and IID definitions.
   Do not patch guessed interface layouts to get a successful compile.
2. `ClrSuspension.cpp` and the wider native protection implementation still use
   Windows locks, sleep, exports and platform APIs. Recovering this header does
   not port those mechanisms. Runtime suspension/attachment must be validated
   on the target CoreCLR before claiming Linux protection support.
3. The official sample is useful for PAL include order, `SOS_INCLUDE`, platform
   compiler options, GUID definitions, exports and pthread/dl linking. It is not
   a drop-in replacement for the project's native protection library. Full
   profiler integration must choose and test the same pinned headers throughout.
4. Windows compatibility with the project's old SDK/interface wrapper was not
   compiled here. The generated base requires Callback11 to be declared before
   inclusion, which is the existing `ClrSuspension.cpp` inclusion order.

## Upstream license notice

The method signatures are derived from the MIT-licensed dotnet/runtime project.
[Original license](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/LICENSE.TXT):

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
