using System.Collections.Generic;
using LihuatLib.Core;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Domains
{
    internal static class SubmapTransferRules
    {
        internal static bool Connected(Map source, Map destination)
        {
            if (source == null || destination == null || source == destination || source.Disposed || destination.Disposed
                || !Find.Maps.Contains(source) || !Find.Maps.Contains(destination)
                || source.Parent is not PocketMapParent && destination.Parent is not PocketMapParent) return false;
            Map sourceRoot = Root(source), destinationRoot = Root(destination);
            return sourceRoot != null && ReferenceEquals(sourceRoot, destinationRoot);
        }

        private static Map Root(Map map)
        {
            var visited = new HashSet<Map>(ReferenceIdentity<Map>.Instance);
            while (map != null && !map.Disposed && Find.Maps.Contains(map) && visited.Add(map))
            {
                if (map.Parent is not PocketMapParent pocket) return map;
                map = pocket.sourceMap;
            }
            return null;
        }
    }
}
