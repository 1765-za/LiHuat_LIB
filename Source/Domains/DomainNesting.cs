using System.Collections.Generic;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private readonly HashSet<Map> nestedSources = new(ReferenceIdentity<Map>.Instance);

        private void RebuildNestedSources()
        {
            nestedSources.Clear();
            foreach (DomainArea area in external)
            {
                if (area.Closed || !area.Enclosed) continue;
                if (area.Source != null) nestedSources.Add(area.Source);
                if (area.ReturnPath != null)
                    foreach (Map source in area.ReturnPath.Maps)
                        if (source != null) nestedSources.Add(source);
            }
        }

        public bool HasNestedDomain(Map source) => source != null && nestedSources.Contains(source);

        public bool ContainsInNestedDomain(Pawn pawn, Map source)
        {
            if (!HasNestedDomain(source) || pawn == null || !byPawn.TryGetValue(pawn, out Encounter group)
                || pawn.MapHeld != group.Map) return false;
            foreach (DomainArea area in group.Areas)
                if (area.Source == source || area.ReturnPath?.Contains(source) == true) return true;
            return false;
        }
    }
}
