using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;

// This test can target only the disposable process it creates, never a supplied PID.
if (args.Length != 2) throw new ArgumentException("Expected smoke DLL and profiler DSO paths");
string smoke = Path.GetFullPath(args[0]);
string profiler = Path.GetFullPath(args[1]);
if (!File.Exists(smoke) || !File.Exists(profiler)) throw new FileNotFoundException("Missing integration build outputs");
string host = Environment.ProcessPath ?? throw new Exception("Cannot resolve running dotnet host");
if (Path.GetFileName(host) != "dotnet") throw new Exception("Run this driver with dotnet AttachDriver.dll");
var start = new ProcessStartInfo(host)
{
    UseShellExecute = false,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
start.ArgumentList.Add(smoke);
start.ArgumentList.Add("--attach-child");
// Keep this child outside any inherited startup profiler/diagnostic-port setup.
foreach (string name in start.Environment.Keys.Where(name =>
    name.StartsWith("CORECLR_PROFILER", StringComparison.Ordinal) ||
    name.StartsWith("DOTNET_PROFILER", StringComparison.Ordinal) ||
    name.StartsWith("CORECLR_NOTIFICATION_PROFILERS", StringComparison.Ordinal)).ToArray())
    start.Environment.Remove(name);
start.Environment["CORECLR_ENABLE_PROFILING"] = "0";
start.Environment["DOTNET_ENABLE_PROFILING"] = "0";
start.Environment["CORECLR_ENABLE_NOTIFICATION_PROFILERS"] = "0";
start.Environment.Remove("DOTNET_DiagnosticPorts");
start.Environment.Remove("COMPlus_DiagnosticPorts");
start.Environment["DOTNET_DefaultDiagnosticPortSuspend"] = "0";
start.Environment["DOTNET_EnableDiagnostics"] = "1";
start.Environment["DOTNET_EnableDiagnostics_IPC"] = "1";
start.Environment["DOTNET_EnableDiagnostics_Profiler"] = "1";

using var child = new Process { StartInfo = start };
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
bool passed = false;
child.Start();
Task output = ReadOutput();
Task errors = ReadErrors();
try
{
    await ready.Task.WaitAsync(deadline.Token);
    Console.WriteLine($"Attaching support profiler to disposable child {child.Id}");
    var client = new DiagnosticsClient(child.Id);
    // AttachProfiler is synchronous; the outer deadline also bounds connection setup.
    await Task.Run(() => client.AttachProfiler(TimeSpan.FromSeconds(10),
        new Guid("8B93831B-5C4D-4B8C-9E55-9ABA9118E482"), profiler))
        .WaitAsync(deadline.Token);
    await child.StandardInput.WriteLineAsync("attached");
    await child.StandardInput.FlushAsync();
    child.StandardInput.Close();
    await child.WaitForExitAsync(deadline.Token);
    await Task.WhenAll(output, errors).WaitAsync(deadline.Token);
    if (child.ExitCode != 0 || !passed)
        throw new Exception($"Dynamic attach child failed: exit={child.ExitCode}, completion={passed}");
    Console.WriteLine("DiagnosticsClient external attach, readiness and real suspension/resume PASS");
}
finally
{
    if (!child.HasExited)
    {
        child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
}

async Task ReadOutput()
{
    while (await child.StandardOutput.ReadLineAsync(deadline.Token) is string line)
    {
        Console.WriteLine($"child: {line}");
        if (line == "ATTACH_CHILD_READY") ready.TrySetResult();
        if (line == "Dynamic attach readiness, managed-thread rejection, native suspension, nesting, ownership and thread-exit cleanup PASS")
            passed = true;
    }
    ready.TrySetException(new Exception("Child exited before attach readiness marker"));
}
async Task ReadErrors()
{
    while (await child.StandardError.ReadLineAsync(deadline.Token) is string line)
        Console.Error.WriteLine($"child: {line}");
}
