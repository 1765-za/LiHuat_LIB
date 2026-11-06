using System;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class CorpseRegistration
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex =
            AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");

        public static bool Settle(Pawn pawn)
        {
            if (!OperationScope.Terminates(pawn))
                throw new InvalidOperationException("Corpse registration settlement requires a terminal operation.");
            if (!PawnLife.Dead(pawn) || !(pawn.holdingOwner?.Owner is Corpse corpse)
                || !ReferenceEquals(corpse.InnerPawn, pawn)) return false;
            int index = mapIndex(pawn);
            if (index < 0) return true;
            Map map = index < Find.Maps.Count ? Find.Maps[index] : null;
            CellRect footprint = MapRegistrationStorage.Footprint(pawn);
            mapIndex(pawn) = -1;
            if (map != null) MapTransferResidue.Clear(map, pawn, footprint);
            LifecycleComponentRelease.Killed(pawn);
            return mapIndex(pawn) < 0 && ReferenceEquals(pawn.holdingOwner?.Owner, corpse)
                && (map == null || !MapTransferResidue.Contains(map, pawn, footprint));
        }
    }
}
