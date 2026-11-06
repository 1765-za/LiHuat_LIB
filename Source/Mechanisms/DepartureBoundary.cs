using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DepartureBoundary
    {
        internal struct Scope { internal Thing Previous, Thing; internal bool Root, Allowed; internal PawnDepartureTransition Transition; }
        [ThreadStatic] private static Thing current;
        internal static void Install()
        {
            foreach (Type type in new[] { typeof(Thing), typeof(ThingWithComps), typeof(Building), typeof(Pawn) })
            {
                var method = AccessTools.DeclaredMethod(type, nameof(Thing.DeSpawn));
                if (method == null) continue;
                NativeIlGuards.Scoped(method, AccessTools.Method(typeof(DepartureBoundary), nameof(Begin)), new[] { 0 },
                    AccessTools.Method(typeof(DepartureBoundary), nameof(End)), Array.Empty<int>());
            }
            NativeIlGuards.Guard(AccessTools.Method(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch)),
                AccessTools.Method(typeof(DepartureBoundary), nameof(AllowLaunch)), 0);
        }
        private static bool Begin(Thing thing, out Scope scope)
        {
            scope = default;
            if (ReferenceEquals(current, thing)) return true;
            scope = new Scope { Previous = current, Thing = thing, Root = true };
            current = thing;
            scope.Allowed = thing is Pawn pawn ? LihuatGame.Current?.Lifecycle.Before(pawn, DestructiveEntry.DeSpawn) != false
                : LihuatGame.Current?.Lifecycle.BeforeThing(thing, DestructiveEntry.DeSpawn) != false
                    && LihuatGame.Current?.Domains.AllowsDeparture(thing) != false;
            if (scope.Allowed && thing is Pawn departing) scope.Transition = new PawnDepartureTransition(departing);
            return scope.Allowed;
        }
        private static void End(Scope scope)
        {
            if (!scope.Root) return;
            try
            {
                if (scope.Allowed) scope.Transition.Complete();
                if (scope.Allowed && scope.Thing is Pawn pawn && !pawn.Spawned)
                    LihuatGame.Current?.Lifecycle.After(pawn, DestructiveEntry.DeSpawn);
            }
            finally { current = scope.Previous; }
        }
        private static bool AllowLaunch(CompLaunchable launchable)
        {
            var group = launchable.parent.GetComp<CompTransporter>().TransportersInGroup(launchable.parent.Map);
            if (group == null) return true;
            foreach (CompTransporter transporter in group)
                if (LihuatGame.Current?.Domains.AllowsDeparture(transporter.parent) == false) return false;
            return true;
        }
    }
}
