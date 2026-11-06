using System;
using System.Collections.Generic;
using LihuatLib.Domains;
using Verse;

namespace LihuatLib.Combat
{
    internal sealed class AttachedCombatTargets : ICombatProxyProvider
    {
        private readonly Dictionary<Type, AttachedHealthPlan[]> plans = new();
        private readonly record struct Binding(AttachedHealthPlan Health, object State);
        public string Id => "lihuat.attachedHealth";

        private bool Resolve(Thing target, out Binding binding) => Resolve(target, out binding, out _);
        private bool Resolve(Thing target, out Binding binding, out bool recognized)
        {
            binding = default;
            recognized = false;
            if (target == null) return false;
            int count = 0;
            if (!Collect(target, target, false, ref binding, ref count, ref recognized)) return false;
            if (target is ThingWithComps withComps)
                foreach (ThingComp comp in withComps.AllComps)
                    if (!Collect(comp, target, true, ref binding, ref count, ref recognized)) return false;
            return count == 1;
        }

        private bool Collect(object host, Thing target, bool component, ref Binding result, ref int count, ref bool recognized)
        {
            Type type = host.GetType();
            if (!plans.TryGetValue(type, out AttachedHealthPlan[] found)) plans[type] = found = AttachedHealthPlan.Discover(type, component);
            recognized |= found.Length > 0;
            foreach (AttachedHealthPlan plan in found)
            {
                object state = plan.State(host, target);
                if (state == null) continue;
                if (count > 0 && ReferenceEquals(result.State, state) && result.Health.Damage == plan.Damage) continue;
                if (++count > 1) return false;
                result = new Binding(plan, state);
            }
            return true;
        }

        public bool Owns(Thing target) => Resolve(target, out _);
        internal ICombatProxyProvider Select(Thing target, ICombatProxyProvider ordinary)
        {
            if (Resolve(target, out _, out bool recognized)) return this;
            return !recognized && target != null && ordinary.Owns(target) ? ordinary : null;
        }
        public bool Alive(Thing target) => !target.Destroyed && target.Spawned && (target is not Pawn pawn || !pawn.Dead)
            && Resolve(target, out Binding bound) && bound.Health.Alive(bound.State);
        public object Identity(Thing target) => target;
        public float CoreHealthCurrent(Thing target)
        {
            float value = Resolve(target, out Binding bound) ? bound.Health.Current(bound.State) : 0f;
            return float.IsFinite(value) ? value : 0f;
        }
        public float CoreHealthMaximum(Thing target) => Resolve(target, out Binding bound) ? bound.Health.Capacity(bound.State) : 0f;
        public void Hit(Thing target, DamageInfo damage, bool direct)
        {
            if (!direct) { target.TakeDamage(damage); return; }
            if (Resolve(target, out Binding bound) && bound.Health.Alive(bound.State)) bound.Health.Hit(bound.State, damage);
        }
        public IEnumerable<DomainEntity> Capture(Map map, IntVec3 center, float radius)
        {
            foreach (Thing target in DomainTransit.CaptureCandidates(map, center, radius))
                if (target is not Pawn && target.Position.InHorDistOf(center, radius) && CombatProxyRegistry.Provider(target) == this && Alive(target))
                    yield return new ThingDomainEntity { Target = target, Id = target.ThingID, Map = map, Source = map, Origin = target.Position };
        }
    }
}
