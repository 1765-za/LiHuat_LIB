using System;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private void MoveMobile(DomainArea area)
        {
            Map map = area.Owner?.MapHeld;
            IntVec3 cell = area.Owner?.PositionHeld ?? IntVec3.Invalid;
            if (map == area.Map && cell == area.Center) return;
            Map previousMap = area.Map;
            CellRect previous = area.Footprint;
            if (map != previousMap)
            {
                Encounter encounter = null;
                if (map != null) byMap.TryGetValue(map, out encounter);
                area.Map = map;
                area.Source = encounter?.Areas[0].Source ?? map;
                area.ReturnPath = encounter?.Areas[0].ReturnPath ?? DomainReturnPath.Capture(area.Source);
                area.EncounterId = encounter?.Areas[0].EncounterId ?? area.Id;
                area.Center = cell; area.Anchor = cell;
                Rebuild();
                if (previousMap != null) RefreshRegion(previousMap, previous, CellRect.Empty);
                if (map != null) RefreshRegion(map, CellRect.Empty, area.Footprint);
                if (area.Owner != null) Refresh(area.Owner, true);
                return;
            }
            area.Center = cell; area.Anchor = cell;
            ObserveGeometry(area);
            if (map != null) RefreshRegion(map, previous, area.Footprint);
        }

        public bool DamageStability(string id, float amount)
        {
            DomainArea area = GetArea(id);
            if (area == null || area.Ending || area.Closed || !(amount > 0f) || float.IsInfinity(amount)) return false;
            if (area.Profile.HasShell) return DamageShell(id, amount);
            area.Stability = Math.Max(0f, area.Stability - amount);
            if (area.Stability == 0f) Collapse(id, "stability-broken");
            return true;
        }
    }
}
