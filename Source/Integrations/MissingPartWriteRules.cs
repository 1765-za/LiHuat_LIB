using Verse;

namespace LihuatLib.Integrations
{
    internal static class MissingPartWriteRules
    {
        internal static bool CanAdd(Pawn pawn, HediffDef definition, BodyPartRecord part)
        {
            return definition?.hediffClass != null
                && typeof(Hediff_MissingPart).IsAssignableFrom(definition.hediffClass)
                && OwnsPart(pawn, part)
                && !pawn.health.hediffSet.PartIsMissing(part);
        }

        internal static bool IsRegistered(Hediff_MissingPart hediff)
        {
            // AddDirect registers the hediff before PostAdd propagates it to child parts.
            return hediff != null && OwnsPart(hediff.pawn, hediff.Part)
                && hediff.pawn.health.hediffSet.hediffs.Contains(hediff);
        }

        private static bool OwnsPart(Pawn pawn, BodyPartRecord part)
        {
            if (pawn?.health?.hediffSet == null || part?.def == null) return false;
            if (pawn.Destroyed)
            {
                // Killing a pawn marks it destroyed before its corpse is butchered.
                Corpse corpse = pawn.Corpse;
                if (corpse == null || corpse.Destroyed || !ReferenceEquals(corpse.InnerPawn, pawn)) return false;
            }
            BodyDef body = pawn.RaceProps?.body;
            return body != null && (part.body == null || ReferenceEquals(part.body, body))
                && body.AllParts.Contains(part);
        }
    }
}
