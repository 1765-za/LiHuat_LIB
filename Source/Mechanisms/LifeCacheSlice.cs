using System;
using System.IO;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCacheSlice : Stream
    {
        private readonly Stream source;
        private readonly long start, length;
        internal LifeCacheSlice(Stream source, long start, long length)
        { this.source = source; this.start = start; this.length = length; source.Position = start; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => source.Position - start; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => source.Read(buffer.Slice(0, (int)Math.Min(buffer.Length, length - Position)));
        public override long Seek(long offset, SeekOrigin origin)
        {
            long next = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset,
                SeekOrigin.End => length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (next < 0 || next > length) throw new IOException("Cache record seek is outside its payload.");
            source.Position = start + next; return next;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
}
