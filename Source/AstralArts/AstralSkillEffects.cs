using System.Collections.Generic;
using LihuatLib.StarPaths;

namespace LihuatLib.AstralArts
{
    internal sealed class AstralSkillEffects
    {
        internal static readonly AstralSkillEffects Empty = new();
        internal readonly float Capacity, Recovery, Meditation, Repair, EnhancementPower, EnhancementReduction, IsolationReduction, HealingEcho;
        internal readonly float RepairCost = 1f, RegrowthCost = 1f, HealingCost = 1f, EnhancementUpkeep = 1f, IsolationCost = 1f, IsolationCap = 1f;
        internal readonly int HealingRange;
        internal readonly AstralSpellDef[] Spells = System.Array.Empty<AstralSpellDef>();
        private AstralSkillEffects() { }
        internal AstralSkillEffects(StarPathAccount account, AstralSkillTreeDef tree)
        {
            var spells = new List<AstralSpellDef>();
            foreach (StarPathInvestment investment in account.Investments)
            {
                AstralSkillNode skill = tree.Find(investment.Node);
                if (skill.spell != null) { spells.Add(skill.spell); continue; }
                AstralSkillEffect effect = skill.effects[investment.Rank - 1];
                Capacity += effect.capacity; Recovery += effect.recovery; Meditation += effect.meditation; Repair += effect.repair;
                EnhancementPower += effect.enhancementPower; EnhancementReduction += effect.enhancementReduction;
                IsolationReduction += effect.isolationReduction; HealingEcho += effect.healingEcho; HealingRange += effect.healingRange;
                RepairCost *= effect.repairCost; RegrowthCost *= effect.regrowthCost; HealingCost *= effect.healingCost;
                EnhancementUpkeep *= effect.enhancementUpkeep; IsolationCost *= effect.isolationCost; IsolationCap *= effect.isolationCap;
            }
            spells.Sort((a, b) => a.node.row != b.node.row ? a.node.row.CompareTo(b.node.row) : a.node.column.CompareTo(b.node.column));
            Spells = spells.ToArray();
        }
    }
}
