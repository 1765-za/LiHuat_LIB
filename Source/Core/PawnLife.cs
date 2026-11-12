using HarmonyLib;
using Verse;

namespace LihuatLib.Core
{
    public static class PawnLife
    {
        public static bool Dead(Pawn pawn) => pawn?.health != null && PawnHealthStateAccess.Read(pawn.health) == PawnHealthState.Dead;
    }
}
