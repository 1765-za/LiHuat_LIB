using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class WoundEvidence
    {
        [ThreadStatic] private static bool settling;
        [ThreadStatic] private static HashSet<Pawn> evaluating;
        private static readonly AccessTools.FieldRef<Hediff, float> severity = AccessTools.FieldRefAccess<Hediff, float>("severityInt");
        private static readonly Func<BodyPartDef, Pawn, float> maximum = OriginalMethods.Create<Func<BodyPartDef, Pawn, float>>(
            AccessTools.Method(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth)));

        public static bool Lethal(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.RaceProps?.body == null) return false;
            HashSet<Pawn> active = evaluating ??= new HashSet<Pawn>();
            // Patched health reads can ask the lifecycle gate about this pawn again.
            // Only the completed outer evaluation may authorize lethal removal.
            if (!active.Add(pawn)) return false;
            try { return HasWounds(pawn.health.hediffSet.hediffs) && Evaluate(pawn); }
            finally { active.Remove(pawn); }
        }

        private static bool HasWounds(List<Hediff> hediffs)
        {
            for (int i = 0; i < hediffs.Count; i++)
                if (hediffs[i] is Hediff_Injury || hediffs[i] is Hediff_MissingPart) return true;
            return false;
        }

        private static bool Evaluate(Pawn pawn)
        {
            StarBlood.StarBloodSubject subject = StarBlood.StarBloodRegistry.For(pawn)?.Subject;
            if (!settling && subject?.SettleWounds != null)
            {
                settling = true;
                try { subject.SettleWounds(); }
                finally { settling = false; }
            }
            var body = pawn.RaceProps.body;
            WoundEvidenceScratch scratch = null;
            List<Hediff_Injury> injuries = null;
            HashSet<BodyPartRecord> missing = null;
            float total = 0f;
            float lethalDamage = 0f;
            bool thresholdRead = false;
            Func<Hediff, bool> acceptsWound = subject?.AcceptsWound;
            try
            {
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff is not Hediff_Injury && hediff is not Hediff_MissingPart) continue;
                    if (acceptsWound != null && !acceptsWound(hediff)) continue;
                    if (hediff.Part == null || !body.AllParts.Contains(hediff.Part)) continue;
                    if (hediff is Hediff_MissingPart)
                    {
                        if (missing == null) missing = (scratch ??= WoundEvidenceScratch.Rent()).Missing;
                        missing.Add(hediff.Part);
                    }
                    else if (hediff is Hediff_Injury injury)
                    {
                        float amount = severity(hediff);
                        if (amount > 0f && !float.IsNaN(amount) && !float.IsInfinity(amount))
                        {
                            if (!thresholdRead)
                            {
                                lethalDamage = subject?.DamageThreshold?.Invoke() ?? 150f * pawn.HealthScale;
                                thresholdRead = true;
                            }
                            if (injuries == null) injuries = (scratch ??= WoundEvidenceScratch.Rent()).Injuries;
                            injuries.Add(injury); total += amount;
                            if (total >= lethalDamage) return true;
                        }
                    }
                }
                if (injuries == null && missing == null) return false;
                if (injuries != null) scratch.IndexInjuries();
                int firstPart = injuries?.Count == 1 ? body.AllParts.IndexOf(injuries[0].Part) : 0;
                int endPart = injuries?.Count == 1 ? firstPart + 1 : body.AllParts.Count;
                if (injuries != null) for (int partIndex = firstPart; partIndex >= 0 && partIndex < endPart; partIndex++)
                {
                    BodyPartRecord part = body.AllParts[partIndex];
                    if (missing?.Contains(part) == true) continue;
                    int first = scratch.FirstInjury(part);
                    if (first < 0) continue;
                    float health = maximum(part.def, pawn);
                    for (int index = first; index >= 0; index = scratch.FollowingInjury(part, index))
                    {
                        Hediff_Injury injury = injuries[index];
                        health = injury.destroysBodyParts ? health - severity(injury) : Mathf.Max(health - severity(injury), 1f);
                    }
                    if (part.def.destroyableByDamage && Mathf.RoundToInt(Mathf.Max(health, 0f)) == 0)
                    {
                        if (missing == null) missing = scratch.Missing;
                        missing.Add(part);
                    }
                }
                if (missing == null) return false;
                if (!Intact(body.corePart, missing)) return true;
                HashSet<BodyPartTagDef> checkedTags = null;
                foreach (BodyPartRecord part in body.AllParts)
                {
                    List<BodyPartTagDef> tags = part.def.tags;
                    if (tags == null) continue;
                    foreach (BodyPartTagDef tag in tags)
                    {
                        if (!tag.vital) continue;
                        checkedTags ??= scratch.Tags;
                        if (checkedTags.Add(tag) && !HasVitalPart(body.AllParts, tag, missing)) return true;
                    }
                }
                return false;
            }
            finally { scratch?.Dispose(); }
        }

        private static bool HasVitalPart(List<BodyPartRecord> parts, BodyPartTagDef tag, HashSet<BodyPartRecord> missing)
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].def.tags?.Contains(tag) == true && Intact(parts[i], missing)) return true;
            return false;
        }

        private static bool Intact(BodyPartRecord part, HashSet<BodyPartRecord> missing)
        {
            for (; part != null; part = part.parent) if (missing.Contains(part)) return false;
            return true;
        }
    }
}
