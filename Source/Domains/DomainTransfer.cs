using System;
using Verse;
using LihuatLib.Mechanisms;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        public void ImportRollback(DomainRollback transaction)
        {
            if (rollback != null) throw new InvalidOperationException("A domain transfer is already pending.");
            rollback = transaction;
        }
        public void Adopt(DomainArea area)
        {
            if (area == null || external.Contains(area)) return;
            external.Add(area);
            Initialize();
        }

        public void TransferTo(Thing thing, Map map, IntVec3 near)
        {
            if (!OperationScope.Transferring) throw new InvalidOperationException("No active domain transfer operation.");
            if (map == null || map.Disposed || !Find.Maps.Contains(map)) throw new InvalidOperationException("Domain destination is unavailable.");
            IntVec3 preferred = near.ClampInsideMap(map);
            IntVec3 cell = preferred.Standable(map) ? preferred : CellFinder.StandableCellNear(preferred, map, 8);
            if (!cell.IsValid) throw new InvalidOperationException("No valid domain destination cell for " + thing.ThingID);
            Pawn pawn = thing as Pawn;
            if (pawn != null && (DenyLivingTransfer?.Invoke(pawn) == true || PawnLife.Dead(pawn)))
                throw new InvalidOperationException("Cannot transfer a terminal or dead pawn as living.");
            bool drafted = pawn?.Drafted == true;
            using (new MapTransferIntent(thing, map, true)) MapTransfer.Place(thing, map, cell, thing.Rotation);
            if (drafted) pawn.drafter.Drafted = true;
            pawn?.Drawer.tweener.ResetTweenedPosToRoot();
        }

        internal bool StoreIn(Thing thing, ThingOwner holder)
        {
            if (!OperationScope.Transferring) throw new InvalidOperationException("No active domain transfer operation.");
            using (new MapTransferIntent(thing, ThingOwnerUtility.GetRootMap(holder.Owner), true))
                return MapTransfer.Store(thing, holder);
        }
    }
}
