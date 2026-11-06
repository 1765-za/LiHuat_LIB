using System;
using LihuatLib.AstralArts;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class AstralSpellDescriptions
    {
        internal static string Rank(AstralSpellDef spell, int index)
        {
            AstralSpellRank rank = spell.ranks[index];
            return spell.effectKey.Translate(rank.power.ToString("0.##"), rank.range.ToString("0.##"), rank.radius.ToString("0.##"),
                rank.duration.ToString("0.##"), (rank.moveFactor - 1f).ToStringPercent(), (rank.damageFactor - 1f).ToStringPercent(),
                (1f - rank.aimFactor).ToStringPercent(), rank.reduction.ToStringPercent(), rank.armorPenetration.ToStringPercent())
                + "\n\n" + "Lihuat_AstralSpellCost".Translate(rank.cost.ToString("0.##"), rank.cooldown.ToString("0.##"));
        }
        internal static string Command(AstralEntry entry, AstralSpellDef spell)
        {
            string text = spell.node.descriptionKey.Translate() + "\n\n" + Rank(spell, entry.Skills.Rank(spell.node) - 1);
            int remaining = entry.Spells.ReadyAt(spell) - Find.TickManager.TicksGame;
            return remaining > 0 ? text + "\n" + "Lihuat_AstralSpellRemaining".Translate(Math.Ceiling(remaining / 60d)) : text;
        }
    }
}
