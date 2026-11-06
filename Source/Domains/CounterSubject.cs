using System;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class CounterSubject
    {
        public Pawn Pawn { get; }
        public Func<bool> Available { get; }
        public Func<bool> FormalMastery { get; }
        public Func<int, bool> Spend { get; }
        public Func<int> Clock { get; }
        public Action Changed { get; }
        public int CostPerSecond { get; }
        public CounterSubject(Pawn pawn, Func<bool> available, Func<bool> formalMastery, Func<int, bool> spend,
            Func<int> clock, Action changed, int costPerSecond = 2)
        {
            Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            Available = available ?? throw new ArgumentNullException(nameof(available));
            FormalMastery = formalMastery ?? throw new ArgumentNullException(nameof(formalMastery));
            Spend = spend ?? throw new ArgumentNullException(nameof(spend));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Changed = changed ?? throw new ArgumentNullException(nameof(changed));
            if (costPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(costPerSecond));
            CostPerSecond = costPerSecond;
        }
    }
}
