using System;
using LihuatLib.Core;
using LihuatLib.SkillTrees;
using LihuatLib.StarPaths;
using RimWorld;

namespace LihuatLib.AstralArts
{
    public sealed partial class AstralArtsService
    {
        public void SetSkillLevel(AstralEntry entry, int level)
        {
            RequireSkillEntry(entry);
            entry.Skills.SetLevel(level); RebuildSkills(entry);
        }
        public string SkillUpgradeFailure(AstralEntry entry, StarPathNodeDef node)
        {
            if (entry == null || Find(entry.Pawn) != entry) return "Lihuat_StarPathUnavailable";
            bool available = Available(entry) && entry.Pawn.Faction == Faction.OfPlayer;
            string failure = SkillTreeProgression.Failure(entry.Skills, AstralSkillDefs.Lihuat_AstralSkills.layout, node, available);
            if (failure != null) return failure;
            AstralArt art = AstralSkillDefs.Lihuat_AstralSkills.Find(node).art;
            if (art != AstralArt.None && !entry.Allows(art)) return "Lihuat_AstralSkillUnavailable";
            return SkillTreeProgression.Points(entry.Skills, node);
        }
        public bool TryUpgradeSkill(AstralEntry entry, StarPathNodeDef node)
        {
            RequireSkillEntry(entry);
            if (SkillUpgradeFailure(entry, node) != null) return false;
            entry.Skills.Purchase(node, node.ranks[entry.Skills.Rank(node)].cost);
            RebuildSkills(entry); RefreshEnhancement(entry);
            return true;
        }
        private void RequireSkillEntry(AstralEntry entry)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (entry == null || Find(entry.Pawn) != entry) throw new InvalidOperationException("Astral skill account is not registered.");
        }
        private static void RebuildSkills(AstralEntry entry)
        {
            CompileSkills(entry);
            entry.Account.Initialize(entry.Capacity);
            AstralEnhancementStats.Invalidate(entry.Pawn); entry.SkillRevision++;
        }
        private static void CompileSkills(AstralEntry entry)
        {
            entry.SkillEffects = new AstralSkillEffects(entry.Skills, AstralSkillDefs.Lihuat_AstralSkills);
            entry.Account.SetIsolationBonus(entry.SkillEffects.IsolationReduction);
        }
    }
}
