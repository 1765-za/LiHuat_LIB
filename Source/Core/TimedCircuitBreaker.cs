using System;
using Verse;

namespace LihuatLib.Core
{
    public sealed class TimedCircuitBreaker : IExposable
    {
        public int UntilTick = -1;

        public bool Active(int now) => UntilTick > now;
        public int Remaining(int now) => Math.Max(0, UntilTick - now);

        public bool Trip(int now, int duration)
        {
            if (duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
            int until = checked(now + duration);
            if (UntilTick >= until) return false;
            UntilTick = until;
            return true;
        }

        public bool ClearExpired(int now)
        {
            if (UntilTick < 0 || now < UntilTick) return false;
            UntilTick = -1;
            return true;
        }

        public bool TryReset(int now, int cost, Func<int, bool> spend)
        {
            if (!Active(now) || cost < 0 || spend == null) return false;
            if (cost > 0 && !spend(cost)) return false;
            UntilTick = -1;
            return true;
        }

        public void Clear() => UntilTick = -1;
        public void ExposeData() => ExposeData("untilTick");
        public void ExposeData(string label) => Scribe_Values.Look(ref UntilTick, label, -1);
    }
}
