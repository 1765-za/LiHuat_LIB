using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LihuatLib.Mechanisms
{
    internal static class MapRegistrationStorage
    {
        private static readonly AccessTools.FieldRef<Thing, IntVec3> position = AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt");
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly AccessTools.FieldRef<Thing, Rot4> rotation = AccessTools.FieldRefAccess<Thing, Rot4>("rotationInt");
        private static readonly AccessTools.FieldRef<ThingGrid, List<Thing>[]> grid = AccessTools.FieldRefAccess<ThingGrid, List<Thing>[]>("thingGrid");
        private static readonly AccessTools.FieldRef<RegionGrid, Region[]> regions = AccessTools.FieldRefAccess<RegionGrid, Region[]>("regionGrid");
        private static readonly AccessTools.FieldRef<Region, ListerThings> regionLister = AccessTools.FieldRefAccess<Region, ListerThings>("listerThings");
        private static readonly AccessTools.FieldRef<TooltipGiverList, List<Thing>> tooltips = AccessTools.FieldRefAccess<TooltipGiverList, List<Thing>>("givers");
        private static readonly AccessTools.FieldRef<DynamicDrawManager, List<Thing>> drawing = AccessTools.FieldRefAccess<DynamicDrawManager, List<Thing>>("drawThings");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<IAttackTarget>> all = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<IAttackTarget>>("allTargets");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, Dictionary<Faction, HashSet<IAttackTarget>>> hostile = AccessTools.FieldRefAccess<AttackTargetsCache, Dictionary<Faction, HashSet<IAttackTarget>>>("targetsHostileToFaction");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> aggressive = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("pawnsInAggroMentalState");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> factionless = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("factionlessHumanlikes");
        private static readonly FieldInfo[] pawnLists = typeof(MapPawns).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(List<Pawn>) || f.FieldType == typeof(List<Thing>) || f.FieldType.DeclaringType == typeof(MapPawns)).ToArray();
        private static readonly Dictionary<Type, FieldInfo[]> nestedLists = new Dictionary<Type, FieldInfo[]>();

        internal static CellRect Footprint(Thing thing) => GenAdj.OccupiedRect(position(thing), rotation(thing), thing.def.size);

        internal static bool Placed(Map map, Thing thing)
        {
            if (map == null || thing == null || mapIndex(thing) < 0 || mapIndex(thing) >= Find.Maps.Count
                || Find.Maps[mapIndex(thing)] != map || thing.holdingOwner != map.spawnedThings
                || !RegistrationStorage.Holder(map.spawnedThings, thing, false)
                || !ThingListerStorage.Registered(map.listerThings, thing)) return false;
            CellRect footprint = Footprint(thing);
            List<Thing>[] cells = grid(map.thingGrid);
            int width = map.Size.x, height = map.Size.z;
            if (footprint.minX < 0 || footprint.minZ < 0 || footprint.maxX >= width || footprint.maxZ >= height) return false;
            for (int z = footprint.minZ; z <= footprint.maxZ; z++)
                for (int x = footprint.minX; x <= footprint.maxX; x++)
                    if (!ReferenceListStorage<Thing>.Contains(cells[z * width + x], thing)) return false;
            return MapTargetRegistration.Registered(map, thing);
        }

        internal static bool Process(Map map, Thing target, CellRect footprint, bool remove)
        {
            bool found = RegistrationStorage.Holder(map.spawnedThings, target, remove);
            found |= ThingListerStorage.Process(map.listerThings, target, remove);
            found |= RegistrationStorage.List(tooltips(map.tooltipGiverList), target, remove);
            found |= RegistrationStorage.List(drawing(map.dynamicDrawManager), target, remove);
            if (target is Pawn pawn) found |= Pawns(map.mapPawns, pawn, remove);
            found |= Targets(map.attackTargetsCache, target, remove);

            List<Thing>[] cells = grid(map.thingGrid);
            Region[] regionCells = regions(map.regionGrid);
            var visited = new HashSet<Region>();
            int width = map.Size.x, height = map.Size.z;
            for (int z = Math.Max(0, footprint.minZ - 1); z <= Math.Min(height - 1, footprint.maxZ + 1); z++)
                for (int x = Math.Max(0, footprint.minX - 1); x <= Math.Min(width - 1, footprint.maxX + 1); x++)
                {
                    int index = z * width + x;
                    if (x >= footprint.minX && x <= footprint.maxX && z >= footprint.minZ && z <= footprint.maxZ)
                        found |= RegistrationStorage.List(cells[index], target, remove);
                    Region region = regionCells[index];
                    if (region != null && visited.Add(region)) found |= ThingListerStorage.Process(regionLister(region), target, remove);
                }
            return found;
        }

        private static bool Pawns(MapPawns map, Pawn target, bool remove)
        {
            bool found = false;
            foreach (FieldInfo field in pawnLists)
            {
                object value = field.GetValue(map);
                if (value is List<Pawn> list) found |= RegistrationStorage.List(list, target, remove);
                else if (value is List<Thing> things) found |= RegistrationStorage.List(things, (Thing)target, remove);
                else if (value != null)
                {
                    Type type = value.GetType();
                    if (!nestedLists.TryGetValue(type, out FieldInfo[] fields))
                        nestedLists[type] = fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (FieldInfo nested in fields)
                    {
                        object entry = nested.GetValue(value);
                        if (entry is List<Pawn> pawns) found |= RegistrationStorage.List(pawns, target, remove);
                        else if (entry is IDictionary dictionary)
                            foreach (object item in dictionary.Values)
                                if (item is List<Pawn> members) found |= RegistrationStorage.List(members, target, remove);
                    }
                }
            }
            return found;
        }

        private static bool Targets(AttackTargetsCache cache, Thing thing, bool remove)
        {
            if (!(thing is IAttackTarget target)) return false;
            bool found = RegistrationStorage.Set(all(cache), target, remove);
            foreach (HashSet<IAttackTarget> set in hostile(cache).Values) found |= RegistrationStorage.Set(set, target, remove);
            if (thing is Pawn pawn) found |= RegistrationStorage.Set(aggressive(cache), pawn, remove) | RegistrationStorage.Set(factionless(cache), pawn, remove);
            return found;
        }
    }
}
