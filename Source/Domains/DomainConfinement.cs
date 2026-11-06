using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Mechanisms;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> rawMap = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly AccessTools.FieldRef<Thing, IntVec3> rawPosition = AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt");
        private readonly Dictionary<Pawn, IntVec3> lastContainedCells = new Dictionary<Pawn, IntVec3>(Core.ReferenceIdentity<Pawn>.Instance);
        private readonly List<Pawn> confinement = new List<Pawn>();
        private readonly Dictionary<Pawn, int> nextConfinementWarning = new Dictionary<Pawn, int>(Core.ReferenceIdentity<Pawn>.Instance);
        public long PreventedDepartures { get; private set; }
        public long RecoveredDepartures { get; private set; }

        private IEnumerable<Pawn> ContainedBy(Thing thing)
        {
            if (thing is Pawn pawn) { if (byPawn.ContainsKey(pawn)) yield return pawn; yield break; }
            if (!(thing is IThingHolder) && (!(thing is ThingWithComps comps) || !comps.AllComps.Any(c => c is IThingHolder))) yield break;
            foreach (Pawn member in confinement) if (MapTransferIntent.BelongsTo(member, thing)) yield return member;
        }

        internal bool AllowsDestination(Thing thing, Map destination)
        {
            if (thing == null || byPawn.Count == 0 || DeveloperExecution.Active || Scribe.mode == LoadSaveMode.LoadingVars) return true;
            if (thing is Pawn arrival && !byPawn.ContainsKey(arrival) && !AllowsSpawn(arrival, destination)) return false;
            foreach (Pawn pawn in ContainedBy(thing))
                if (byPawn.TryGetValue(pawn, out Encounter group) && group.Map != destination
                    && !MapTransferIntent.AuthorizedDestination(pawn, destination)) return false;
            return true;
        }
        internal bool AllowsDeparture(Thing thing)
        {
            if (thing == null || byPawn.Count == 0 || DeveloperExecution.Active || OperationScope.Terminates(thing)) return true;
            foreach (Pawn pawn in ContainedBy(thing))
                if (!PawnLife.Dead(pawn) && !pawn.Destroyed && !Core.LifecycleExecution.EndingLife(pawn) && byPawn.TryGetValue(pawn, out Encounter group)
                    && !MapTransferIntent.AllowsDeparture(pawn, group.Map)) return false;
            return true;
        }
        internal bool AllowsWorld(Pawn pawn) => pawn == null || DeveloperExecution.Active || PawnLife.Dead(pawn) || !byPawn.ContainsKey(pawn);
        internal bool AllowsCell(Thing thing, IntVec3 cell)
        {
            if (!Barriers.AllowsMove(thing, cell)) return false;
            if (!(thing is Pawn pawn) || !byPawn.TryGetValue(pawn, out Encounter group) || DeveloperExecution.Active) return true;
            return cell.InBounds(group.Map) || MapTransferIntent.AllowsDeparture(pawn, group.Map);
        }
        internal bool AllowsRegistrationRemoval(Thing thing, bool movement = false)
            => movement && PawnContinuity.IsMoving(thing) || AllowsDeparture(thing);

        internal bool BlocksDeparture(Pawn pawn, DestructiveEntry entry)
        {
            if (entry != DestructiveEntry.DeSpawn && entry != DestructiveEntry.WorldGC) return false;
            bool blocked = !AllowsDeparture(pawn);
            if (blocked) PreventedDepartures++;
            return blocked;
        }
        internal bool BlocksMapRemoval(Map map) => !OperationScope.Transferring && Protects(map);
        internal bool AllowsLocationWrite(FieldInfo field, Thing thing, object value)
        {
            if (field.DeclaringType == typeof(Thing) && field.Name == "positionInt" && value is IntVec3 destination
                && !Barriers.AllowsMove(thing, destination)) return false;
            if (field.DeclaringType != typeof(Thing) || byPawn.Count == 0 || DeveloperExecution.Active
                || Scribe.mode == LoadSaveMode.LoadingVars || OperationScope.Terminates(thing)) return true;
            if (field.Name == "mapIndexOrState" && value is sbyte map)
                return map < -1 || (map == -1 ? AllowsDeparture(thing)
                    : map < Find.Maps.Count && AllowsDestination(thing, Find.Maps[map]));
            if (field.Name == "positionInt" && value is IntVec3 cell) return AllowsCell(thing, cell);
            if (field.Name == nameof(Thing.holdingOwner))
                return value is ThingOwner holder ? AllowsDestination(thing, ThingOwnerUtility.GetRootMap(holder.Owner)) : AllowsDeparture(thing);
            return true;
        }

        private void RebuildConfinement()
        {
            confinement.Clear(); confinement.AddRange(byPawn.Keys);
            foreach (Pawn stale in lastContainedCells.Keys.Where(p => !byPawn.ContainsKey(p)).ToArray()) lastContainedCells.Remove(stale);
            foreach (Pawn stale in nextConfinementWarning.Keys.Where(p => !byPawn.ContainsKey(p)).ToArray()) nextConfinementWarning.Remove(stale);
            foreach (Pawn pawn in confinement) RememberContained(pawn);
        }
        private void RememberContained(Pawn pawn)
        {
            if (byPawn.TryGetValue(pawn, out Encounter group) && pawn.MapHeld == group.Map && pawn.PositionHeld.InBounds(group.Map))
                lastContainedCells[pawn] = pawn.PositionHeld;
        }
        private void ReconcileConfinement()
        {
            for (int i = 0; i < confinement.Count; i++)
            {
                Pawn pawn = confinement[i];
                if (pawn == null || pawn.Destroyed || PawnLife.Dead(pawn) || !byPawn.TryGetValue(pawn, out Encounter group)
                    || group.Map == null || !Find.Maps.Contains(group.Map) || group.Areas.All(a => a.Ending || a.Closed)) continue;
                int index = rawMap(pawn);
                Map actual = index >= 0 && index < Find.Maps.Count ? Find.Maps[index] : null;
                bool valid = actual == group.Map ? PawnMapPresence.Registered(pawn, group.Map)
                    : index == -1 && pawn.holdingOwner != null && pawn.MapHeld == group.Map;
                if (valid) { RememberContained(pawn); continue; }
                IntVec3 cell = lastContainedCells.TryGetValue(pawn, out IntVec3 previous) ? previous : group.Map.Center;
                try
                {
                    using (new OperationScope(OperationKind.Transfer))
                    {
                        if (actual != group.Map)
                            MapTransferResidue.Clear(group.Map, pawn, GenAdj.OccupiedRect(cell, pawn.Rotation, pawn.def.size));
                        TransferTo(pawn, group.Map, cell);
                    }
                    RecoveredDepartures++; RememberContained(pawn);
                }
                catch (Exception error)
                {
                    int now = Find.TickManager.TicksGame;
                    if (!nextConfinementWarning.TryGetValue(pawn, out int next) || now >= next)
                    { nextConfinementWarning[pawn] = now + 1200; Log.Warning("[LihuatLib.Domain] Member return incomplete: " + pawn.ThingID + " " + error.GetType().Name); }
                }
            }
        }
    }
}
