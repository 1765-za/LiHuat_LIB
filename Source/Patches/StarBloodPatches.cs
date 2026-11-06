using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using LihuatLib.Core;
using LihuatLib.StarBlood;

namespace LihuatLib.Patches
{
    [DispatchTarget]
    [DispatchOrder(560)]
    internal static class StarBloodDamagePatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo apply = AccessTools.Method(typeof(DamageWorker), nameof(DamageWorker.Apply));
            MethodInfo dispatch = AccessTools.Method(typeof(DamageEffectDispatcher), nameof(DamageEffectDispatcher.Apply));
            foreach (CodeInstruction instruction in instructions)
            {
                if (__originalMethod.DeclaringType == typeof(Thing) && instruction.Calls(apply))
                { instruction.opcode = OpCodes.Call; instruction.operand = dispatch; }
                yield return instruction;
            }
        }
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage));
            HashSet<MethodBase> seen = new HashSet<MethodBase>();
            foreach (DamageDef def in DefDatabase<DamageDef>.AllDefsListForReading)
            {
                MethodInfo method = AccessTools.Method(def.workerClass, "Apply", new[] { typeof(DamageInfo), typeof(Thing) });
                if (method?.GetMethodBody() != null && seen.Add(method)) yield return method;
            }
        }
        [DispatchPriority(int.MaxValue)]
        static bool Prefix(object __instance, object[] __args, ref DamageInfo __0, ref DamageWorker.DamageResult __result, out DamageEntryState __state)
        {
            DamageInfo info = __0;
            Thing victim = __instance as Thing ?? __args[1] as Thing;
            __state = default;
            __state.Preparation = LihuatGame.Current?.DamagePreparation.Enter(victim, ref info, __instance is Thing);
            __0 = info; __args[0] = info;
            if (LihuatGame.Current?.Domains.Barriers.BlocksDamage(victim, info) == true)
            {
                LihuatGame.Current.Domains.Barriers.NotifyDamage(victim, info);
                __result = new DamageWorker.DamageResult(); return false;
            }
            if (__instance is Pawn astralPawn && LihuatGame.Current?.AstralArts.TryBlockOrdinary(astralPawn, info) == true)
            { __result = new DamageWorker.DamageResult { deflected = true }; return false; }
            Pawn target = victim as Pawn;
            if (__instance is Thing) __state.StarPath = DamageEffectDispatcher.Begin(victim, info);
            __state.Attack = StarBloodRegistry.For(target)?.Begin(target, info.Instigator, info.Def.harmsHealth && info.Amount > 0f);
            return true;
        }
        [DispatchPriority(int.MinValue)]
        static Exception Finalizer(Exception __exception, DamageEntryState __state)
        {
            try
            {
                if (__state.Attack != null)
                {
                    if (__exception == null) __state.Attack.Effective = true;
                    __state.Attack.Dispose();
                }
            }
            finally { try { __state.StarPath?.Dispose(); } finally { __state.Preparation?.Dispose(); } }
            return __exception;
        }
    }

    [DispatchTarget(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    [DispatchOrder(570)]
    internal static class StarBloodHediffPatch
    {
        [DispatchPriority(int.MaxValue)]
        static void Prefix(Pawn ___pawn, Hediff hediff, DamageInfo? dinfo, out HediffEntryState __state)
        {
            __state = default;
            __state.Injury = InjuryEffectDispatcher.Admit(___pawn, hediff, dinfo);
            __state.Attack = dinfo.HasValue ? StarBloodRegistry.For(___pawn)?.Begin(___pawn, dinfo.Value.Instigator, dinfo.Value.Def.harmsHealth && dinfo.Value.Amount > 0f) : null;
        }
        [DispatchPriority(int.MinValue)]
        static Exception Finalizer(Exception __exception, HediffEntryState __state)
        {
            try
            { if (__state.Attack != null) { __state.Attack.Effective = __exception == null; __state.Attack.Dispose(); } }
            finally { __state.Injury?.Dispose(); }
            return __exception;
        }
    }

    internal struct HediffEntryState
    {
        internal StarBloodRules.Attack Attack;
        internal InjuryAdmission Injury;
    }

    internal struct DamageEntryState
    {
        internal StarBloodRules.Attack Attack;
        internal StarPaths.StarPathAttackScope StarPath;
        internal IDisposable Preparation;
    }
}
