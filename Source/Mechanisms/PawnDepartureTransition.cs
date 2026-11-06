using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal readonly struct PawnDepartureTransition
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private readonly Pawn pawn;
        private readonly Map source;
        internal PawnDepartureTransition(Pawn pawn)
        {
            this.pawn = pawn;
            int index = mapIndex(pawn);
            source = index >= 0 && index < Find.Maps.Count ? Find.Maps[index] : null;
        }
        internal void Complete()
        {
            if (source == null || pawn == null || PawnLife.Dead(pawn)) return;
            int index = mapIndex(pawn);
            if (index < 0 || index >= Find.Maps.Count || Find.Maps[index] != source) return;
            using (new OperationScope(OperationKind.Transfer)) PawnMapPresence.Restore(pawn, source);
        }
    }
}
