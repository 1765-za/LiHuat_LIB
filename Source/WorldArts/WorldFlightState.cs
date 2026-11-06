using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed class WorldFlightState : IExposable
    {
        public Pawn Pawn;
        public Caravan Caravan;
        public string SubjectId;
        public bool Enabled;
        public bool Paused;
        public PlanetTile Origin = PlanetTile.Invalid;
        public PlanetTile Destination = PlanetTile.Invalid;
        public float Distance;
        public float Progress;
        public float PaidTicks;
        public CaravanArrivalAction Arrival;
        public bool Moving => Enabled && Destination.Valid && Progress < Distance;
        public Vector3 DrawPosition => Moving
            ? Vector3.Slerp(Origin.Layer.GetTileCenter(Origin), Destination.Layer.GetTileCenter(Destination), Progress / Distance)
            : Caravan.DrawPos;

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_References.Look(ref Caravan, "caravan");
            Scribe_Values.Look(ref SubjectId, "subject");
            Scribe_Values.Look(ref Enabled, "enabled");
            Scribe_Values.Look(ref Paused, "paused");
            Scribe_Values.Look(ref Origin, "origin", PlanetTile.Invalid);
            Scribe_Values.Look(ref Destination, "destination", PlanetTile.Invalid);
            Scribe_Values.Look(ref Distance, "distance");
            Scribe_Values.Look(ref Progress, "progress");
            Scribe_Values.Look(ref PaidTicks, "paidTicks");
            Scribe_Deep.Look(ref Arrival, "arrival");
        }
    }
}
