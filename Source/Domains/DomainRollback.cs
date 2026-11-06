using System.Collections.Generic;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class DomainRollback : IExposable
    {
        public DomainArea Area;
        public List<DomainMove> Positions = new List<DomainMove>();
        public List<DomainEntityMove> EntityPositions = new();
        public void ExposeData()
        {
            Scribe_Deep.Look(ref Area, "area");
            Scribe_Collections.Look(ref Positions, "positions", LookMode.Deep);
            Scribe_Collections.Look(ref EntityPositions, "entityPositions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && EntityPositions == null) EntityPositions = new();
        }
    }

    public sealed class DomainMove : IExposable
    {
        public Thing Thing;
        public Map Map;
        public IntVec3 Position;
        public Rot4 Rotation;
        public DomainMember HeldPawn;
        public bool Restored;
        public void ExposeData()
        {
            Scribe_References.Look(ref Thing, "thing");
            Scribe_References.Look(ref Map, "map");
            Scribe_Values.Look(ref Position, "position");
            Scribe_Values.Look(ref Rotation, "rotation");
            Scribe_Deep.Look(ref HeldPawn, "heldPawn");
            Scribe_Values.Look(ref Restored, "restored");
        }
    }
}
