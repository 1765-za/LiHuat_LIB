using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed partial class WorldArtService
    {
        public bool Cast(WorldArtSubject subject, WorldArtKind kind, PlanetTile target)
            => subject != null && BeginCast(subject, kind, target, RangeFor(subject)) != null;

        public bool Cast(WorldArtSubject subject, WorldArtKind kind, PlanetTile target, WorldArtRange range)
            => BeginCast(subject, kind, target, range) != null;

        internal WorldArtCast BeginCast(WorldArtSubject subject, WorldArtKind kind, PlanetTile target, WorldArtRange range)
        {
            if (!Available(subject) || !WorldArtGeometry.Surface(target)) return null;
            var profile = subject.Profile;
            float cost = kind == WorldArtKind.Opening ? profile.clearingCost : profile.impactCost;
            WorldArtRange shape = range.Copy();
            var tiles = WorldArtGeometry.Footprint(target, kind, shape);
            if (!subject.Spend(cost)) return null;
            int now = Verse.Find.TickManager.TicksGame;
            var cast = new WorldArtCast { Pawn = subject.Pawn, Profile = profile, Kind = kind, Target = target,
                Range = shape, Tiles = tiles, StartedAt = now,
                ImpactAt = now + (kind == WorldArtKind.Starfall ? profile.impactDelayTicks : profile.clearingDelayTicks) };
            cast.Prepare(); casts.Add(cast);
            return cast;
        }

        private void Resolve(WorldArtCast cast)
        {
            if (!WorldArtGeometry.Surface(cast.Target)) return;
            bool impact = cast.Kind == WorldArtKind.Starfall;
            var factions = new HashSet<Faction>();
            bool needsRecache = false;
            foreach (PlanetTile tile in cast.Tiles)
            {
                if (tile.Layer[tile] is not SurfaceTile surface) continue;
                bool water = surface.WaterCovered;
                if (impact && water)
                {
                    surface.elevation = 100f;
                    surface.PrimaryBiome = BiomeDefOf.TemperateForest;
                    surface.rainfall = Mathf.Max(surface.rainfall, 1000f);
                }
                if (impact)
                {
                    float distance = cast.Frame.Project(tile.Layer.GetTileCenter(tile)).magnitude;
                    bool core = distance <= Mathf.Max(.5f, cast.Range.Radius * .45f);
                    surface.elevation = water ? (core ? 160f : 90f) : Mathf.Max(25f, surface.elevation + (core ? -200f : 120f));
                    surface.hilliness = core ? Hilliness.Flat : Hilliness.SmallHills;
                }
                else
                {
                    surface.hilliness = Hilliness.Flat;
                    if (!water) surface.elevation = Mathf.Min(surface.elevation, 120f);
                }
                surface.swampiness = 0f;
                Verse.Find.WorldPathGrid.RecalculatePerceivedMovementDifficultyAt(tile, out bool changed);
                needsRecache |= changed;
                tile.Layer.FastTileFinder.DirtyTile(tile);
                foreach (WorldObject worldObject in Verse.Find.WorldObjects.ObjectsAt(tile).ToArray())
                {
                    if (worldObject is not MapParent parent) continue;
                    if (parent.Faction != null && parent.Faction != cast.Pawn?.Faction) factions.Add(parent.Faction);
                    if (parent.HasMap)
                    {
                        mapWork.Add(new WorldArtMapWork(parent.Map, cast.Pawn, impact, cast.Profile, cast.Range));
                    }
                    else parent.Destroy();
                }
            }
            foreach (Faction faction in factions)
                if (cast.Pawn?.Faction is Faction source) faction.TryAffectGoodwillWith(source, -75);
            if (needsRecache) Verse.Find.WorldReachability.ClearCache();
            cast.Target.Layer.SetAllLayersDirty();
        }
    }
}
