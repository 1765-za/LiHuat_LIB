using System;
using System.Diagnostics;

namespace LihuatLib.Experimental.VirtualEntities
{
    public enum VmObservationState { Baseline, Ready, Comparing, Complete, Unavailable, Disposed }

    public sealed class VmMemoryObservation : IDisposable
    {
        private const int ChunkBytes = 4096, MaximumOffsets = 1024;
        private readonly VmExperimentSession session;
        private readonly VmNativeMemory memory;
        private readonly IntPtr address;
        private byte[] baseline, current;
        private uint[] scratch = new uint[MaximumOffsets];
        private int[] offsets = new int[MaximumOffsets];
        private int cursor, offsetCount;
        internal VmTarget Target { get; }
        public int Length { get; }
        public int BytesRead => cursor;
        public int ChangedBytes { get; private set; }
        public VmObservationState State { get; private set; } = VmObservationState.Baseline;
        public ReadOnlyMemory<int> ChangedOffsets => offsets.AsMemory(0, offsetCount);
        public bool OffsetsTruncated => ChangedBytes > offsetCount;

        internal VmMemoryObservation(VmExperimentSession session, VmNativeMemory memory, VmTarget target, IntPtr address, int length)
        {
            this.session = session; this.memory = memory; Target = target; this.address = address; Length = length;
            baseline = new byte[length]; current = new byte[length];
        }

        public VmObservationState Step(int byteBudget = 65536, int milliseconds = 2)
        {
            session.RequireActive();
            ObjectDisposedException.ThrowIf(State == VmObservationState.Disposed, this);
            if (byteBudget < ChunkBytes || byteBudget > 65536) throw new ArgumentOutOfRangeException(nameof(byteBudget));
            if (milliseconds < 1 || milliseconds > 10) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            if (State != VmObservationState.Baseline && State != VmObservationState.Comparing) return State;
            long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * milliseconds / 1000;
            int read = 0;
            do
            {
                int count = Math.Min(ChunkBytes, Math.Min(Length - cursor, byteBudget - read));
                bool comparing = State == VmObservationState.Comparing;
                if (!memory.Sample(IntPtr.Add(address, cursor), comparing ? baseline : null, comparing ? current : baseline,
                    cursor, count, scratch, out uint changed)) { State = VmObservationState.Unavailable; return State; }
                ChangedBytes += (int)changed;
                int saved = Math.Min((int)changed, MaximumOffsets - offsetCount);
                for (int i = 0; i < saved; i++) offsets[offsetCount++] = cursor + (int)scratch[i];
                cursor += count; read += count;
                if (cursor == Length) { State = comparing ? VmObservationState.Complete : VmObservationState.Ready; break; }
            } while (read < byteBudget && Stopwatch.GetTimestamp() < until);
            return State;
        }

        public void BeginComparison()
        {
            session.RequireActive();
            if (State != VmObservationState.Ready) throw new InvalidOperationException("Capture a complete baseline before comparison.");
            cursor = 0; ChangedBytes = offsetCount = 0; State = VmObservationState.Comparing;
        }

        public void Dispose()
        {
            if (State == VmObservationState.Disposed) return;
            session.RequireActive();
            baseline = current = Array.Empty<byte>(); scratch = Array.Empty<uint>(); offsets = Array.Empty<int>(); offsetCount = 0;
            State = VmObservationState.Disposed; session.Release(this);
        }
    }
}
