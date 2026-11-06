using System;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Domains
{
    internal static class PawnFlightRules
    {
        internal sealed class FlightScope : IDisposable
        {
            private readonly MapTransferIntent intent;
            private readonly OperationScope operation;
            internal FlightScope(Thing thing, Map map) { intent = new MapTransferIntent(thing, map); operation = new OperationScope(OperationKind.Transfer); }
            public void Dispose() { try { operation.Dispose(); } finally { intent.Dispose(); } }
        }
        internal static bool BeginTakeoff<TFlyer>(Pawn pawn, ref IntVec3 destination, out FlightScope scope, ref TFlyer result) where TFlyer : Thing
        {
            scope = null;
            if (pawn?.MapHeld is Map map)
            {
                if (LihuatGame.Current?.Domains.Contains(pawn) == true) destination = destination.ClampInsideMap(map);
                if (!DomainShellRules.PlaceArrival(pawn, map, ref destination)) destination = pawn.PositionHeld;
                scope = new FlightScope(pawn, map);
            }
            return true;
        }
        internal static bool BeginLanding(Thing flyer, out FlightScope scope)
        {
            scope = null;
            if (!KeepValid(flyer)) return false;
            scope = new FlightScope(flyer, flyer.Map);
            return true;
        }
        internal static void End(FlightScope scope) => scope?.Dispose();
        internal static bool HasPassenger(Thing flyer) => ((IThingHolder)flyer).GetDirectlyHeldThings()?.Count > 0;
        internal static bool KeepValid(Thing flyer)
        {
            if (HasPassenger(flyer)) return true;
            if (!flyer.Destroyed) flyer.Destroy();
            return false;
        }
    }
}
