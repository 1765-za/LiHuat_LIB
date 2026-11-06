using System;
using LihuatLib.Domains;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class SubmapTransferScope : IDisposable
    {
        private readonly Pawn pawn;
        private readonly Map source, destination;
        private readonly IntVec3 sourceCell;
        private readonly Rot4 rotation;
        private readonly bool drafted;
        private readonly MapTransferIntent intent;

        private SubmapTransferScope(Pawn pawn, Map destination)
        {
            this.pawn = pawn; this.destination = destination;
            source = pawn.Map; sourceCell = pawn.Position; rotation = pawn.Rotation; drafted = pawn.Drafted;
            intent = new MapTransferIntent(pawn, destination, handoff: true);
        }

        internal static SubmapTransferScope Begin(Thing thing, IntVec3 cell, Map destination, bool afterLoad)
        {
            if (afterLoad || thing is not Pawn pawn || !pawn.Spawned || pawn.Dead || pawn.Destroyed
                || MapTransfer.InHandoff(pawn) || destination == null || !cell.InBounds(destination)
                || !SubmapTransferRules.Connected(pawn.Map, destination)) return null;
            return new SubmapTransferScope(pawn, destination);
        }

        internal void Detach()
        {
            MapTransfer.Detach(pawn);
            PawnContinuity.PrepareArrival(pawn);
        }

        private bool Arrived => pawn.Spawned && pawn.Map == destination && PawnMapPresence.Complete(pawn, destination)
            && MapRegistrationStorage.Placed(destination, pawn);

        public void Dispose()
        {
            try
            {
                if (!Arrived)
                {
                    using var returning = new MapTransferIntent(pawn, source, handoff: true);
                    MapTransfer.Place(pawn, source, sourceCell, rotation);
                }
                if (pawn.drafter != null) pawn.drafter.Drafted = drafted;
                pawn.Drawer?.tweener?.ResetTweenedPosToRoot();
                PawnContinuity.AcceptHandoff(pawn);
            }
            finally { intent.Dispose(); }
        }
    }
}
