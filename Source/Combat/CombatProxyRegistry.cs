using System;
using System.Collections.Generic;
using System.Linq;
using LihuatLib.Domains;
using LihuatLib.Configuration;
using RimWorld;
using Verse;

namespace LihuatLib.Combat
{
    public interface ICombatProxyProvider
    {
        string Id { get; }
        bool Owns(Thing proxy);
        bool Alive(Thing proxy);
        object Identity(Thing proxy);
        float CoreHealthMaximum(Thing proxy);
        float CoreHealthCurrent(Thing proxy);
        bool HasHealth(Thing proxy) => true;
        void Hit(Thing proxy, DamageInfo damage, bool direct);
        bool HostileTo(Thing proxy, Thing other) => proxy.HostileTo(other);
        IEnumerable<DomainEntity> Capture(Map map, IntVec3 center, float radius);
    }

    public interface IRetirableCombatProvider
    {
        bool Retire(Thing proxy, Pawn attacker);
    }

    public static class CombatProxyRegistry
    {
        private static readonly List<ICombatProxyProvider> providers = new();
        private static readonly ICombatProxyProvider ordinary = new OrdinaryCombatTargets();
        private static readonly AttachedCombatTargets attached = new();
        public static void Register(ICombatProxyProvider provider)
        {
            if (providers.Any(p => p.Id == provider.Id)) throw new InvalidOperationException("Duplicate combat proxy provider: " + provider.Id);
            providers.Add(provider);
            providers.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        }
        public static ICombatProxyProvider Provider(Thing proxy)
        {
            if (proxy is Verse.AI.IAttackTarget && proxy is not Pawn && proxy is not Building || proxy is RenderedCombatTarget)
                ManagedCombatDiscovery.Prepare(proxy.Map);
            if (proxy != null) foreach (ICombatProxyProvider provider in providers) if (provider.Owns(proxy)) return provider;
            return attached.Select(proxy, ordinary);
        }
        public static bool Alive(Thing proxy) => proxy?.Spawned == true && !proxy.Destroyed && Provider(proxy)?.Alive(proxy) == true;
        public static bool HasHealth(Thing proxy) => Provider(proxy)?.HasHealth(proxy) == true;
        public static bool Retire(Thing proxy, Pawn attacker) => Provider(proxy) is IRetirableCombatProvider provider && provider.Retire(proxy, attacker);
        public static object Identity(Thing proxy) => Provider(proxy)?.Identity(proxy) ?? proxy;
        public static bool HostileTo(Thing proxy, Thing other) => Provider(proxy)?.HostileTo(proxy, other) ?? proxy.HostileTo(other);
        public static float CoreHealthMaximum(Thing proxy) => Provider(proxy)?.CoreHealthMaximum(proxy) ?? 0f;
        public static float Drain(Thing proxy, Pawn caster, float fraction)
        {
            if (!LibraryMod.Mechanics.Allows(GameplayMechanic.PercentageDamage)) return 0f;
            ICombatProxyProvider provider = Provider(proxy);
            if (provider == null || !provider.Alive(proxy)) return 0f;
            float before = provider.CoreHealthCurrent(proxy);
            float amount = Math.Min(provider.CoreHealthMaximum(proxy) * fraction, Math.Max(0f, before - 1f));
            if (amount <= 0f) return 0f;
            provider.Hit(proxy, new DamageInfo(RimWorld.DamageDefOf.Cut, amount, instigator: caster, intendedTarget: proxy), true);
            return Math.Max(0f, before - provider.CoreHealthCurrent(proxy));
        }
        public static bool Hit(Thing proxy, DamageInfo damage, bool direct)
        {
            ICombatProxyProvider provider = Provider(proxy);
            if (provider == null) return false;
            if (!provider.HasHealth(proxy)) return false;
            if (provider.Alive(proxy) && damage.Amount > 0f) provider.Hit(proxy, damage, direct);
            return true;
        }
        internal static IEnumerable<DomainEntity> Capture(Map map, IntVec3 center, float radius)
        {
            ManagedCombatDiscovery.Prepare(map);
            return providers.ToArray().SelectMany(p => p.Capture(map, center, radius)).Concat(attached.Capture(map, center, radius)).Concat(ordinary.Capture(map, center, radius));
        }

        public static void PrepareTargets(Pawn caster, CellRect bounds)
        {
            Map map = caster.Map;
            ManagedCombatDiscovery.Prepare(map);
            foreach (RenderedCombatSource source in providers.OfType<RenderedCombatSource>())
                foreach (RenderedCombatEntity entity in source.Within(map, bounds).ToList())
                {
                    object identity = entity.Identity;
                    if (identity != null && !identity.GetType().IsValueType && identity is not MapComponent
                        && identity is not GameComponent && identity is not RimWorld.Planet.WorldComponent)
                        Core.RetirementSubjects.Register(identity.GetType());
                    map.GetComponent<RenderedTargetMap>().Prepare(source, entity, caster);
                }
        }
    }
}
