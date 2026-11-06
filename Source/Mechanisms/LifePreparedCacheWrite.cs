using System;
using System.Threading;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifePreparedCacheWrite : IDisposable
    {
        private readonly LifeCachePackage package;
        private int disposed;
        internal readonly string Key;
        internal readonly long Offset;
        internal readonly int Length, FrameLength;
        internal LifePreparedCacheWrite(LifeCachePackage package, string key, long offset, int length, int frameLength)
        { this.package = package; Key = key; Offset = offset; Length = length; FrameLength = frameLength; }
        internal void Commit() => package.Commit(this);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) package.Release();
        }
    }
}
