using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Verse;

namespace LihuatLib.Mechanisms
{
    public sealed partial class LifeStructureSnapshot
    {
        private readonly List<(HediffWithComps Owner, List<HediffComp> Components, int Count)> rootHediffGroups = new();
        private int rootThingCount, rootHediffCount;

        public bool RootsMatch()
        {
            if (pendingDetach) return false;
            ReadOnlySpan<object> expected = CollectionsMarshal.AsSpan(roots);
            if (!ReferencesMatch<ThingComp>(CollectionsMarshal.AsSpan(pawn.AllComps), expected, rootThingCount)) return false;
            expected = expected.Slice(rootThingCount);
            if (!ReferencesMatch<Hediff>(CollectionsMarshal.AsSpan(pawn.health.hediffSet.hediffs), expected, rootHediffCount)) return false;
            expected = expected.Slice(rootHediffCount);
            foreach (var group in CollectionsMarshal.AsSpan(rootHediffGroups))
            {
                if (!ReferenceEquals(group.Owner.comps, group.Components)
                    || !ReferencesMatch<HediffComp>(CollectionsMarshal.AsSpan(group.Components), expected, group.Count)) return false;
                expected = expected.Slice(group.Count);
            }
            return expected.Length == 0;
        }

        private static bool ReferencesMatch<T>(ReadOnlySpan<T> current, ReadOnlySpan<object> expected, int count) where T : class
        {
            if (current.Length != count || expected.Length < count) return false;
            int index = 0;
            for (; index + 3 < count; index += 4)
                if (!ReferenceEquals(current[index], expected[index]) || !ReferenceEquals(current[index + 1], expected[index + 1])
                    || !ReferenceEquals(current[index + 2], expected[index + 2]) || !ReferenceEquals(current[index + 3], expected[index + 3])) return false;
            for (; index < count; index++) if (!ReferenceEquals(current[index], expected[index])) return false;
            return true;
        }
    }
}
