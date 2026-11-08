using System.Net.Sockets;
using System.Reflection;
using System.Text;
using LihuatLib.Mechanisms;

// Controlled decision tests, not real profiler lifecycle or native binding tests.
internal static class Program
{
    const int AlreadyActive = unchecked((int)0x8013136A);
    const int Failure = unchecked((int)0x80004005);
    static readonly Guid Primary = new("4bafc2d1-9ceb-4bf7-9d53-9b482d9eed11");
    static readonly Guid Notification = new("70d8330e-2177-46c0-b40a-7c496625a8f2");
    const string FakePath = "/test-only/not-a-real-profiler.so";
    record Reply(Guid Identity, int Result, int ReadyAfter);

    static async Task Main()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Unix test peer required");
        await Case("initial own readiness", 1, [], null);
        await Case("primary success with readiness", 0, [new(Primary, 0, 1)], null);
        await Case("primary success without readiness", 0, [new(Primary, 0, 0)], "without a suspension service");
        await Case("unrelated primary failure remains failure", 0, [new(Primary, Failure, 1)], "80004005");
        await Case("AlreadyActive plus changed own readiness (current conservative failure)", 0,
            [new(Primary, AlreadyActive, 1)], "8013136A");
        await Case("unrelated active profiler needs notification", 0,
            [new(Primary, AlreadyActive, 0), new(Notification, 0, 1)], null);
        await Case("notification success without own readiness fails", 0,
            [new(Primary, AlreadyActive, 0), new(Notification, 0, 0)], "without a suspension service");
        await Case("notification failure stays failure even with readiness", 0,
            [new(Primary, AlreadyActive, 0), new(Notification, Failure, 1)], "80004005");
        await Case("unrelated active profiler never becomes own readiness", 0,
            [new(Primary, AlreadyActive, 0), new(Notification, AlreadyActive, 0)], "8013136A");
        await Case("own readiness lost before final check", 0, [new(Primary, 0, 1)],
            "without a suspension service", dropAtRead: 2);
        await Case("concurrent Send callers serialize", 0, [new(Primary, 0, 1)], null, concurrent: true);
        Console.WriteLine("11 controlled Send decision cases PASS; no native/game readiness claim");
    }

    static Task Send() => (Task)(typeof(ClrProfilerAttach).GetMethod("Send", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("Send")).Invoke(null, new object[] { FakePath });

    static async Task Case(string label, int initial, Reply[] replies, string expectedError,
        int dropAtRead = 0, bool concurrent = false)
    {
        string directory = Path.Combine("/tmp", "lh-decide-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string oldTemp = Environment.GetEnvironmentVariable("TMPDIR");
        int state = initial, reads = 0, received = 0;
        Task sending = null, second = null, server = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var firstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Isolate the fake peer; never overwrite or connect to the CLR's real socket.
            Environment.SetEnvironmentVariable("TMPDIR", directory);
            NativeModule.Binds = 0;
            NativeModule.Probe = () => {
                if (Interlocked.Increment(ref reads) == dropAtRead) Volatile.Write(ref state, 0);
                return Volatile.Read(ref state);
            };
            listener.Bind(new UnixDomainSocketEndPoint(ClrProfilerAttach.LinuxDiagnosticEndpoint()));
            listener.Listen(2);
            server = Serve();
            sending = Send();
            if (concurrent)
            {
                await firstRequest.Task.WaitAsync(timeout.Token);
                // Send executes synchronously until its first incomplete await.
                // The second call must wait at the semaphore before Bind/Request.
                second = Send();
                if (NativeModule.Binds != 1 || second.IsCompleted)
                    throw new Exception("Concurrent caller bypassed the attach semaphore");
                releaseReply.SetResult();
            }
            Exception failure = null;
            try { await sending.WaitAsync(timeout.Token); }
            catch (Exception error) { failure = error; }
            if (second != null) await second.WaitAsync(timeout.Token);
            await server.WaitAsync(timeout.Token);
            if (expectedError == null ? failure != null
                : failure is not InvalidOperationException || !failure.Message.Contains(expectedError, StringComparison.Ordinal))
                throw new Exception($"{label}: unexpected Send outcome", failure);
            if (received != replies.Length || listener.Poll(0, SelectMode.SelectRead))
                throw new Exception($"{label}: unexpected request count");
            if (NativeModule.Binds != (concurrent ? 2 : 1)) throw new Exception("Wrong bind count");
            Console.WriteLine($"PASS {label}: requests={received}, readiness reads={reads}");
        }
        finally
        {
            timeout.Cancel();
            listener.Dispose();
            // Ensure failed test operations cannot leak into a later scenario.
            bool quiesced = true;
            foreach (Task task in new[] { server, sending, second })
                if (task != null)
                {
                    try { await task.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                    quiesced &= task.IsCompleted;
                }
            // On a terminal timeout keep the private routing/probe in place:
            // a late continuation must never see the runtime's real TMPDIR.
            if (!quiesced) throw new Exception("Test operations did not stop; refusing to restore routing or continue");
            NativeModule.Probe = null;
            Environment.SetEnvironmentVariable("TMPDIR", oldTemp);
            Directory.Delete(directory, true);
        }

        async Task Serve()
        {
            foreach (Reply reply in replies)
            {
                using Socket peer = await listener.AcceptAsync(timeout.Token);
                using var stream = new NetworkStream(peer, ownsSocket: false);
                byte[] header = new byte[20];
                await stream.ReadExactlyAsync(header, timeout.Token);
                int length = BitConverter.ToUInt16(header, 14);
                if (Encoding.ASCII.GetString(header, 0, 14) != "DOTNET_IPC_V1\0"
                    || header[16] != 3 || header[17] != 1 || length < 48)
                    throw new Exception("Wrong attach request header");
                byte[] body = new byte[length - 20];
                await stream.ReadExactlyAsync(body, timeout.Token);
                using var reader = new BinaryReader(new MemoryStream(body), Encoding.Unicode);
                if (reader.ReadUInt32() != 5000 || new Guid(reader.ReadBytes(16)) != reply.Identity)
                    throw new Exception("Wrong primary/notification request identity");
                uint chars = reader.ReadUInt32();
                if (chars != FakePath.Length + 1 || Encoding.Unicode.GetString(reader.ReadBytes(checked((int)chars * 2))) != FakePath + '\0'
                    || reader.ReadUInt32() != 0 || reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new Exception("Wrong request payload");
                received++;
                if (concurrent && received == 1)
                {
                    firstRequest.SetResult();
                    await releaseReply.Task.WaitAsync(timeout.Token);
                }
                // Model an independent native lifecycle change while Request is
                // in flight, not another Send caller bypassing the semaphore.
                Volatile.Write(ref state, reply.ReadyAfter);
                using var buffer = new MemoryStream();
                using (var writer = new BinaryWriter(buffer, Encoding.ASCII, true))
                {
                    writer.Write(Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0"));
                    writer.Write((ushort)24); writer.Write((byte)0xff);
                    writer.Write((byte)(reply.Result < 0 ? 0xff : 0)); writer.Write((ushort)0);
                    writer.Write(reply.Result);
                }
                await stream.WriteAsync(buffer.ToArray(), timeout.Token);
            }
        }
    }
}

namespace LihuatLib.Mechanisms
{
    // Managed test double only. No claim of real NativeModule export binding.
    internal static class NativeModule
    {
        internal static Func<int> Probe;
        internal static int Binds;
        private static int Read() => (Probe ?? throw new Exception("No controlled readiness probe"))();
        internal static T Bind<T>(string name) where T : Delegate
        {
            if (name != "LihuatNative_SuspensionReady" || Probe == null)
                throw new NotSupportedException("Unexpected native binding in controlled test");
            Interlocked.Increment(ref Binds);
            return (T)Delegate.CreateDelegate(typeof(T), typeof(NativeModule).GetMethod(nameof(Read), BindingFlags.NonPublic | BindingFlags.Static));
        }
    }
}
