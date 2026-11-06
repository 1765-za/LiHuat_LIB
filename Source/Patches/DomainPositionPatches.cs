using LihuatLib.Dispatching;
using HarmonyLib;
using Verse;
using LihuatLib.Domains;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(Thing), nameof(Thing.SetPositionDirect))]
    [DispatchOrder(350)]
    internal static class DomainPositionPatch
    {
        static bool Prefix(Thing __instance, IntVec3 newPos) => LihuatGame.Current?.Domains.AllowsCell(__instance, newPos) != false
            && DomainShellRules.AllowsMove(__instance, newPos);
        static void Postfix(Thing __instance)
        {
            if (__instance is Pawn && __instance.Spawned) LihuatGame.Current?.Domains.Moved(__instance);
        }
    }

    [DispatchTarget(typeof(Pawn), nameof(Pawn.Notify_Teleported))]
    [DispatchOrder(360)]
    internal static class DomainTeleportPatch
    {
        static void Postfix(Pawn __instance) => LihuatGame.Current?.Domains.Moved(__instance);
    }
}
