using HarmonyLib;
using Verse;

namespace LihuatLib.Core
{
    public static class PawnLife
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, PawnHealthState> state =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, PawnHealthState>("healthState");
        public static bool Dead(Pawn pawn) => pawn?.health != null && state(pawn.health) == PawnHealthState.Dead;
    }
}
