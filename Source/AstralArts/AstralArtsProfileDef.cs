using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LihuatLib.AstralArts
{
    public sealed class AstralArtsProfileDef : Def
    {
        public float startingCapacity = 100f, masteredCapacity = 1000f;
        public float startingRecoveryFraction = .01f, masteredRecoveryFraction = .05f, restingRecoveryFactor = 3f;
        public float startingRepair = 20f, masteredRepair = 600f;
        public float startingRepairCost = .5f, masteredRepairCost = 1.5f;
        public float isolationUpkeep = 1.5f, ordinaryDamageCost = .08f, minimumBlockCost = 1f;
        public float enhancementUpkeep = 20f, advancedEnhancementUpkeep = 60f;
        public float Capacity(float strength) => Blend(startingCapacity, masteredCapacity, strength);
        public float RecoveryFraction(float strength) => Blend(startingRecoveryFraction, masteredRecoveryFraction, strength);
        public float Recovery(float strength, bool resting) => Capacity(strength) * RecoveryFraction(strength) * (resting ? restingRecoveryFactor : 1f);
        public float Repair(float strength) => Blend(startingRepair, masteredRepair, strength);
        public float RepairCost(float strength) => Blend(startingRepairCost, masteredRepairCost, strength);
        private static float Blend(float start, float end, float strength) => start + (end - start) * strength;
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            foreach (float value in new[] { startingCapacity, masteredCapacity, startingRecoveryFraction, masteredRecoveryFraction,
                restingRecoveryFactor, startingRepair, masteredRepair, startingRepairCost, masteredRepairCost,
                isolationUpkeep, ordinaryDamageCost, minimumBlockCost, enhancementUpkeep, advancedEnhancementUpkeep })
                if (!AstralAccount.PositiveFinite(value)) { yield return "Astral parameters must be finite and positive."; break; }
            if (masteredCapacity < startingCapacity || masteredRecoveryFraction < startingRecoveryFraction || masteredRepair < startingRepair)
                yield return "Astral strength must improve capacity, recovery and healing.";
            if (masteredRecoveryFraction > 1f) yield return "Astral recovery fraction cannot exceed one.";
        }
    }

    [DefOf]
    public static class AstralArtsDefs
    {
        public static AstralArtsProfileDef Lihuat_AstralArts;
        public static JobDef Lihuat_AstralHeal;
        static AstralArtsDefs() { DefOfHelper.EnsureInitializedInCtor(typeof(AstralArtsDefs)); }
    }
}
