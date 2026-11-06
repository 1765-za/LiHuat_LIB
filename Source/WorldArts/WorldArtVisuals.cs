using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed partial class WorldArtService
    {
        internal void Draw()
        {
            if (casts.Count == 0 && flights.Count == 0) return;
            foreach (var state in flights)
                if (state.Enabled && state.Caravan?.Spawned == true && state.Caravan.Tile.Layer == PlanetLayer.Selected)
                {
                    Vector3 normal = (state.Caravan.DrawPos - state.Caravan.Tile.Layer.Origin).normalized;
                    WorldArtEffectDraw.Quad(state.Caravan.DrawPos + normal * .06f, normal,
                        Vector2.one * state.Caravan.Tile.Layer.AverageTileSize * .9f,
                        WorldArtEffectDraw.Ring, new Color(.3f, .95f, 1f, .8f));
                }
            int now = Verse.Find.TickManager.TicksGame;
            foreach (var cast in casts)
            {
                if (cast.Target.Layer != PlanetLayer.Selected) continue;
                if (cast.Kind == WorldArtKind.Opening) WorldOpeningVisuals.Draw(cast, now);
                else WorldStarfallVisuals.Draw(cast, now);
            }
        }
    }

}
