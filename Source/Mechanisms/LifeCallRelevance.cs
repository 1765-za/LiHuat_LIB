using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    [Flags]
    internal enum LifeCallUse { None = 0, Value = 1, Death = 2, Unknown = 4, Deferred = 8 }

    internal sealed class LifeCallRelevance
    {
        private readonly Type rootType;
        private readonly LifeIlReader reader;
        private readonly Dictionary<MethodInfo, LifeCallUse> cache = new Dictionary<MethodInfo, LifeCallUse>();
        internal int VisitedMethods;
        public LifeCallRelevance(Type rootType, LifeIlReader reader) { this.rootType = rootType; this.reader = reader; }

        public bool Needed(MethodInfo method)
        {
            LifeCallUse use = Inspect(method);
            Type result = method.ReturnType;
            bool trackedResult = result != typeof(void) && (!result.IsValueType || LifeResourceEvidence.ScalarState(result));
            return (use & (LifeCallUse.Death | LifeCallUse.Unknown | LifeCallUse.Deferred)) != 0 || trackedResult && (use & LifeCallUse.Value) != 0;
        }

        internal bool HasEffects(MethodInfo method) => (Inspect(method) & (LifeCallUse.Death | LifeCallUse.Unknown | LifeCallUse.Deferred)) != 0;

        private LifeCallUse Inspect(MethodInfo method)
        {
            if (cache.TryGetValue(method, out LifeCallUse found)) return found;
            LifeCallUse use = LifeCallUse.None;
            var visited = new HashSet<MethodInfo>();
            var pending = new Stack<MethodInfo>();
            pending.Push(method);
            try
            {
                while (pending.Count > 0)
                {
                    MethodInfo current = pending.Pop();
                    if (!visited.Add(current)) continue;
                    VisitedMethods++;
                    if (cache.TryGetValue(current, out LifeCallUse known)) { use |= known; continue; }
                    List<CodeInstruction> code = reader.Read(current);
                    if (code == null) { use |= LifeCallUse.Unknown; continue; }
                    foreach (CodeInstruction line in code)
                    {
                    if (line.opcode.Name.StartsWith("ldarg")) use |= LifeCallUse.Value;
                    if (line.operand is FieldInfo field)
                    {
                        if ((line.opcode.Name == "ldfld" || line.opcode.Name == "ldflda") && field.DeclaringType.IsAssignableFrom(rootType)) use |= LifeCallUse.Value;
                        if (line.opcode.Name == "stfld" && field.DeclaringType == typeof(Pawn_HealthTracker)) use |= LifeCallUse.Death;
                        if ((line.opcode.Name == "stfld" || line.opcode.Name == "stsfld") && typeof(Delegate).IsAssignableFrom(field.FieldType)) use |= LifeCallUse.Deferred;
                    }
                    if (!(line.operand is MethodInfo called)) continue;
                    foreach (ParameterInfo parameter in called.GetParameters())
                        if (typeof(Delegate).IsAssignableFrom(parameter.ParameterType)) use |= LifeCallUse.Deferred;
                    if (typeof(Delegate).IsAssignableFrom(called.DeclaringType) && called.Name == "Invoke") use |= LifeCallUse.Deferred;
                    if (typeof(Entity).IsAssignableFrom(called.DeclaringType) && (called.Name == "Kill" || called.Name == "Destroy" || called.Name == "Discard" || called.Name == "DeSpawn")
                        || called.DeclaringType == typeof(Pawn_HealthTracker) && called.Name == "SetDead") use |= LifeCallUse.Death;
                    else if (typeof(FieldInfo).IsAssignableFrom(called.DeclaringType) && called.Name == "SetValue"
                        || typeof(MethodBase).IsAssignableFrom(called.DeclaringType) && called.Name == "Invoke") use |= LifeCallUse.Unknown;
                    else if (called.DeclaringType == typeof(Hediff) && called.Name == "get_Severity"
                        || called.DeclaringType == typeof(HediffComp) && called.Name == "get_Pawn"
                        || called.DeclaringType.IsGenericType && (called.Name == "get_Item" || called.Name == "TryGetValue")) use |= LifeCallUse.Value;
                    else if (LifeResourceEvidence.IsCustom(called.DeclaringType)) pending.Push(called);
                    }
                }
                cache[method] = use;
                return use;
            }
            catch (Exception) { return LifeCallUse.Unknown; }
        }
    }
}
