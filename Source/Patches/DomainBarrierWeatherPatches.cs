using LihuatLib.Dispatching;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(WeatherEvent_LightningStrike), nameof(WeatherEvent_LightningStrike.DoStrike))]
    [DispatchOrder(260)]
    internal static class DomainBarrierLightningPatch
    {
        static bool Prefix(ref IntVec3 strikeLoc, Map map, ref Mesh boltMesh)
        {
            var barriers = LihuatGame.Current?.Domains.Barriers;
            if (barriers?.Active != true) return true;
            if (!strikeLoc.IsValid) strikeLoc = CellFinderLoose.RandomCellWith(c => c.Standable(map) && !map.roofGrid.Roofed(c), map);
            if (!barriers.Sheltered(map, strikeLoc)) return true;
            boltMesh = LightningBoltMeshPool.RandomBoltMesh;
            return false;
        }
    }

    [DispatchTarget(typeof(GameConditionManager), nameof(GameConditionManager.DoSteadyEffects))]
    [DispatchOrder(270)]
    internal static class DomainBarrierFalloutPatch
    {
        static bool Prefix(IntVec3 c, Map map) => LihuatGame.Current?.Domains.Barriers.Sheltered(map, c) != true;
    }

    [DispatchTarget(typeof(ToxicUtility), nameof(ToxicUtility.DoAirbornePawnToxicDamage))]
    [DispatchOrder(280)]
    internal static class DomainBarrierToxicPatch
    {
        static bool Prefix(Pawn p) => p?.Spawned != true || LihuatGame.Current?.Domains.Barriers.Sheltered(p.Map, p.Position) != true;
    }

    [DispatchTarget]
    [DispatchOrder(290)]
    internal static class DomainBarrierPrecipitationPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.AddFallenSnowAt));
            yield return AccessTools.Method(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.AddFallenSandAt));
        }
        static bool Prefix(IntVec3 c, Map ___map) => LihuatGame.Current?.Domains.Barriers.Sheltered(___map, c) != true;
    }

    [DispatchTarget(typeof(GenTemperature), nameof(GenTemperature.GetTemperatureForCell))]
    [DispatchOrder(300)]
    internal static class DomainBarrierTemperaturePatch
    {
        static void Postfix(IntVec3 c, Map map, ref float __result)
        {
            if (LihuatGame.Current?.Domains.Barriers.Sheltered(map, c) == true && (c.GetRoom(map)?.UsesOutdoorTemperature ?? true))
                __result = 21f;
        }
    }

    [DispatchTarget(typeof(SteadyEnvironmentEffects), "DoCellSteadyEffects")]
    [DispatchOrder(310)]
    internal static class DomainBarrierRainPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo roofed = AccessTools.Method(typeof(RoofGrid), nameof(RoofGrid.Roofed), new[] { typeof(IntVec3) });
            MethodInfo shelter = AccessTools.Method(typeof(DomainBarrierRainPatch), nameof(Protected));
            FieldInfo map = AccessTools.Field(typeof(SteadyEnvironmentEffects), "map");
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(roofed)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, map);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Call, shelter);
            }
        }
        private static bool Protected(bool roofed, Map map, IntVec3 cell) => roofed || LihuatGame.Current?.Domains.Barriers.Sheltered(map, cell) == true;
    }
}
