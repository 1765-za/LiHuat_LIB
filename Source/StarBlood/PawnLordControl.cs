using LihuatLib.Mechanisms;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace LihuatLib.StarBlood
{
    internal static class PawnLordControl
    {
        internal static bool Allows(Pawn pawn, Lord lord)
        {
            if (pawn == null || lord?.faction == null || lord.faction == pawn.Faction
                || pawn.Faction != Faction.OfPlayer || DeveloperExecution.Allows(pawn)) return true;
            return StarBloodRegistry.For(pawn)?.Subject.ContinuityEnabled != true;
        }

        internal static bool BeforeWorldDeparture(Pawn pawn)
        {
            if (Allows(pawn, pawn?.lord)) return true;
            Reconcile(pawn);
            return false;
        }

        internal static void Reconcile(Pawn pawn)
        {
            Lord lord = pawn?.lord;
            if (Allows(pawn, lord)) return;
            lord.RemovePawn(pawn);
            if (pawn.CurJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
    }
}
