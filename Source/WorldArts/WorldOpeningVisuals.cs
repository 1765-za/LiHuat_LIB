namespace LihuatLib.WorldArts
{
    internal static class WorldOpeningVisuals
    {
        internal static void Draw(WorldArtCast cast, int now)
        {
            float age = (now - cast.ImpactAt + 8f) / 48f;
            if (age < 0f || age >= 1f) return;
            WorldOpeningRenderer.Draw(cast, age);
        }
    }
}
