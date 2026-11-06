using LihuatLib.Dispatching;
using HarmonyLib;
using RimWorld;
using Verse;
using LihuatLib.Domains;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(Pawn), nameof(Pawn.HealthScale), DispatchMember.Getter)]
    [DispatchOrder(430)]
    internal static class DomainScalePatch
    {
        [DispatchPriority(Priority.Last)]
        static void Postfix(Pawn __instance, ref float __result) => DomainStatRules.HealthScale(__instance, ref __result);
    }
    [DispatchTarget(typeof(Verb), "TryCastNextBurstShot")]
    [DispatchOrder(440)]
    internal static class CounterBurstPatch
    {
        static bool Prefix(Verb __instance)
        {
            if (LihuatGame.Current?.Counters.Signing(__instance.CasterPawn) != true) return true;
            __instance.Reset(); return false;
        }
    }
}
