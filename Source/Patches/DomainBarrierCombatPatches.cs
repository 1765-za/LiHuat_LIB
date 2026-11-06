using LihuatLib.Dispatching;
using HarmonyLib;
using System;
using LihuatLib.Domains;
using UnityEngine;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(Projectile), "CheckForFreeInterceptBetween")]
    [DispatchOrder(240)]
    internal static class DomainBarrierProjectilePatch
    {
        static bool Prefix(Projectile __instance, Vector3 ___origin, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            if (LihuatGame.Current?.Domains.Barriers.Intercepts(__instance.Map, ___origin, lastExactPos, newExactPos) != true) return true;
            LihuatGame.Current.Domains.Barriers.NotifyImpact(__instance.Map, lastExactPos);
            __instance.Destroy(DestroyMode.Vanish);
            __result = true;
            return false;
        }
    }

    [DispatchTarget(typeof(Explosion), "AffectCell")]
    [DispatchOrder(250)]
    internal static class DomainBarrierExplosionPatch
    {
        static bool Prefix(Explosion __instance, IntVec3 c, out DomainImpactScope __state)
        {
            __state = default;
            var barriers = LihuatGame.Current?.Domains.Barriers;
            if (barriers?.Active != true) return true;
            if (barriers.Blocks(__instance.Map, __instance.Position, __instance.Map, c))
            { barriers.NotifyImpact(__instance.Map, __instance.Position.ToVector3Shifted()); return false; }
            __state = new DomainImpactScope(__instance.Map, __instance.Position);
            return true;
        }
        static Exception Finalizer(Exception __exception, DomainImpactScope __state)
        { __state.Dispose(); return __exception; }
    }
}
