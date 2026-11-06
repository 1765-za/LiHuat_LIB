using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed class WorldArtRange : IExposable
    {
        public int Width = 5, Length = 25, Radius = 1;
        public float Heading;
        public const int MaxWidth = 15, MaxLength = 61, MaxRadius = 8;

        public WorldArtRange Copy() => new() { Width = Mathf.Clamp(Width, 1, MaxWidth),
            Length = Mathf.Clamp(Length, 1, MaxLength), Radius = Mathf.Clamp(Radius, 0, MaxRadius),
            Heading = float.IsFinite(Heading) ? Mathf.Repeat(Heading, 180f) : 0f };

        internal static WorldArtRange From(WorldArtProfileDef profile) => new WorldArtRange {
            Width = profile.clearingWidth, Length = profile.clearingLength, Radius = profile.impactRadius }.Copy();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Width, "width", 5);
            Scribe_Values.Look(ref Length, "length", 25);
            Scribe_Values.Look(ref Radius, "radius", 1);
            Scribe_Values.Look(ref Heading, "heading");
        }
    }
}
