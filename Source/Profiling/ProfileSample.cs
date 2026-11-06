namespace LihuatLib.Profiling
{
    public sealed class ProfileSample
    {
        public string Id { get; }
        public string Category { get; }
        public long Calls { get; }
        public double TotalMilliseconds { get; }
        public double SelfMilliseconds { get; }
        public double MaximumMilliseconds { get; }
        public long? AllocatedBytes { get; }
        public long? SelfAllocatedBytes { get; }
        internal ProfileSample(string id, string category, long calls, long total, long self, long maximum, long? allocated, long? selfAllocated)
        {
            Id = id; Category = category; Calls = calls;
            double scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            TotalMilliseconds = total * scale; SelfMilliseconds = self * scale; MaximumMilliseconds = maximum * scale;
            AllocatedBytes = allocated; SelfAllocatedBytes = selfAllocated;
        }
    }

    public sealed class ProfileSnapshot
    {
        public bool Running { get; }
        public long DroppedScopes { get; }
        public System.Collections.Generic.IReadOnlyList<ProfileSample> Samples { get; }
        internal ProfileSnapshot(bool running, long dropped, ProfileSample[] samples)
        { Running = running; DroppedScopes = dropped; Samples = System.Array.AsReadOnly(samples); }
    }
}
