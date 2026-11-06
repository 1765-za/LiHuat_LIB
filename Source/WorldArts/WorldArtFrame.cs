using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;

namespace LihuatLib.WorldArts
{
    internal readonly struct WorldArtFrame
    {
        internal readonly Vector3 Origin, Center, Normal, Forward, Right;
        internal readonly float Radius, Unit;

        internal WorldArtFrame(PlanetTile tile, float heading)
        {
            Origin = tile.Layer.Origin; Center = tile.Layer.GetTileCenter(tile);
            Radius = (Center - Origin).magnitude; Normal = (Center - Origin) / Radius;
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, Normal);
            if (north.sqrMagnitude < .001f) north = Vector3.ProjectOnPlane(Vector3.forward, Normal);
            Forward = Quaternion.AngleAxis(heading, Normal) * north.normalized;
            Right = Vector3.Cross(Forward, Normal).normalized;
            var neighbors = new List<PlanetTile>(8);
            tile.Layer.GetTileNeighbors(tile, neighbors);
            float distance = 0f;
            foreach (PlanetTile neighbor in neighbors) distance += Vector3.Distance(Center, tile.Layer.GetTileCenter(neighbor));
            Unit = neighbors.Count > 0 ? distance / neighbors.Count : tile.Layer.AverageTileSize;
        }

        internal Vector3 Point(float across, float along, float height = .05f)
        {
            Vector3 tangent = Right * across + Forward * along;
            float angle = tangent.magnitude * Unit / Radius;
            return Origin + (Normal * Mathf.Cos(angle) + tangent.normalized * Mathf.Sin(angle)) * (Radius + height);
        }

        internal Vector2 Project(Vector3 position)
        {
            Vector3 direction = (position - Origin).normalized;
            float dot = Mathf.Clamp(Vector3.Dot(Normal, direction), -1f, 1f);
            Vector3 tangent = (direction - Normal * dot).normalized;
            float distance = Mathf.Acos(dot) * Radius / Unit;
            return new(Vector3.Dot(tangent, Right) * distance, Vector3.Dot(tangent, Forward) * distance);
        }
    }
}
