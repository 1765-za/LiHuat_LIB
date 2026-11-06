using System.Collections.Generic;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        public bool MayApplyPressure(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pressureMemory.Count != 0 && pressureMemory.ContainsKey(pawn)) return true;
            if (byId.Count == 0) return false;
            if (byPawn.ContainsKey(pawn)) return true;
            Map map = pawn.MapHeld;
            return map != null && spatial.ContainsKey(map);
        }

        private AreaCandidates Candidates(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            if (map == null) return default;
            byPawn.TryGetValue(pawn, out Encounter group);
            List<DomainArea> projected = null;
            Map source = group?.Areas[0].Source;
            if (source != null && !source.Disposed && source != map)
                spatial.TryGetValue(source, out projected);
            spatial.TryGetValue(map, out List<DomainArea> local);
            return new AreaCandidates(group?.Areas, projected, local);
        }

        private struct AreaCandidates
        {
            private readonly List<DomainArea> primary, projected, local;
            private int phase, index;
            public DomainArea Current { get; private set; }
            internal AreaCandidates(List<DomainArea> primary, List<DomainArea> projected, List<DomainArea> local)
            { this.primary = primary; this.projected = projected; this.local = local; phase = index = 0; Current = null; }
            public AreaCandidates GetEnumerator() => this;
            internal bool Contains(DomainArea area) => primary?.Contains(area) == true
                || projected?.Contains(area) == true || local?.Contains(area) == true;
            public bool MoveNext()
            {
                while (phase < 3)
                {
                    List<DomainArea> areas = phase == 0 ? primary : phase == 1 ? projected : local;
                    while (areas != null && index < areas.Count)
                    {
                        DomainArea area = areas[index++];
                        if (phase == 2 && primary != null && primary.Contains(area)) continue;
                        Current = area;
                        return true;
                    }
                    phase++; index = 0;
                }
                Current = null;
                return false;
            }
        }
    }
}
