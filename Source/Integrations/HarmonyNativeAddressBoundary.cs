using System;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyNativeAddressBoundary
    {
        internal static void Install() => InstallFor(typeof(Harmony).Assembly);
        internal static void InstallFor(Assembly assembly)
        {
            Type platform = assembly.GetType("MonoMod.Core.Platforms.PlatformTriple", true);
            NativeIlGuards.FilterArguments(AccessTools.Method(platform, "GetNativeMethodBody", new[] { typeof(MethodBase) }),
                AccessTools.Method(typeof(DynamicWriteNative), nameof(DynamicWriteNative.Publish)), 1);
        }
    }
}
