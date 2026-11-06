using System;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class MedicalRestoration
    {
        internal static readonly Action<Hediff_Injury, float> Heal = OriginalMethods.Create<Action<Hediff_Injury, float>>(
            AccessTools.Method(typeof(Hediff_Injury), nameof(Hediff_Injury.Heal)));
        private static readonly Action<Pawn_HealthTracker, BodyPartRecord, Hediff, bool> restore =
            OriginalMethods.Create<Action<Pawn_HealthTracker, BodyPartRecord, Hediff, bool>>(
                AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RestorePart)));

        internal static void RestorePart(Pawn pawn, BodyPartRecord part) => restore(pawn.health, part, null, true);
    }
}
