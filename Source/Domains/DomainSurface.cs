using System.Collections.Generic;
using System.Linq;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private readonly Dictionary<Map, List<DomainArea>> spatial = new Dictionary<Map, List<DomainArea>>();
        private DomainArea OpenSurface(DomainArea area, Encounter existing, Map map)
        {
            area.Map = map;
            if (existing != null) area.MemberSet.UnionWith(existing.Members);
            external.Add(area); Rebuild();
            Opened?.Invoke(area);
            RefreshAll(); ResolvePendingOverlaps();
            area.EntryCommitted = !area.Ending && !area.Closed;
            return area.EntryCommitted ? area : null;
        }

        public bool Covers(DomainArea area, Pawn pawn)
        {
            if (area.Covers(pawn)) return true;
            if (area.Enclosed || pawn == null || !byPawn.TryGetValue(pawn, out Encounter group)) return false;
            foreach (DomainArea closed in group.Areas)
                if (closed.Enclosed && closed.Source == area.Map)
                    return area.Footprint.Contains(closed.Anchor + (pawn.PositionHeld - closed.MapCenter));
            return false;
        }

        private IEnumerable<Pawn> AffectedPawns()
        {
            foreach (Pawn pawn in observedPressure.Keys.Concat(byPawn.Keys).Distinct(Core.ReferenceIdentity<Pawn>.Instance)) yield return pawn;
            foreach (Map map in spatial.Keys)
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned) yield return pawn;
        }

        private void RefreshRegion(Map map, CellRect first, CellRect second)
        {
            var affected = new HashSet<Pawn>(Core.ReferenceIdentity<Pawn>.Instance);
            foreach (IntVec3 cell in first.Cells.Concat(second.Cells))
                if (cell.InBounds(map))
                    foreach (Thing thing in cell.GetThingList(map))
                        if (thing is Pawn pawn) affected.Add(pawn);
            foreach (Pawn pawn in affected) Refresh(pawn, false);
        }
    }
}
