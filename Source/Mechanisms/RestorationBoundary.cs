using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class RestorationBoundary
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> owner =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
        internal static void Install()
        {
            var resurrect = AccessTools.Method(typeof(ResurrectionUtility), nameof(ResurrectionUtility.TryResurrect));
            NativeIlGuards.Guard(resurrect, AccessTools.Method(typeof(RestorationBoundary), nameof(Allows)), 0);
            NativeIlGuards.Guard(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_Resurrected)),
                AccessTools.Method(typeof(RestorationBoundary), nameof(AllowsState)), 0);
            NativeIlGuards.FilterArguments(resurrect, AccessTools.Method(typeof(RestorationBoundary), nameof(Completed)), 0);
        }
        private static bool Allows(Pawn pawn) => LihuatGame.Current?.Lifecycle.AllowsRestore(pawn) != false;
        private static bool AllowsState(Pawn_HealthTracker health) => Allows(owner(health));
        private static bool Completed(Pawn pawn, bool result)
        {
            if (result && !PawnLife.Dead(pawn)) LihuatGame.Current?.Lifecycle.Restored(pawn);
            return result;
        }
    }
}
