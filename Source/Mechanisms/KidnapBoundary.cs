using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class KidnapBoundary
    {
        private static readonly AccessTools.FieldRef<KidnappedPawnsTracker, Faction> faction =
            AccessTools.FieldRefAccess<KidnappedPawnsTracker, Faction>("faction");

        internal static void Install() => NativeIlGuards.Scoped(
            AccessTools.Method(typeof(KidnappedPawnsTracker), nameof(KidnappedPawnsTracker.Kidnap)),
            AccessTools.Method(typeof(KidnapBoundary), nameof(Begin)), new[] { 0, 1 },
            AccessTools.Method(typeof(KidnapBoundary), nameof(End)), Array.Empty<int>());

        private static bool Begin(KidnappedPawnsTracker tracker, Pawn pawn, out KidnapScope scope)
        {
            scope = null;
            if (LihuatGame.Current?.Domains.AllowsDestination(pawn, null) == false) return false;
            scope = new KidnapScope(tracker, pawn);
            return true;
        }

        private static void End(KidnapScope scope) => scope?.Dispose();

        private sealed class KidnapScope : IDisposable
        {
            private readonly KidnappedPawnsTracker tracker;
            private readonly Pawn pawn;
            private readonly Map source;
            private readonly bool alreadyHeld;
            private readonly WorldDepartureScope operation;

            internal KidnapScope(KidnappedPawnsTracker tracker, Pawn pawn)
            {
                this.tracker = tracker;
                this.pawn = pawn;
                source = pawn.MapHeld;
                alreadyHeld = tracker.KidnappedPawnsListForReading.Contains(pawn);
                operation = new WorldDepartureScope(pawn);
            }

            public void Dispose()
            {
                try
                {
                    if (!alreadyHeld && !pawn.Spawned && tracker.KidnappedPawnsListForReading.Contains(pawn)
                        && Find.WorldPawns.Contains(pawn))
                        LihuatGame.Current?.Lifecycle.Kidnapped(pawn, faction(tracker), source);
                }
                finally { operation.Dispose(); }
            }
        }
    }
}
