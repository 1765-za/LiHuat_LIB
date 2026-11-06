using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal readonly record struct AvailabilityStorage(FieldInfo Field, int Argument = -1)
    {
        internal bool Known => Field != null || Argument >= 0;
    }

    internal sealed class LifeAvailabilityStorageFlow
    {
        internal sealed class Summary
        {
            internal readonly HashSet<AvailabilityStorage> Reads = new();
            internal readonly HashSet<AvailabilityStorage> ReferenceReads = new();
            internal readonly Dictionary<AvailabilityStorage, int> Writes = new();
            internal IEnumerable<KeyValuePair<AvailabilityStorage, int>> Mutations
            {
                get
                {
                    foreach (var write in Writes)
                        if (write.Value != 1 || !ReferenceReads.Contains(write.Key)) yield return write;
                }
            }
        }
        private readonly Func<MethodInfo, List<CodeInstruction>> read;
        internal readonly Dictionary<MethodInfo, Summary> Summaries = new();
        internal LifeAvailabilityStorageFlow(Func<MethodInfo, List<CodeInstruction>> read) { this.read = read; }

        internal Summary Inspect(MethodInfo method)
        {
            if (Summaries.TryGetValue(method, out Summary known)) return known;
            var result = new Summary(); Summaries.Add(method, result);
            int target = AvailabilityIlPatterns.Recipient(method);
            if (target < 0 || !LifeResourceEvidence.IsCustom(method.DeclaringType)) return result;
            var code = read(method);
            if (code == null) return result;
            var flow = new LifeControlFlow(code);
            result.ReferenceReads.UnionWith(LifeAvailabilityLookupFlow.Returns(method, code, target));
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Call && code[i].opcode != OpCodes.Callvirt || code[i].operand is not MethodInfo call) continue;
                bool reads = AvailabilityIlPatterns.MembershipCall(call, false), writes = AvailabilityIlPatterns.MembershipCall(call, true);
                if (reads || writes)
                {
                    AvailabilityStorage storage = Source(code, AvailabilityIlPatterns.Input(code, i, call, 0));
                    if (!storage.Known) continue;
                    bool same = AvailabilityIlPatterns.TargetInput(code, i, call, 1, target, true);
                    if (reads && same && LifeAvailabilityBooleanFlow.Returns(flow, i, false)) result.Reads.Add(storage);
                    if (writes) Add(result.Writes, storage, same ? 1 : 2);
                    continue;
                }
                int input = AvailabilityIlPatterns.Recipient(call);
                if (input < 0 || call.ContainsGenericParameters || call.Module.Assembly != method.Module.Assembly) continue;
                bool sameTarget = AvailabilityIlPatterns.TargetInput(code, i, call, input, target);
                Summary child = Inspect(call);
                if (sameTarget && method.ReturnType == typeof(bool) && LifeAvailabilityBooleanFlow.Returns(flow, i, false))
                    foreach (var storage in call.ReturnType == typeof(bool) ? child.Reads : child.ReferenceReads)
                    {
                        var bound = Bind(storage, code, i, call);
                        if (bound.Known) result.Reads.Add(bound);
                    }
                // A guarded lookup may populate its own cache. Its callers still need their original return semantics.
                foreach (var pair in child.Mutations)
                {
                    var bound = Bind(pair.Key, code, i, call);
                    if (bound.Known) Add(result.Writes, bound, sameTarget ? pair.Value : 2);
                }
            }
            return result;
        }

        private static void Add(Dictionary<AvailabilityStorage, int> writes, AvailabilityStorage storage, int flags) =>
            writes[storage] = writes.TryGetValue(storage, out int before) ? before | flags : flags;

        private static AvailabilityStorage Bind(AvailabilityStorage storage, List<CodeInstruction> code, int at, MethodInfo call) =>
            storage.Field != null ? storage : Source(code, AvailabilityIlPatterns.Input(code, at, call, storage.Argument));

        internal static AvailabilityStorage Source(List<CodeInstruction> code, int at)
        {
            at = AvailabilityIlPatterns.Origin(code, at);
            if (at < 0) return new(null);
            var line = code[at];
            if (line.opcode == OpCodes.Ldsfld) return new(line.operand as FieldInfo);
            return line.opcode.Name.StartsWith("ldarg", StringComparison.Ordinal)
                && line.opcode != OpCodes.Ldarga && line.opcode != OpCodes.Ldarga_S
                ? new(null, AvailabilityIlPatterns.Index(line)) : new(null);
        }
    }
}
