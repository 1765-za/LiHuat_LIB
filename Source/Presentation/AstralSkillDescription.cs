using System.Text;
using LihuatLib.AstralArts;
using LihuatLib.StarPaths;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class AstralSkillDescription
    {
        internal static string Effects(StarPathNodeDef node, int rank)
        {
            AstralSkillNode skill = AstralSkillDefs.Lihuat_AstralSkills.Find(node);
            if (skill.spell != null) return AstralSpellDescriptions.Rank(skill.spell, rank);
            AstralSkillEffect effect = skill.effects[rank];
            var text = new StringBuilder();
            Bonus(text, "Capacity", effect.capacity); Bonus(text, "Recovery", effect.recovery);
            Bonus(text, "Meditation", effect.meditation); Bonus(text, "Repair", effect.repair);
            Bonus(text, "Power", effect.enhancementPower); Bonus(text, "Ward", effect.enhancementReduction);
            Bonus(text, "Bulwark", effect.isolationReduction); Bonus(text, "Echo", effect.healingEcho);
            Bonus(text, "RepairCost", 1f - effect.repairCost); Bonus(text, "RegrowthCost", 1f - effect.regrowthCost);
            Bonus(text, "HealingCost", 1f - effect.healingCost); Bonus(text, "EnhanceCost", 1f - effect.enhancementUpkeep);
            Bonus(text, "IsolationCost", 1f - effect.isolationCost); Bonus(text, "IsolationCap", 1f - effect.isolationCap);
            if (effect.healingRange > 0) text.AppendLine("Lihuat_AstralSkillRange".Translate(effect.healingRange));
            return text.ToString().TrimEnd();
        }
        private static void Bonus(StringBuilder text, string key, float value)
        {
            if (value <= 0f) return;
            string amount = key == "Power" || key == "Ward" || key == "Bulwark" ? (value * 100f).ToString("0.##") : value.ToStringPercent();
            text.AppendLine(("Lihuat_AstralSkill" + key).Translate(amount));
        }
    }
}
