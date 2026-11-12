using System;
using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisSnapshots
    {
        private readonly Dictionary<MethodInfo, LinkedListNode<(MethodInfo Method, LifeMethodSnapshot Snapshot)>> entries = new();
        private readonly LinkedList<(MethodInfo Method, LifeMethodSnapshot Snapshot)> recent = new();
        private readonly int maximumCount;
        private readonly long maximumBytes;
        private long bytes;
        internal long Hits { get; private set; }
        internal int Count { get { lock (entries) return entries.Count; } }
        internal long Bytes { get { lock (entries) return bytes; } }
        internal LifeAnalysisSnapshots(int maximumCount = 2048, long maximumBytes = 16 * 1024 * 1024)
        { this.maximumCount = maximumCount; this.maximumBytes = maximumBytes; }

        internal LifeMethodSnapshot Find(MethodInfo method)
        {
            method = MethodIdentity.Declaration(method);
            lock (entries)
            {
                if (!entries.TryGetValue(method, out var node)) return null;
                recent.Remove(node); recent.AddLast(node); Hits++;
                return node.Value.Snapshot;
            }
        }
        internal LifeMethodSnapshot Intern(MethodInfo method, byte[] body, Type[] locals)
        {
            method = MethodIdentity.Declaration(method);
            lock (entries)
            {
                if (entries.TryGetValue(method, out var node))
                {
                    if (node.Value.Snapshot.Matches(body, locals))
                    {
                        recent.Remove(node); recent.AddLast(node); Hits++;
                        return node.Value.Snapshot;
                    }
                    Remove(node);
                }
                var snapshot = new LifeMethodSnapshot(body, locals);
                long size = Size(snapshot);
                if (size > maximumBytes || maximumCount <= 0) return snapshot;
                while (recent.Count >= maximumCount || bytes + size > maximumBytes) Remove(recent.First);
                entries.Add(method, recent.AddLast((method, snapshot))); bytes += size;
                return snapshot;
            }
        }
        internal void Invalidate(MethodInfo method)
        {
            method = MethodIdentity.Declaration(method);
            lock (entries)
                if (entries.TryGetValue(method, out var node)) Remove(node);
        }
        internal void Clear()
        {
            lock (entries) { entries.Clear(); recent.Clear(); bytes = 0; }
        }
        // Include decoded instructions and their labels in the retention allowance.
        private static long Size(LifeMethodSnapshot value) => 256L + (value.Body?.LongLength ?? 0) * 128 + value.Locals.LongLength * 16;
        private void Remove(LinkedListNode<(MethodInfo Method, LifeMethodSnapshot Snapshot)> node)
        { entries.Remove(node.Value.Method); recent.Remove(node); bytes -= Size(node.Value.Snapshot); }
    }
}
