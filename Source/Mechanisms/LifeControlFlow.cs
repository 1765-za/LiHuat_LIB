using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeControlFlow
    {
        public readonly List<CodeInstruction> Code;
        public readonly int[][] Next;
        public readonly int[] Join;
        public readonly HashSet<int>[] LiveLocals;
        public readonly int[] Checkpoint;
        public readonly int CheckpointCount;

        public LifeControlFlow(List<CodeInstruction> code)
        {
            Code = code;
            int count = code.Count;
            var labels = new Dictionary<Label, int>();
            for (int i = 0; i < count; i++) foreach (Label label in code[i].labels) labels[label] = i;
            Next = new int[count][];
            for (int i = 0; i < count; i++)
            {
                CodeInstruction line = code[i];
                if (line.opcode.FlowControl == FlowControl.Return || line.opcode.FlowControl == FlowControl.Throw) Next[i] = new[] { count };
                else if (line.operand is Label destination)
                    Next[i] = line.opcode.FlowControl == FlowControl.Cond_Branch ? new[] { labels[destination], i + 1 } : new[] { labels[destination] };
                else if (line.operand is Label[] choices) Next[i] = choices.Select(x => labels[x]).Concat(new[] { i + 1 }).ToArray();
                else Next[i] = new[] { i + 1 };
            }
            Join = LifePostDominance.Joins(Next);
            var incoming = new int[count];
            var backward = new bool[count];
            for (int i = 0; i < count; i++) foreach (int target in Next[i])
                if (target < count) { incoming[target]++; if (target <= i) backward[target] = true; }
            Checkpoint = new int[count];
            for (int i = 0; i < count; i++)
                Checkpoint[i] = i == 0 || incoming[i] > 1 || backward[i] ? CheckpointCount++ : -1;
            bool changed;
            LiveLocals = new HashSet<int>[count + 1];
            for (int i = 0; i <= count; i++) LiveLocals[i] = new HashSet<int>();
            do
            {
                changed = false;
                for (int i = count - 1; i >= 0; i--)
                {
                    var live = new HashSet<int>();
                    foreach (int next in Next[i]) live.UnionWith(LiveLocals[next]);
                    string op = code[i].opcode.Name;
                    if (op.StartsWith("ldloc") || op.StartsWith("stloc"))
                    {
                        char last = op[op.Length - 1];
                        int index = last >= '0' && last <= '3' ? last - '0'
                            : code[i].operand is LocalBuilder local ? local.LocalIndex
                            : code[i].operand is System.Reflection.LocalVariableInfo variable ? variable.LocalIndex
                            : System.Convert.ToInt32(code[i].operand);
                        if (op.StartsWith("stloc")) live.Remove(index); else live.Add(index);
                    }
                    if (!live.SetEquals(LiveLocals[i])) { LiveLocals[i] = live; changed = true; }
                }
            } while (changed);
        }
    }
}
