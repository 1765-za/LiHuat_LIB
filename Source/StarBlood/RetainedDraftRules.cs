using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.StarBlood
{
    internal static class RetainedDraftRules
    {
        private static readonly AccessTools.FieldRef<Pawn_DraftController, Pawn> owner =
            AccessTools.FieldRefAccess<Pawn_DraftController, Pawn>("pawn");

        internal static bool AllowsTick(Pawn_DraftController tracker)
        {
            Pawn pawn = owner(tracker);
            return pawn.Spawned || StarBloodRegistry.For(pawn)?.Subject.ContinuityEnabled != true;
        }
    }
}
