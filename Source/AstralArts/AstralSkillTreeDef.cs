using System;
using System.Collections.Generic;
using LihuatLib.StarPaths;
using Verse;

namespace LihuatLib.AstralArts
{
    public sealed class AstralSkillEffect
    {
        public float capacity, recovery, meditation, repair, enhancementPower, enhancementReduction, isolationReduction, healingEcho;
        public float repairCost = 1f, regrowthCost = 1f, healingCost = 1f, enhancementUpkeep = 1f, isolationCost = 1f, isolationCap = 1f;
        public int healingRange;
    }

    public sealed class AstralSkillNode
    {
        public StarPathNodeDef node;
        public AstralArt art;
        public AstralSpellDef spell;
        public List<AstralSkillEffect> effects = new();
    }

    public sealed class AstralSkillTreeDef : Def
    {
        public StarPathTreeDef layout;
        public List<AstralSkillNode> skills = new();
        private readonly Dictionary<StarPathNodeDef, AstralSkillNode> byNode = new();
        public override void ResolveReferences()
        {
            base.ResolveReferences(); byNode.Clear();
            foreach (AstralSkillNode skill in skills) if (skill.node != null) byNode.Add(skill.node, skill);
        }
        internal AstralSkillNode Find(StarPathNodeDef node) => node != null && byNode.TryGetValue(node, out AstralSkillNode skill) ? skill : null;
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (layout == null) { yield return "Astral skills require a tree layout."; yield break; }
            var seen = new HashSet<StarPathNodeDef>();
            foreach (AstralSkillNode skill in skills)
            {
                if (skill.node == null || !seen.Add(skill.node) || !layout.nodes.Contains(skill.node)
                    || (skill.spell == null ? skill.effects.Count != skill.node.ranks.Count
                        : skill.spell.node != skill.node || skill.effects.Count != 0)) yield return "Astral skill nodes and effect ranks must match the layout.";
                foreach (AstralSkillEffect effect in skill.effects)
                {
                    foreach (float value in new[] { effect.capacity, effect.recovery, effect.meditation, effect.repair, effect.enhancementPower,
                        effect.enhancementReduction, effect.isolationReduction, effect.healingEcho })
                        if (!float.IsFinite(value) || value < 0f || value > 1f) yield return "Astral skill bonuses must be finite fractions.";
                    foreach (float value in new[] { effect.repairCost, effect.regrowthCost, effect.healingCost,
                        effect.enhancementUpkeep, effect.isolationCost, effect.isolationCap })
                        if (!float.IsFinite(value) || value <= 0f || value > 1f) yield return "Astral skill cost factors must be positive fractions.";
                    if (effect.healingRange < 0 || effect.healingRange > 10) yield return "Astral healing range must be bounded.";
                }
            }
            if (seen.Count != layout.nodes.Count) yield return "Every astral tree node requires an effect definition.";
        }
    }

    [RimWorld.DefOf]
    public static class AstralSkillDefs
    {
        public static AstralSkillTreeDef Lihuat_AstralSkills;
        static AstralSkillDefs() { RimWorld.DefOfHelper.EnsureInitializedInCtor(typeof(AstralSkillDefs)); }
    }
}
