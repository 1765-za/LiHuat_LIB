using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.WorldArts
{
    public enum WorldArtKind { Opening, Starfall }

    internal sealed class WorldArtCast : IExposable
    {
        internal Pawn Pawn;
        internal WorldArtProfileDef Profile;
        internal WorldArtKind Kind;
        internal PlanetTile Target;
        internal int StartedAt;
        internal int ImpactAt;
        internal int EndAt => ImpactAt + (Kind == WorldArtKind.Starfall ? 240 : 48);
        internal bool Resolved;
        internal WorldArtRange Range;
        internal List<PlanetTile> Tiles;
        internal WorldArtFrame Frame;

        internal void Prepare() => Frame = new WorldArtFrame(Target, Range.Heading);
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "caster");
            Scribe_Defs.Look(ref Profile, "profile");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Target, "target");
            Scribe_Values.Look(ref StartedAt, "startedAt");
            Scribe_Values.Look(ref ImpactAt, "impactAt");
            Scribe_Values.Look(ref Resolved, "resolved");
            Scribe_Deep.Look(ref Range, "range");
            Scribe_Collections.Look(ref Tiles, "tiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && WorldArtGeometry.Surface(Target))
            {
                Range ??= WorldArtRange.From(Profile);
                Tiles ??= WorldArtGeometry.Footprint(Target, Kind, Range);
                Prepare();
            }
        }
    }
}
