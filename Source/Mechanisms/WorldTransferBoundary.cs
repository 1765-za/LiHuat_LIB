using System;
using HarmonyLib;
using LihuatLib.Core;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class WorldTransferBoundary
    {
        internal static void Install()
        {
            NativeIlGuards.FilterArguments(AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawnOrDeselect)),
                AccessTools.Method(typeof(WorldDepartureScope), nameof(WorldDepartureScope.Complete)), 0);
            Scope(AccessTools.Method(typeof(Pawn), nameof(Pawn.ExitMap)), 0);
            Scope(AccessTools.Method(typeof(Caravan), nameof(Caravan.AddPawn)), 1);
        }

        private static void Scope(System.Reflection.MethodInfo method, int pawnArgument)
            => NativeIlGuards.Scoped(method, AccessTools.Method(typeof(WorldTransferBoundary), nameof(Begin)), new[] { pawnArgument },
                AccessTools.Method(typeof(WorldTransferBoundary), nameof(End)), Array.Empty<int>());

        private static bool Begin(Pawn pawn, out WorldDepartureScope scope)
        {
            scope = null;
            if (LihuatGame.Current?.Lifecycle.BeforeWorldDeparture(pawn) == false) return false;
            var domains = LihuatGame.Current?.Domains;
            if (domains?.AllowsDestination(pawn, null) == false
                || pawn.carryTracker?.CarriedThing is Thing carried && domains?.AllowsDestination(carried, null) == false) return false;
            scope = new WorldDepartureScope(pawn);
            return true;
        }

        private static void End(WorldDepartureScope scope) => scope?.Dispose();
    }
}
