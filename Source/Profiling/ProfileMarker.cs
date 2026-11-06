using System;

namespace LihuatLib.Profiling
{
    public sealed class ProfileMarker
    {
        private readonly ProfileSession session;
        private readonly object sync = new object();
        private long calls, total, self, maximum, allocated, selfAllocated;
        public string Id { get; }
        public string Category { get; }
        internal ProfileMarker(ProfileSession session, string id, string category) { this.session = session; Id = id; Category = category; }
        public ProfileSession.Scope Measure() => session.Begin(this);
        internal void Add(long epoch, long elapsed, long exclusive, long bytes, long exclusiveBytes)
        {
            lock (sync)
            {
                if (session.Epoch != epoch || epoch <= 0) return;
                calls++; total += elapsed; self += exclusive; maximum = Math.Max(maximum, elapsed);
                allocated += bytes; selfAllocated += exclusiveBytes;
            }
        }
        internal void Clear() { lock (sync) calls = total = self = maximum = allocated = selfAllocated = 0; }
        internal ProfileSample Snapshot()
        {
            lock (sync) return new ProfileSample(Id, Category, calls, total, self, maximum,
                session.CaptureAllocations ? allocated : (long?)null, session.CaptureAllocations ? selfAllocated : (long?)null);
        }
    }
}
