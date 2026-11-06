using System;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class MapTransferResidue
    {
        private static readonly Action<MapPawns> notify = OriginalMethods.CreateTransfer<Action<MapPawns>>(
            AccessTools.Method(typeof(MapPawns), "DoListChangedNotifications"));

        internal static bool Contains(Map map, Thing thing, CellRect footprint)
            => MapRegistrationStorage.Process(map, thing, footprint, false);

        internal static void Clear(Map map, Thing thing, CellRect footprint)
        {
            if (!OperationScope.Transferring && !OperationScope.Terminates(thing) && !OperationScope.Rebirthing && !MapTransfer.InHandoff(thing))
                throw new InvalidOperationException("Registration cleanup requires an authorized lifecycle operation.");
            bool changed = MapRegistrationStorage.Process(map, thing, footprint, true);
            RegistrationStorage.Ticks(thing, true);
            if (thing is Pawn && changed) notify(map.mapPawns);
        }
    }
}
