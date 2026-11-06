using LihuatLib.Dispatching;
using System.Collections.Generic;
using System.Reflection;
using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDead))]
    [DispatchOrder(500)]
    internal static class ShouldDieEntryPatch
    {
        [DispatchPriority(int.MinValue)]
        static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (__result && LihuatGame.Current?.Lifecycle.Block(___pawn, DestructiveEntry.ShouldBeDead) == true) __result = false;
        }
    }
    [DispatchTarget]
    [DispatchOrder(510)]
    internal static class RuntimeComponentRemovalPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.RemoveComponentsOnKilled));
            yield return AccessTools.Method(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.RemoveComponentsOnDespawned));
        }
        static bool Prefix(Pawn pawn, MethodBase __originalMethod) => Mechanisms.LifecycleComponentRelease.Allowed(pawn,
            __originalMethod.Name == nameof(PawnComponentsUtility.RemoveComponentsOnKilled))
            && LihuatGame.Current?.Lifecycle.AllowsComponentRemoval(pawn) != false;
    }
}
