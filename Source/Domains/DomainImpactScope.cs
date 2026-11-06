using System;
using Verse;

namespace LihuatLib.Domains
{
    internal readonly struct DomainImpactScope : IDisposable
    {
        [ThreadStatic] private static Map map;
        [ThreadStatic] private static IntVec3 cell;
        [ThreadStatic] private static int depth;
        private readonly Map previousMap;
        private readonly IntVec3 previousCell;
        private readonly bool entered;

        internal DomainImpactScope(Map sourceMap, IntVec3 sourceCell)
        {
            previousMap = map; previousCell = cell; entered = true;
            map = sourceMap; cell = sourceCell; depth++;
        }
        internal static bool TryOrigin(out Map sourceMap, out IntVec3 sourceCell)
        { sourceMap = map; sourceCell = cell; return depth != 0; }
        public void Dispose()
        {
            if (!entered) return;
            map = previousMap; cell = previousCell; depth--;
        }
    }
}
