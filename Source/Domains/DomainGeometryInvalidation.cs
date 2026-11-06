using System.Collections.Generic;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private readonly struct Geometry
        {
            private readonly Map map, source;
            private readonly IntVec3 center, anchor;
            private readonly int size;
            private readonly DomainKind kind;
            private readonly bool ending, closed;
            internal Geometry(DomainArea area)
            {
                map = area.Map; source = area.Source;
                center = area.MapCenter; anchor = area.Anchor; size = area.Size;
                kind = area.Profile.kind; ending = area.Ending; closed = area.Closed;
            }
            internal bool Matches(Geometry other) => map == other.map && source == other.source
                && center == other.center && anchor == other.anchor && size == other.size && kind == other.kind
                && ending == other.ending && closed == other.closed;
        }
        private readonly Dictionary<DomainArea, Geometry> geometry = new Dictionary<DomainArea, Geometry>();
        private bool overlapDirty, resolvingOverlaps;
        public long OverlapScans { get; private set; }

        private void ResetGeometry()
        {
            geometry.Clear();
            overlapDirty = true;
        }
        private void ObserveGeometry(DomainArea area)
        {
            if (area.Closed)
            {
                if (geometry.Remove(area)) overlapDirty = true;
                return;
            }
            var current = new Geometry(area);
            if (geometry.TryGetValue(area, out Geometry previous) && previous.Matches(current)) return;
            geometry[area] = current;
            overlapDirty = true;
        }
        private void ResolvePendingOverlaps()
        {
            if (!overlapDirty || resolvingOverlaps) return;
            overlapDirty = false;
            if (external.Count < 2) return;
            resolvingOverlaps = true;
            try { OverlapScans++; ResolvePrototypeOverlaps(); }
            finally { resolvingOverlaps = false; }
        }
    }
}
