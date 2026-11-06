using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using HarmonyLib;
using LihuatLib.AstralArts;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(HediffSet), nameof(HediffSet.AddDirect))]
    [DispatchOrder(100)]
    internal static class AstralInjuryPatch
    {
        [DispatchPriority(int.MaxValue)]
        static void Prefix(Pawn ___pawn, Hediff hediff, out InjuryAdmission __state) => __state = InjuryEffectDispatcher.Admit(___pawn, hediff, null);
        static Exception Finalizer(Exception __exception, InjuryAdmission __state)
        { __state?.Dispose(); return __exception; }
    }
    [DispatchTarget(typeof(Pawn), nameof(Pawn.GetGizmos))]
    [DispatchOrder(110)]
    internal static class AstralGizmosPatch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result) =>
            __result = PawnGizmoDispatcher.Add(__instance, __result);
    }
}
