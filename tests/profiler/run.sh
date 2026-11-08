#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
build=$(realpath "${1:-$root/build/linux-x64}")
# SDK, driver and child processes must not inherit a startup profiler or a
# diagnostic listener that can suspend them before the timeout is installed.
unset CORECLR_PROFILER CORECLR_PROFILER_PATH CORECLR_PROFILER_PATH_64 CORECLR_PROFILER_PATH_32
unset DOTNET_PROFILER DOTNET_PROFILER_PATH DOTNET_PROFILER_PATH_64 DOTNET_PROFILER_PATH_32
unset CORECLR_NOTIFICATION_PROFILERS CORECLR_NOTIFICATION_PROFILERS_64 CORECLR_NOTIFICATION_PROFILERS_32
unset CORECLR_NOTIFICATION_PROFILERS_ARM32 CORECLR_NOTIFICATION_PROFILERS_ARM64
unset DOTNET_DiagnosticPorts COMPlus_DiagnosticPorts
export CORECLR_ENABLE_PROFILING=0 DOTNET_ENABLE_PROFILING=0
export CORECLR_ENABLE_NOTIFICATION_PROFILERS=0
export DOTNET_DefaultDiagnosticPortSuspend=0 DOTNET_EnableDiagnostics=1
export DOTNET_EnableDiagnostics_IPC=1 DOTNET_EnableDiagnostics_Profiler=1
dotnet build "$root/tests/profiler/ProfilerSmoke.csproj" -c Release -o "$build/profiler-smoke"
dotnet restore "$root/tests/profiler-attach/AttachDriver.csproj" --locked-mode --source https://api.nuget.org/v3/index.json
dotnet build "$root/tests/profiler-attach/AttachDriver.csproj" --no-restore -c Release -o "$build/profiler-attach"
dotnet build "$root/tests/profiler-self-transport/ProfilerSelfTransport.csproj" -c Release -o "$build/profiler-self-transport"
export LD_LIBRARY_PATH="$build${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
# Keep the SDK/compiler processes outside profiling; profile only the test app.
CORECLR_ENABLE_PROFILING=0 timeout 30s dotnet "$build/profiler-smoke/ProfilerSmoke.dll" --disabled
CORECLR_ENABLE_PROFILING=1 DOTNET_ENABLE_PROFILING=1 \
CORECLR_PROFILER='{8B93831B-5C4D-4B8C-9E55-9ABA9118E482}' \
CORECLR_PROFILER_PATH="$build/libLihuatProfilerSupport.so" \
timeout 30s dotnet "$build/profiler-smoke/ProfilerSmoke.dll"
timeout 35s dotnet "$build/profiler-attach/AttachDriver.dll" \
    "$build/profiler-smoke/ProfilerSmoke.dll" "$build/libLihuatProfilerSupport.so"
# A separate disposable process uses the linked production Request/Connect
# implementation against its own default CLR socket, with the support CLSID.
timeout --kill-after=5s 35s dotnet "$build/profiler-self-transport/ProfilerSelfTransport.dll" \
    "$build/libLihuatProfilerSupport.so"
