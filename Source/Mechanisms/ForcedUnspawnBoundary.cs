using System;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ForcedUnspawnBoundary
    {
        internal static void Install() => NativeIlGuards.Scoped(
            AccessTools.Method(typeof(Thing), nameof(Thing.ForceSetStateToUnspawned)),
            AccessTools.Method(typeof(ForcedUnspawnBoundary), nameof(Begin)), new[] { 0 },
            AccessTools.Method(typeof(ForcedUnspawnBoundary), nameof(End)), Array.Empty<int>());

        private static bool Begin(Thing thing, out Pawn state)
        {
            state = null;
            if (!(thing is Pawn pawn)) return LihuatGame.Current?.Lifecycle.BeforeThing(thing, DestructiveEntry.DeSpawn) != false;
            if (LihuatGame.Current?.Lifecycle.Before(pawn, DestructiveEntry.DeSpawn) == false) return false;
            state = pawn;
            return true;
        }
        private static void End(Pawn state)
        {
            if (state != null) LihuatGame.Current?.Lifecycle.After(state, DestructiveEntry.DeSpawn);
        }
    }
}
