using System;
using System.Collections;
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

        internal static void Replace(MethodBase method, byte[] body, out IntPtr previous, out long expected)
        {
            var registry = (IDictionary)detours.GetValue(null);
            lock (registry)
            {
                object existing = registry[method];
                if (existing != null) undo.Invoke(existing, null);
                try
                {
                    previous = CoreClrEntryAddress.Code(method);
                    expected = Marshal.ReadInt64(previous);
                    RuntimeMethodBodies.Replace(method, body);
                }
                catch
                {
                    if (existing != null) apply.Invoke(existing, null);
                    throw;
                }
                if (existing == null) return;
                registry.Remove(method);
                ((IDisposable)existing).Dispose();
            }
        }
    }
}
