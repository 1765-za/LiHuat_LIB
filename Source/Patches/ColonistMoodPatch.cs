using LihuatLib.Dispatching;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(MoodThresholdExtensions), nameof(MoodThresholdExtensions.CurrentMoodThresholdFor))]
    [DispatchOrder(120)]
    internal static class ColonistMoodPatch
    {
        static bool Prefix(Pawn pawn, ref MoodThreshold __result)
        {
            // A retained mind state cannot supply mood after its needs tracker has been released.
            if (pawn.needs != null) return true;
            __result = MoodThreshold.None;
            return false;
        }
    }
}
