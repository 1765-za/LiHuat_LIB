using System;
using System.Collections.Generic;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class PawnComponentMutation
    {
        internal static void Clear(List<ThingComp> list)
        { if (!PawnContinuity.BlocksComponentMutation(list)) list.Clear(); }

        internal static bool Remove(List<ThingComp> list, ThingComp component) =>
            !PawnContinuity.BlocksComponentMutation(list) && list.Remove(component);

        internal static void RemoveAt(List<ThingComp> list, int index)
        { if (!PawnContinuity.BlocksComponentMutation(list)) list.RemoveAt(index); }

        internal static int RemoveAll(List<ThingComp> list, Predicate<ThingComp> predicate) =>
            PawnContinuity.BlocksComponentMutation(list) ? 0 : list.RemoveAll(predicate);

        internal static void RemoveRange(List<ThingComp> list, int index, int count)
        { if (!PawnContinuity.BlocksComponentMutation(list)) list.RemoveRange(index, count); }

        internal static void Replace(List<ThingComp> list, int index, ThingComp component)
        { if (!PawnContinuity.BlocksComponentMutation(list)) list[index] = component; }

        internal static void CollectionClear(ICollection<ThingComp> list)
        { if (!PawnContinuity.BlocksComponentMutation(list)) list.Clear(); }

        internal static bool CollectionRemove(ICollection<ThingComp> list, ThingComp component) =>
            !PawnContinuity.BlocksComponentMutation(list) && list.Remove(component);

        internal static void ListRemoveAt(IList<ThingComp> list, int index)
        { if (!PawnContinuity.BlocksComponentMutation(list)) list.RemoveAt(index); }

        internal static void ListReplace(IList<ThingComp> list, int index, ThingComp component)
        { if (!PawnContinuity.BlocksComponentMutation(list)) list[index] = component; }
    }
}
