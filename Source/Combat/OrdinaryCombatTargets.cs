using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using LihuatLib.Domains;
using Verse;
using Verse.AI;

namespace LihuatLib.Combat
{
    internal sealed class OrdinaryCombatTargets : ICombatProxyProvider
    {
        private readonly ConcurrentDictionary<Type, bool> usesThingHealth = new();
        public string Id => "lihuat.thingHealth";
        public bool Owns(Thing proxy) => proxy is IAttackTarget && proxy is not Pawn && proxy is not Building
            && proxy.def.useHitPoints && usesThingHealth.GetOrAdd(proxy.GetType(), type =>
            {
                Type owner = type.GetMethod(nameof(Thing.PreApplyDamage)).DeclaringType;
                return owner == typeof(Thing) || owner == typeof(ThingWithComps);
            });
        public bool Alive(Thing proxy) => proxy.Spawned && !proxy.Destroyed && proxy.HitPoints > 0;
        public object Identity(Thing proxy) => proxy;
        public float CoreHealthMaximum(Thing proxy) => proxy.MaxHitPoints;
        public float CoreHealthCurrent(Thing proxy) => proxy.HitPoints;
        public void Hit(Thing proxy, DamageInfo damage, bool direct)
        {
            if (!direct) { proxy.TakeDamage(damage); return; }
            proxy.HitPoints = (int)Math.Max(0d, proxy.HitPoints - Math.Ceiling(damage.Amount));
            if (proxy.HitPoints == 0) proxy.Kill(damage);
        }
        public IEnumerable<DomainEntity> Capture(Map map, IntVec3 center, float radius)
        {
            foreach (Thing thing in DomainTransit.CaptureCandidates(map, center, radius))
                if (thing.Position.InHorDistOf(center, radius) && CombatProxyRegistry.Provider(thing) == this && Alive(thing))
                    yield return new ThingDomainEntity { Target = thing, Id = thing.ThingID, Map = map, Source = map, Origin = thing.Position };
        }
    }
}
