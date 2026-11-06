using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LihuatLib.Domains
{
    internal static class DomainEntityTransit
    {
        internal static void Enter(DomainArea area, IReadOnlyList<DomainArea> existing, Map entryMap, IntVec3 anchor,
            float radius, Map host, IntVec3 translation, DomainRollback transaction, System.Func<DomainEntity, bool> filter)
        {
            if (existing == null) area.Entities.AddRange(Combat.CombatProxyRegistry.Capture(entryMap, anchor, radius).Where(e => filter == null || filter(e)));
            var entities = (existing ?? Enumerable.Empty<DomainArea>()).SelectMany(a => a.Entities).Concat(area.Entities).Distinct().ToList();
            foreach (DomainEntity entity in entities)
            {
                if (entity.Map == host) continue;
                transaction.EntityPositions.Add(new DomainEntityMove { Entity = entity, Map = entity.Map, Position = entity.Position });
                Vector3 direction = (entity.Position - anchor).ToVector3();
                float distance = Mathf.Clamp(direction.magnitude * .4f, 3f, host.Size.x / 2f - 2f);
                IntVec3 destination = existing == null ? host.Center + (direction.normalized * distance).ToIntVec3()
                    : DomainCoordinates.InArena(existing, entryMap, entity.Map, entity.Position) + translation;
                entity.Move(host, destination);
            }
        }

        internal static void Return(IEnumerable<DomainArea> areas, DomainReturnDestinations destinations)
        {
            foreach (DomainEntity entity in areas.SelectMany(a => a.Entities).Distinct())
            {
                DomainReturnDestination destination = destinations.Resolve(entity.Source, entity.Origin);
                entity.Move(destination.Map, destination.Cell);
            }
        }

        internal static void CollectBornInside(IReadOnlyList<DomainArea> areas, Map arena)
        {
            DomainArea store = areas[0];
            var known = areas.SelectMany(a => a.Entities).Select(e => e.Key).ToHashSet();
            foreach (Map map in areas.SelectMany(a => new[] { a.Map, a.HomeMap }).Where(m => m != null).Distinct())
                foreach (DomainEntity entity in Combat.CombatProxyRegistry.Capture(map, map.Center, map.Size.x + map.Size.z))
                    if (known.Add(entity.Key))
                    {
                        entity.Origin = DomainCoordinates.OnSource(areas, arena, map, entity.Position);
                        entity.Source = store.Source;
                        store.Entities.Add(entity);
                    }
        }
    }
}
