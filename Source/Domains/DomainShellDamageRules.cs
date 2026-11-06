using System;
using Verse;

namespace LihuatLib.Domains
{
    public static class DomainShellDamageRules
    {
        public const int SingleDamagePercent = 15;

        public static int MaximumSingleDamage(Building_DomainShell shell) =>
            (int)((long)shell.Capacity * SingleDamagePercent / 100);

        internal static int LimitHitPoints(Building_DomainShell shell, int current, int requested)
        {
            if (!shell.Spawned || requested >= current) return requested;
            return Math.Max(requested, Math.Max(0, current - MaximumSingleDamage(shell)));
        }

        internal static float Limit(float amount, Thing victim)
        {
            if (!(victim is Building_DomainShell shell)) return amount;
            int maximum = MaximumSingleDamage(shell);
            float limit = maximum;
            if ((double)limit > maximum) limit -= Math.Max(1f, limit / 8388608f);
            return Math.Min(amount, limit);
        }
    }
}
