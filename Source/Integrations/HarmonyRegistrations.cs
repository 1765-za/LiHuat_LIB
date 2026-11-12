using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyRegistrations
    {
        private sealed class Registration
        {
            internal MethodBase TargetView;
            internal HashSet<MethodInfo> Prefixes;
            internal readonly HashSet<MethodBase> Views = new();
        }
        private static readonly object sync = new();
        private static readonly Dictionary<MethodBase, Registration> targets = new();
        private static readonly ConcurrentDictionary<MethodInfo, int> prefixes = new();

        internal static bool Contains(MethodInfo method) => prefixes.ContainsKey(MethodIdentity.Declaration(method));
        internal static MethodBase TargetView(MethodBase method)
        {
            lock (sync) return targets.TryGetValue(MethodIdentity.Declaration(method), out Registration registration)
                ? registration.TargetView : method;
        }
        internal static MethodBase[] TargetViews(MethodBase method)
        {
            lock (sync) return targets.TryGetValue(MethodIdentity.Declaration(method), out Registration registration)
                ? registration.Views.ToArray() : new[] { method };
        }
        internal static HarmonyLib.Patches ReadPatches(MethodBase method) => Harmony.GetPatchInfo(TargetView(method));

        internal static void Initialize()
        {
            foreach (var group in Harmony.GetAllPatchedMethods().GroupBy(MethodIdentity.Declaration))
            {
                MethodBase[] views = group.ToArray();
                MethodBase target = HarmonyPatchOwnership.ActiveView(views);
                HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                Record(target, info.Prefixes);
                lock (sync) targets[group.Key].Views.UnionWith(views);
            }
        }

        internal static void Published(MethodBase target, PatchInfo info) => Record(target, info.prefixes);

        private static void Record(MethodBase target, IEnumerable<Patch> patches)
        {
            MethodBase key = MethodIdentity.Declaration(target);
            lock (sync)
            {
                var next = new HashSet<MethodInfo>();
                foreach (Patch patch in patches)
                {
                    MethodInfo method = MethodIdentity.Declaration(patch.PatchMethod);
                    if (method.ReturnType == typeof(bool)) next.Add(method);
                }
                if (!targets.TryGetValue(key, out Registration registration)) targets.Add(key, registration = new Registration());
                registration.TargetView = target;
                registration.Views.Add(target);
                HashSet<MethodInfo> previous = registration.Prefixes;
                if (previous != null)
                {
                    if (previous.SetEquals(next)) return;
                    foreach (MethodInfo method in previous)
                    {
                        if (next.Contains(method)) continue;
                        int remaining = prefixes[method] - 1;
                        if (remaining == 0) prefixes.TryRemove(method, out _);
                        else prefixes[method] = remaining;
                    }
                }
                foreach (MethodInfo method in next)
                    if (previous == null || !previous.Contains(method))
                        prefixes.AddOrUpdate(method, 1, static (_, count) => count + 1);
                registration.Prefixes = next;
            }
        }
    }
}
