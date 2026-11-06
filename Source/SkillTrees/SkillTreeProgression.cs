using LihuatLib.StarPaths;

namespace LihuatLib.SkillTrees
{
    internal static class SkillTreeProgression
    {
        internal static string Failure(StarPathAccount account, StarPathTreeDef tree, StarPathNodeDef node, bool available)
        {
            if (!available || node == null || !tree.nodes.Contains(node)) return "Lihuat_StarPathUnavailable";
            int current = account.Rank(node);
            if (current >= node.ranks.Count) return "Lihuat_StarPathMastered";
            if (account.Level < node.ranks[current].level) return "Lihuat_StarPathNeedLevel";
            foreach (StarPathRequirement requirement in node.requirements)
                if (account.Rank(requirement.node) < requirement.rank) return "Lihuat_StarPathNeedPrerequisite";
            return null;
        }
        internal static string Points(StarPathAccount account, StarPathNodeDef node)
            => account.AvailablePoints < node.ranks[account.Rank(node)].cost ? "Lihuat_StarPathNeedPoints" : null;
    }
}
