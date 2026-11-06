using System.Collections.Generic;
using LihuatLib.Core;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class DomainReturnPath : IExposable
    {
        private List<Map> maps = new();
        private PlanetTile tile = PlanetTile.Invalid;
        internal IEnumerable<Map> Maps => maps;
        internal PlanetTile Tile => tile;
        internal bool Contains(Map map) => maps.Contains(map);

        internal static DomainReturnPath Capture(Map source)
        {
            var result = new DomainReturnPath();
            DomainLocation.TryResolve(source, out result.tile);
            var seen = new HashSet<Map>(ReferenceIdentity<Map>.Instance);
            for (Map map = source; map != null && seen.Add(map); map = (map.Parent as PocketMapParent)?.sourceMap)
                result.maps.Add(map);
            return result;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                maps.RemoveAll(map => map == null || map.Disposed || !Find.Maps.Contains(map));
            Scribe_Collections.Look(ref maps, "maps", LookMode.Reference);
            Scribe_Values.Look(ref tile, "tile", PlanetTile.Invalid);
            if (Scribe.mode == LoadSaveMode.LoadingVars && maps == null) maps = new();
        }
    }
}
