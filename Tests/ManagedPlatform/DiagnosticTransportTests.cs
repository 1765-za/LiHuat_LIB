using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LihuatLib.Mechanisms;

internal static class DiagnosticTransportTests
{
    internal static async Task Run()
    {
        // Never replace the running CLR's socket. A private TMPDIR isolates the fake peer.
        string directory = Path.Combine("/tmp", "lh-ipc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string previousTemp = Environment.GetEnvironmentVariable("TMPDIR");
        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", directory);
            await Exchange(0, 0, false, false);
            await Exchange(unchecked((int)0x8013136A), 0xff, false, false);
            await Exchange(0, 0, true, false);
            await Exchange(0, 0, false, true);
            await Cancellation();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", previousTemp);
            Directory.Delete(directory, true);
        }
        Console.WriteLine("Unix diagnostic transport: fragmented success/error, malformed/truncated response and cancellation tests completed.");
    }

    private static Task<int> Request(Guid id, string path, CancellationToken token)
    {
        // Exercise the production private connection + request + response path without binding a native profiler.
        var method = typeof(ClrProfilerAttach).GetMethod("Request", BindingFlags.NonPublic | BindingFlags.Static);
        return (Task<int>)method.Invoke(null, new object[] { id, path, token });
    }

    private static async Task Exchange(int result, byte command, bool malformed, bool truncated)
    {
        string endpoint = ClrProfilerAttach.LinuxDiagnosticEndpoint();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(endpoint));
        listener.Listen(1);
        Guid id = Guid.NewGuid();
        const string path = "/not-a-profiler/libLihuatNative.so";
        try
        {
            Task server = Serve();
            Exception failure = null;
            int actual = 0;
            try { actual = await Request(id, path, timeout.Token); }
            catch (Exception error) { failure = error; }
            await server;
            if (malformed && failure is not InvalidDataException)
                throw new InvalidOperationException("Malformed diagnostic header was not rejected.", failure);
            if (truncated && failure is not EndOfStreamException)
                throw new InvalidOperationException("Truncated diagnostic response was not rejected.", failure);
            if (!malformed && !truncated && (failure != null || actual != result))
                throw new InvalidOperationException("Diagnostic result did not round-trip.", failure);
        }
        finally { listener.Dispose(); File.Delete(endpoint); }

        async Task Serve()
        {
            using Socket peer = await listener.AcceptAsync(timeout.Token);
            using var stream = new NetworkStream(peer, ownsSocket: false);
            byte[] header = new byte[20];
            await stream.ReadExactlyAsync(header, timeout.Token);
            int length = BitConverter.ToUInt16(header, 14);
            if (Encoding.ASCII.GetString(header, 0, 14) != "DOTNET_IPC_V1\0"
                || header[16] != 3 || header[17] != 1 || length < 48)
                throw new InvalidDataException("Invalid profiler attach request framing.");
            byte[] body = new byte[length - 20];
            await stream.ReadExactlyAsync(body, timeout.Token);
            using var reader = new BinaryReader(new MemoryStream(body), Encoding.Unicode);
            if (reader.ReadUInt32() != 5000 || new Guid(reader.ReadBytes(16)) != id)
                throw new InvalidDataException("Invalid attach timeout or profiler identity.");
            uint chars = reader.ReadUInt32();
            if (chars != path.Length + 1 || Encoding.Unicode.GetString(reader.ReadBytes(checked((int)chars * 2))) != path + '\0'
                || reader.ReadUInt32() != 0 || reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Invalid attach path or client data.");
            byte[] response;
            using (var buffer = new MemoryStream())
            {
                using var writer = new BinaryWriter(buffer, Encoding.ASCII, true);
                writer.Write(Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0"));
                writer.Write((ushort)24); writer.Write((byte)0xff); writer.Write(command); writer.Write((ushort)0);
                writer.Write(result);
                response = buffer.ToArray();
            }
            if (malformed) response[0] = 0;
            int count = malformed ? 20 : truncated ? 22 : response.Length;
            // Fragment both header and payload so the client's ReadExactlyAsync paths are exercised.
            for (int offset = 0; offset < count; offset += 3)
                await stream.WriteAsync(response.AsMemory(offset, Math.Min(3, count - offset)), timeout.Token);
        }
    }

    private static async Task Cancellation()
    {
        string endpoint = ClrProfilerAttach.LinuxDiagnosticEndpoint();
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(endpoint));
        listener.Listen(1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        try
        {
            Task<int> request = Request(Guid.NewGuid(), "/not-a-profiler/test.so", cancel.Token);
            using Socket peer = await listener.AcceptAsync(timeout.Token);
            cancel.Cancel(); // Connected, but the peer never sends a response.
            try { await request; }
            catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("Diagnostic request ignored cancellation.");
        }
        finally { listener.Dispose(); File.Delete(endpoint); }
    }
}
