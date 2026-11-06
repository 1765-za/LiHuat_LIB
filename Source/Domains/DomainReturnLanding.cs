using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Domains
{
    internal static class DomainReturnLanding
    {
        internal static PlanetTile Location(DomainArea area)
        {
            PlanetTile tile = area.ReturnPath?.Tile ?? PlanetTile.Invalid;
            if (tile.Valid) return tile;
            return DomainLocation.TryResolve(area.HomeMap ?? area.Map ?? area.Source, out tile) ? tile : PlanetTile.Invalid;
        }

        internal static Map Create(IEnumerable<DomainArea> areas)
        {
            foreach (DomainArea area in areas)
            {
                PlanetTile tile = Location(area);
                if (!tile.Valid) continue;
                MapParent parent = Find.WorldObjects.MapParentAt(tile);
                if (parent?.Map != null) continue;
                if (parent == null)
                {
                    parent = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Camp);
                    parent.Tile = tile;
                    Find.WorldObjects.Add(parent);
                }
                // Game.FindMap(tile) also returns pocket maps; those are the maps being retired here.
                return MapGenerator.GenerateMap(Find.World.info.initialMapSize, parent,
                    parent.MapGeneratorDef, parent.ExtraGenStepDefs);
            }
            throw new InvalidOperationException("The domain has no surviving map or recorded planet location.");
        }
    }
}
