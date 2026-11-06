using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using LihuatLib.Domains;
using LihuatLib.Mechanisms;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private bool finishingClosures;

        public void Flush()
        {
            if (finishingClosures || !pendingClosure || UnityEngine.Time.realtimeSinceStartup < nextFinishAt) return;
            finishingClosures = true;
            try
            {
                foreach (Encounter group in encounters.Values.ToArray()) if (group.Areas.Any(a => a.Ending)) Finish(group);
            }
            finally { finishingClosures = false; }
        }

        private void RestoreEntry()
        {
            if (rollback == null) return;
            try
            {
                using (new OperationScope(OperationKind.Transfer))
                {
                    var destinations = new DomainReturnDestinations(new[] { rollback.Area }, rollback: true);
                    foreach (DomainEntityMove move in rollback.EntityPositions.AsEnumerable().Reverse())
                    {
                        DomainReturnDestination destination = destinations.Resolve(move.Map, move.Position);
                        move.Entity.Move(destination.Map, destination.Cell);
                    }
                    foreach (DomainMove move in rollback.Positions.AsEnumerable().Reverse()) DomainTransit.Restore(move, destinations);
                    DomainArea area = rollback.Area;
                    area.HomeMap = area.HomeMap ?? area.Parent?.Map;
                    DomainMapRetirement.Remove(area);
                    rollback = null;
                }
                RefreshAll();
            }
            catch (Exception ex) { Log.ErrorOnce("[LihuatLib.Domain] Expansion rollback retained: " + ex, 14581571); }
        }

        private void Finish(Encounter group)
        {
            if (rollback?.Positions.Any(p => !p.Restored && p.Map != null
                && byMap.TryGetValue(p.Map, out Encounter needed) && needed == group) == true) return;
            try
            {
                DomainArea[] ending;
                do
                {
                    ending = group.Areas.Where(a => a.Ending).ToArray();
                    foreach (DomainArea area in ending) PrepareClosure(area);
                }
                while (group.Areas.Any(a => a.Ending && !a.ClosurePrepared));
                DomainArea[] remaining = group.Areas.Where(a => !a.Ending).Concat(external.Where(a => !a.Ending && !a.Closed
                    && !group.Areas.Contains(a) && ending.Any(e => Intersects(e, a)))).Distinct().ToArray();
                if (!group.Areas.Any(a => a.Enclosed))
                {
                    RetreatSurface(ending, remaining);
                    CompleteClosures(ending); return;
                }
                List<Thing> things = DomainTransit.Contents(group.Areas);
                DomainEntityTransit.CollectBornInside(group.Areas, group.Map);
                var members = group.Areas.SelectMany(a => a.Members).ToList();
                var indexed = new HashSet<Pawn>(members.Where(m => m.Pawn != null).Select(m => m.Pawn), Core.ReferenceIdentity<Pawn>.Instance);
                DomainArea store = remaining.FirstOrDefault() ?? group.Areas[0];
                foreach (Pawn pawn in things.OfType<Pawn>())
                    if (indexed.Add(pawn))
                    {
                        DomainMember added = DomainTransit.Capture(pawn);
                        added.Position = DomainCoordinates.OnSource(group.Areas, group.Map, added.Source, added.Position);
                        added.Source = store.Source; added.Container = null;
                        added.ContainerPath = null; added.Transferred = true;
                        store.Members.Add(added); members.Add(added);
                    }
                using (new OperationScope(OperationKind.Transfer))
                {
                    foreach (DomainArea area in ending) DomainShellRules.Remove(area);
                    if (remaining.Length > 0)
                    {
                        DomainArea survivor = remaining.OrderByDescending(a => a.Enclosed ? a.Size : 0)
                            .ThenByDescending(a => a.HomeMap == group.Map).ThenBy(a => a.Id, StringComparer.Ordinal).First();
                        Map destination = survivor.HomeMap ?? survivor.Source;
                        bool shared = group.Areas.Contains(survivor);
                        DomainArea frame = shared ? survivor : DomainCoordinates.Frame(group.Areas, group.Map);
                        IntVec3 destinationCenter = survivor.Enclosed ? destination.Center : frame.Anchor;
                        IntVec3 translation = destinationCenter - frame.MapCenter;
                        foreach (DomainEntity entity in group.Areas.SelectMany(a => a.Entities).Distinct())
                        {
                            IntVec3 current = DomainCoordinates.InArena(group.Areas, group.Map, entity.Map, entity.Position);
                            entity.Move(destination, RetreatCell(survivor, destination, current + translation));
                        }
                        foreach (Thing thing in things)
                        {
                            if (thing.MapHeld == destination) continue;
                            IntVec3 current = DomainCoordinates.InArena(group.Areas, group.Map, thing.MapHeld, thing.PositionHeld);
                            TransferTo(thing, destination, RetreatCell(survivor, destination, current + translation));
                        }
                        foreach (DomainArea area in group.Areas) DomainCoordinates.Move(area, destination, translation);
                        foreach (DomainArea area in ending)
                        {
                            foreach (DomainMember member in area.Members) if (!survivor.Members.Contains(member)) survivor.Members.Add(member);
                            area.Members.Clear();
                            foreach (DomainEntity entity in area.Entities) if (!survivor.Entities.Contains(entity)) survivor.Entities.Add(entity);
                            area.Entities.Clear();
                        }
                        Current.Game.CurrentMap = destination;
                    }
                    else
                    {
                        if (things.Count > 0 || group.Areas.Any(area => area.Entities.Count > 0))
                        {
                            var destinations = new DomainReturnDestinations(group.Areas);
                            DomainReturnDestination exit = destinations.Resolve(store.Source, store.Anchor);
                            DomainEntityTransit.Return(group.Areas, destinations);
                            foreach (DomainMember member in members) DomainTransit.Return(member, destinations);
                            foreach (Thing thing in DomainTransit.Contents(group.Areas))
                            {
                                IntVec3 position = DomainCoordinates.OnSource(group.Areas, group.Map, thing.MapHeld, thing.PositionHeld);
                                DomainReturnDestination destination = destinations.Resolve(store.Source, position);
                                TransferTo(thing, destination.Map, destination.Cell);
                            }
                            Current.Game.CurrentMap = exit.Map;
                        }
                    }
                    foreach (DomainArea area in ending) DomainMapRetirement.Remove(area);
                    CompleteClosures(ending);
                }
            }
            catch (Exception ex)
            {
                Rebuild(); RefreshAll();
                nextFinishAt = UnityEngine.Time.realtimeSinceStartup + 1f;
                Log.ErrorOnce("[LihuatLib.Domain] Collapse transfer retained: " + ex, 14581572);
            }
        }

    }
}
