using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class GravshipTransferRules
    {
        internal static bool AllowsTakeoff(Building_GravEngine engine) => !ModsConfig.OdysseyActive
            || Passengers(engine).All(pawn => Allows(pawn, null));

        internal static bool BeginTakeoff(Building_GravEngine engine, out PawnTransferBatch scope, out Gravship result)
        {
            result = null;
            scope = null;
            if (!ModsConfig.OdysseyActive) return true;
            List<Pawn> passengers = Passengers(engine);
            if (passengers.Any(pawn => !Allows(pawn, null)))
                throw new InvalidOperationException("Gravship passenger departure was not authorized.");
            scope = new PawnTransferBatch(passengers, null);
            return true;
        }

        internal static bool BeginLanding(Gravship ship, Map destination, out PawnTransferBatch scope)
        {
            scope = null;
            if (!ModsConfig.OdysseyActive) return true;
            List<Pawn> passengers = ship.Pawns.ToList();
            if (passengers.Any(pawn => !Allows(pawn, destination)))
                throw new InvalidOperationException("Gravship passenger arrival was not authorized.");
            scope = new PawnTransferBatch(passengers, destination);
            return true;
        }

        internal static void End(PawnTransferBatch scope) => scope?.Dispose();

        private static bool Allows(Pawn pawn, Map destination)
        {
            LihuatGame game = LihuatGame.Current;
            if (game?.Lifecycle.BeforeWorldDeparture(pawn) == false
                || game?.Domains.AllowsDestination(pawn, destination) == false) return false;
            return pawn.carryTracker?.CarriedThing is not Thing carried
                || game?.Domains.AllowsDestination(carried, destination) != false;
        }

        private static List<Pawn> Passengers(Building_GravEngine engine)
        {
            var passengers = new HashSet<Pawn>();
            Map map = engine.Map;
            foreach (IntVec3 cell in engine.ValidSubstructure)
                foreach (Thing thing in cell.GetThingList(map))
                    if (thing is Pawn pawn && pawn.Position == cell && pawn.def.bringAlongOnGravship)
                        passengers.Add(pawn);
            return passengers.ToList();
        }
    }
}
