using System.Collections.Generic;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class MapSelectionCandidates
    {
        private static bool BelongsToMap(Thing thing, Map map)
        {
            if (map == null || thing?.def == null || thing.Destroyed) return false;
            Thing parent = thing.SpawnedParentOrMe;
            return parent?.def != null && !parent.Destroyed && parent.Spawned
                && parent.Map == map && parent.Position.InBounds(map);
        }

        internal static void Add(List<Thing> candidates, Thing thing)
        {
            if (BelongsToMap(thing, Find.CurrentMap)) candidates.Add(thing);
        }

        internal static void AddRange(List<Thing> candidates, IEnumerable<Thing> things)
        {
            Map map = Find.CurrentMap;
            foreach (Thing thing in things)
                if (BelongsToMap(thing, map)) candidates.Add(thing);
        }
    }
}
