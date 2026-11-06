using System.Text;
using System;
using LihuatLib.StarPaths;
using LihuatLib.Configuration;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class StarPathDescription
    {
        internal static string Effects(StarPathRank rank)
        {
            var text = new StringBuilder();
            Share(text, "Lihuat_StarPathAdvanced", rank.advanced);
            Share(text, "Lihuat_StarPathSpecialized", rank.specialized);
            Share(text, "Lihuat_StarPathTrue", rank.trueDamage, GameplayMechanic.TrueDamage);
            Share(text, "Lihuat_StarPathGlobal", rank.globalReduction);
            Share(text, "Lihuat_StarPathPenetration", rank.penetration);
            if (rank.guaranteedHit) text.Append("Lihuat_StarPathGuaranteedHit".Translate())
                .AppendLine(Status(GameplayMechanic.GuaranteedHit));
            if (rank.advancedIncoming != 1f) text.AppendLine("Lihuat_StarPathAdvancedIncoming".Translate(rank.advancedIncoming.ToStringPercent()));
            if (rank.specializedIncoming != 1f) text.AppendLine("Lihuat_StarPathSpecializedIncoming".Translate(rank.specializedIncoming.ToStringPercent()));
            foreach (StarPathStat modifier in rank.stats)
            {
                string value = modifier.offset != 0f ? "Lihuat_StarPathPercentagePoints".Translate(
                    (modifier.offset > 0f ? "+" : "") + (modifier.offset * 100f).ToString("0.##")).ToString()
                    : (modifier.factor - 1f >= 0f ? "+" : "") + (modifier.factor - 1f).ToStringPercent();
                text.AppendLine(modifier.stat.LabelCap + "  " + value);
            }
            return text.ToString().TrimEnd();
        }
        private static void Share(StringBuilder text, string key, float fraction, GameplayMechanic mechanic = GameplayMechanic.None)
        { if (fraction > 0f) text.Append(key.Translate(fraction.ToStringPercent())).AppendLine(Status(mechanic)); }

        private static string Status(GameplayMechanic mechanic) => LibraryMod.Mechanics.Allows(mechanic)
            ? string.Empty : "Lihuat_MechanismInactive".Translate().ToString();

        internal static string Detail(StarPathAccount account, StarPathNodeDef node, Func<StarPathNodeDef, int, string> effects)
        {
            var text = new StringBuilder(node.descriptionKey.Translate().ToString());
            int rank = account.Rank(node);
            if (rank > 0) text.Append("\n\n").AppendLine("Lihuat_StarPathCurrent".Translate()).Append(effects(node, rank - 1));
            if (rank < node.ranks.Count)
            {
                StarPathRank next = node.ranks[rank];
                text.Append("\n\n").AppendLine("Lihuat_StarPathNext".Translate()).Append(effects(node, rank));
                text.Append("\n\n").Append("Lihuat_StarPathLevelRequirement".Translate(next.level));
                foreach (StarPathRequirement requirement in node.requirements)
                    text.Append('\n').Append("Lihuat_StarPathPrerequisite".Translate(requirement.node.Title, requirement.rank));
            }
            return text.ToString();
        }
    }
}
