using LihuatLib.Dispatching;
using HarmonyLib;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(TooltipGiverList), nameof(TooltipGiverList.Notify_ThingSpawned))]
    [DispatchOrder(580)]
    internal static class TooltipArrivalPatch
    {
        static bool Prefix(TooltipGiverList __instance, Thing t) => !TooltipRegistration.Contains(__instance, t);
    }

    [DispatchTarget(typeof(TooltipGiverList), nameof(TooltipGiverList.Notify_ThingDespawned))]
    [DispatchOrder(590)]
    internal static class TooltipDeparturePatch
    {
        static bool Prefix(TooltipGiverList __instance, Thing t)
        {
            TooltipRegistration.Remove(__instance, t);
            return false;
        }
    }
}
