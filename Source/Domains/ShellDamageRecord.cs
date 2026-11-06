using Verse;

namespace LihuatLib.Domains
{
    internal struct ShellDamageRecord
    {
        private int writes, firstTick, lastTick, lastApplied;
        private long total, lastRequested;

        internal void Record(int before, int requested, int after)
        {
            int now = Find.TickManager.TicksGame;
            if (writes++ == 0) firstTick = now;
            lastTick = now;
            lastRequested = (long)before - requested;
            lastApplied = before - after;
            total += lastApplied;
        }

        internal void Report(Building_DomainShell shell) => Log.Message(
            "[LihuatLib.Domain] Shell broken id=" + shell.DomainId + " hp=" + shell.HitPoints
            + " capacity=" + shell.Capacity + " limit=" + DomainShellDamageRules.MaximumSingleDamage(shell)
            + " writes=" + writes + " total=" + total + " tickSpan=" + (lastTick - firstTick)
            + " lastRequested=" + lastRequested + " lastApplied=" + lastApplied);
    }
}
