using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Combat
{
    internal static class ManagedCombatDiscovery
    {
        private static readonly Dictionary<Type, ManagedCombatSource[]> managers = new();
        private static readonly ConditionalWeakTable<Map, ManagedCombatSource[]> maps = new();
        internal static void Prepare(Map map)
        {
            if (map == null || maps.TryGetValue(map, out _)) return;
            var sources = new List<ManagedCombatSource>();
            foreach (MapComponent component in map.components)
            {
                Type type = component.GetType();
                if (!managers.TryGetValue(type, out ManagedCombatSource[] found))
                {
                    var shapes = new List<ManagedCombatSource>();
                    if (type.Assembly != typeof(MapComponent).Assembly && type.Assembly != typeof(ManagedCombatDiscovery).Assembly)
                    {
                        var il = new ManagedCombatIl();
                        foreach (FieldInfo field in type.GetFields(ManagedCombatIl.Members).Where(f => f.FieldType.IsGenericType
                            && f.FieldType.GetGenericTypeDefinition() == typeof(List<>)))
                        {
                            var shape = ManagedCombatShape.Discover(type, field, il);
                            if (shape != null)
                            {
                                Core.RetirementSubjects.Register(shape.StateType);
                                shapes.Add(new ManagedCombatSource(shape));
                            }
                        }
                    }
                    managers[type] = found = shapes.ToArray();
                    foreach (ManagedCombatSource source in found) CombatProxyRegistry.Register(source);
                }
                sources.AddRange(found);
            }
            maps.Add(map, sources.ToArray());
        }
        internal static ManagedCombatSource Source(Map map, string id)
        {
            Prepare(map);
            return maps.GetValue(map, _ => Array.Empty<ManagedCombatSource>()).FirstOrDefault(s => s.Id == id);
        }
        internal static void RestoreRetirements(Map map)
        {
            Prepare(map);
            CombatRetirementMap ledger = map.GetComponent<CombatRetirementMap>();
            foreach (ManagedCombatSource source in maps.GetValue(map, _ => Array.Empty<ManagedCombatSource>()))
                if (ledger.HasSource(source.Id)) ManagedRegistrationBoundary.Install(source.Shape);
        }
    }
}
