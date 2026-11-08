#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
build=$(realpath "${1:-$root/build/linux-x64}")
dotnet build "$root/tests/profiler/ProfilerSmoke.csproj" -c Release -o "$build/profiler-smoke"
export LD_LIBRARY_PATH="$build${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
# Keep the SDK/compiler processes outside profiling; profile only the test app.
CORECLR_ENABLE_PROFILING=0 timeout 30s dotnet "$build/profiler-smoke/ProfilerSmoke.dll" --disabled
CORECLR_ENABLE_PROFILING=1 \
CORECLR_PROFILER='{8B93831B-5C4D-4B8C-9E55-9ABA9118E482}' \
CORECLR_PROFILER_PATH="$build/libLihuatProfilerSupport.so" \
timeout 30s dotnet "$build/profiler-smoke/ProfilerSmoke.dll"
