using System;
using System.Collections.Generic;
using System.Linq;
using LihuatLib.Domains;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Combat
{
    internal sealed class ManagedCombatSource(ManagedCombatShape shape) : RenderedCombatSource, IRetirableCombatProvider
    {
        internal ManagedCombatShape Shape => shape;
        public override string Id => shape.Id;
        private object State(Thing thing) => thing is RenderedCombatTarget target ? shape.Find(thing.Map, target.EntityId)
            : shape.ProxyState?.Invoke(thing, null);
        public override bool Owns(Thing proxy) => shape.ProxyState?.DeclaringType.IsInstanceOfType(proxy) == true || base.Owns(proxy);
        public override bool Alive(Thing proxy) => State(proxy) is object state && shape.Map(state) == proxy.Map && !shape.Health.IsDead(state);
        public override object Identity(Thing proxy) => State(proxy);
        public override bool HasHealth(Thing proxy) => true;
        public override bool Retire(Thing proxy, Pawn attacker) => ManagedCombatRetirement.Retire(shape, State(proxy), attacker);
        public override bool HostileTo(Thing proxy, Thing other) => State(proxy) is object state && shape.Faction(state)?.HostileTo(other.Faction) == true;
        public override float CoreHealthMaximum(Thing proxy) => State(proxy) is object state ? shape.Health.Capacity(state) : 0f;
        public override float CoreHealthCurrent(Thing proxy) => State(proxy) is object state ? shape.Health.Current(state) : 0f;
        public override void Hit(Thing proxy, DamageInfo damage, bool direct)
        {
            object state = State(proxy);
            if (state == null || shape.Health.IsDead(state)) return;
            int part = proxy is RenderedCombatTarget || shape.ProxyPart == null ? shape.Health.Core : (int)shape.ProxyPart.GetValue(proxy);
            shape.Health.Hit(state, part, damage, direct);
        }
        public override RenderedCombatEntity Find(Map map, string id) => shape.Find(map, id) is object state ? new ManagedCombatEntity(shape, state) : null;
        public override IEnumerable<RenderedCombatEntity> Within(Map map, CellRect bounds) => shape.States(map).Cast<object>()
            .Where(s => shape.Map(s) == map && !shape.Health.IsDead(s) && bounds.Contains(shape.Position(s).ToIntVec3()))
            .Select(s => new ManagedCombatEntity(shape, s));
    }

    internal sealed class ManagedCombatEntity(ManagedCombatShape shape, object state) : RenderedCombatEntity
    {
        public override string Id => shape.Key(state);
        public override string Label => NativeTarget?.LabelNoCount ?? DefDatabase<ThingDef>.GetNamed("Lihuat_RenderedCombatTarget").label;
        public override object Identity => state;
        public override Map Map => shape.Map(state);
        public override Vector3 Position => shape.Position(state);
        public override Faction Faction => shape.Faction(state);
        public override bool Alive => !shape.Health.IsDead(state);
        public override float CoreHealthMaximum => shape.Health.Capacity(state);
        public override float CoreHealthCurrent => shape.Health.Current(state);
        public override Thing NativeTarget => shape.NativeTarget(state);
        public override void Hit(DamageInfo damage, bool direct) => shape.Health.Hit(state, shape.Health.Core, damage, direct);
        public override DomainEntity Capture() => new ManagedCombatDomainEntity { SourceId = shape.Id, EntityId = Id, Map = Map, Source = Map, Origin = Position.ToIntVec3() };
    }
}
