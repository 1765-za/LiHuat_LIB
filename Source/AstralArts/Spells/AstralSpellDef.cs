using System;
using System.Collections.Generic;
using LihuatLib.StarPaths;
using Verse;

namespace LihuatLib.AstralArts
{
    public enum AstralSpellKind { Warp, Haste, Rally, Bolt, Nova, Lance, Barrier, Bastion, Stasis, Mend, Rejuvenate, Rain }
    public enum AstralSpellTarget { Self, Cell, Enemy, Ally }

    public sealed class AstralSpellRank
    {
        public float cost, cooldown, range, radius, duration, power, armorPenetration;
        public float moveFactor = 1f, damageFactor = 1f, aimFactor = 1f, reduction;
    }

    public sealed class AstralSpellDef : Def
    {
        public StarPathNodeDef node;
        public AstralSpellKind kind;
        public AstralSpellTarget target;
        public string iconPath, effectKey;
        public List<AstralSpellRank> ranks = new();
        public string Title => node.Title;
        public AstralSpellRank Learned(AstralEntry entry)
        {
            int rank = entry?.Skills.Rank(node) ?? 0;
            return rank > 0 ? ranks[rank - 1] : null;
        }
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (node == null || ranks.Count != node.ranks.Count || string.IsNullOrWhiteSpace(iconPath) || string.IsNullOrWhiteSpace(effectKey))
                yield return "Astral spells require a matching skill node, ranks, icon and effect key.";
            foreach (AstralSpellRank rank in ranks)
            {
                if (!float.IsFinite(rank.cost) || rank.cost <= 0f || !float.IsFinite(rank.cooldown) || rank.cooldown <= 0f)
                    yield return "Spell costs and cooldowns must be finite and positive.";
                foreach (float value in new[] { rank.range, rank.radius, rank.duration, rank.power, rank.armorPenetration })
                    if (!float.IsFinite(value) || value < 0f) yield return "Spell dimensions and power must be finite and nonnegative.";
                if (rank.range > 32f || rank.radius > 6f || rank.duration > 60f || rank.cooldown > 300f)
                    yield return "Spell dimensions and time must stay within the supported bounds.";
                if (!float.IsFinite(rank.reduction) || rank.reduction < 0f || rank.reduction >= 1f
                    || !float.IsFinite(rank.moveFactor) || rank.moveFactor < 1f
                    || !float.IsFinite(rank.damageFactor) || rank.damageFactor < 1f
                    || !float.IsFinite(rank.aimFactor) || rank.aimFactor <= 0f || rank.aimFactor > 1f)
                    yield return "Spell stat factors and reduction must be valid.";
            }
        }
    }
}
