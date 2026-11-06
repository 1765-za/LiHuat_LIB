using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LihuatLib.StarPaths
{
    public enum DamageChannel { Ordinary, Advanced, Specialized, True }

    public sealed class StarPathStat
    {
        public StatDef stat;
        public float offset;
        public float factor = 1f;
        public bool equipment;
    }

    public sealed class StarPathRank
    {
        public int cost = 1;
        public int level;
        public float advanced, specialized, trueDamage, globalReduction;
        public float penetration;
        public float advancedIncoming = 1f, specializedIncoming = 1f;
        public bool guaranteedHit;
        public List<StarPathStat> stats = new List<StarPathStat>();
    }

    public sealed class StarPathRequirement
    {
        public StarPathNodeDef node;
        public int rank = 1;
    }

    public sealed class StarPathNodeDef : Def
    {
        public string titleKey, descriptionKey;
        public int column, row;
        public List<StarPathRequirement> requirements = new List<StarPathRequirement>();
        public List<StarPathRank> ranks = new List<StarPathRank>();
        public string Title => titleKey.Translate();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (string.IsNullOrWhiteSpace(titleKey) || string.IsNullOrWhiteSpace(descriptionKey)) yield return "Star path nodes require translation keys.";
            if (column < 0 || row < 0 || ranks.Count == 0) yield return "Star path nodes require a position and ranks.";
            int previousLevel = -1;
            foreach (StarPathRank rank in ranks)
            {
                if (rank.cost < 1 || rank.level < previousLevel) yield return "Rank costs must be positive and level requirements ordered.";
                previousLevel = rank.level;
                if (!Fraction(rank.advanced) || !Fraction(rank.specialized) || !Fraction(rank.trueDamage)
                    || !Fraction(rank.globalReduction) || rank.advanced + rank.specialized + rank.trueDamage > 1f)
                    yield return "Damage shares and reductions must be finite fractions.";
                if (!float.IsFinite(rank.advancedIncoming) || !float.IsFinite(rank.specializedIncoming)
                    || rank.advancedIncoming < 0f || rank.specializedIncoming < 0f) yield return "Incoming coefficients must be finite and nonnegative.";
                if (!float.IsFinite(rank.penetration) || rank.penetration < 0f) yield return "Armor penetration bonus must be finite and nonnegative.";
                foreach (StarPathStat stat in rank.stats)
                    if (stat.stat == null || !float.IsFinite(stat.offset) || !float.IsFinite(stat.factor) || stat.factor <= 0f)
                        yield return "Star path stat modifiers require a stat and finite values with a positive factor.";
            }
            foreach (StarPathRequirement requirement in requirements)
                if (requirement.node == null || requirement.node == this || requirement.rank < 1 || requirement.rank > requirement.node.ranks.Count)
                    yield return "Invalid star path prerequisite.";
        }

        private static bool Fraction(float value) => float.IsFinite(value) && value >= 0f && value <= 1f;
    }

    public sealed class StarPathTreeDef : Def
    {
        public List<StarPathNodeDef> nodes = new List<StarPathNodeDef>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            var members = new HashSet<StarPathNodeDef>();
            var positions = new HashSet<(int, int)>();
            foreach (StarPathNodeDef node in nodes)
            {
                if (node == null || !members.Add(node)) { yield return "Star path tree contains a missing or duplicate node."; continue; }
                if (!positions.Add((node.column, node.row))) yield return "Star path nodes share the same position.";
            }
            foreach (StarPathNodeDef node in members)
            {
                foreach (StarPathRequirement requirement in node.requirements)
                    if (!members.Contains(requirement.node)) yield return "Prerequisite belongs to another star path tree.";
                if (Cycles(node, new HashSet<StarPathNodeDef>())) yield return "Star path prerequisites contain a cycle.";
            }
        }

        private static bool Cycles(StarPathNodeDef node, HashSet<StarPathNodeDef> visiting)
        {
            if (!visiting.Add(node)) return true;
            foreach (StarPathRequirement requirement in node.requirements)
                if (requirement.node != null && Cycles(requirement.node, visiting)) return true;
            visiting.Remove(node);
            return false;
        }
    }

    [DefOf]
    public static class StarPathDefs
    {
        public static StarPathTreeDef Lihuat_StarPath;
        static StarPathDefs() { DefOfHelper.EnsureInitializedInCtor(typeof(StarPathDefs)); }
    }
}
