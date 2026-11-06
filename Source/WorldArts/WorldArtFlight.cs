using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed partial class WorldArtService
    {
        internal bool TryGetFlightSpeed(Caravan caravan, out float tilesPerSecond)
        {
            tilesPerSecond = 0f;
            WorldFlightState state = Flight(caravan);
            if (state?.Enabled != true) return false;
            if (subjects.TryGetValue(state.SubjectId, out var subject) && subject.Permitted)
                tilesPerSecond = subject.Profile.flightTilesPerSecond;
            return true;
        }

        public bool EnableFlight(WorldArtSubject subject)
        {
            if (!Available(subject)) return false;
            Caravan caravan = subject.Pawn.GetCaravan();
            WorldFlightState state = Flight(caravan);
            if (state == null)
            {
                state = new WorldFlightState { Caravan = caravan };
                flights.Add(state); byCaravan.Add(caravan, state);
            }
            if (state.Moving) return false;
            caravan.pather.StopDead();
            if (state.SubjectId != subject.Id) state.PaidTicks = 0;
            state.Pawn = subject.Pawn; state.SubjectId = subject.Id; state.Enabled = true; state.Paused = false;
            return true;
        }

        public bool Land(WorldFlightState state)
        {
            if (state == null || state.Moving || !WorldArtGeometry.Surface(state.Caravan.Tile)
                || !Verse.Find.WorldPathGrid.Passable(state.Caravan.Tile)) return false;
            state.Enabled = false; state.Paused = false;
            return true;
        }

        public bool StartFlight(WorldFlightState state, PlanetTile target, CaravanArrivalAction arrival = null)
        {
            if (state?.Enabled != true || !WorldArtGeometry.Valid(target) || state.Moving
                || !subjects.TryGetValue(state.SubjectId, out var subject) || !Available(subject)) return false;
            if (target.Layer.Def != PlanetLayerDefOf.Surface && (target.Layer.Def != PlanetLayerDefOf.Orbit || !subject.Profile.allowOrbit)) return false;
            if (arrival != null && !arrival.StillValid(state.Caravan, target)) return false;
            if (state.PaidTicks <= 0 && subject.Available < subject.Profile.flightCostPerSecond) return false;
            state.Origin = state.Caravan.Tile; state.Destination = target;
            state.Distance = WorldArtGeometry.Distance(state.Origin, target); state.Progress = 0;
            state.Arrival = arrival; state.Paused = false;
            state.Caravan.pather.StopDead();
            return true;
        }

        public bool ChangeLayer(WorldFlightState state, PlanetLayerDef definition)
        {
            PlanetLayer layer = Verse.Find.WorldGrid.PlanetLayers.Values.FirstOrDefault(p => p.Def == definition);
            return layer != null && state != null && StartFlight(state, WorldArtGeometry.Above(state.Caravan.Tile, layer));
        }

        private void Advance(WorldFlightState state)
        {
            if (!subjects.TryGetValue(state.SubjectId, out var subject)) return;
            if (!subject.Permitted) { state.Paused = true; return; }
            if (state.PaidTicks <= 0)
            {
                state.Paused = true;
                if (!subject.Spend(subject.Profile.flightCostPerSecond)) return;
                state.Paused = false;
                state.PaidTicks = 60f;
            }
            float speed = subject.Profile.flightTilesPerSecond / 60f;
            float moved = System.Math.Min(speed, state.Distance - state.Progress);
            state.Progress += moved;
            state.PaidTicks = System.Math.Max(0f, state.PaidTicks - moved / speed);
            if (state.Progress < state.Distance) return;
            var caravan = state.Caravan;
            var destination = state.Destination;
            var arrival = state.Arrival;
            state.Destination = PlanetTile.Invalid; state.Arrival = null;
            caravan.Tile = destination;
            caravan.tweener.ResetTweenedPosToRoot();
            if (WorldRendererUtility.WorldRendered && Verse.Find.WorldSelector.SingleSelectedObject == caravan)
                PlanetLayer.Selected = destination.Layer;
            if (arrival?.StillValid(caravan, destination) == true) arrival.Arrived(caravan);
        }
    }
}
