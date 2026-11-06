using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LihuatLib.Core;
using UnityEngine;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class DomainBarrierService
    {
        private sealed class Departure { internal Map Map; internal IntVec3 Cell; }
        private readonly Dictionary<Map, List<DomainArea>> byMap = new Dictionary<Map, List<DomainArea>>();
        private ConditionalWeakTable<Pawn, Departure> departures = new ConditionalWeakTable<Pawn, Departure>();
        public bool Active => byMap.Count != 0;
        public event Action<Map, Vector3> Impact;

        internal void NotifyImpact(Map map, Vector3 point)
        {
            try { Impact?.Invoke(map, point); }
            catch (Exception error) { Log.ErrorOnce("[LihuatLib.Domain] Impact presentation failed: " + error, 128409731); }
        }

        internal void NotifyDamage(Thing target, DamageInfo damage)
        {
            Vector3 point = target.DrawPos;
            if (DomainImpactScope.TryOrigin(out Map originMap, out IntVec3 origin) && originMap == target.Map)
                point = origin.ToVector3Shifted();
            else if (damage.Instigator?.MapHeld == target.Map) point = damage.Instigator.PositionHeld.ToVector3Shifted();
            NotifyImpact(target.Map, point);
        }

        internal void Rebuild(IEnumerable<DomainArea> areas)
        {
            byMap.Clear();
            foreach (DomainArea area in areas)
            {
                if (area.ExternalBarrierRadius <= 0f || area.Ending || area.Closed || area.Map == null) continue;
                if (!byMap.TryGetValue(area.Map, out List<DomainArea> list)) byMap.Add(area.Map, list = new List<DomainArea>());
                list.Add(area);
            }
            if (!Active) departures = new ConditionalWeakTable<Pawn, Departure>();
        }

        private static bool Live(DomainArea area) => !area.Ending && !area.Closed;
        public bool Sheltered(Map map, IntVec3 cell)
        {
            if (map == null || !byMap.TryGetValue(map, out List<DomainArea> list)) return false;
            foreach (DomainArea area in list) if (Live(area) && area.CoversCell(cell)) return true;
            return false;
        }

        public bool Blocks(Map originMap, IntVec3 origin, Map targetMap, IntVec3 target)
        {
            if (targetMap == null || !byMap.TryGetValue(targetMap, out List<DomainArea> list)) return false;
            foreach (DomainArea area in list)
                if (Live(area) && area.CoversCell(target) && (originMap != targetMap || !area.CoversCell(origin))) return true;
            return false;
        }

        public bool Intercepts(Map map, Vector3 origin, Vector3 from, Vector3 to)
        {
            if (map == null || !byMap.TryGetValue(map, out List<DomainArea> list)) return false;
            Vector2 a = new Vector2(from.x, from.z), b = new Vector2(to.x, to.z), step = b - a;
            foreach (DomainArea area in list)
            {
                if (!Live(area) || area.CoversCell(origin.ToIntVec3())) continue;
                Vector3 center = area.MapCenter.ToVector3Shifted();
                Vector2 c = new Vector2(center.x, center.z);
                float t = step.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector2.Dot(c - a, step) / step.sqrMagnitude) : 0f;
                if ((a + t * step - c).sqrMagnitude <= area.ExternalBarrierRadius * area.ExternalBarrierRadius) return true;
            }
            return false;
        }

        public bool BlocksDamage(Thing target, DamageInfo damage)
        {
            if (!Active || damage.Amount <= 0f || target?.Spawned != true) return false;
            if (DomainImpactScope.TryOrigin(out Map originMap, out IntVec3 origin))
                return Blocks(originMap, origin, target.Map, target.Position);
            Thing source = damage.Instigator;
            return source != null
                && Blocks(source.MapHeld, source.PositionHeld, target.Map, target.Position);
        }

        internal bool AllowsMove(Thing thing, IntVec3 destination) => !Active || !(thing is Pawn) || !thing.Spawned
            || thing.Position.DistanceToSquared(destination) <= 2 || !Blocks(thing.Map, thing.Position, thing.Map, destination);

        internal void ObserveDeparture(Pawn pawn, DestructiveEntry entry)
        {
            if (!Active || entry != DestructiveEntry.DeSpawn || pawn?.Spawned != true) return;
            Departure saved = departures.GetOrCreateValue(pawn);
            saved.Map = pawn.Map; saved.Cell = pawn.Position;
        }

        internal void Arrived(Thing thing)
        {
            if (thing is Pawn pawn && pawn.Spawned) departures.Remove(pawn);
        }

        internal bool PlaceArrival(Thing thing, Map map, ref IntVec3 cell)
        {
            if (!Active || !(thing is Pawn || thing is RimWorld.Skyfaller || thing is RimWorld.PawnFlyer)) return true;
            Map originMap = thing.MapHeld;
            IntVec3 origin = thing.PositionHeld;
            if (thing is Pawn pawn && !pawn.Spawned && departures.TryGetValue(pawn, out Departure saved))
            { originMap = saved.Map; origin = saved.Cell; }
            if (!Blocks(originMap, origin, map, cell)) return true;
            // Redirect arrivals after a foreign caller has already unspawned them; rejecting Spawn would strand the payload.
            int radius = 1;
            if (byMap.TryGetValue(map, out List<DomainArea> list))
                foreach (DomainArea area in list) if (Live(area) && area.CoversCell(cell)) radius = Mathf.Max(radius, Mathf.CeilToInt(area.ExternalBarrierRadius * 2f + 2f));
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(cell, radius, false))
            {
                if (!candidate.InBounds(map) || !candidate.Standable(map) || Sheltered(map, candidate)) continue;
                cell = candidate; return true;
            }
            return false;
        }
    }
}
