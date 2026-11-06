using System;
using HarmonyLib;
using LihuatLib.Core;
using LihuatLib.Domains;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ThingRemovalBoundary
    {
        internal static void Install()
        {
            foreach (Type type in new[] { typeof(Thing), typeof(ThingWithComps), typeof(Building), typeof(Building_DomainShell) })
            {
                Register(type, nameof(Thing.Kill), DestructiveEntry.Kill);
                Register(type, nameof(Thing.Destroy), DestructiveEntry.Destroy);
            }
        }

        private static void Register(Type type, string name, DestructiveEntry entry)
        {
            var method = AccessTools.DeclaredMethod(type, name);
            if (method != null) NativeIlGuards.GuardTagged(method,
                AccessTools.Method(typeof(ThingRemovalBoundary), nameof(Allowed)), (int)entry, 0);
        }

        private static bool Allowed(int entry, Thing thing) => thing is Pawn
            || LihuatGame.Current?.Lifecycle.BeforeThing(thing, (DestructiveEntry)entry) != false;
    }
}
