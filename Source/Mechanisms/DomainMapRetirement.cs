using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Domains;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DomainMapRetirement
    {
        private static readonly Action<Game, Map, bool> remove = Build();

        internal static void Remove(DomainArea area)
        {
            Map map = area.HomeMap;
            if (map == null || !Find.Maps.Contains(map)) return;
            if (!OperationScope.Transferring || !area.Enclosed || area.Parent == null || map == area.Source)
                throw new InvalidOperationException("Map retirement requires an owned domain pocket map.");
            if (!DomainTransit.Empty(map)) throw new InvalidOperationException("Domain map still holds participants or cargo: " + map.uniqueID);
            try { remove(Current.Game, map, true); }
            finally
            {
                if (!Find.Maps.Contains(map))
                {
                    ReferenceListStorage<PocketMapParent>.Remove(Find.World.pocketMaps, area.Parent);
                    Find.World.renderer.wantedMode = WorldRenderMode.None;
                }
            }
            if (Find.Maps.Contains(map)) throw new InvalidOperationException("Domain map removal was blocked: " + map.uniqueID);
        }

        private static Action<Game, Map, bool> Build()
        {
            MethodInfo original = AccessTools.Method(typeof(Game), nameof(Game.DeinitAndRemoveMap));
            var body = IndependentIl.Clone(original, (method, virtualCall) =>
            {
                if (method.DeclaringType == typeof(Map) && method.Name == nameof(Map.Dispose))
                    return AccessTools.Method(typeof(DomainMapDisposal), nameof(DomainMapDisposal.Dispose));
                if (method.DeclaringType != typeof(List<Map>)) return method;
                if (method.Name == "Remove") return AccessTools.Method(typeof(DomainMapRetirement), nameof(RemoveMap));
                if (method.Name == "Contains") return AccessTools.Method(typeof(DomainMapRetirement), nameof(ContainsMap));
                if (method.Name == "IndexOf") return AccessTools.Method(typeof(DomainMapRetirement), nameof(IndexOfMap));
                return method;
            });
            return (Action<Game, Map, bool>)body.CreateDelegate(typeof(Action<Game, Map, bool>));
        }
        private static bool RemoveMap(List<Map> maps, Map target) => ReferenceListStorage<Map>.Remove(maps, target) != 0;
        private static bool ContainsMap(List<Map> maps, Map target) => ReferenceListStorage<Map>.Contains(maps, target);
        private static int IndexOfMap(List<Map> maps, Map target)
        {
            Map[] items = ReferenceListStorage<Map>.Items(maps);
            int count = ReferenceListStorage<Map>.Count(maps);
            for (int i = 0; i < count; i++) if (ReferenceEquals(items[i], target)) return i;
            return -1;
        }
    }
}
