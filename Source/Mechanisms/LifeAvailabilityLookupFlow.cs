using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class LifeAvailabilityLookupFlow
    {
        internal static HashSet<AvailabilityStorage> Returns(MethodInfo method, List<CodeInstruction> code, int target)
        {
            var result = new HashSet<AvailabilityStorage>();
            if (method.ReturnType.IsValueType || method.ReturnType.IsByRef || method.ReturnType.IsPointer) return result;
            var outputs = new Dictionary<int, HashSet<AvailabilityStorage>>();
            var addresses = new HashSet<int>();
            var invalid = new HashSet<int>();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Call && code[i].opcode != OpCodes.Callvirt
                    || code[i].operand is not MethodInfo call || !AvailabilityIlPatterns.LookupCall(call)) continue;
                int at = AvailabilityIlPatterns.Input(code, i, call, 2);
                if (at < 0 || code[at].opcode != OpCodes.Ldloca && code[at].opcode != OpCodes.Ldloca_S) continue;
                int local = AvailabilityIlPatterns.Index(code[at]);
                AvailabilityStorage storage = LifeAvailabilityStorageFlow.Source(code, AvailabilityIlPatterns.Input(code, i, call, 0));
                if (!storage.Known || !AvailabilityIlPatterns.TargetInput(code, i, call, 1, target, true))
                { invalid.Add(local); continue; }
                addresses.Add(at);
                if (!outputs.TryGetValue(local, out var sources)) outputs.Add(local, sources = new());
                sources.Add(storage);
            }
            for (int i = 0; i < code.Count; i++)
            {
                var op = code[i].opcode;
                if (op.Name.StartsWith("stloc") || (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S) && !addresses.Contains(i))
                    invalid.Add(AvailabilityIlPatterns.Index(code[i]));
            }
            // Only lookup outputs or null may leave the method; a constructed/default record is not membership evidence.
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ret) continue;
                if (code[i].labels.Count != 0) return new();
                int at = i - 1;
                while (at >= 0 && (code[at].opcode == OpCodes.Nop || code[at].opcode == OpCodes.Castclass)) at--;
                if (at < 0) return new();
                if (code[at].opcode == OpCodes.Ldnull) continue;
                if (!code[at].opcode.Name.StartsWith("ldloc") || code[at].opcode == OpCodes.Ldloca || code[at].opcode == OpCodes.Ldloca_S)
                    return new();
                int local = AvailabilityIlPatterns.Index(code[at]);
                if (invalid.Contains(local) || !outputs.TryGetValue(local, out var sources)) return new();
                result.UnionWith(sources);
            }
            return result;
        }
    }
}
