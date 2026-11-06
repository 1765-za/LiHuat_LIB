using System;
using System.Linq;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralRegrowth
    {
        internal static bool Treatable(Hediff_Injury injury)
            => AstralAccount.PositiveFinite(injury.Severity) && !BrainOrDescendant(injury.Part);

        private static bool IsBrain(BodyPartRecord part)
            => part.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource);

        private static bool BrainOrDescendant(BodyPartRecord part)
        {
            for (; part != null; part = part.parent) if (IsBrain(part)) return true;
            return false;
        }

        private static bool ContainsBrain(BodyPartRecord part)
        {
            if (IsBrain(part)) return true;
            foreach (BodyPartRecord child in part.parts) if (ContainsBrain(child)) return true;
            return false;
        }

        internal static bool CanRegrow(Pawn patient, BodyPartRecord part)
        {
            if (part?.parent == null || BrainOrDescendant(part) || ContainsBrain(part)
                || patient.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(part)) return false;
            for (BodyPartRecord parent = part.parent; parent != null; parent = parent.parent)
                if (patient.health.hediffSet.PartIsMissing(parent)) return false;
            return patient.health.hediffSet.PartIsMissing(part);
        }

        private static float FullHealth(Pawn patient, BodyPartRecord part)
        {
            float result = part.def.GetMaxHealth(patient);
            foreach (BodyPartRecord child in part.parts) result += FullHealth(patient, child);
            return result;
        }

        private static float PresentHealth(Pawn patient, BodyPartRecord part)
        {
            if (patient.health.hediffSet.PartIsMissing(part)) return 0f;
            float result = Math.Max(0f, Math.Min(part.def.GetMaxHealth(patient), patient.health.hediffSet.GetPartHealth(part)));
            foreach (BodyPartRecord child in part.parts) result += PresentHealth(patient, child);
            return result;
        }

        internal static float Apply(AstralEntry payer, Pawn patient, float budget, float cost)
        {
            if (!AstralAccount.PositiveFinite(budget) || patient.Dead || patient.Destroyed) return 0f;
            cost *= payer.SkillEffects.RegrowthCost;
            var missing = patient.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().ToArray();
            float total = 0f;
            foreach (Hediff_MissingPart hediff in missing)
            {
                BodyPartRecord part = hediff.Part;
                if (patient.Dead || patient.Destroyed) break;
                if (!CanRegrow(patient, part)) continue;
                float value = FullHealth(patient, part), required = value * cost;
                if (!AstralAccount.PositiveFinite(required) || required > budget || !payer.Account.TryReserve(required, out var payment)) continue;
                try { MedicalRestoration.RestorePart(patient, part); }
                finally
                {
                    float restored = Math.Min(value, PresentHealth(patient, part)), spent = restored * cost;
                    using (payment) payment.Commit(spent);
                    budget -= spent;
                    total += restored;
                }
            }
            return total;
        }
    }
}
