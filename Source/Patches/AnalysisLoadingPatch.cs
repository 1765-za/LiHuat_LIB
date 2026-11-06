using HarmonyLib;
using LihuatLib.Dispatching;
using System;
using Verse;

namespace LihuatLib.Patches
{
    internal static class AnalysisLoadingPatch
    {
        internal static void Install() => NativeDispatcher.Register(
            AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML)),
            prefix: new DispatchMethod(typeof(AnalysisLoadingPatch), nameof(Prefix)));

        private static void Prefix(bool hotReload)
        {
            if (hotReload || !Configuration.HighMechanismPolicy.Scanning) return;
            try { StarBlood.LifeAnalysisPreparation.BeginLoading(); }
            catch (Exception error) { Log.Warning("[LihuatLib.Analysis] Loading preparation unavailable: " + error); }
        }
    }
}
