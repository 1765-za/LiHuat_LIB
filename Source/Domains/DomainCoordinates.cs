using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace LihuatLib.Domains
{
    internal static class DomainCoordinates
    {
        public static DomainArea Frame(IReadOnlyList<DomainArea> areas, Map arena) =>
            areas.FirstOrDefault(a => a.Enclosed && a.HomeMap == arena) ?? areas.First(a => a.Enclosed);

        public static IntVec3 InArena(IReadOnlyList<DomainArea> areas, Map arena, Map source, IntVec3 cell)
        {
            if (source == arena) return cell;
            foreach (DomainArea area in areas)
                if (area.Enclosed && area.HomeMap == source) return area.MapCenter + (cell - source.Center);
            DomainArea frame = Frame(areas, arena);
            if (source == frame.Source) return frame.MapCenter + (cell - frame.Anchor);
            throw new InvalidOperationException("Domain position has no source frame.");
        }

        public static IntVec3 OnSource(IReadOnlyList<DomainArea> areas, Map arena, Map source, IntVec3 cell)
        {
            DomainArea frame = Frame(areas, arena);
            return frame.Anchor + (InArena(areas, arena, source, cell) - frame.MapCenter);
        }

        public static void Move(DomainArea area, Map destination, IntVec3 translation)
        {
            IntVec3 center = area.MapCenter + translation;
            area.Map = destination;
            if (area.Enclosed) area.CenterOffset = center - destination.Center;
            else area.Center = center;
        }

        internal sealed class Placement
        {
            private readonly DomainArea area;
            private readonly Map map;
            private readonly IntVec3 center, offset;
            public Placement(DomainArea area)
            { this.area = area; map = area.Map; center = area.Center; offset = area.CenterOffset; }
            public void Restore() { area.Map = map; area.Center = center; area.CenterOffset = offset; }
        }
    }
}
