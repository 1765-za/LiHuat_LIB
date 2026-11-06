using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class TooltipRegistration
    {
        private static readonly AccessTools.FieldRef<TooltipGiverList, List<Thing>> givers =
            AccessTools.FieldRefAccess<TooltipGiverList, List<Thing>>("givers");

        internal static bool Contains(TooltipGiverList list, Thing thing)
            => ReferenceListStorage<Thing>.Contains(givers(list), thing);

        internal static void Remove(TooltipGiverList list, Thing thing)
            => ReferenceListStorage<Thing>.Remove(givers(list), thing);
    }
}
