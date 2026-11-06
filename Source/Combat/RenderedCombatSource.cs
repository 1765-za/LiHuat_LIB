using System.Collections.Generic;
using System.Linq;
using LihuatLib.Domains;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Combat
{
    public abstract class RenderedCombatEntity
    {
        public abstract string Id { get; }
        public abstract string Label { get; }
        public abstract object Identity { get; }
        public abstract Map Map { get; }
        public abstract Vector3 Position { get; }
        public abstract Faction Faction { get; }
        public abstract bool Alive { get; }
        public abstract float CoreHealthMaximum { get; }
        public abstract float CoreHealthCurrent { get; }
        public virtual bool HasHealth => true;
        public virtual Thing NativeTarget => null;
        public abstract void Hit(DamageInfo damage, bool direct);
        public virtual bool Retire(Pawn attacker) => false;
        public abstract DomainEntity Capture();
    }

    public abstract class RenderedCombatSource : ICombatProxyProvider, IRetirableCombatProvider
    {
        public abstract string Id { get; }
        public abstract RenderedCombatEntity Find(Map map, string id);
        public abstract IEnumerable<RenderedCombatEntity> Within(Map map, CellRect bounds);
        protected RenderedCombatEntity Resolve(Thing proxy) => proxy is RenderedCombatTarget target ? Find(proxy.Map, target.EntityId) : null;
        public virtual bool Owns(Thing proxy) => proxy is RenderedCombatTarget target && target.SourceId == Id;
        public virtual bool Alive(Thing proxy) => Resolve(proxy)?.Alive == true;
        public virtual object Identity(Thing proxy) => Resolve(proxy)?.Identity;
        public virtual bool HasHealth(Thing proxy) => Resolve(proxy)?.HasHealth == true;
        public virtual bool Retire(Thing proxy, Pawn attacker) => Resolve(proxy)?.Retire(attacker) == true;
        public virtual float CoreHealthMaximum(Thing proxy) => Resolve(proxy)?.CoreHealthMaximum ?? 0f;
        public virtual float CoreHealthCurrent(Thing proxy) => Resolve(proxy)?.CoreHealthCurrent ?? 0f;
        public virtual void Hit(Thing proxy, DamageInfo damage, bool direct) => Resolve(proxy)?.Hit(damage, direct);
        public virtual bool HostileTo(Thing proxy, Thing other) => Resolve(proxy)?.Faction?.HostileTo(other.Faction) == true;
        public IEnumerable<DomainEntity> Capture(Map map, IntVec3 center, float radius) =>
            Within(map, CellRect.CenteredOn(center, Mathf.CeilToInt(radius)).ClipInsideMap(map))
                .Where(e => e.Alive && e.Position.ToIntVec3().InHorDistOf(center, radius)).Select(e => e.Capture());
    }
}
