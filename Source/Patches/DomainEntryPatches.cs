using LihuatLib.Dispatching;
using HarmonyLib;
using Verse;
using LihuatLib.Domains;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(Thing), nameof(Thing.MaxHitPoints), DispatchMember.Getter)]
    [DispatchOrder(320)]
    internal static class ShellCapacityPatch
    {
        static void Postfix(Thing __instance, ref int __result) { if (__instance is Building_DomainShell shell) __result = shell.Capacity; }
    }
    [DispatchTarget(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) })]
    [DispatchOrder(330)]
    internal static class DomainSpawnPatch
    {
        static bool Prefix(Thing newThing, Map map, ref IntVec3 loc, bool respawningAfterLoad, ref Thing __result)
        {
            if (LihuatGame.Current?.Domains.AllowsDestination(newThing, map) == false) { __result = newThing; return false; }
            if (!respawningAfterLoad && LihuatGame.Current?.Domains.Barriers.PlaceArrival(newThing, map, ref loc) == false)
            { __result = newThing; return false; }
            if (respawningAfterLoad || OperationScope.Transferring || OperationScope.Terminates(newThing)) return true;
            if (DomainShellRules.PlaceArrival(newThing, map, ref loc)
                && (!(newThing is Pawn pawn) || LihuatGame.Current?.Domains.AllowsSpawn(pawn, map) != false)) return true;
            __result = newThing; return false;
        }
        static void Postfix(Thing newThing)
        {
            LihuatGame.Current?.Domains.Barriers.Arrived(newThing);
            LihuatGame.Current?.Domains.Spawned(newThing);
        }
    }
    [DispatchTarget(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    [DispatchOrder(340)]
    internal static class CounterCastPatch
    {
        static bool Prefix(Verb __instance, ref bool __result)
        {
            if (LihuatGame.Current?.Counters.Signing(__instance.CasterPawn) != true) return true;
            __result = false; return false;
        }
    }
}
