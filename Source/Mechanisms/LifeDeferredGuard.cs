using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Core;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeDeferredGuard
    {
        private static readonly List<LifeDeferredSite> sites = new List<LifeDeferredSite>();
        private static readonly Dictionary<MethodBase, List<LifeDeferredSite>> byMethod = new Dictionary<MethodBase, List<LifeDeferredSite>>();
        internal static readonly MethodInfo BindMethod = AccessTools.Method(typeof(LifeDeferredGuard), nameof(Bind));
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthPawn = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
        public static int InstalledSites => sites.Count(s => s.Installed);

        internal static List<LifeDeferredSite> For(MethodBase method) => byMethod.TryGetValue(method, out List<LifeDeferredSite> found) ? found : null;

        internal static bool Invalidate(MethodInfo method)
        {
            if (!byMethod.TryGetValue(method, out List<LifeDeferredSite> previous)) return false;
            foreach (LifeDeferredSite site in previous) site.Installed = false;
            byMethod.Remove(method);
            return true;
        }

        internal static void Register(IEnumerable<LifeDeferredSite> candidates)
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            var changed = new HashSet<MethodBase>();
            foreach (LifeDeferredSite candidate in candidates)
            {
                MethodInfo method = candidate.Method;
                if (method.IsGenericMethod || method.DeclaringType.IsGenericType || method.DeclaringType.IsValueType || method.Module.Assembly.IsDynamic) continue;
                if (!byMethod.TryGetValue(method, out List<LifeDeferredSite> group)) byMethod[method] = group = new List<LifeDeferredSite>();
                if (group.Any(s => s.SameLocation(candidate))) continue;
                candidate.Id = sites.Count;
                sites.Add(candidate); group.Add(candidate); changed.Add(method);
            }
            LifeExecutionGuard.Refresh(changed);
        }

        private static void Bind(int id, Delegate work, object source)
        {
            if (work == null) return;
            object recipient = sites[id].Owner.Read(source);
            Pawn owner = recipient as Pawn ?? (recipient is Pawn_HealthTracker health ? healthPawn(health) : null);
            if (owner != null) StarBloodRegistry.For(owner)?.BindDeferred(work, owner);
        }
    }
}
