using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    internal static class WorldArtGeometry
    {
        internal static bool Valid(PlanetTile tile) => tile.Valid && tile.Layer != null && tile.Layer.InBounds(tile);
        internal static bool Surface(PlanetTile tile) => Valid(tile) && tile.Layer.Def == PlanetLayerDefOf.Surface
            && tile.Layer[tile] is SurfaceTile;

        internal static List<PlanetTile> Footprint(PlanetTile center, WorldArtKind kind, WorldArtRange range)
        {
            if (kind == WorldArtKind.Starfall) return Area(center, range.Radius);
            var frame = new WorldArtFrame(center, range.Heading);
            var result = new List<PlanetTile>();
            foreach (PlanetTile tile in Area(center, (range.Width + range.Length + 1) / 2 + 2))
            {
                Vector2 position = frame.Project(center.Layer.GetTileCenter(tile));
                if (Mathf.Abs(position.x) <= range.Width * .5f && Mathf.Abs(position.y) <= range.Length * .5f) result.Add(tile);
            }
            return result;
        }

        internal static float Distance(PlanetTile from, PlanetTile to)
        {
            Vector3 a = from.Layer.GetTileCenter(from) - from.Layer.Origin;
            Vector3 b = to.Layer.GetTileCenter(to) - to.Layer.Origin;
            float angle = Vector3.Angle(a, b) * Mathf.Deg2Rad;
            return Mathf.Max(1f, from.Layer.ApproxDistanceInTiles(angle)
                + Mathf.Abs(a.magnitude - b.magnitude) / from.Layer.AverageTileSize);
        }

        internal static PlanetTile Above(PlanetTile from, PlanetLayer layer)
        {
            Vector3 direction = (from.Layer.GetTileCenter(from) - from.Layer.Origin).normalized;
            PlanetTile current = layer.PlanetTileForID(0);
            var neighbors = new List<PlanetTile>(8);
            float score = Score(current);
            while (true)
            {
                neighbors.Clear(); layer.GetTileNeighbors(current, neighbors);
                PlanetTile best = current;
                foreach (PlanetTile next in neighbors)
                {
                    float candidate = Score(next);
                    if (candidate < score) { score = candidate; best = next; }
                }
                if (best == current) return current;
                current = best;
            }
            float Score(PlanetTile tile) => ((layer.GetTileCenter(tile) - layer.Origin).normalized - direction).sqrMagnitude;
        }

        internal static List<PlanetTile> Area(PlanetTile center, int radius)
        {
            var result = new List<PlanetTile> { center };
            var seen = new HashSet<PlanetTile> { center };
            var neighbors = new List<PlanetTile>(8);
            int start = 0, end = 1;
            for (int ring = 0; ring < radius; ring++)
            {
                for (int i = start; i < end; i++)
                {
                    neighbors.Clear(); center.Layer.GetTileNeighbors(result[i], neighbors);
                    foreach (PlanetTile tile in neighbors) if (seen.Add(tile)) result.Add(tile);
                }
                start = end; end = result.Count;
            }
            return result;
        }
    }
}
