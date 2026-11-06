using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.StarPaths
{
    internal static class StarPathHitRules
    {
        private static readonly AccessTools.FieldRef<Projectile, Vector3> origin = AccessTools.FieldRefAccess<Projectile, Vector3>("origin");
        internal static float MeleeHit(Verb_MeleeAttack verb, float result)
            => StarPathCombat.GuaranteedHit(verb.CasterPawn) ? 1f : result;

        internal static float MeleeDodge(Verb_MeleeAttack verb, float result)
            => StarPathCombat.GuaranteedHit(verb.CasterPawn) ? 0f : result;

        internal static void Aim(Thing launcher, ref LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
            ref ProjectileHitFlags hitFlags, ref ThingDef targetCoverDef)
        {
            if (!intendedTarget.IsValid || !StarPathCombat.GuaranteedHit(launcher as Pawn)) return;
            usedTarget = intendedTarget;
            hitFlags |= ProjectileHitFlags.IntendedTarget;
            targetCoverDef = null;
        }

        internal static bool AllowsIntercept(Projectile projectile, Vector3 last, Vector3 next)
            => !StarPathCombat.GuaranteedHit(projectile.Launcher as Pawn)
                || LihuatGame.Current.Domains.Barriers.Intercepts(projectile.Map, origin(projectile), last, next);
    }
}
