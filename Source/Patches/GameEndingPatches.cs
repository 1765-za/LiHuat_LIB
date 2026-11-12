using LihuatLib.Dispatching;
using HarmonyLib;
using RimWorld;
using UnityEngine;
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

    [DispatchTarget(typeof(GenGameEnd), nameof(GenGameEnd.EndGameDialogMessage), new[] { typeof(string), typeof(bool), typeof(Color) })]
    [DispatchOrder(470)]
    internal static class EndingGameOverPatch
    {
        static bool Prefix(string msg, bool allowKeepPlaying, Color screenFillColor)
            => LihuatGame.Current?.Endings.AllowGameOver(msg, allowKeepPlaying, screenFillColor) != false;
    }
}
