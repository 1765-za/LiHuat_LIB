using HarmonyLib;
using LihuatLib.Mechanisms;
using LihuatLib.StarBlood;
using RimWorld;

namespace LihuatLib.Presentation
{
    internal static class PawnSelectionBoundary
    {
        internal static void Install() => NativeIlGuards.Guard(
            AccessTools.Method(typeof(Selector), nameof(Selector.Select)),
            AccessTools.Method(typeof(PawnSelectionRules), nameof(PawnSelectionRules.Select)), 0, 1, 2, 3);
    }
}
