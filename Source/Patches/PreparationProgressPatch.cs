using LihuatLib.Dispatching;
using HarmonyLib;
using LihuatLib.Presentation;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    [DispatchOrder(550)]
    internal static class PreparationProgressPatch
    {
        private static void Postfix() => PreparationProgressView.Draw();
    }
}
