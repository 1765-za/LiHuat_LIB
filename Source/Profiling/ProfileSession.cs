using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace LihuatLib.Profiling
{
    public sealed class ProfileSession : IDisposable
    {
        internal struct Entry
        {
            internal ProfileMarker Marker;
            internal long Serial, Started, Children, Bytes, ChildBytes;
        }
        internal sealed class ThreadState
        {
            internal readonly int Thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            internal readonly Entry[] Stack;
            internal int Depth;
            internal long Epoch, Serial;
            internal ThreadState(int depth) { Stack = new Entry[depth]; }
        }
        public readonly struct Scope : IDisposable
        {
            private readonly ProfileSession owner;
            private readonly ThreadState state;
            private readonly int slot;
            private readonly long epoch, serial;
            internal Scope(ProfileSession owner, ThreadState state, int slot, long epoch, long serial)
            { this.owner = owner; this.state = state; this.slot = slot; this.epoch = epoch; this.serial = serial; }
            public void Dispose() => owner?.End(state, slot, epoch, serial);
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, ProfileMarker> markers = new Dictionary<string, ProfileMarker>(StringComparer.Ordinal);
        private readonly ThreadLocal<ThreadState> threads;
        private readonly int maximumMarkers;
        private readonly Func<long> allocatedBytes;
        private long epoch, sequence, dropped;
        private int readers;
        private bool disposed;
        public string Owner { get; }
        public bool CaptureAllocations => allocatedBytes != null;
        internal long Epoch => Interlocked.Read(ref epoch);
        public bool Running => Epoch > 0;

        public ProfileSession(string owner, bool captureAllocations = false, int maximumMarkers = 256, int maximumDepth = 64)
        {
            LibraryCapabilities.Require(LibraryCapability.Profiling);
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("A profiler session needs an owner.", nameof(owner));
            if (maximumMarkers <= 0) throw new ArgumentOutOfRangeException(nameof(maximumMarkers));
            if (maximumDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            Owner = owner; this.maximumMarkers = maximumMarkers;
            if (captureAllocations) allocatedBytes = CreateAllocationCounter();
            threads = new ThreadLocal<ThreadState>(() => new ThreadState(maximumDepth));
        }

        public static bool ProbeAllocationCounter(out string failure)
        {
            LibraryCapabilities.Require(LibraryCapability.Profiling);
            try { CreateAllocationCounter(); failure = null; return true; }
            catch (PlatformNotSupportedException error) { failure = error.Message; return false; }
        }
        private static Func<long> CreateAllocationCounter()
        {
            MethodInfo getter = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            if (getter == null) throw new PlatformNotSupportedException("Thread allocation counters are unavailable in this runtime.");
            var counter = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), getter);
            long before = counter();
            byte[] probe = new byte[1024];
            long after = counter();
            GC.KeepAlive(probe);
            if (after - before < 1024) throw new PlatformNotSupportedException("The runtime does not report thread allocations; allocation samples would be inaccurate.");
            return counter;
        }

        public ProfileMarker Marker(string id, string category = "")
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A marker needs an identity.", nameof(id));
            lock (sync)
            {
                RequireOpen();
                if (markers.TryGetValue(id, out ProfileMarker marker))
                {
                    if (marker.Category != category) throw new ArgumentException("A marker identity already uses a different category.", nameof(category));
                    return marker;
                }
                if (markers.Count == maximumMarkers) throw new InvalidOperationException("Profiler marker capacity reached.");
                marker = new ProfileMarker(this, id, category); markers.Add(id, marker); return marker;
            }
        }
        public void Start()
        {
            lock (sync) { RequireOpen(); if (!Running) Interlocked.Exchange(ref epoch, ++sequence); }
        }
        public void Stop() { lock (sync) Interlocked.Exchange(ref epoch, 0); }
        public void Clear()
        {
            lock (sync)
            {
                RequireOpen();
                Interlocked.Exchange(ref epoch, Running ? ++sequence : 0);
                foreach (ProfileMarker marker in markers.Values) marker.Clear();
                Interlocked.Exchange(ref dropped, 0);
            }
        }
        public ProfileSnapshot Capture()
        {
            lock (sync)
            {
                RequireOpen();
                var samples = new ProfileSample[markers.Count]; int i = 0;
                foreach (ProfileMarker marker in markers.Values) samples[i++] = marker.Snapshot();
                return new ProfileSnapshot(Running, Interlocked.Read(ref dropped), samples);
            }
        }
        private void RequireOpen() { if (disposed) throw new ObjectDisposedException(nameof(ProfileSession)); }
        internal Scope Begin(ProfileMarker marker)
        {
            long current = Epoch;
            if (current <= 0) return default;
            ThreadState state;
            Interlocked.Increment(ref readers);
            try { if (Epoch != current) return default; state = threads.Value; }
            finally { Interlocked.Decrement(ref readers); }
            if (state.Epoch != current) { Array.Clear(state.Stack, 0, state.Depth); state.Depth = 0; state.Epoch = current; }
            if (state.Depth == state.Stack.Length) { Interlocked.Increment(ref dropped); return default; }
            int slot = state.Depth++;
            long serial = ++state.Serial;
            state.Stack[slot] = new Entry { Marker = marker, Serial = serial, Started = Stopwatch.GetTimestamp(), Bytes = allocatedBytes?.Invoke() ?? 0 };
            return new Scope(this, state, slot, current, serial);
        }
        private void End(ThreadState state, int slot, long generation, long serial)
        {
            if (Epoch != generation || generation <= 0) return;
            if (state.Thread != Thread.CurrentThread.ManagedThreadId) { Interlocked.Increment(ref dropped); return; }
            if (state.Epoch != generation || state.Stack[slot].Serial != serial) return;
            if (slot != state.Depth - 1)
            { Interlocked.Increment(ref dropped); Array.Clear(state.Stack, 0, state.Depth); state.Depth = 0; return; }
            long end = Stopwatch.GetTimestamp(), bytes = allocatedBytes?.Invoke() ?? 0;
            Entry entry = state.Stack[slot]; state.Stack[slot] = default; state.Depth--;
            long elapsed = Math.Max(0, end - entry.Started), allocated = Math.Max(0, bytes - entry.Bytes);
            if (state.Depth > 0)
            { state.Stack[state.Depth - 1].Children += elapsed; state.Stack[state.Depth - 1].ChildBytes += allocated; }
            entry.Marker.Add(generation, elapsed, Math.Max(0, elapsed - entry.Children), allocated, Math.Max(0, allocated - entry.ChildBytes));
        }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                Interlocked.Exchange(ref epoch, 0); disposed = true;
                var wait = new SpinWait();
                while (Volatile.Read(ref readers) != 0) wait.SpinOnce();
                threads.Dispose();
            }
        }
    }
}
