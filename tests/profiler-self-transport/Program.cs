using System.Reflection;
using System.Runtime.InteropServices;
using LihuatLib.Mechanisms;

internal static class Program
{
    [DllImport("LihuatProfilerSupport")] static extern uint LihuatProfiler_GetSupportAbi();
    [DllImport("LihuatProfilerSupport")] static extern int LihuatProfiler_IsReady();
    [DllImport("LihuatProfilerSupport")] static extern int LihuatProfiler_SuspendRuntime();
    [DllImport("LihuatProfilerSupport")] static extern int LihuatProfiler_ResumeRuntime();
    [DllImport("ProfilerHarness")] static extern void Tick();
    [DllImport("ProfilerHarness")] static extern int RunSuspensionTest();
    static int stop;
    static long progress;

    static async Task Main(string[] args)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("This integration test requires Linux x86-64.");
        if (args.Length != 1) throw new ArgumentException("Expected the support profiler DSO path, never a PID");
        string path = Path.GetFullPath(args[0]);
        if (!File.Exists(path)) throw new FileNotFoundException("Missing support profiler", path);
        Console.WriteLine($"Self-transport runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
        if (LihuatProfiler_GetSupportAbi() != 1 || LihuatProfiler_IsReady() != 0)
            throw new Exception("Expected support ABI 1 without a startup profiler");
        RequireManagedRejection();

        // Link the production source unchanged and exercise its private Request ->
        // Connect -> LinuxDiagnosticEndpoint path against this process's real CLR.
        // Do not call Attach/Send: their production GUIDs and Bind contract differ.
        MethodInfo request = typeof(ClrProfilerAttach).GetMethod("Request", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(Guid), typeof(string), typeof(CancellationToken) }, null)
            ?? throw new MissingMethodException("Production diagnostic Request signature changed");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Console.WriteLine($"Self-transport endpoint: {ClrProfilerAttach.LinuxDiagnosticEndpoint()}");
        var operation = (Task<int>)request.Invoke(null, new object[] {
            new Guid("8B93831B-5C4D-4B8C-9E55-9ABA9118E482"), path, timeout.Token });
        int result = await operation.WaitAsync(timeout.Token);
        Console.WriteLine($"Production Request returned HRESULT 0x{result:X8}");
        if (result != 0) throw new Exception("Real runtime rejected support profiler self-transport request");
        if (!SpinWait.SpinUntil(() => LihuatProfiler_IsReady() == 1, TimeSpan.FromSeconds(5)))
            throw new Exception("ProfilerAttachComplete did not establish support readiness");
        RequireManagedRejection();

        Thread heartbeat = new(() => {
            while (Volatile.Read(ref stop) == 0) { Tick(); Interlocked.Increment(ref progress); }
        });
        heartbeat.Start();
        try
        {
            if (!SpinWait.SpinUntil(() => Interlocked.Read(ref progress) > 0, TimeSpan.FromSeconds(5)))
                throw new Exception("Managed heartbeat did not start");
            int native = RunSuspensionTest();
            if (native != 0) throw new Exception($"Native suspension test failed: {native}");
            long previous = Interlocked.Read(ref progress);
            if (!SpinWait.SpinUntil(() => Interlocked.Read(ref progress) != previous, TimeSpan.FromSeconds(5)))
                throw new Exception("Managed runtime did not resume");
        }
        finally
        {
            Volatile.Write(ref stop, 1);
            if (!heartbeat.Join(TimeSpan.FromSeconds(5))) throw new Exception("Managed heartbeat did not stop");
        }
        Console.WriteLine("Production Request/Connect self-transport, support readiness, real suspension/resume, nesting, ownership and thread-exit cleanup PASS");
    }

    static void RequireManagedRejection()
    {
        if (LihuatProfiler_SuspendRuntime() >= 0 || LihuatProfiler_ResumeRuntime() >= 0)
            throw new Exception("Managed-thread/unbalanced suspension accepted");
    }
}
