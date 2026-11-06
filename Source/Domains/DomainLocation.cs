using System.Collections.Generic;
using LihuatLib.Core;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Domains
{
    internal static class DomainLocation
    {
        internal static bool TryResolve(Map source, out PlanetTile tile)
        {
            var visited = new HashSet<Map>(ReferenceIdentity<Map>.Instance);
            for (Map map = source; map != null && visited.Add(map); map = (map.Parent as PocketMapParent)?.sourceMap)
            {
                tile = map.Tile;
                if (tile.Valid && tile.tileId < tile.Layer.TilesCount) return true;
            }
            tile = PlanetTile.Invalid;
            return false;
        }
    }
}
