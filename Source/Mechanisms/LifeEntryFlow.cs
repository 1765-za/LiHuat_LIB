using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class LifeEntryFlow
    {
        internal static bool Terminates(List<CodeInstruction> code, HashSet<int> terminals)
        {
            if (terminals.Count == 0 || code.Count == 0) return false;
            var labels = new Dictionary<Label, int>();
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction line = code[i];
                if (line.opcode == OpCodes.Leave || line.opcode == OpCodes.Leave_S
                    || line.opcode == OpCodes.Endfinally || line.opcode == OpCodes.Endfilter || line.opcode == OpCodes.Rethrow) return false;
                foreach (Label label in line.labels) labels[label] = i;
            }
            var edges = new Dictionary<int, List<int>>();
            var incoming = new Dictionary<int, int>();
            var pending = new Queue<int>();
            pending.Enqueue(0);
            bool reachedTerminal = false;
            while (pending.Count != 0)
            {
                int at = pending.Dequeue();
                if (at >= code.Count) return false;
                if (terminals.Contains(at)) { reachedTerminal = true; continue; }
                if (edges.ContainsKey(at)) continue;
                CodeInstruction line = code[at];
                if (line.opcode.FlowControl is FlowControl.Return or FlowControl.Throw) return false;
                var next = new List<int>(2);
                edges.Add(at, next);
                incoming.TryAdd(at, 0);
                if (line.operand is Label branch)
                {
                    if (!labels.TryGetValue(branch, out int target)) return false;
                    next.Add(target);
                }
                else if (line.operand is Label[] choices)
                    foreach (Label choice in choices)
                    {
                        if (!labels.TryGetValue(choice, out int target)) return false;
                        next.Add(target);
                    }
                if (line.opcode.FlowControl != FlowControl.Branch) next.Add(at + 1);
                foreach (int target in next)
                {
                    pending.Enqueue(target);
                    if (!terminals.Contains(target)) incoming[target] = incoming.GetValueOrDefault(target) + 1;
                }
            }
            if (!reachedTerminal) return false;
            foreach (var pair in incoming) if (pair.Value == 0) pending.Enqueue(pair.Key);
            int completed = 0;
            while (pending.Count != 0)
            {
                int at = pending.Dequeue();
                completed++;
                foreach (int next in edges[at])
                    if (!terminals.Contains(next) && --incoming[next] == 0) pending.Enqueue(next);
            }
            // A loop with a non-terminal path cannot be promoted to an unconditional entry denial.
            return completed == edges.Count;
        }
    }
}
