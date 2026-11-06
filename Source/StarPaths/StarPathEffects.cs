using System;
using System.Collections.Generic;
using RimWorld;
using LihuatLib.Configuration;

namespace LihuatLib.StarPaths
{
    internal readonly struct StarPathStatEffect
    {
        internal readonly float Offset, Factor;
        internal readonly bool Equipment;
        internal StarPathStatEffect(float offset, float factor, bool equipment) { Offset = offset; Factor = factor; Equipment = equipment; }
        internal float Apply(float value) => value * Factor + Offset;
    }

    internal sealed class StarPathEffects
    {
        internal static readonly StarPathEffects Empty = new StarPathEffects();
        internal readonly Dictionary<StatDef, StarPathStatEffect> Stats = new Dictionary<StatDef, StarPathStatEffect>();
        internal readonly float Advanced, Specialized, True, GlobalReduction;
        internal readonly float AllocatedConversion;
        internal readonly float AdvancedIncoming = 1f, SpecializedIncoming = 1f;
        internal readonly float PenetrationFactor = 1f;
        internal readonly bool GuaranteedHit;
        internal float Converted => Advanced + Specialized + True;
        private StarPathEffects() { }
        internal StarPathEffects(StarPathAccount account)
        {
            float globalFactor = 1f;
            foreach (StarPathInvestment investment in account.Investments)
            {
                StarPathRank rank = investment.Node.ranks[investment.Rank - 1];
                Advanced += rank.advanced; Specialized += rank.specialized; True += rank.trueDamage;
                globalFactor *= 1f - rank.globalReduction;
                AdvancedIncoming *= rank.advancedIncoming; SpecializedIncoming *= rank.specializedIncoming;
                PenetrationFactor *= 1f + rank.penetration;
                if (rank.guaranteedHit) GuaranteedHit = true;
                foreach (StarPathStat modifier in rank.stats)
                {
                    Stats.TryGetValue(modifier.stat, out StarPathStatEffect old);
                    float factor = old.Factor == 0f ? modifier.factor : old.Factor * modifier.factor;
                    Stats[modifier.stat] = new StarPathStatEffect(old.Offset + modifier.offset, factor, modifier.equipment);
                }
            }
            GlobalReduction = 1f - globalFactor;
            AllocatedConversion = Converted;
            if (AllocatedConversion > 1.000001f) throw new InvalidOperationException("Star path conversion exceeds the original damage budget.");
            MechanismSettings mechanics = LibraryMod.Mechanics;
            if (!mechanics.Allows(GameplayMechanic.TrueDamage)) True = 0f;
        }
    }

    public readonly struct StarPathDamageSplit
    {
        public readonly float Ordinary, Advanced, Specialized, True;
        internal StarPathDamageSplit(float amount, float advanced, float specialized, float trueDamage)
        {
            Advanced = amount * advanced; Specialized = amount * specialized; True = amount * trueDamage;
            Ordinary = Math.Max(0f, amount - Advanced - Specialized - True);
        }
        public float Total => Ordinary + Advanced + Specialized + True;
    }
}
