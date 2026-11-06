using LihuatLib.Dispatching;
using HarmonyLib;
using RimWorld;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(StatWorker), nameof(StatWorker.FinalizeValue))]
    [DispatchOrder(130)]
    internal static class CombatStatPatch
    {
        [DispatchPriority(Priority.Last)]
        private static void Postfix(StatDef ___stat, StatRequest req, ref float val) => Core.StatEffectDispatcher.Apply(___stat, req, ref val);
    }
}
