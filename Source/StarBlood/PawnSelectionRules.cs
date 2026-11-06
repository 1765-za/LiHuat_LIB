using System;
using HarmonyLib;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.StarBlood
{
    internal static class PawnSelectionRules
    {
        private static readonly Action<Selector, object, bool, bool> select =
            OriginalMethods.Create<Action<Selector, object, bool, bool>>(
                AccessTools.Method(typeof(Selector), "SelectInternal"));

        internal static bool Select(Selector selector, object target, bool playSound, bool forceDesignatorDeselect)
        {
            if (!(target is Pawn pawn) || !pawn.Spawned || !PawnContinuity.Protects(pawn)) return true;
            select(selector, target, playSound, forceDesignatorDeselect);
            return false;
        }
    }
}
