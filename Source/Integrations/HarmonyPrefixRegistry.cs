using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace LihuatLib.Integrations
{
    internal static class HarmonyPrefixRegistry
    {
        private static readonly object sync = new();
        private static readonly Dictionary<MethodBase, HashSet<MethodInfo>> targets = new();
        private static readonly ConcurrentDictionary<MethodInfo, int> prefixes = new();

        internal static bool Contains(MethodInfo method) => prefixes.ContainsKey(method);

        internal static void Initialize()
        {
            foreach (MethodBase target in Harmony.GetAllPatchedMethods())
            {
                HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
                if (info != null) Record(target, info.Prefixes);
            }
        }

        internal static void Published(MethodBase target, PatchInfo info) => Record(target, info.prefixes);

        private static void Record(MethodBase target, IEnumerable<Patch> patches)
        {
            lock (sync)
            {
                var next = new HashSet<MethodInfo>();
                foreach (Patch patch in patches)
                {
                    MethodInfo method = patch.PatchMethod;
                    if (method.ReturnType == typeof(bool)) next.Add(method);
                }
                if (targets.TryGetValue(target, out HashSet<MethodInfo> previous))
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
                if (next.Count == 0) targets.Remove(target);
                else targets[target] = next;
            }
        }
    }
}
