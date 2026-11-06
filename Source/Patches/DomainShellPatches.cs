using LihuatLib.Dispatching;
using System;
using HarmonyLib;
using Verse;
using LihuatLib.Domains;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    [DispatchOrder(380)]
    internal static class DomainShellPlacementPatch
    {
        [DispatchPriority(int.MinValue)]
        static void Finalizer(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result, Exception __exception)
        {
            if (__exception == null && DomainShellRules.Coexists(newEntDef, oldEntDef)) __result = false;
        }
    }

    [DispatchTarget(typeof(GenGrid), nameof(GenGrid.CanBeSeenOver), new[] { typeof(IntVec3), typeof(Map) })]
    [DispatchOrder(390)]
    internal static class DomainShellVisionPatch
    {
        static void Postfix(IntVec3 c, Map map, ref bool __result)
        {
            if (__result && DomainShellRules.Blocks(map, c)) __result = false;
        }
    }
}
