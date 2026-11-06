using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;
using LihuatLib.StarPaths;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Integrations
{
    internal static class StarPathHitBoundary
    {
        internal static void Install()
        {
            var hit = AccessTools.Method(typeof(Verb_MeleeAttack), "GetNonMissChance");
            var dodge = AccessTools.Method(typeof(Verb_MeleeAttack), "GetDodgeChance");
            var launch = AccessTools.Method(typeof(Projectile), nameof(Projectile.Launch), new[] { typeof(Thing), typeof(Vector3),
                typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) });
            var intercept = AccessTools.Method(typeof(Projectile), "CheckForFreeInterceptBetween");
            IlDispatch.Register(hit, "lihuat.lib", "starpath-hit", NativeIlGuards.ResultOrder + 100,
                body => IlBoundaryEmitter.Filter(hit, body, AccessTools.Method(typeof(StarPathHitRules), nameof(StarPathHitRules.MeleeHit)), new[] { 0 }));
            IlDispatch.Register(dodge, "lihuat.lib", "starpath-dodge", NativeIlGuards.ResultOrder + 100,
                body => IlBoundaryEmitter.Filter(dodge, body, AccessTools.Method(typeof(StarPathHitRules), nameof(StarPathHitRules.MeleeDodge)), new[] { 0 }));
            IlDispatch.Register(launch, "lihuat.lib", "starpath-launch", NativeIlGuards.GateOrder,
                body => Launch(launch, body));
            IlDispatch.Register(intercept, "lihuat.lib", "starpath-intercept", NativeIlGuards.GateOrder,
                body => IndependentIl.Guard(intercept, body, AccessTools.Method(typeof(StarPathHitRules), nameof(StarPathHitRules.AllowsIntercept)), new[] { 0, 1, 2 }));
        }

        private static DynamicMethod Launch(MethodInfo original, MethodInfo continuation)
        {
            DynamicMethod wrapper = IndependentIl.New(original);
            ILGenerator il = wrapper.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarga, (short)3);
            il.Emit(OpCodes.Ldarg, (short)4);
            il.Emit(OpCodes.Ldarga, (short)5);
            il.Emit(OpCodes.Ldarga, (short)8);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(StarPathHitRules), nameof(StarPathHitRules.Aim))));
            for (int i = 0; i < IndependentIl.Parameters(original).Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
