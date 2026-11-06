using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private void RetreatSurface(DomainArea[] ending, DomainArea[] remaining)
        {
            if (remaining.Length == 0) return;
            DomainArea survivor = remaining.OrderByDescending(a => a.Profile.internalStrength).First();
            Map destination = survivor.Map;
            var captured = new HashSet<Pawn>(Core.ReferenceIdentity<Pawn>.Instance);
            foreach (DomainArea area in ending)
                foreach (Pawn pawn in DomainTransit.CaptureArea(area.Map, area.Center, area.Size))
                    if (area.Covers(pawn)) captured.Add(pawn);
            using (new OperationScope(OperationKind.Transfer))
                foreach (Pawn pawn in captured)
                {
                    if (Covers(survivor, pawn)) continue;
                    DomainMember origin = survivor.Members.FirstOrDefault(m => m.Pawn == pawn);
                    if (origin == null && survivor.Enclosed) { origin = DomainTransit.Capture(pawn); survivor.Members.Add(origin); }
                    IntVec3 preferred = pawn.MapHeld == destination ? pawn.PositionHeld
                        : survivor.MapCenter + (pawn.PositionHeld - survivor.Anchor);
                    TransferTo(pawn, destination, RetreatCell(survivor, destination, preferred));
                    if (origin != null) origin.Transferred = true;
                }
        }

        private static IntVec3 RetreatCell(DomainArea area, Map map, IntVec3 preferred)
        {
            IntVec3 center = map == area.Map ? area.MapCenter : area.Enclosed ? map.Center : area.Anchor;
            CellRect bounds = CellRect.CenteredOn(center, area.Size, area.Size).ClipInsideMap(map);
            preferred.x = UnityEngine.Mathf.Clamp(preferred.x, bounds.minX, bounds.maxX);
            preferred.z = UnityEngine.Mathf.Clamp(preferred.z, bounds.minZ, bounds.maxZ);
            if (preferred.Standable(map)) return preferred;
            foreach (IntVec3 cell in bounds.Cells.OrderBy(c => (c - preferred).LengthHorizontalSquared))
                if (cell.Standable(map)) return cell;
            throw new InvalidOperationException("Remaining domain has no valid return cell.");
        }
    }
}
