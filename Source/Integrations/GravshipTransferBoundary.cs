using System;
using HarmonyLib;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.Integrations
{
    internal static class GravshipTransferBoundary
    {
        internal static void Install()
        {
            NativeIlGuards.Guard(AccessTools.Method(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateTakeoff)),
                AccessTools.Method(typeof(GravshipTransferRules), nameof(GravshipTransferRules.AllowsTakeoff)), 1);
            NativeIlGuards.Scoped(AccessTools.Method(typeof(GravshipUtility), nameof(GravshipUtility.GenerateGravship)),
                AccessTools.Method(typeof(GravshipTransferRules), nameof(GravshipTransferRules.BeginTakeoff)), new[] { 0 },
                AccessTools.Method(typeof(GravshipTransferRules), nameof(GravshipTransferRules.End)), Array.Empty<int>());
            NativeIlGuards.Scoped(AccessTools.Method(typeof(GravshipPlacementUtility), nameof(GravshipPlacementUtility.PlaceGravshipInMap)),
                AccessTools.Method(typeof(GravshipTransferRules), nameof(GravshipTransferRules.BeginLanding)), new[] { 0, 2 },
                AccessTools.Method(typeof(GravshipTransferRules), nameof(GravshipTransferRules.End)), Array.Empty<int>());
        }
    }
}
