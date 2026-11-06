using System;
using System.Collections.Generic;
using System.Linq;
using LihuatLib.Core;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Domains
{
    internal readonly record struct DomainReturnDestination(Map Map, IntVec3 Cell);

    internal sealed class DomainReturnDestinations
    {
        private readonly DomainArea[] areas;
        private readonly HashSet<Map> retiring;
        private readonly Dictionary<Map, Map> resolved = new(ReferenceIdentity<Map>.Instance);
        private Map orphanDestination;

        internal DomainReturnDestinations(IEnumerable<DomainArea> areas, bool rollback = false)
        {
            this.areas = areas.ToArray();
            retiring = new HashSet<Map>(this.areas.SelectMany(a => rollback ? new[] { a.HomeMap } : new[] { a.HomeMap, a.Map })
                .Where(map => map != null), ReferenceIdentity<Map>.Instance);
        }

        private bool Available(Map map) => map != null && !map.Disposed && Find.Maps.Contains(map) && !retiring.Contains(map);

        internal DomainReturnDestination Resolve(Map source, IntVec3 position)
        {
            if (Available(source)) return new DomainReturnDestination(source, position);
            Map destination;
            if (source != null && resolved.TryGetValue(source, out destination) && Available(destination))
                return new DomainReturnDestination(destination, destination.Center);
            if (source == null && Available(orphanDestination)) return new DomainReturnDestination(orphanDestination, orphanDestination.Center);

            destination = Ancestor(source);
            if (destination == null)
                foreach (DomainArea area in areas.OrderByDescending(area => area.Source == source))
                {
                    destination = area.ReturnPath?.Maps.FirstOrDefault(Available) ?? Ancestor(area.Source);
                    if (destination != null) break;
                }
            if (destination == null)
            {
                foreach (DomainArea area in areas)
                {
                    PlanetTile tile = DomainReturnLanding.Location(area);
                    if (!tile.Valid) continue;
                    destination = Find.Maps.FirstOrDefault(map => Available(map) && map.Parent is not PocketMapParent && map.Tile == tile);
                    if (destination != null) break;
                }
            }
            destination ??= Find.Maps.FirstOrDefault(map => Available(map) && map.IsPlayerHome && map.Parent is not PocketMapParent);
            destination ??= DomainReturnLanding.Create(areas);
            if (source == null) orphanDestination = destination;
            else resolved[source] = destination;
            return new DomainReturnDestination(destination, destination.Center);
        }

        private Map Ancestor(Map source)
        {
            var seen = new HashSet<Map>(ReferenceIdentity<Map>.Instance);
            for (Map map = source; map != null && seen.Add(map); map = (map.Parent as PocketMapParent)?.sourceMap)
                if (Available(map)) return map;
            return null;
        }
    }
}
