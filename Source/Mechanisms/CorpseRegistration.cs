using System;
using System.Collections.Generic;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class CorpseRegistration
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex =
            AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly AccessTools.FieldRef<Corpse, ThingOwner<Pawn>> container =
            AccessTools.FieldRefAccess<Corpse, ThingOwner<Pawn>>("innerContainer");
        private static readonly AccessTools.FieldRef<ThingOwner<Pawn>, List<Pawn>> contents =
            AccessTools.FieldRefAccess<ThingOwner<Pawn>, List<Pawn>>("innerList");
        private static readonly Action<ThingOwner, Thing> notifyAdded = OriginalMethods.Create<Action<ThingOwner, Thing>>(
            AccessTools.DeclaredMethod(typeof(ThingOwner), "NotifyAdded"), true);

        internal static void Attach(Corpse corpse, Pawn pawn)
        {
            ThingOwner<Pawn> owner = container(corpse);
            List<Pawn> pawns = contents(owner);
            if (pawn?.health == null || ReferenceListStorage<Pawn>.Count(pawns) != 0)
                throw new InvalidOperationException("Corpse assembly requires a pawn health tracker and an empty corpse.");
            using (new OperationScope(OperationKind.TerminalDeath, pawn))
            {
                MapTransfer.Detach(pawn);
                PawnHealthStateAccess.Write(pawn.health, PawnHealthState.Dead);
                ReferenceListStorage<Pawn>.Ensure(pawns, pawn);
                pawn.holdingOwner = owner;
                if (corpse is IThingHolderEvents<Pawn> events) events.Notify_ItemAdded(pawn);
                notifyAdded(owner, pawn);
            }
        }

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
