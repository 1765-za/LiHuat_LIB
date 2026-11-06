using System;
using System.Linq;
using Verse;
using Verse.AI;

namespace LihuatLib.AstralArts
{
    public static class AstralHealing
    {
        public const int SelectionRange = 5, CastRange = 4;
        public static float Cost(AstralEntry entry) => 2f * entry.Profile.RepairCost(entry.Strength) * entry.SkillEffects.HealingCost;
        public static int SelectionRadius(AstralEntry entry) => SelectionRange + (entry?.SkillEffects.HealingRange ?? 0);
        public static int CastRadius(AstralEntry entry) => CastRange + (entry?.SkillEffects.HealingRange ?? 0);
        public static bool Within(IntVec3 from, IntVec3 to, int range)
            => Math.Abs(from.x - to.x) <= range && Math.Abs(from.z - to.z) <= range;

        public static bool CanBegin(AstralEntry entry) => entry != null && entry.Allows(AstralArt.Heal)
            && LihuatGame.Current?.AstralArts.Available(entry) == true && entry.Pawn.Spawned
            && !entry.Pawn.Downed && !entry.Pawn.InMentalState && entry.Pawn.jobs != null && entry.Account.AvailableEnergy > .0001f;

        public static bool CanSelect(AstralEntry entry, Pawn patient) => CanBegin(entry) && patient != null
            && patient != entry.Pawn && patient.Spawned && !patient.Dead && !patient.Destroyed
            && patient.Map == entry.Pawn.Map && patient.health?.hediffSet != null
            && Within(entry.Pawn.Position, patient.Position, SelectionRadius(entry))
            && !patient.Position.Fogged(patient.Map) && HasTreatableDamage(patient);

        private static bool HasTreatableDamage(Pawn patient)
        {
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
                if (hediff is Hediff_Injury injury && AstralRegrowth.Treatable(injury)
                    || hediff is Hediff_MissingPart && AstralRegrowth.CanRegrow(patient, hediff.Part)) return true;
            return false;
        }

        public static bool CanCastFrom(Pawn caster, Pawn patient, IntVec3 cell)
            => caster?.Map != null && patient?.Spawned == true && patient.Map == caster.Map
                && cell.InBounds(caster.Map) && Within(cell, patient.Position, CastRadius(LihuatGame.Current?.AstralArts.Find(caster)))
                && GenSight.LineOfSight(cell, patient.Position, caster.Map);

        internal static bool TryFindCastCell(AstralEntry entry, Pawn patient, out IntVec3 cell)
        {
            Pawn caster = entry.Pawn;
            cell = IntVec3.Invalid;
            if (!CanSelect(entry, patient)) return false;
            if (CanCastFrom(caster, patient, caster.Position)) { cell = caster.Position; return true; }
            int radius = CastRadius(entry);
            var candidates = CellRect.CenteredOn(patient.Position, radius * 2 + 1, radius * 2 + 1)
                .Where(c => c.InBounds(caster.Map) && c.Standable(caster.Map) && CanCastFrom(caster, patient, c))
                .OrderBy(c => c.DistanceToSquared(caster.Position));
            foreach (IntVec3 candidate in candidates)
                if (caster.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly)) { cell = candidate; return true; }
            return false;
        }

        public static bool Order(AstralEntry entry, Pawn patient, int budget)
        {
            if (!CanSelect(entry, patient) || budget < 1 || budget > entry.Capacity || !TryFindCastCell(entry, patient, out _)) return false;
            Job job = JobMaker.MakeJob(AstralArtsDefs.Lihuat_AstralHeal, patient);
            job.count = budget;
            return entry.Pawn.jobs.TryTakeOrderedJob(job);
        }

        public static float Cast(AstralEntry entry, Pawn patient, int budget)
        {
            if (!CanSelect(entry, patient) || budget < 1 || budget > entry.Capacity
                || !CanCastFrom(entry.Pawn, patient, entry.Pawn.Position)) return 0f;
            float cost = Cost(entry);
            float healed = AstralRepair.Heal(entry, patient, budget / cost, cost, AstralRegrowth.Treatable);
            float total = healed + AstralRegrowth.Apply(entry, patient, Math.Max(0f, budget - healed * cost), cost);
            if (entry.SkillEffects.HealingEcho > 0f) Echo(entry.Pawn, total * entry.SkillEffects.HealingEcho);
            return total;
        }
        private static void Echo(Pawn pawn, float amount)
        {
            using var medical = new Core.OperationScope(Core.OperationKind.Medical, pawn);
            var injuries = pawn.health.hediffSet.hediffs;
            for (int i = injuries.Count - 1; i >= 0 && amount > .0001f; i--)
                if (i < injuries.Count && injuries[i] is Hediff_Injury injury)
                {
                    float before = injury.Severity;
                    if (!AstralAccount.PositiveFinite(before)) continue;
                    Mechanisms.MedicalRestoration.Heal(injury, Math.Min(before, amount));
                    amount -= Math.Max(0f, before - injury.Severity);
                }
        }
    }
}
