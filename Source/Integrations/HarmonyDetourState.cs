using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace LihuatLib.Integrations
{
    internal static class HarmonyDetourState
    {
        private static readonly Assembly harmony = typeof(Harmony).Assembly;
        private static readonly Type core = harmony.GetType("MonoMod.Core.ICoreDetourBase", true);
        private static readonly PropertyInfo applied = core.GetProperty("IsApplied");
        private static readonly Type detourBase = harmony.GetType("MonoMod.Core.Platforms.PlatformTripleDetourFactory+DetourBase", true);
        private static readonly FieldInfo box = AccessTools.DeclaredField(detourBase, "DetourBox");
        private static readonly FieldInfo current = AccessTools.DeclaredField(detourBase.GetNestedType("DetourBoxBase", BindingFlags.NonPublic), "Detour");
        private static readonly Type native = harmony.GetType("MonoMod.Core.Platforms.SimpleNativeDetour", true);
        private static readonly PropertyInfo source = native.GetProperty("Source"), destination = native.GetProperty("Destination");

        internal static bool Applied(object detour) => (bool)applied.GetValue(detour);

        internal static bool Active(object detour)
        {
            if (!Applied(detour)) return false;
            object jump = current.GetValue(box.GetValue(detour));
            if (jump == null) return false;
            IntPtr address = (IntPtr)source.GetValue(jump), target = (IntPtr)destination.GetValue(jump);
            if (Marshal.ReadByte(address) == 0xe9) return address + 5 + Marshal.ReadInt32(address, 1) == target;
            if (Marshal.ReadByte(address) == 0xff && Marshal.ReadByte(address, 1) == 0x25)
                return Marshal.ReadIntPtr(address + 6 + Marshal.ReadInt32(address, 2)) == target;
            return false;
        }
    }
}
