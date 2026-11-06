using System;
using System.Collections.Generic;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class PawnTransferBatch : IDisposable
    {
        private readonly List<Pawn> passengers;
        private readonly List<MapTransferIntent> intents = new();

        internal PawnTransferBatch(List<Pawn> passengers, Map destination)
        {
            this.passengers = passengers;
            foreach (Pawn pawn in passengers)
                intents.Add(new MapTransferIntent(pawn, destination, handoff: true));
        }

        public void Dispose()
        {
            try { foreach (Pawn pawn in passengers) PawnContinuity.AcceptHandoff(pawn); }
            finally { for (int i = intents.Count - 1; i >= 0; i--) intents[i].Dispose(); }
        }
    }
}
