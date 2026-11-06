using System;
using System.Linq;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private static bool Intersects(DomainArea a, DomainArea b) => a.ExternalBarrierRadius <= 0f && b.ExternalBarrierRadius <= 0f && (a.Map == b.Map
            ? a.Footprint.Overlaps(b.Footprint)
            : a.Enclosed && a.Source == b.Map && a.SourceFootprint.Overlaps(b.Footprint)
                || b.Enclosed && b.Source == a.Map && b.SourceFootprint.Overlaps(a.Footprint));

        public DomainArea[] Overlapping(DomainArea area)
        {
            if (area == null || area.Ending || area.Closed) return Array.Empty<DomainArea>();
            return external.Where(other => other != area && !other.Ending && !other.Closed && Intersects(area, other)).ToArray();
        }

        private void ResolvePrototypeOverlaps()
        {
            DomainArea[] snapshot = external.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                DomainArea prototype = snapshot[i];
                if (prototype.Ending || prototype.Closed || prototype.Profile.kind != DomainKind.Prototype) continue;
                for (int j = 0; j < snapshot.Length; j++)
                {
                    DomainArea formal = snapshot[j];
                    if (formal.Ending || formal.Closed || !formal.Profile.Formal || !Intersects(prototype, formal)) continue;
                    Collapse(prototype.Id, "clash-broken");
                    break;
                }
            }
        }

        public bool DamageShell(string id, float amount)
        {
            DomainArea area = GetArea(id);
            if (area?.Shell?.Spawned != true || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
            area.Shell.HitPoints = (int)Math.Max(0d, area.Shell.HitPoints - Math.Ceiling(amount));
            if (area.Shell.HitPoints == 0) DomainShellRules.Remove(area);
            return true;
        }
    }
}
