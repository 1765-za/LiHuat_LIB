using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Domains
{
    public static class DomainStatRules
    {
        public static void Apply(StatDef stat, StatRequest req, ref float value)
        {
            if (DeveloperExecution.Active) return;
            Pawn pawn = req.Thing as Pawn;
            DomainCoordinator domains = LihuatGame.Current?.Domains;
            if (pawn == null || domains == null) return;
            float strength = domains.PressureStrength(pawn, stat == StatDefOf.IncomingDamageFactor ? DomainPressure.Resistance : DomainPressure.Benefits);
            if (strength <= 0f) return;
            if (stat == StatDefOf.IncomingDamageFactor) { value = Mathf.Lerp(value, 1f, strength); return; }
            float baseline = stat.Worker.GetBaseValueFor(req);
            bool benefit = HediffEffectRules.HigherIsHarmful(stat) ? value < baseline : value > baseline;
            if (benefit) value = Mathf.Lerp(value, baseline, strength);
        }
        public static void HealthScale(Pawn pawn, ref float value)
        {
            float strength = LihuatGame.Current?.Domains.PressureStrength(pawn, DomainPressure.Benefits) ?? 0f;
            if (strength <= 0f) return;
            float baseline = pawn.ageTracker.CurLifeStage.healthScaleFactor * pawn.RaceProps.baseHealthScale;
            if (value > baseline) value = Mathf.Lerp(value, baseline, strength);
        }
    }
}
