using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class LifeAvailabilityGuard
    {
        private sealed class Entry { internal MethodInfo Method; internal bool Active; internal bool Denied; }
        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly Dictionary<MethodInfo, int> installed = new Dictionary<MethodInfo, int>();
        private static readonly MethodInfo gate = AccessTools.Method(typeof(LifeAvailabilityGuard), nameof(Allows));
        private static readonly MethodInfo denied = AccessTools.Method(typeof(LifeAvailabilityGuard), nameof(Denied));
        private static HashSet<MethodInfo> observed = new HashSet<MethodInfo>();
        private static HashSet<MethodInfo> roots = new HashSet<MethodInfo>();
        private static bool dirty, scanning;
        private static int lastFrame = -1;
        public static long Prevented { get; private set; }
        public static int Installed => entries.Count(e => e.Active);
        public static int Unreadable { get; private set; }
        public static int Rejected { get; private set; }
        public static MethodInfo LastPreventedMethod { get; private set; }

        static LifeAvailabilityGuard()
        {
            PatchIntegrity.WrapperPreparing += BeforePublication;
            PatchIntegrity.WrapperCommitted += method =>
            { if (!scanning && method is MethodInfo changed && observed.Contains(changed)) dirty = true; };
        }

        internal static void Discover()
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            Discover(null, null);
        }

        private static void BeforePublication(MethodBase method, PatchInfo info)
        {
            if (scanning || method is not MethodInfo changed || !roots.Contains(changed)) return;
            dirty = true;
            if (Current.ProgramState == ProgramState.Playing && !StarBloodScanPolicy.CacheOnly)
                Discover(changed, info.prefixes.Select(p => p.PatchMethod).ToArray());
        }

        private static void Discover(MethodInfo proposedRoot, MethodInfo[] prefixes)
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            if (!LibraryCapabilities.Has(LibraryCapability.StarBlood)) return;
            if (scanning) return;
            scanning = true;
            try
            {
                var catalog = new LifeAvailabilityCatalog();
                if (proposedRoot != null) catalog.ProposedPrefixes.Add(proposedRoot, prefixes);
                var key = new LifeAnalysisKey(LifeAnalysisKind.Availability, typeof(LifeAvailabilityCatalog));
                LifeAnalysisArtifact cached = proposedRoot == null ? LifeCachedPlans.Load(key) : null;
                if (cached != null && new HashSet<MethodInfo>(cached.Result.Methods).SetEquals(catalog.ReadPrefixes()))
                {
                    foreach (var entry in cached.Result.Availability) catalog.Guards.Add(entry.Key, entry.Value);
                    catalog.Reads.UnionWith(cached.Inputs.Keys);
                }
                else if (StarBloodScanPolicy.CacheOnly) { dirty = false; return; }
                else
                {
                    catalog.Scan();
                    LifeCachedPlans.Store(key, new LifeAnalysisResult { Methods = catalog.Prefixes.ToArray(),
                        Availability = catalog.Guards.ToArray() }, catalog.Reads);
                }
                observed = new HashSet<MethodInfo>(catalog.Reads);
                roots = catalog.Roots;
                observed.UnionWith(catalog.Roots);
                Unreadable = catalog.Unreadable;
                foreach (Entry entry in entries) entry.Active = false;
                foreach (var candidate in catalog.Guards)
                {
                    MethodInfo method = candidate.Key;
                    try
                    {
                        if (!installed.TryGetValue(method, out int id))
                        {
                            id = entries.Count;
                            entries.Add(new Entry { Method = method, Denied = candidate.Value });
                            int argument = AvailabilityIlPatterns.Recipient(method);
                            int tag = id;
                            NativeIlGuards.RegisterTransform(method, typeof(LifeAvailabilityGuard).FullName, NativeIlGuards.GateOrder + 100,
                                continuation => AvailabilityEntryEmitter.Wrap(method, continuation, gate, denied, tag, argument));
                            installed.Add(method, id);
                        }
                        entries[id].Denied = candidate.Value;
                        entries[id].Active = true;
                    }
                    catch (Exception error)
                    { Rejected++; Log.Warning("[LihuatLib.StarBlood] Availability entry unavailable: " + method + ": " + error.GetType().Name); }
                }
                dirty = false;
            }
            finally { scanning = false; }
        }

        internal static void Update()
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            if (StarBloodScanPolicy.CacheOnly || !dirty || lastFrame == UnityEngine.Time.frameCount) return;
            lastFrame = UnityEngine.Time.frameCount;
            Discover();
        }

        internal static void QueueRefresh(MethodInfo method)
        { if (observed.Contains(MethodIdentity.Declaration(method))) dirty = true; }

        private static bool Allows(int id, object target)
        {
            Pawn pawn = target as Pawn;
            if (!entries[id].Active || pawn == null || Core.PawnLife.Dead(pawn) || PawnContinuity.Authorized(pawn)
                || StarBloodRegistry.For(pawn)?.Subject.ContinuityEnabled != true) return true;
            Prevented++;
            LastPreventedMethod = entries[id].Method;
            return false;
        }

        private static bool Denied(int id) => entries[id].Denied;

        public static bool Guards(MethodInfo method) => installed.TryGetValue(MethodIdentity.Declaration(method), out int id) && entries[id].Active;
    }
}
