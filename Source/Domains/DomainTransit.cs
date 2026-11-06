using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using LihuatLib.Domains;
using LihuatLib.Mechanisms;

namespace LihuatLib.Domains
{
    internal static class DomainTransit
    {
        public static DomainMember Capture(Pawn pawn)
        {
            IThingHolder holder = pawn.ParentHolder;
            while (holder != null && !(holder is Thing)) holder = holder.ParentHolder;
            var member = new DomainMember { Pawn = pawn, Source = pawn.MapHeld, Position = pawn.PositionHeld,
                Rotation = pawn.Rotation, Container = holder as Thing, Faction = pawn.Faction, DraftedOnEntry = pawn.Drafted };
            DomainContainers.Capture(member);
            return member;
        }

        public static HashSet<Pawn> CaptureArea(Map map, IntVec3 center, float radius)
        {
            var result = new HashSet<Pawn>(Core.ReferenceIdentity<Pawn>.Instance);
            var seen = new HashSet<IThingHolder>();
            var pending = new List<IThingHolder>();
            var children = new List<IThingHolder>();
            foreach (Thing thing in CaptureCandidates(map, center, radius))
            {
                if (thing is Corpse || !thing.PositionHeld.InHorDistOf(center, radius)) continue;
                if (thing is Pawn pawn && !PawnLife.Dead(pawn)) result.Add(pawn);
                if (thing is IThingHolder holder) Collect(holder, result, seen, pending, children);
            }
            return result;
        }

        internal static IEnumerable<Thing> CaptureCandidates(Map map, IntVec3 center, float radius)
        {
            int extent = Mathf.CeilToInt(Mathf.Min(radius, map.Size.x + map.Size.z));
            CellRect bounds = CellRect.CenteredOn(center, extent).ClipInsideMap(map);
            if (bounds.Area >= map.listerThings.AllThings.Count)
            {
                foreach (Thing thing in map.listerThings.AllThings) yield return thing;
                yield break;
            }
            foreach (IntVec3 cell in bounds)
                foreach (Thing thing in cell.GetThingList(map))
                    if (thing.Position == cell) yield return thing;
        }

        private static void Collect(IThingHolder holder, HashSet<Pawn> result, HashSet<IThingHolder> seen,
            List<IThingHolder> pending, List<IThingHolder> children)
        {
            pending.Add(holder);
            while (pending.Count != 0)
            {
                int last = pending.Count - 1;
                IThingHolder current = pending[last];
                pending.RemoveAt(last);
                if (current is Corpse || !seen.Add(current)) continue;
                ThingOwner contents = current.GetDirectlyHeldThings();
                if (contents != null) foreach (Thing thing in contents)
                    if (thing is Pawn pawn && !PawnLife.Dead(pawn)) result.Add(pawn);
                children.Clear();
                current.GetChildHolders(children);
                pending.AddRange(children);
            }
        }

        public static List<Thing> Contents(IEnumerable<DomainArea> areas)
        {
            var things = new HashSet<Thing>();
            foreach (DomainArea area in areas)
            {
                foreach (DomainMember member in area.Members)
                {
                    Thing thing = PawnLife.Dead(member.Pawn) ? (Thing)member.Pawn.Corpse : member.Pawn;
                    if (!member.Returned && thing != null && !thing.Destroyed) things.Add(thing);
                }
                foreach (Map map in new[] { area.Map, area.HomeMap }.Where(m => m != null).Distinct())
                {
                    foreach (Thing thing in map.listerThings.AllThings)
                        if ((thing is Pawn || thing is Corpse || thing.def.category == ThingCategory.Item)
                            && Combat.CombatProxyRegistry.Provider(thing) == null) things.Add(thing);
                    foreach (Pawn pawn in CaptureArea(map, map.Center, map.Size.x + map.Size.z)) things.Add(pawn);
                }
            }
            return things.Where(t => t != null && !t.Destroyed).OrderBy(t => t.Spawned ? 1 : 0).ThenBy(t => t.thingIDNumber).ToList();
        }

