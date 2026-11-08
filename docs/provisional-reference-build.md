# Pinned reference compilation probe

The Linux branch can be compiled against a specific public reference snapshot
without installing or running a game. This is **compile-only evidence**, not
validation of the user's game installation, native protection, or runtime APIs.
The production project still requires explicitly supplied target-host paths.
No `PackageReference` or fallback runtime has been added to that project.

Pinned inputs:

- Official .NET SDK **10.0.100**, net10.0 target.
- [Krafs.Rimworld.Ref 1.7.4984-unstable](https://www.nuget.org/packages/Krafs.Rimworld.Ref/1.7.4984-unstable), `ref/net10.0`.
- [Lib.Harmony 3.0.0-rc.3](https://www.nuget.org/packages/Lib.Harmony/3.0.0-rc.3), `lib/net10.0`.

The probe fetches these exact package archives from NuGet, checks their pinned
SHA-256 hashes, and extracts only selected DLLs. It does not import or execute
package build scripts. Both selected target-framework dependency groups are
empty. Packages and compiled output stay under the ignored build directory;
no proprietary references or compiled release artifacts are committed.
Harmony's selected DLL is an implementation assembly, but is used only as a
compiler reference here. The probe never loads it or the compiled mod for execution.

```sh
# Requires the exact official SDK already installed:
python3 tools/probe_reference_compile.py
# Or select an isolated dotnet host:
python3 tools/probe_reference_compile.py --dotnet /path/to/dotnet
```

The work directory contains the build log and `report.json` with SDK, source
commit, package hashes, source-item count and output hash. A local `global.json`
selects the pinned SDK. The probe returns the actual compiler result. Success
is deliberately labeled provisional, and it never copies output into a game.

## Observed result

At source commit `3d35a5d9eebabf4fc6cec9404a9cfe81e1db7794`, the original project
compiled **599 source files with zero errors** on Linux x86_64. The only warning
was CS0162 from the generated MSBuild fingerprint task's return scaffolding.
No source files were removed and no runtime APIs were replaced to obtain that
result. This observation is a snapshot; future runs should use their own report.

RimRef's three requested game assemblies carry `ReferenceAssemblyAttribute`.
The package includes 67 net10.0 reference assemblies and excludes the game's
customized .NET implementation. Its documentation explicitly warns that
`WaylithClr` and private .NET members require reflection or `UnsafeAccessor`.
Such contracts are not resolved by a successful C# compilation.

The compile therefore does **not** prove:

- These are the exact game and Harmony versions the user intends to run.
- Ludeon's runtime-specific APIs or private framework members exist on a stock
  Linux CoreCLR, or have the layout/semantics expected by this mod.
- Reflection, emitted IL or UnsafeAccessor targets resolve successfully.
- The complete Linux `libLihuatNative.so` ABI 11 engine exists or its protections
  work. The separate platform/profiler support libraries remain development parts.
- The mod starts, plays correctly or can safely replace a Windows installation.

Exact host references/customized runtime and full native integration are still
required for game validation. Do not distribute or deploy the probe DLL as a release.
