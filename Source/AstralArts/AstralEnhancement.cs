namespace LihuatLib.AstralArts
{
    internal static class AstralEnhancement
    {
        internal static bool Advanced(AstralEntry entry) => entry.Capacity >= 1000f;
        internal static float PositiveFactor(AstralEntry entry) => (Advanced(entry) ? 1.75f : 1.5f) + entry.SkillEffects.EnhancementPower;
        internal static float IncomingFactor(AstralEntry entry) => Advanced(entry) ? .6f : .75f;
        internal static float Reduction(AstralEntry entry) => (Advanced(entry) ? .275f : entry.Capacity > 750f ? .15f : 0f)
            + entry.SkillEffects.EnhancementReduction;
        internal static float Upkeep(AstralEntry entry) => (Advanced(entry) ? entry.Profile.advancedEnhancementUpkeep : entry.Profile.enhancementUpkeep)
            * entry.SkillEffects.EnhancementUpkeep;
    }
}
