using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class MapTransfer
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly Action<Thing, DestroyMode> despawn = OriginalMethods.CreateTransfer<Action<Thing, DestroyMode>>(
            AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn)));
        private static readonly Func<ThingOwner, Thing, bool> remove = OriginalMethods.CreateTransfer<Func<ThingOwner, Thing, bool>>(
            AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.Remove), new[] { typeof(Thing) }));
        private static readonly Func<ThingOwner, Thing, bool, bool> add = OriginalMethods.CreateTransfer<Func<ThingOwner, Thing, bool, bool>>(
            AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.TryAdd), new[] { typeof(Thing), typeof(bool) }));
        private static readonly Func<WorldPawns, Pawn, bool> inWorld = OriginalMethods.CreateTransfer<Func<WorldPawns, Pawn, bool>>(
            AccessTools.Method(typeof(WorldPawns), nameof(WorldPawns.Contains), new[] { typeof(Pawn) }));
        private static readonly Action<WorldPawns, Pawn> removeWorld = OriginalMethods.CreateTransfer<Action<WorldPawns, Pawn>>(
            AccessTools.Method(typeof(WorldPawns), nameof(WorldPawns.RemovePawn)));
        private static readonly Func<Thing, IntVec3, Map, Rot4, WipeMode, bool, bool, Thing> spawn =
            OriginalMethods.CreateTransfer<Func<Thing, IntVec3, Map, Rot4, WipeMode, bool, bool, Thing>>(
                AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) }));

        private static Map CurrentMap(Thing thing)
        {
            int index = mapIndex(thing);
            return index >= 0 && index < Find.Maps.Count ? Find.Maps[index] : null;
        }
        public static long ReconciledDepartures { get; private set; }
        public static bool InHandoff(Thing thing) => MapTransferIntent.Handoff(thing) != null;
        internal static bool HandoffAllowsCell(Thing thing, IntVec3 cell)
            => MapTransferIntent.HandoffDestination(thing) is Map destination && cell.InBounds(destination);
        public static bool HandoffAllowsWrite(Pawn pawn, FieldInfo field, object target, object value)
        {
            MapTransferIntent intent = MapTransferIntent.Handoff(pawn);
            if (intent == null || !ReferenceEquals(target, pawn) || field.DeclaringType != typeof(Thing)) return false;
            Map destination = intent.Destination;
            if (field.Name == "positionInt") return destination != null && value is IntVec3 cell && cell.InBounds(destination);
            if (field.Name == "mapIndexOrState") return value is sbyte index && (index == -1 || destination != null && index == Find.Maps.IndexOf(destination));
            if (field.Name == nameof(Thing.holdingOwner)) return value == null
                || ReferenceEquals(value, intent.DestinationOwner ?? destination?.GetDirectlyHeldThings());
            return false;
        }

        public static void Detach(Thing thing)
        {
            if (!OperationScope.Transferring && !OperationScope.Terminates(thing) && !OperationScope.Rebirthing && !InHandoff(thing))
                throw new InvalidOperationException("Map transfer requires an authorized lifecycle operation.");
            Map previous = CurrentMap(thing); IntVec3 cell = thing.Position;
            if (previous != null)
            {
                CellRect footprint = thing.OccupiedRect();
                try { if (MapTransferResidue.Contains(previous, thing, footprint)) despawn(thing, DestroyMode.Vanish); }
                finally
                {
                    bool retained = CurrentMap(thing) != null || MapTransferResidue.Contains(previous, thing, footprint);
                    MapTransferResidue.Clear(previous, thing, footprint);
                    mapIndex(thing) = -1;
                    if (retained) ReconciledDepartures++;
                }
                if (CurrentMap(thing) != null || MapTransferResidue.Contains(previous, thing, footprint))
                    throw new InvalidOperationException("Source map retained " + thing.ThingID + " mapIndex=" + mapIndex(thing)
                        + " source=" + previous.uniqueID + " current=" + CurrentMap(thing)?.uniqueID
                        + " things=" + previous.listerThings.AllThings.Contains(thing)
                        + " grid=" + (cell.InBounds(previous) && cell.GetThingList(previous).Contains(thing))
                        + " pawns=" + (thing is Pawn retained && previous.mapPawns.AllPawnsSpawned.Contains(retained))
                        + " holder=" + thing.holdingOwner?.Owner?.GetType().FullName + " targets=" + MapTargetRegistration.Contains(previous, thing));
            }
            ThingOwner holder = thing.holdingOwner;
            if (holder != null)
            {
                if (RegistrationStorage.Holder(holder, thing, false)) remove(holder, thing);
                RegistrationStorage.Holder(holder, thing, true);
                thing.holdingOwner = null;
                if (RegistrationStorage.Holder(holder, thing, false) || thing.holdingOwner != null)
                    throw new InvalidOperationException("Source container retained " + thing.ThingID);
            }
            if (thing is Pawn pawn)
            {
                if (inWorld(Find.WorldPawns, pawn)) removeWorld(Find.WorldPawns, pawn);
                RegistrationStorage.World(pawn, true);
                if (RegistrationStorage.World(pawn, false)) throw new InvalidOperationException("World membership retained " + thing.ThingID);
            }
            RegistrationStorage.Signals(thing, true);
        }

        public static void Place(Thing thing, Map map, IntVec3 cell, Rot4 rotation)
        {
            if (map == null || !Find.Maps.Contains(map) || !cell.InBounds(map))
                throw new InvalidOperationException("Invalid transfer destination for " + thing?.ThingID + ": " + cell);
            if (LihuatGame.Current?.Domains.AllowsDestination(thing, map) == false)
                throw new InvalidOperationException("The pawn is confined to its domain map.");
            if (LihuatGame.Current?.Domains.Barriers.PlaceArrival(thing, map, ref cell) == false)
                throw new InvalidOperationException("No arrival cell outside the destination barrier.");
            using var intent = new MapTransferIntent(thing, map);
            Detach(thing);
            if (thing is Pawn arriving) StarBlood.PawnContinuity.PrepareArrival(arriving);
            spawn(thing, cell, map, rotation, WipeMode.VanishOrMoveAside, false, false);
            if (thing is Pawn) MapCellRegistration.Reconcile(map, thing.OccupiedRect().ExpandedBy(1));
            bool placed = MapRegistrationStorage.Placed(map, thing);
            Pawn pawn = thing as Pawn;
            bool pawnRegistrationValid = pawn == null || PawnMapPresence.Complete(pawn, map) || PawnMapPresence.Restore(pawn, map);
            bool worldMember = thing is Pawn member && RegistrationStorage.World(member, false);
            if (!placed || !pawnRegistrationValid || worldMember)
                throw new InvalidOperationException("Destination registration failed for " + thing.ThingID + " map=" + CurrentMap(thing)?.uniqueID
                    + " expected=" + map.uniqueID + " holder=" + thing.holdingOwner + " placed=" + placed
                    + " pawnRegistration=" + pawnRegistrationValid + " active=" + pawn?.mindState.Active + " world=" + worldMember);
        }

        public static bool Store(Thing thing, ThingOwner holder)
        {
            Map map = ThingOwnerUtility.GetRootMap(holder.Owner);
            if (LihuatGame.Current?.Domains.AllowsDestination(thing, map) == false) return false;
            using var intent = new MapTransferIntent(thing, map);
            Detach(thing);
            return add(holder, thing, false) && thing.holdingOwner == holder && RegistrationStorage.Holder(holder, thing, false) && CurrentMap(thing) == null;
        }
    }
}
