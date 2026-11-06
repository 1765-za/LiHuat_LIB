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
        internal static void Attach(string path) => Send(path).GetAwaiter().GetResult();
        private static async Task Send(string path)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await attaching.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var ready = NativeModule.Bind<Ready>("LihuatNative_SuspensionReady");
                if (ready() != 0) return;
                await Request(path, timeout.Token).ConfigureAwait(false);
                if (ready() == 0) throw new InvalidOperationException("CLR profiler attached without a suspension service.");
            }
            finally { attaching.Release(); }
        }
        private static async Task Request(string path, CancellationToken cancellation)
        {
            using var pipe = new NamedPipeClientStream(".", "dotnet-diagnostic-" + Environment.ProcessId,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellation).ConfigureAwait(false);
            using var request = new MemoryStream();
            using (var writer = new BinaryWriter(request, Encoding.Unicode, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0"));
                writer.Write((ushort)0); writer.Write((byte)3); writer.Write((byte)1); writer.Write((ushort)0);
                writer.Write(5000u); writer.Write(new Guid("4bafc2d1-9ceb-4bf7-9d53-9b482d9eed11").ToByteArray());
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
            int result = response.Length >= 4 ? BitConverter.ToInt32(response) : 0;
            if (header[17] != 0 || result < 0)
                throw new InvalidOperationException("CLR suspension profiler attachment failed: 0x" + result.ToString("X8"));
        }
    }
}
