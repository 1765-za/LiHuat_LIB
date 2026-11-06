using System;
using System.Linq;
using LihuatLib.Core;
using LihuatLib.Domains;
using LihuatLib.Mechanisms;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Combat
{
    public sealed class ManagedCombatDomainEntity : DomainEntity
    {
        public string SourceId, EntityId;
        public override string Key => SourceId + ":" + EntityId;
        private ManagedCombatShape Shape => ManagedCombatDiscovery.Source(Map, SourceId)?.Shape
            ?? throw new InvalidOperationException("Combat state source unavailable: " + SourceId);
        private object State => Shape.Find(Map, EntityId);
        private Thing Remains(ManagedCombatShape shape) => Map.listerThings.AllThings.FirstOrDefault(t => shape.Embedded(t) is object state && shape.Key(state) == EntityId);
        public override IntVec3 Position => State is object state ? Shape.Position(state).ToIntVec3() : Remains(Shape)?.Position ?? Origin;
        public override Faction Faction => State is object state ? Shape.Faction(state) : Remains(Shape)?.Faction;

        public override void Move(Map destination, IntVec3 cell)
        {
            if (!OperationScope.Transferring) throw new InvalidOperationException("Combat state transfer requires a domain operation.");
            if (destination == Map) return;
            ManagedCombatShape shape = Shape;
            object state = shape.Find(Map, EntityId);
            if (state != null && shape.Health.IsDead(state) && shape.Settle != null)
            {
                object settled = shape.Settle.Invoke(shape.Manager(Map), new[] { state });
                if (settled is false) throw new InvalidOperationException("Combat remains settlement rejected.");
                state = shape.Find(Map, EntityId);
            }
            if (state == null)
            {
                if (Map.GetComponent<CombatRetirementMap>().Contains(SourceId, EntityId))
                {
                    destination.GetComponent<CombatRetirementMap>().Record(SourceId, EntityId);
                    ManagedCombatDiscovery.RestoreRetirements(destination);
                }
                Thing remains = Remains(shape);
                if (remains != null)
                {
                    int margin = Math.Max(remains.def.size.x, remains.def.size.z) / 2 + 1;
                    cell.x = Mathf.Clamp(cell.x, margin, destination.Size.x - margin - 1);
                    cell.z = Mathf.Clamp(cell.z, margin, destination.Size.z - margin - 1);
                    shape.Position(shape.Embedded(remains), cell);
                    MapTransfer.Place(remains, destination, cell, remains.Rotation);
                }
                Map = destination; return;
            }
            Map previous = Map;
            IntVec3 position = shape.Position(state).ToIntVec3();
            var source = shape.States(previous);
            var target = shape.States(destination);
            try
            {
                shape.Detach.Invoke(state, null);
                source.Remove(state);
                target.Add(state);
                shape.Arrive.Invoke(state, new object[] { destination, cell.ClampInsideMap(destination) });
                if (shape.Map(state) != destination) throw new InvalidOperationException("Combat state arrival did not acquire the destination map.");
                Map = destination;
            }
            catch
            {
                shape.Detach.Invoke(state, null);
                target.Remove(state);
                if (!source.Contains(state)) source.Add(state);
                shape.Arrive.Invoke(state, new object[] { previous, position });
                throw;
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref SourceId, "sourceId");
            Scribe_Values.Look(ref EntityId, "entityId");
        }
    }
}
