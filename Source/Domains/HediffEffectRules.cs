using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using LihuatLib.Core;
using LihuatLib.Mechanisms;
using HarmonyLib;

namespace LihuatLib.Domains
{
    public sealed class HediffEffectRules
    {
        private static readonly System.Func<object, object> clone = (System.Func<object, object>)typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(System.Func<object, object>));
        private static readonly System.Func<Hediff, HediffStage> originalStage = OriginalMethods.Create<System.Func<Hediff, HediffStage>>(AccessTools.PropertyGetter(typeof(Hediff), nameof(Hediff.CurStage)));
        private sealed class CachedStage { public Pawn Pawn; public HediffStage Stage; }
        private readonly Dictionary<Hediff, CachedStage> stages = new Dictionary<Hediff, CachedStage>();
        private readonly Dictionary<Pawn, HashSet<Hediff>> stagesByPawn = new Dictionary<Pawn, HashSet<Hediff>>(Core.ReferenceIdentity<Pawn>.Instance);
        private readonly Dictionary<Pawn, System.Func<float>> resistance = new Dictionary<Pawn, System.Func<float>>(Core.ReferenceIdentity<Pawn>.Instance);
        public void RegisterResistance(Pawn pawn, System.Func<bool> enabled) =>
            RegisterResistance(pawn, () => enabled() ? .45f : -1f);
        public void RegisterResistance(Pawn pawn, System.Func<float> remainingFactor)
        {
            LibraryCapabilities.Require(LibraryCapability.Domains);
            if (pawn != null && remainingFactor != null) resistance[pawn] = remainingFactor;
        }
        private float Resistance(Pawn pawn) => pawn != null && resistance.TryGetValue(pawn, out System.Func<float> factor)
            ? UnityEngine.Mathf.Clamp(factor(), -1f, 1f) : -1f;
        private static float Strength(Pawn pawn) => LihuatGame.Current?.Domains.PressureStrength(pawn, DomainPressure.Benefits) ?? 0f;
        public bool HasOverrides => stages.Count != 0;
        public HediffStage Read(Hediff hediff, HediffStage original) => !DeveloperExecution.Active && hediff != null && stages.TryGetValue(hediff, out CachedStage result) ? result.Stage : original;
        public void Remove(Hediff hediff)
        {
            if (hediff == null || !stages.TryGetValue(hediff, out CachedStage entry)) return;
            stages.Remove(hediff);
            if (stagesByPawn.TryGetValue(entry.Pawn, out HashSet<Hediff> owned))
            {
                owned.Remove(hediff);
                if (owned.Count == 0) stagesByPawn.Remove(entry.Pawn);
            }
        }

