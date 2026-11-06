using UnityEngine;

namespace LihuatLib.WorldArts
{
    internal static class WorldStarfallVisuals
    {
        internal static Vector3 MeteorPosition(WorldArtCast cast, float progress)
        {
            var frame = cast.Frame;
            float height = frame.Unit * 26f * (1f - progress * progress);
            return frame.Point((1f - progress) * 8f, (1f - progress) * -10f, height);
        }

        internal static void Draw(WorldArtCast cast, int now) => WorldStarfallRenderer.Draw(cast, now);
    }
}
