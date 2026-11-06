using LihuatLib.Dispatching;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(WindowStack), nameof(WindowStack.Add))]
    [DispatchOrder(450)]
    internal static class EndingWindowPatch
    {
        static bool Prefix(Window window) => LihuatGame.Current?.Endings.AllowWindow(window) != false;
    }

    [DispatchTarget(typeof(GameVictoryUtility), nameof(GameVictoryUtility.ShowCredits))]
    [DispatchOrder(460)]
    internal static class EndingCreditsPatch
    {
        static bool Prefix(string victoryText, SongDef endCreditsSong, bool exitToMainMenu, float songStartDelay)
            => LihuatGame.Current?.Endings.AllowCredits(victoryText, endCreditsSong, exitToMainMenu, songStartDelay) != false;
    }
}
