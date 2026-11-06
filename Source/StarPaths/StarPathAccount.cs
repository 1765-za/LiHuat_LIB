using System;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.StarPaths
{
    public sealed class StarPathInvestment : IExposable
    {
        private StarPathNodeDef node;
        private int rank, spent;
        public StarPathNodeDef Node => node;
        public int Rank => rank;
        public int Spent => spent;
        public StarPathInvestment() { }
        internal StarPathInvestment(StarPathNodeDef node) { this.node = node; }
        internal void Advance(int cost) { rank++; spent = checked(spent + cost); }
        public void ExposeData()
        {
            Scribe_Defs.Look(ref node, "node");
            Scribe_Values.Look(ref rank, "rank");
            Scribe_Values.Look(ref spent, "spent");
        }
    }

    public sealed class StarPathAccount : IExposable
    {
        private int level, highestLevel, spent;
        private List<StarPathInvestment> investments = new List<StarPathInvestment>();
        private readonly Dictionary<StarPathNodeDef, StarPathInvestment> byNode = new Dictionary<StarPathNodeDef, StarPathInvestment>();
        private IReadOnlyList<StarPathInvestment> readOnly;
        public int Level => level;
        public int HighestLevel => highestLevel;
        public int TotalPoints => highestLevel / 2;
        public int SpentPoints => spent;
        public int AvailablePoints => TotalPoints - spent;
        public IReadOnlyList<StarPathInvestment> Investments => readOnly;
        public StarPathAccount() { readOnly = investments.AsReadOnly(); }
        public int Rank(StarPathNodeDef node) => node != null && byNode.TryGetValue(node, out StarPathInvestment investment) ? investment.Rank : 0;
        internal void SetLevel(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            level = value; highestLevel = Math.Max(highestLevel, value);
        }
        internal void Purchase(StarPathNodeDef node, int cost)
        {
            if (!byNode.TryGetValue(node, out StarPathInvestment investment))
            { investment = new StarPathInvestment(node); investments.Add(investment); byNode.Add(node, investment); }
            investment.Advance(cost); spent = checked(spent + cost);
        }
        internal void ClearInvestments() { investments.Clear(); byNode.Clear(); spent = 0; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref level, "level");
            Scribe_Values.Look(ref highestLevel, "highestLevel");
            Scribe_Collections.Look(ref investments, "investments", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            readOnly = investments.AsReadOnly();
            byNode.Clear(); spent = 0;
            foreach (StarPathInvestment investment in investments)
            {
                if (investment.Node == null || investment.Rank < 1 || investment.Rank > investment.Node.ranks.Count || investment.Spent < 1)
                    throw new InvalidOperationException("Invalid star path investment in save.");
                byNode.Add(investment.Node, investment); spent = checked(spent + investment.Spent);
            }
            if (highestLevel < level || spent > TotalPoints) throw new InvalidOperationException("Star path skill point ledger is inconsistent.");
        }
    }
}
