using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LihuatLib.Mechanisms
{
    internal static class ClrProfilerAttach
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Ready();
        private static readonly SemaphoreSlim attaching = new(1, 1);
        private static readonly Guid primary = new("4bafc2d1-9ceb-4bf7-9d53-9b482d9eed11");
        private static readonly Guid notification = new("70d8330e-2177-46c0-b40a-7c496625a8f2");
        private const int AlreadyActive = unchecked((int)0x8013136A);
        internal static void Attach(string path) => Send(path).GetAwaiter().GetResult();
        private static async Task Send(string path)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await attaching.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var ready = NativeModule.Bind<Ready>("LihuatNative_SuspensionReady");
                if (ready() != 0) return;
                int result = await Request(primary, path, timeout.Token).ConfigureAwait(false);
                if (result == AlreadyActive && ready() == 0)
                    result = await Request(notification, path, timeout.Token).ConfigureAwait(false);
                if (result < 0) throw new InvalidOperationException("CLR suspension profiler attachment failed: 0x" + result.ToString("X8"));
                if (ready() == 0) throw new InvalidOperationException("CLR profiler attached without a suspension service.");
            }
            finally { attaching.Release(); }
        }
        private static async Task<int> Request(Guid identity, string path, CancellationToken cancellation)
        {
            using var pipe = new NamedPipeClientStream(".", "dotnet-diagnostic-" + Environment.ProcessId,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellation).ConfigureAwait(false);
            using var request = new MemoryStream();
            using (var writer = new BinaryWriter(request, Encoding.Unicode, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0"));
                writer.Write((ushort)0); writer.Write((byte)3); writer.Write((byte)1); writer.Write((ushort)0);
                writer.Write(5000u); writer.Write(identity.ToByteArray());
                writer.Write((uint)path.Length + 1); writer.Write(Encoding.Unicode.GetBytes(path + '\0'));
                writer.Write(0u);
                request.Position = 14; writer.Write(checked((ushort)request.Length));
            }
            await pipe.WriteAsync(request.ToArray(), cancellation).ConfigureAwait(false);
            byte[] header = new byte[20];
            await pipe.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
            int length = BitConverter.ToUInt16(header, 14);
            if (Encoding.ASCII.GetString(header, 0, 14) != "DOTNET_IPC_V1\0" || header[16] != 0xff
                || length < 20 || length > 4096) throw new InvalidDataException("Invalid CLR profiler response.");
            byte[] response = new byte[length - 20];
            await pipe.ReadExactlyAsync(response, cancellation).ConfigureAwait(false);
            if (response.Length < 4) throw new InvalidDataException("Missing CLR profiler response code.");
            int result = BitConverter.ToInt32(response);
            if (header[17] != 0 && result >= 0) throw new InvalidDataException("CLR profiler error response has no failure code.");
            return result;
        }
    }
}
