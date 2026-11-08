# Portable managed build (development only)

The restored project builds `lihuat_lib.dll` with the .NET 10 SDK. It is adapted
from the public [historical project](https://github.com/1765-za/star_company/blob/c1c3034b0c6b773db937cf1124bb6f6e64e4cedc/lihuat_lib/Source/lihuat_lib.csproj)
and its [analysis policy target](https://github.com/1765-za/star_company/blob/c1c3034b0c6b773db937cf1124bb6f6e64e4cedc/lihuat_lib/Source/AnalysisPolicy.targets).
That historical native code was ABI 10. This branch retains the current managed
ABI 11 checks. Restoring build metadata does **not** establish compatibility with
the historical binaries or with the target game runtime.

## Supply the real reference assemblies

You need authorized local copies of the target host's exact:

- `Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll` together in a game
  managed-assembly directory.
- `0Harmony.dll` in a Harmony directory (which can be the same directory).

Do not substitute an arbitrary NuGet Harmony, fake stubs, or files from another
host version. These dependencies are not committed, downloaded automatically,
or copied into the output directory. The project reports which path is missing.
Existence validation is not an assertion that the files are correct assemblies;
the compiler and later host integration tests still have to validate them.

From the repository root, with .NET 10 installed:

```sh
dotnet build Source/lihuat_lib.csproj -c Release \
  -p:RimWorldManaged="/absolute/path/to/game-managed" \
  -p:HarmonyDir="/absolute/path/to/harmony"
```

Alternatively set `LIHUAT_GAME_MANAGED` and `LIHUAT_HARMONY_DIR` in the environment.
Explicit MSBuild properties take priority over those environment variables.
Use absolute paths, especially if invoking MSBuild from a different directory.
No developer-specific paths are embedded. Build output is under `build/managed/`
by default; `OutputPath` and `BaseIntermediateOutputPath` can be overridden using
normal MSBuild properties. It is not deployed to a game installation.

This project does **not** invoke PowerShell, build native binaries, install a mod,
or certify the native backend. A successful build creates a managed assembly
only. Its `LihuatBuildScope` assembly metadata and build message explicitly say
that native ABI 11 is unvalidated. The platform support library cannot stand in
for the full native backend. Real host startup, protection, JIT and authority
checks must pass before calling the mod usable.

## Analysis cache fingerprint

`LifeAnalysisDiskCache` requires exactly one `LifeAnalysisPolicy` assembly
metadata value. `FingerprintAnalysisPolicy`, before `GetAssemblyAttributes`,
sets it to a SHA-256 digest of a canonical UTF-8/LF manifest containing:

- SHA-256 content hashes of **all managed C# sources** in `Source/`, excluding
  output/intermediate directories, plus the project and policy target files.
- Content hashes of **all four** external reference assemblies, including Waylith.
- SDK version, target framework, language version, optimization, defines,
  unsafe/nullable/overflow settings, platform target and runtime identifier.
- An explicit `none-managed-only` native-certification marker.

Inputs use stable project-relative or reference logical names and ordinal
sorting, never machine-specific absolute paths or filesystem enumeration order.
Settings are base64-encoded to preserve delimiters unambiguously. The manifest is
saved as `analysis-policy.txt` in the intermediate output directory. Missing
reference files, missing build files, a missing core cache source, duplicate
logical names or unreadable files fail instead of producing a partial key.
Content changes, including line-ending changes, deliberately change the digest.
The same source/reference bytes and settings under the same SDK produce the same
key after relocation. This is cache invalidation, not a binary signature,
security attestation, whole-build reproducibility guarantee or native ABI test.
Nonstandard imported build customizations are outside this project's fingerprint
contract; include any additional compiler inputs in the policy before using them.

To validate paths and generate the fingerprint without compiling or restoring:

```sh
dotnet msbuild Source/lihuat_lib.csproj -t:FingerprintAnalysisPolicy \
  -p:RimWorldManaged="/absolute/path/to/game-managed" \
  -p:HarmonyDir="/absolute/path/to/harmony"
```

## Tests without proprietary references

```sh
python3 Tests/BuildMetadata/test_build_metadata.py
```

Two static contract tests run without an SDK. With a .NET 10 SDK on `PATH`, five
additional tests invoke the real MSBuild target in temporary directories. They
check repeat/relocation stability, changes to every reference and source,
compiler-option invalidation, new source inclusion, creation-order independence,
missing paths and missing core source failures. The tests use clearly labeled
opaque byte fixtures for hashing only, never fake reference APIs or a claimed
full managed build. No game assembly is distributed or needed for these tests.
A missing SDK is reported as an explicit skip, not a successful dynamic test.
Full managed compilation and game compatibility still require real references
and the actual target host.
