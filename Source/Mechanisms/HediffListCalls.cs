using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class HediffListCalls
    {
        private static readonly Dictionary<MethodInfo, MethodInfo> targets = CreateTargets();
        private static readonly Dictionary<MethodInfo, DynamicMethod> bridges = new();
        internal static void Prepare() => _ = targets.Count;
        internal static IEnumerable<MethodInfo> Methods => targets.Keys;
        private static Dictionary<MethodInfo, MethodInfo> CreateTargets()
        {
            var result = new Dictionary<MethodInfo, MethodInfo>();
            foreach (string name in new[] { "Add", "Insert", "AddRange", "InsertRange", "set_Item" })
                result.Add(AccessTools.Method(typeof(List<Hediff>), name), AccessTools.Method(typeof(HediffListInsertion), name == "set_Item" ? "Replace" : name));
            result.Add(AccessTools.Method(typeof(ICollection<Hediff>), "Add"), AccessTools.Method(typeof(HediffListInsertion), "CollectionAdd"));
            result.Add(AccessTools.Method(typeof(IList<Hediff>), "Insert"), AccessTools.Method(typeof(HediffListInsertion), "ListInsert"));
            result.Add(AccessTools.PropertySetter(typeof(IList<Hediff>), "Item"), AccessTools.Method(typeof(HediffListInsertion), "ListReplace"));
            return result;
        }
        internal static bool Known(MethodInfo method) => method != null && targets.ContainsKey(method);
        internal static MethodInfo Replacement(MethodInfo method) => method != null ? targets.GetValueOrDefault(method) : null;
        internal static DynamicMethod Body(MethodInfo method)
        {
            MethodInfo target = Replacement(method);
            if (target == null) return null;
            lock (bridges)
            {
                if (bridges.TryGetValue(method, out var known)) return known;
                bool previous = DynamicWriteCatalogue.Building;
                DynamicWriteCatalogue.Building = true;
                try
                {
                    DynamicMethod body = IndependentIl.New(method);
                    ILGenerator il = body.GetILGenerator();
                    int count = target.GetParameters().Length;
                    for (short i = 0; i < count; i++) il.Emit(OpCodes.Ldarg, i);
                    il.Emit(OpCodes.Call, target); il.Emit(OpCodes.Ret);
                    bridges.Add(method, body); return body;
                }
                finally { DynamicWriteCatalogue.Building = previous; }
            }
        }
        internal static void RequireReceiver(object value)
        { if (value == null) throw new NullReferenceException(); }
    }
}
