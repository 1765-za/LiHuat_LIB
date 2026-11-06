using System;
using System.Collections.Generic;
using HarmonyLib;
using LihuatLib.Mechanisms;
using LihuatLib.StarBlood;
using RimWorld;
using RimWorld.QuestGen;
using RimWorld.Utility;
using Verse;

namespace LihuatLib.Integrations
{
    internal static class AnomalyTransferIntegration
    {
        private static readonly AccessTools.FieldRef<QuestPart_MetalHell, Pawn> traveler =
            AccessTools.FieldRefAccess<QuestPart_MetalHell, Pawn>("pawnToSend");
        private static readonly AccessTools.FieldRef<QuestPart_MetalHell, Map> destination =
            AccessTools.FieldRefAccess<QuestPart_MetalHell, Map>("metalHell");
        [ThreadStatic] private static QuestPart_MetalHell entering;

        internal static void Install()
        {
            NativeIlGuards.Scoped(AccessTools.Method(typeof(QuestPart_MetalHell), "GenerateMetalHellAndSkipPawn"),
                AccessTools.Method(typeof(AnomalyTransferIntegration), nameof(BeginEntry)), new[] { 0 },
                AccessTools.Method(typeof(AnomalyTransferIntegration), nameof(EndEntry)), Array.Empty<int>());
            NativeIlGuards.Scoped(AccessTools.Method(typeof(VoidAwakeningUtility), "CloseMetalHell"),
                AccessTools.Method(typeof(AnomalyTransferIntegration), nameof(BeginClose)), new[] { 0 },
                AccessTools.Method(typeof(AnomalyTransferIntegration), nameof(EndClose)), Array.Empty<int>());
        }

        private static bool BeginEntry(QuestPart_MetalHell part, out QuestPart_MetalHell previous)
        { previous = entering; entering = part; return true; }
        private static void EndEntry(QuestPart_MetalHell previous) => entering = previous;

        internal static bool AllowsSkip(Pawn pawn, Map map)
        {
            if (entering != null && ReferenceEquals(traveler(entering), pawn) && ReferenceEquals(destination(entering), map)) return true;
            return ModsConfig.AnomalyActive && !pawn.Spawned && Find.Anomaly.monolith?.Map == map
                && Find.Anomaly.metalHellPawns.Contains(pawn);
        }

        private sealed class WaitingPawn(Pawn pawn, Map map)
        {
            internal readonly Pawn Pawn = pawn;
            internal readonly Map Source = map;
            internal readonly IntVec3 Cell = pawn.Position;
            internal readonly Rot4 Rotation = pawn.Rotation;
            internal readonly MapTransferIntent Intent = new(pawn, null, handoff: true);
        }

        private static bool BeginClose(Pawn activator, out List<WaitingPawn> state)
        {
            state = null;
            Map map = activator.MapHeld;
            if (map?.IsPocketMap != true) return true;
            foreach (Pawn pawn in map.mapPawns.AllPawns)
                if (LihuatGame.Current?.Domains.AllowsDestination(pawn, null) == false) return false;
            state = new List<WaitingPawn>();
            foreach (Pawn pawn in map.mapPawns.AllPawns)
                if (PawnContinuity.Find(pawn) != null) state.Add(new WaitingPawn(pawn, map));
            return true;
        }

        private static void EndClose(List<WaitingPawn> state)
        {
            if (state == null) return;
            try
            {
                foreach (WaitingPawn item in state)
                {
                    if (!item.Pawn.Spawned && Find.Anomaly.metalHellPawns.Contains(item.Pawn))
                        PawnContinuity.AcceptHandoff(item.Pawn);
                    else if (Find.Maps.Contains(item.Source))
                    {
                        using var restore = new MapTransferIntent(item.Pawn, item.Source, handoff: true);
                        MapTransfer.Place(item.Pawn, item.Source, item.Cell, item.Rotation);
                        PawnContinuity.AcceptHandoff(item.Pawn);
                    }
                }
            }
            finally
            {
                for (int i = state.Count - 1; i >= 0; i--) state[i].Intent.Dispose();
            }
        }
    }
}
