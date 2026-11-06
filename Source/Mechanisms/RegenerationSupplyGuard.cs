using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class RegenerationSupplyGuard
    {
        private sealed class Entry
        {
            internal RegenerationSupplySite Site;
            internal readonly HashSet<MethodInfo> Roots = new HashSet<MethodInfo>();
        }
        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly HashSet<MethodInfo> installed = new HashSet<MethodInfo>();
        private static readonly HashSet<MethodInfo> preparations = new HashSet<MethodInfo>();
        internal static int Installed => installed.Count;
        internal static int Rejected { get; private set; }
        internal static long Denied { get; private set; }

        internal static void Register(MethodInfo root, RegenerationSupplySite[] sites)
        {
            foreach (MethodInfo factory in sites.Select(s => s.Factory).Distinct())
            {
                if (preparations.Contains(factory)) continue;
                try
                {
                    MethodInfo begin = AccessTools.Method(typeof(RegenerationSupplyGuard), factory.ReturnType == typeof(void)
                        ? nameof(BeginPreparation) : nameof(BeginPreparedPawn));
                    MethodInfo end = AccessTools.Method(typeof(RegenerationSupplyGuard), nameof(End));
                    NativeIlGuards.RegisterTransform(factory, "lihuat.lib.regeneration.preparation", 1250,
                        body => IlBoundaryEmitter.Scope(factory, body, begin, Array.Empty<int>(), end, Array.Empty<int>()));
                    preparations.Add(factory);
                }
                catch (Exception e) { Rejected++; Log.Warning("[LihuatLib.Regeneration] Preparation scope was not installed for " + factory + ": " + e.Message); }
            }
            foreach (var group in sites.GroupBy(s => s.Caller))
            {
                var ids = new List<int>();
                foreach (RegenerationSupplySite site in group)
                {
                    int id = entries.FindIndex(e => e.Site.Same(site));
                    if (id < 0) { id = entries.Count; entries.Add(new Entry { Site = site }); }
                    entries[id].Roots.Add(root); ids.Add(id);
                }
                if (installed.Contains(group.Key)) continue;
                try
                {
                    var patches = Harmony.GetPatchInfo(group.Key);
                    if (NativeIlGuards.Contains(group.Key) || patches != null && patches.Owners.Any())
                        throw new InvalidOperationException("Supply caller already has an execution owner.");
                    var plans = ids.Select(id => entries[id].Site).ToArray();
                    var current = new RegenerationSupplyAnalysis(new LifeIlReader()).FindSites(group.Key, new HashSet<MethodInfo>(plans.Select(p => p.Factory))).ToArray();
                    if (plans.Any(p => !current.Any(p.Same))) throw new InvalidOperationException("Supply caller IL changed after analysis.");
                    var body = RegenerationSupplyEmitter.Rewrite(group.Key, plans, ids.ToArray());
                    NativeIlGuards.InstallControls(new[] { group.Key }, new[] { body });
                    installed.Add(group.Key);
                }
                catch (Exception e)
                {
                    Rejected++;
                    Log.Warning("[LihuatLib.Regeneration] Supply interception was not installed for " + group.Key + ": " + e.Message);
                }
            }
        }
        internal static bool Begin(int id, Pawn source, out RegenerationFlow.Scope scope)
        {
            scope = default;
            if (source == null || LihuatGame.Current == null) return true;
            bool active = false;
            foreach (MethodInfo root in entries[id].Roots) if (RegenerationGuard.Active(root)) { active = true; break; }
            if (!active) return true;
            bool allowed = LihuatGame.Current.Regeneration.Begin(source, RegenerationKind.Preparation, out scope);
            if (!allowed) Denied++;
            return allowed;
        }
        internal static void End(RegenerationFlow.Scope scope) => scope.Dispose();
        private static bool BeginPreparation(out RegenerationFlow.Scope scope)
        {
            scope = default;
            return LihuatGame.Current == null || LihuatGame.Current.Regeneration.BeginPreparation(out scope);
        }
        private static bool BeginPreparedPawn(out RegenerationFlow.Scope scope, out Pawn result)
        { result = null; return BeginPreparation(out scope); }
    }
}
