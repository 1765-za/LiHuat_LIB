using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class MapCellRegistration
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex =
            AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly Action<MapPawns> notify = OriginalMethods.CreateTransfer<Action<MapPawns>>(
            AccessTools.Method(typeof(MapPawns), "DoListChangedNotifications"));

        internal static void Reconcile(Map map, CellRect area)
        {
            HashSet<Pawn> departed = null;
            int aliases = 0;
            foreach (IntVec3 cell in area.ClipInsideMap(map))
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (i >= things.Count || things[i] is not Pawn pawn) continue;
                    int index = mapIndex(pawn);
                    bool belongs = index >= 0 && index < Find.Maps.Count && ReferenceEquals(Find.Maps[index], map);
                    if (belongs && MapRegistrationStorage.Footprint(pawn).Contains(cell)) continue;
                    aliases += ReferenceListStorage<Thing>.Remove(things, pawn);
                    if (!belongs) (departed ??= new HashSet<Pawn>(Core.ReferenceIdentity<Pawn>.Instance)).Add(pawn);
                }
            }
            if (departed != null)
            {
                foreach (Pawn pawn in departed)
                    MapRegistrationStorage.Process(map, pawn, MapRegistrationStorage.Footprint(pawn), true);
                notify(map.mapPawns);
            }
            if (aliases != 0)
                Log.Message("[LihuatLib.Domain] Reconciled map cell registrations: map=" + map.uniqueID
                    + " staleCells=" + aliases + " departedPawns=" + (departed?.Count ?? 0));
        }
    }
}
