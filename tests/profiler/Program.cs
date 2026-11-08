using System.Runtime.InteropServices;
class Program
{
    [DllImport("LihuatProfilerSupport")] static extern uint LihuatProfiler_GetSupportAbi();
    [DllImport("LihuatProfilerSupport")] static extern int LihuatProfiler_IsReady();
    [DllImport("LihuatProfilerSupport")] static extern int LihuatProfiler_SuspendRuntime();
    [DllImport("LihuatProfilerSupport")] static extern int LihuatProfiler_ResumeRuntime();
    [DllImport("ProfilerHarness")] static extern void Tick();
    [DllImport("ProfilerHarness")] static extern int RunSuspensionTest();
    static int stop;
    static long progress;
    static void Main(string[] args)
    {
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
        if (LihuatProfiler_GetSupportAbi() != 1) throw new Exception("Wrong support ABI");
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--disabled" && args[0] != "--attach-child"))
            throw new ArgumentException("Expected no arguments, --disabled or --attach-child");
        bool attachChild = args.Length == 1 && args[0] == "--attach-child";
        bool expectedReady = args.Length == 0;
        if ((LihuatProfiler_IsReady() == 1) != expectedReady) throw new Exception("Unexpected profiler readiness");
        if (LihuatProfiler_SuspendRuntime() >= 0 || LihuatProfiler_ResumeRuntime() >= 0)
            throw new Exception("Managed-thread/unbalanced suspension accepted");
        if (attachChild)
        {
            // The driver attaches only after this proves startup profiling did not run.
            Console.WriteLine("ATTACH_CHILD_READY");
            Console.Out.Flush();
            string? command = Task.Run(Console.ReadLine).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            if (command != "attached") throw new Exception("Missing attach acknowledgement");
            if (!SpinWait.SpinUntil(() => LihuatProfiler_IsReady() == 1, TimeSpan.FromSeconds(5)))
                throw new Exception("ProfilerAttachComplete did not establish readiness");
            if (LihuatProfiler_SuspendRuntime() >= 0 || LihuatProfiler_ResumeRuntime() >= 0)
                throw new Exception("Attached profiler accepted managed-thread/unbalanced suspension");
        }
        else if (!expectedReady) { Console.WriteLine("Disabled-profiler rejection PASS"); return; }
        Thread heartbeat = new(() => { while (Volatile.Read(ref stop) == 0) { Tick(); Interlocked.Increment(ref progress); } });
        heartbeat.Start();
        while (Interlocked.Read(ref progress) == 0) Thread.Yield();
        int result = RunSuspensionTest();
        if (result != 0) throw new Exception($"Native suspension test failed: {result}");
        long previous = Interlocked.Read(ref progress);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Interlocked.Read(ref progress) == previous && DateTime.UtcNow < deadline) Thread.Yield();
        if (Interlocked.Read(ref progress) == previous) throw new Exception("Managed runtime did not resume");
        Volatile.Write(ref stop, 1);
        heartbeat.Join();
        Console.WriteLine($"{(attachChild ? "Dynamic attach" : "Startup")} readiness, managed-thread rejection, native suspension, nesting, ownership and thread-exit cleanup PASS");
    }
}