        public void Refresh(Pawn pawn)
        {
            if (pawn?.health == null) return;
            if (stagesByPawn.TryGetValue(pawn, out HashSet<Hediff> owned))
            {
                foreach (Hediff hediff in owned) stages.Remove(hediff);
                stagesByPawn.Remove(pawn);
            }
            LihuatGame game = LihuatGame.Current;
            float domain = Strength(pawn);
            bool resistanceRead = domain == 0f;
            float resist = resistanceRead ? Resistance(pawn) : -1f;
            if (domain == 0f && resist < 0f) return;
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                Remove(hediff);
                if (!CanAdjust(hediff, pawn, game)) continue;
                if (hediff.def.isBad && !resistanceRead)
                {
                    resist = Resistance(pawn);
                    resistanceRead = true;
                }
                ApplyStage(hediff, pawn, domain, hediff.def.isBad ? resist : -1f);
            }
        }

        public void Refresh(Hediff hediff, Pawn pawn)
        {
            if (hediff == null) { Refresh(pawn); return; }
            Remove(hediff);
            LihuatGame game = LihuatGame.Current;
            if (!CanAdjust(hediff, pawn, game)) return;
            float domain = Strength(pawn);
            float resist = hediff.def.isBad ? Resistance(pawn) : -1f;
            ApplyStage(hediff, pawn, domain, resist);
        }

        private static bool CanAdjust(Hediff hediff, Pawn pawn, LihuatGame game) => game != null && pawn != null
            && hediff != null && !(hediff is Hediff_Injury) && !(hediff is Hediff_AddedPart)
            && !(hediff is Hediff_MissingPart) && hediff.def != HediffDefOf.Anesthetic;

        private void ApplyStage(Hediff hediff, Pawn pawn, float domain, float resist)
        {
            if (domain <= 0f && resist < 0f) return;
            HediffStage original = originalStage(hediff);
            if (original == null) return;
            HediffStage copy = (HediffStage)clone(original);
            // StatWorker applies domain pressure after all stat contributions have been combined.
            copy.statOffsets = AdjustStats(original.statOffsets, false, 0f, resist);
            copy.statFactors = AdjustStats(original.statFactors, true, 0f, resist);
            copy.statOffsetsBySeverity = AdjustCurves(original.statOffsetsBySeverity, false, 0f, resist);
            copy.statFactorsBySeverity = AdjustCurves(original.statFactorsBySeverity, true, 0f, resist);
            if (original.capMods != null)
            {
                copy.capMods = new List<PawnCapacityModifier>(original.capMods.Count);
                foreach (PawnCapacityModifier modifier in original.capMods)
                {
                    PawnCapacityModifier copyModifier = (PawnCapacityModifier)clone(modifier);
                    copyModifier.offset = Adjust(modifier.offset, 0f, false, domain, resist);
                    copyModifier.postFactor = Adjust(modifier.postFactor, 1f, false, domain, resist);
                    if (copyModifier.setMax != 999f) copyModifier.setMax = Adjust(copyModifier.setMax, 1f, false, domain, resist);
                    copy.capMods.Add(copyModifier);
                }
            }
            copy.painOffset = Adjust(original.painOffset, 0f, true, domain, resist);
            copy.painFactor = Adjust(original.painFactor, 1f, true, domain, resist);
            copy.hungerRateFactor = Adjust(original.hungerRateFactor, 1f, true, domain, resist);
            copy.hungerRateFactorOffset = Adjust(original.hungerRateFactorOffset, 0f, true, domain, resist);
            copy.restFallFactor = Adjust(original.restFallFactor, 1f, true, domain, resist);
            copy.restFallFactorOffset = Adjust(original.restFallFactorOffset, 0f, true, domain, resist);
            copy.naturalHealingFactor = Adjust(original.naturalHealingFactor, 1f, false, domain, resist);
            stages.Add(hediff, new CachedStage { Pawn = pawn, Stage = copy });
            if (!stagesByPawn.TryGetValue(pawn, out HashSet<Hediff> owned)) stagesByPawn.Add(pawn, owned = new HashSet<Hediff>());
            owned.Add(hediff);
        }

        private static List<StatModifier> AdjustStats(List<StatModifier> source, bool factors, float domain, float resist)
        {
            if (source == null || domain <= 0f && resist < 0f) return source;
            var result = new List<StatModifier>(source.Count);
            float neutral = factors ? 1f : 0f;
            foreach (StatModifier modifier in source)
            {
                if (modifier?.stat == null) continue;
                result.Add(new StatModifier { stat = modifier.stat, value = Adjust(modifier.value, neutral, HigherIsHarmful(modifier.stat), domain, resist) });
            }
            return result;
        }

        private static List<StatModifierBySeverity> AdjustCurves(List<StatModifierBySeverity> source, bool factors, float domain, float resist)
        {
            if (source == null || domain <= 0f && resist < 0f) return source;
            List<StatModifierBySeverity> result = new List<StatModifierBySeverity>(source.Count);
            float neutral = factors ? 1f : 0f;
            foreach (StatModifierBySeverity modifier in source)
            {
                if (modifier?.stat == null || modifier.valueBySeverity == null) continue;
                SimpleCurve curve = new SimpleCurve();
                CurvePoint? previous = null;
                foreach (CurvePoint point in modifier.valueBySeverity.Points)
                {
                    if (previous.HasValue && (previous.Value.y - neutral) * (point.y - neutral) < 0f)
                    {
                        float fraction = (neutral - previous.Value.y) / (point.y - previous.Value.y);
                        curve.Add(new CurvePoint(previous.Value.x + (point.x - previous.Value.x) * fraction, neutral));
                    }
                    curve.Add(new CurvePoint(point.x, Adjust(point.y, neutral, HigherIsHarmful(modifier.stat), domain, resist)));
                    previous = point;
                }
                result.Add(new StatModifierBySeverity { stat = modifier.stat, valueBySeverity = curve });
            }
            return result;
        }

        public static bool HigherIsHarmful(StatDef stat)
        {
            string name = stat.defName;
            return name == "IncomingDamageFactor" || name == "ToxicSensitivity" || name == "MentalBreakThreshold"
                || name == "HungerRateMultiplier" || name == "AimingDelayFactor" || name == "MeleeCooldownFactor"
                || name == "FoodPoisonChance";
        }

        private static float Adjust(float value, float neutral, bool higherIsHarmful, float domain, float resist)
        {
            float delta = value - neutral;
            bool harmful = higherIsHarmful ? delta > 0f : delta < 0f;
            if (domain > 0f && !harmful) return neutral + delta * (1f - domain);
            if (resist >= 0f && harmful) return neutral + delta * resist;
            return value;
        }
    }
}
