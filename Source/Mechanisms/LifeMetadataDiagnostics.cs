using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeMetadataDiagnostics
    {
        private sealed class Entry(string assembly, string member)
        {
            internal readonly string Assembly = assembly, Member = member;
            internal readonly HashSet<string> Reasons = new();
            internal readonly HashSet<string> Pending = new();
        }

        private sealed class Batch(LifeMetadataDiagnostics owner) : IDisposable
        {
            private LifeMetadataDiagnostics active = owner;
            public void Dispose() => Interlocked.Exchange(ref active, null)?.EndBatch();
        }

        private readonly Dictionary<string, Entry> entries = new();
        private readonly HashSet<Entry> pending = new();
        private readonly Action<string> write;
        private int batches;
        internal LifeMetadataDiagnostics(Action<string> write) { this.write = write; }
        internal int Unavailable { get { lock (entries) return entries.Count; } }

        internal IDisposable BeginBatch()
        {
            lock (entries) batches++;
            return new Batch(this);
        }

        internal void Report(string assembly, string member, string reason)
        {
            string[] messages;
            lock (entries)
            {
                string key = assembly + ":" + member;
                if (!entries.TryGetValue(key, out Entry entry)) entries.Add(key, entry = new Entry(assembly, member));
                if (!entry.Reasons.Add(reason)) return;
                entry.Pending.Add(reason); pending.Add(entry);
                messages = batches == 0 ? Drain() : Array.Empty<string>();
            }
            foreach (string message in messages) write(message);
        }

        private void EndBatch()
        {
            string[] messages;
            lock (entries) messages = --batches == 0 ? Drain() : Array.Empty<string>();
            foreach (string message in messages) write(message);
        }

        private string[] Drain()
        {
            string[] messages = pending.GroupBy(entry => entry.Assembly).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => "[LihuatLib.Analysis] Unavailable metadata: assembly=" + group.Key + " members=" + group.Count()
                    + "\n" + string.Join("\n", group.OrderBy(entry => entry.Member, StringComparer.Ordinal)
                        .SelectMany(entry => entry.Pending.OrderBy(reason => reason, StringComparer.Ordinal)
                            .Select(reason => "  " + entry.Member + ": " + reason)))).ToArray();
            foreach (Entry entry in pending) entry.Pending.Clear();
            pending.Clear();
            return messages;
        }
    }
}
