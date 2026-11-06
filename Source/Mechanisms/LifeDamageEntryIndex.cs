using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeDamageEntryIndex
    {
        private sealed class Snapshot
        {
            internal readonly int Revision;
            internal readonly MethodInfo[] Methods;
            internal Snapshot(int revision, MethodInfo[] methods) { Revision = revision; Methods = methods; }
        }
        private sealed class Entry
        {
            internal int Revision;
            internal Snapshot Snapshot;
        }
        private readonly ConcurrentDictionary<Type, MethodInfo> workers = new ConcurrentDictionary<Type, MethodInfo>();
        private readonly ConcurrentDictionary<MethodBase, Entry> entries = new ConcurrentDictionary<MethodBase, Entry>();
        internal readonly MethodInfo TakeDamage = AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage));
        internal readonly MethodInfo AddHediff = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
            new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) });

        internal MethodInfo Worker(Type type) => workers.GetOrAdd(type,
            worker => AccessTools.Method(worker, "Apply", new[] { typeof(DamageInfo), typeof(Thing) }));

        internal MethodInfo[] Read(MethodInfo method)
        {
            Entry entry = entries.GetOrAdd(method, _ => new Entry());
            int revision = Volatile.Read(ref entry.Revision);
            Snapshot snapshot = Volatile.Read(ref entry.Snapshot);
            if (snapshot != null && snapshot.Revision == revision) return snapshot.Methods;
            var methods = new List<MethodInfo> { method };
            HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
            if (patches != null)
            {
                foreach (Patch patch in patches.Prefixes) methods.Add(patch.PatchMethod);
                foreach (Patch patch in patches.Postfixes) methods.Add(patch.PatchMethod);
                foreach (Patch patch in patches.Finalizers) methods.Add(patch.PatchMethod);
            }
            snapshot = new Snapshot(revision, methods.ToArray());
            // A concurrent registration leaves this snapshot stale, so the next read rebuilds it.
            Volatile.Write(ref entry.Snapshot, snapshot);
            return snapshot.Methods;
        }

        internal void Invalidate(MethodBase method)
        {
            if (entries.TryGetValue(method, out Entry entry)) Interlocked.Increment(ref entry.Revision);
        }
    }
}
