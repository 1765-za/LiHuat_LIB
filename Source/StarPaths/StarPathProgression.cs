using RimWorld;

namespace LihuatLib.StarPaths
{
    internal static class StarPathProgression
    {
        internal static string Failure(StarPathService service, StarPathSubject subject, StarPathNodeDef node)
        {
            string failure = SkillTrees.SkillTreeProgression.Failure(subject.Account, subject.Tree, node,
                service.Active(subject) && subject.Pawn.Faction == Faction.OfPlayer);
            if (failure != null) return failure;
            int current = subject.Account.Rank(node);
            StarPathRank next = node.ranks[current];
            float share = subject.Effects.AllocatedConversion;
            if (current > 0)
            { StarPathRank previous = node.ranks[current - 1]; share -= previous.advanced + previous.specialized + previous.trueDamage; }
            if (share + next.advanced + next.specialized + next.trueDamage > 1.000001f) return "Lihuat_StarPathConversionFull";
            return SkillTrees.SkillTreeProgression.Points(subject.Account, node);
        }
    }
}
