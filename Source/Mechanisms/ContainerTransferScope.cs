using System;
using System.Linq;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class ContainerTransferScope : IDisposable
    {
        private readonly Pawn pawn;
        private readonly Map sourceMap;
        private readonly IntVec3 sourceCell;
        private readonly Rot4 sourceRotation;
        private readonly ThingOwner sourceOwner;
        private readonly ThingOwner destinationOwner;
        private readonly Map destinationMap;
        private readonly MapTransferIntent intent;

        private ContainerTransferScope(Pawn pawn, ThingOwner destinationOwner, Map destinationMap)
        {
            this.pawn = pawn;
            sourceMap = pawn.Map;
            sourceCell = pawn.Position;
            sourceRotation = pawn.Rotation;
            sourceOwner = pawn.holdingOwner;
            this.destinationOwner = destinationOwner;
            this.destinationMap = destinationMap;
            intent = new MapTransferIntent(pawn, destinationMap ?? ThingOwnerUtility.GetRootMap(destinationOwner.Owner),
                handoff: true, destinationOwner: destinationOwner);
        }

        internal static ContainerTransferScope Enter(Thing thing, ThingOwner destination)
        {
            if (!(thing is Pawn pawn) || destination == null || destination.Owner is Map || destination.Owner is Corpse
                || !Tracked(pawn)) return null;
            return new ContainerTransferScope(pawn, destination, null);
        }
        internal static bool AllowsEnter(Thing thing, ThingOwner destination) => destination == null
            || LihuatGame.Current?.Domains.AllowsDestination(thing, ThingOwnerUtility.GetRootMap(destination.Owner)) != false;
        internal static bool AllowsLeave(Thing thing, Map destination) => LihuatGame.Current?.Domains.AllowsDestination(thing, destination) != false;

        internal static ContainerTransferScope ToMap(Pawn pawn, Map map)
            => pawn != null && map != null && Tracked(pawn) ? new ContainerTransferScope(pawn, null, map) : null;

        internal static ContainerTransferScope Leave(ThingOwner source, Thing thing, IntVec3 cell, Map map)
        {
            if (!(thing is Pawn pawn) || map == null || !cell.InBounds(map) || !Owns(source, pawn) || !Tracked(pawn)) return null;
            return new ContainerTransferScope(pawn, null, map);
        }

        internal static bool Owns(ThingOwner owner, Thing thing) => owner != null && owner.Owner is not Map && owner.Owner is not Corpse
            && ReferenceEquals(thing?.holdingOwner, owner) && owner.Contains(thing);

        private static bool Tracked(Pawn pawn) => !pawn.Dead && !pawn.Destroyed
            && (PawnContinuity.Find(pawn) != null || LihuatGame.Current?.Domains.Contains(pawn) == true);
        private bool AtOwner(ThingOwner owner) => !pawn.Spawned && ReferenceEquals(pawn.holdingOwner, owner) && owner.Contains(pawn);
        private bool AtMap(Map map) => pawn.Map == map && pawn.holdingOwner == map.GetDirectlyHeldThings()
            && map.mapPawns.AllPawnsSpawned.Contains(pawn) && map.listerThings.AllThings.Contains(pawn)
            && pawn.Position.InBounds(map) && pawn.Position.GetThingList(map).Contains(pawn);

        public void Dispose()
        {
            try
            {
                if (pawn.Dead || pawn.Destroyed) return;
                if (destinationOwner != null ? AtOwner(destinationOwner) : AtMap(destinationMap)) return;
                if (sourceMap != null && Find.Maps.Contains(sourceMap))
                {
                    using var restore = new MapTransferIntent(pawn, sourceMap, handoff: true);
                    if (!AtMap(sourceMap)) MapTransfer.Place(pawn, sourceMap, sourceCell, sourceRotation);
                }
                else if (sourceOwner != null && !AtOwner(sourceOwner))
                {
                    using var restore = new MapTransferIntent(pawn, ThingOwnerUtility.GetRootMap(sourceOwner.Owner), handoff: true, destinationOwner: sourceOwner);
                    if (!MapTransfer.Store(pawn, sourceOwner)) throw new InvalidOperationException("Container transfer could not restore " + pawn.ThingID);
                }
                else if (sourceMap == null && sourceOwner == null)
                    throw new InvalidOperationException("Destination did not accept " + pawn.ThingID);
            }
            finally { try { PawnContinuity.AcceptHandoff(pawn); } finally { intent.Dispose(); } }
        }
    }
}