        public static DomainMove Snapshot(Thing thing) => new DomainMove { Thing = thing, Map = thing.MapHeld,
            Position = thing.PositionHeld, Rotation = thing.Rotation, HeldPawn = thing is Pawn pawn ? Capture(pawn) : null };

        internal static bool HeldBy(Pawn pawn, HashSet<Pawn> carriers)
        {
            for (IThingHolder holder = pawn.ParentHolder; holder != null; holder = holder.ParentHolder)
                if (holder is Pawn carrier && carriers.Contains(carrier)) return true;
            return false;
        }

        public static IntVec3 EntryPosition(Pawn owner, Thing thing, Map destination, IntVec3 anchor)
        {
            if (thing == owner) return destination.Center;
            Vector3 direction = (thing.PositionHeld - anchor).ToVector3();
            if (direction.sqrMagnitude < 1f) direction = thing.Rotation.FacingCell.ToVector3();
            float radius = Mathf.Clamp(direction.magnitude * .4f, 3f, destination.Size.x / 2f - 2f);
            direction.Normalize();
            return destination.Center + new IntVec3(Mathf.RoundToInt(direction.x * radius), 0, Mathf.RoundToInt(direction.z * radius));
        }

        public static void Restore(DomainMove move, DomainReturnDestinations destinations)
        {
            if (move.Restored) return;
            Thing thing = move.Thing;
            if (thing == null || thing.Destroyed) { move.Restored = true; return; }
            DomainReturnDestination destination = destinations.Resolve(move.Map, move.Position);
            ThingOwner container = move.HeldPawn == null ? null : DomainContainers.Resolve(move.HeldPawn);
            if (container != null && ThingOwnerUtility.GetRootMap(container.Owner) == destination.Map && thing is Pawn pawn && !PawnLife.Dead(pawn))
            {
                if (!LihuatGame.Current.Domains.StoreIn(thing, container)) throw new InvalidOperationException("Domain rollback container rejected " + thing.ThingID);
            }
            else LihuatGame.Current.Domains.TransferTo(thing, destination.Map, destination.Cell);
            thing.Rotation = move.Rotation;
            if (move.HeldPawn != null && move.HeldPawn.Pawn.drafter != null) move.HeldPawn.Pawn.drafter.Drafted = move.HeldPawn.DraftedOnEntry;
            move.Restored = true;
        }

        public static bool Empty(Map map) => map == null || !Contents(new[] { new DomainArea { Map = map, HomeMap = map } }).Any();

        public static void Return(DomainMember member, DomainReturnDestinations destinations)
        {
            if (member.Returned) return;
            Pawn pawn = member.Pawn;
            Thing thing = PawnLife.Dead(pawn) ? (Thing)pawn.Corpse : pawn;
            if (!member.Transferred || thing == null || thing.Destroyed) { member.Returned = true; return; }
            DomainReturnDestination destination = destinations.Resolve(member.Source, member.Position);
            if (!member.ReturnDraftCaptured) { member.ReturnDrafted = pawn.Drafted; member.ReturnDraftCaptured = true; }
            if (pawn.Faction != member.Faction) pawn.SetFaction(member.Faction);
            if (thing.MapHeld == destination.Map) { member.Returned = true; return; }
            ThingOwner container = DomainContainers.Resolve(member);
            if (container != null && ThingOwnerUtility.GetRootMap(container.Owner) == destination.Map && !PawnLife.Dead(pawn))
            {
                if (LihuatGame.Current.Domains.StoreIn(pawn, container)) { member.Returned = true; return; }
            }
            LihuatGame.Current.Domains.TransferTo(thing, destination.Map, destination.Cell);
            thing.Rotation = member.Rotation;
            if (!PawnLife.Dead(pawn) && pawn.drafter != null) pawn.drafter.Drafted = member.ReturnDrafted;
            member.Returned = true;
        }
    }
}
