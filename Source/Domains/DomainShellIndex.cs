using System.Collections.Generic;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private readonly Dictionary<Map, List<DomainArea>> shells = new Dictionary<Map, List<DomainArea>>();
        internal void IndexShell(DomainArea area)
        {
            if (area.Shell?.Spawned != true) return;
            if (!shells.TryGetValue(area.Shell.Map, out List<DomainArea> list)) shells.Add(area.Shell.Map, list = new List<DomainArea>());
            if (!list.Contains(area)) list.Add(area);
        }
        public DomainArea ShellAt(Map map, IntVec3 cell)
        {
            if (map == null || !shells.TryGetValue(map, out List<DomainArea> list)) return null;
            foreach (DomainArea area in list)
                if (!area.Ending && area.Shell?.Spawned == true && area.SourceFootprint.Contains(cell)) return area;
            return null;
        }
    }
}
