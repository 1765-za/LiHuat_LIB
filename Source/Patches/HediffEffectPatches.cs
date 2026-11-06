using LihuatLib.Dispatching;
using HarmonyLib;
using Verse;
using LihuatLib.Core;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(Hediff), nameof(Hediff.CurStage), DispatchMember.Getter)]
    [DispatchOrder(470)]
    internal static class StageEffectPatch
    {
        static void Postfix(Hediff __instance, ref HediffStage __result)
        {
            if (LihuatGame.Current is LihuatGame game && game.Effects.HasOverrides)
                __result = game.Effects.Read(__instance, __result);
        }
    }

    [DispatchTarget(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged))]
    [DispatchOrder(480)]
    internal static class ChangedHediffEffectPatch
    {
        static void Prefix(Pawn ___pawn, Hediff hediff) => LihuatGame.Current?.Effects.Refresh(hediff, ___pawn);
    }

    [DispatchTarget(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    [DispatchOrder(490)]
    internal static class RemovedHediffEffectPatch
    {
        [DispatchPriority(int.MaxValue)]
        static bool Prefix(Hediff hediff) => Mechanisms.PinnedHediffs.AllowRemoval(hediff);
        static void Postfix(Hediff hediff) => LihuatGame.Current?.Effects.Remove(hediff);
    }
}
