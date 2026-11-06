using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class PawnMapPresence
    {
        private static readonly AccessTools.FieldRef<DynamicDrawManager, List<Thing>> drawables =
            AccessTools.FieldRefAccess<DynamicDrawManager, List<Thing>>("drawThings");
        private static readonly AccessTools.FieldRef<DynamicDrawManager, bool> drawing =
            AccessTools.FieldRefAccess<DynamicDrawManager, bool>("drawingNow");
        private static readonly FieldInfo factions = AccessTools.Field(typeof(MapPawns), "pawnsInFactionSpawned");
        private static readonly FieldInfo lists = AccessTools.Field(factions.FieldType, "pawnList");
        private static readonly AccessTools.FieldRef<MapPawns, List<Pawn>> spawned = AccessTools.FieldRefAccess<MapPawns, List<Pawn>>("pawnsSpawned");
        private static readonly AccessTools.FieldRef<MapPawns, List<Pawn>> prisoners = AccessTools.FieldRefAccess<MapPawns, List<Pawn>>("prisonersOfColonySpawned");
        private static readonly AccessTools.FieldRef<MapPawns, List<Pawn>> slaves = AccessTools.FieldRefAccess<MapPawns, List<Pawn>>("slavesOfColonySpawned");
        private static readonly Action<MapPawns> notify = OriginalMethods.CreateTransfer<Action<MapPawns>>(
            AccessTools.Method(typeof(MapPawns), "DoListChangedNotifications"));
        private static readonly Action<MapPawns, Pawn> register = OriginalMethods.CreateSpawn<Action<MapPawns, Pawn>>(
            AccessTools.Method(typeof(MapPawns), nameof(MapPawns.RegisterPawn)));
        private static readonly Action<DynamicDrawManager, Thing> draw = OriginalMethods.CreateSpawn<Action<DynamicDrawManager, Thing>>(
            AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.RegisterDrawable)));
        private static readonly Action<ColonistBar> dirtyBar = OriginalMethods.Create<Action<ColonistBar>>(
            AccessTools.Method(typeof(ColonistBar), nameof(ColonistBar.MarkColonistsDirty)));

        private static bool InFaction(Pawn pawn, Map map)
        {
            Faction faction = pawn.Faction;
            if (faction == null) return true;
            object bucket = factions.GetValue(map.mapPawns);
            return bucket != null && ((Dictionary<Faction, List<Pawn>>)lists.GetValue(bucket)).TryGetValue(faction, out List<Pawn> values)
                && ReferenceListStorage<Pawn>.Contains(values, pawn);
        }

        public static bool Placed(Pawn pawn, Map map) => MapRegistrationStorage.Placed(map, pawn);
        public static bool Registered(Pawn pawn, Map map) => Placed(pawn, map) && Complete(pawn, map);

        public static bool Complete(Pawn pawn, Map map) => pawn != null && map != null
            && (pawn.def.drawerType == DrawerType.None || ReferenceListStorage<Thing>.Contains(drawables(map.dynamicDrawManager), pawn))
            && (!pawn.mindState.Active || ReferenceListStorage<Pawn>.Contains(spawned(map.mapPawns), pawn) && InFaction(pawn, map));

        public static bool Restore(Pawn pawn, Map map)
        {
            if (pawn?.Spawned != true || pawn.Map != map || Core.PawnLife.Dead(pawn) || drawing(map.dynamicDrawManager)) return false;
            bool changed = false;
            if (pawn.mindState.Active && (!ReferenceListStorage<Pawn>.Contains(spawned(map.mapPawns), pawn) || !InFaction(pawn, map)))
            {
                register(map.mapPawns, pawn);
                bool repaired = ReferenceListStorage<Pawn>.Ensure(spawned(map.mapPawns), pawn);
                if (pawn.Faction != null)
                {
                    object bucket = factions.GetValue(map.mapPawns);
                    var groups = (Dictionary<Faction, List<Pawn>>)lists.GetValue(bucket);
                    if (!groups.TryGetValue(pawn.Faction, out List<Pawn> members)) groups.Add(pawn.Faction, members = new List<Pawn>());
                    repaired |= ReferenceListStorage<Pawn>.Ensure(members, pawn);
                }
                if (pawn.IsPrisonerOfColony) repaired |= ReferenceListStorage<Pawn>.Ensure(prisoners(map.mapPawns), pawn);
                if (pawn.IsSlaveOfColony) repaired |= ReferenceListStorage<Pawn>.Ensure(slaves(map.mapPawns), pawn);
                if (repaired) notify(map.mapPawns);
                changed = true;
            }
            if (pawn.def.drawerType != DrawerType.None && !ReferenceListStorage<Thing>.Contains(drawables(map.dynamicDrawManager), pawn))
            { draw(map.dynamicDrawManager, pawn); ReferenceListStorage<Thing>.Ensure(drawables(map.dynamicDrawManager), pawn); changed = true; }
            if (changed)
            {
                pawn.Drawer.renderer.SetAllGraphicsDirty();
                if (Find.ColonistBar != null) dirtyBar(Find.ColonistBar);
            }
            return Complete(pawn, map);
        }
    }
}
