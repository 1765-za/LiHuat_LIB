using System;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyNativeDetourBoundary
    {
        internal static void Install() => InstallFor(typeof(Harmony).Assembly);
        internal static void InstallFor(Assembly assembly)
        {
            Type platform = assembly.GetType("MonoMod.Core.Platforms.PlatformTriple", true);
            Type[] signature = { typeof(IntPtr), typeof(IntPtr), typeof(int), typeof(IntPtr) };
            foreach (string name in new[] { "CreateNativeDetour", "CreateSimpleDetour" })
                NativeIlGuards.Guard(AccessTools.Method(platform, name, signature),
                    AccessTools.Method(typeof(HarmonyNativeDetourBoundary), nameof(RequireEntry)), 1, 4);
        }

        private static void RequireEntry(IntPtr address, IntPtr writable)
        {
            ExecutableWriteProtection.RequireWrite(address, 16);
            if (writable != IntPtr.Zero && writable != address)
                ExecutableWriteProtection.RequireWrite(writable, 16);
        }
    }
}
