using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyPatchOwnership
    {
        private static readonly Type patchTools = typeof(Harmony).Assembly.GetType("HarmonyLib.PatchTools", true);
        private static readonly FieldInfo detours = patchTools.GetField("detours", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly Type coreDetour = typeof(Harmony).Assembly.GetType("MonoMod.Core.ICoreDetourBase", true);
        private static readonly MethodInfo undo = coreDetour.GetMethod("Undo"), apply = coreDetour.GetMethod("Apply");

        internal static MethodBase ActiveView(MethodBase[] views)
        {
            if (views.Length == 1) return views[0];
            var registry = (IDictionary)detours.GetValue(null);
            lock (registry)
            {
                MethodBase[] active = views.Where(view => registry[view] is object detour && HarmonyDetourState.Active(detour)).ToArray();
                if (active.Length != 1) throw new InvalidOperationException("Inherited Harmony views do not identify one active wrapper: " + views[0]);
                return active[0];
            }
        }

        internal static void Replace(MethodBase method, byte[] body, out IntPtr previous, out long expected)
        {
            MethodBase[] views = HarmonyRegistrations.TargetViews(method);
            var registry = (IDictionary)detours.GetValue(null);
            lock (registry)
            {
                var existing = views.Select(view => (View: view, Detour: registry[view]))
                    .Where(entry => entry.Detour != null).ToArray();
                var applied = existing.Where(entry => HarmonyDetourState.Applied(entry.Detour))
                    .OrderBy(entry => HarmonyDetourState.Active(entry.Detour)).ToArray();
                int detached = 0;
                try
                {
                    for (; detached < applied.Length; detached++) undo.Invoke(applied[detached].Detour, null);
                    previous = CoreClrEntryAddress.Code(method);
                    expected = Marshal.ReadInt64(previous);
                    RuntimeMethodBodies.Replace(method, body);
                }
                catch (Exception failure)
                {
                    try { for (int i = 0; i < detached; i++) apply.Invoke(applied[i].Detour, null); }
                    catch (Exception rollback) { throw new AggregateException("Harmony handoff and rollback failed.", failure, rollback); }
                    throw;
                }
                foreach (var entry in existing)
                {
                    registry.Remove(entry.View);
                    ((IDisposable)entry.Detour).Dispose();
                }
            }
        }

        internal static void Release(MethodBase method)
        {
            MethodBase[] views = HarmonyRegistrations.TargetViews(method);
            var registry = (IDictionary)detours.GetValue(null);
            lock (registry)
            {
                foreach (MethodBase view in views)
                {
                    object detour = registry[view];
                    if (detour == null) continue;
                    if (HarmonyDetourState.Applied(detour)) undo.Invoke(detour, null);
                    registry.Remove(view);
                    ((IDisposable)detour).Dispose();
                }
            }
        }
    }
}
