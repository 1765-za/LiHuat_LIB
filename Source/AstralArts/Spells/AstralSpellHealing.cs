using System;
using LihuatLib.Core;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralSpellHealing
    {
        internal static bool Injured(Pawn pawn)
        {
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                if (hediff is Hediff_Injury injury && AstralRegrowth.Treatable(injury) && injury.Severity > 0f) return true;
            return false;
        }
        internal static float Heal(Pawn pawn, float amount)
        {
            float total = 0f;
            using var medical = new OperationScope(OperationKind.Medical, pawn);
            var hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0 && amount > .0001f; i--)
                if (i < hediffs.Count && hediffs[i] is Hediff_Injury injury && AstralRegrowth.Treatable(injury))
                {
                    float before = injury.Severity;
                    if (!AstralAccount.PositiveFinite(before)) continue;
                    MedicalRestoration.Heal(injury, Math.Min(before, amount));
                    float healed = Math.Max(0f, before - injury.Severity); amount -= healed; total += healed;
                }
            return total;
        }
    }
}
