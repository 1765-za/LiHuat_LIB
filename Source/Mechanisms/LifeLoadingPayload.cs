using System.IO;
using System.IO.Compression;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeLoadingPayload
    {
        private readonly byte[] bytes;
        private readonly int length;
        internal long Size => bytes.LongLength;

        internal LifeLoadingPayload(byte[] encoded)
        {
            length = encoded.Length;
            using var output = new MemoryStream();
            using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, true)) compressor.Write(encoded);
            bytes = output.ToArray();
        }

        internal byte[] Decode()
        {
            using var input = new MemoryStream(bytes, false);
            using var decoder = new BrotliStream(input, CompressionMode.Decompress);
            var encoded = new byte[length];
            decoder.ReadExactly(encoded);
            if (decoder.ReadByte() != -1) throw new InvalidDataException();
            return encoded;
        }
    }
}
