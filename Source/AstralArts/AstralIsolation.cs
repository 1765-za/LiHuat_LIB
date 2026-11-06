using System;

namespace LihuatLib.AstralArts
{
    internal static class AstralIsolation
    {
        internal const float HitCapacityFraction = .15f;
        internal static float DamageCost(AstralEntry entry) => entry.Profile.ordinaryDamageCost * entry.SkillEffects.IsolationCost / entry.Account.Multiplier;
        internal static float MinimumCost(AstralEntry entry) => entry.Profile.minimumBlockCost * entry.SkillEffects.IsolationCost / entry.Account.Multiplier;
        internal static float MaximumCost(AstralEntry entry) => entry.Capacity * HitCapacityFraction * entry.SkillEffects.IsolationCap;
        internal static float BlockCost(AstralEntry entry, float damage)
            => Math.Min(MaximumCost(entry), Math.Max(MinimumCost(entry), damage * DamageCost(entry)));
    }
}
