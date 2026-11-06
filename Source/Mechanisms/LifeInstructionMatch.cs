using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class LifeInstructionMatch
    {
        public static bool Equivalent(List<CodeInstruction> expected, List<CodeInstruction> actual)
        {
            if (expected.Count != actual.Count) return false;
            Dictionary<Label, int> left = Labels(expected), right = Labels(actual);
            for (int i = 0; i < expected.Count; i++)
            {
                CodeInstruction a = expected[i], b = actual[i];
                if (a.opcode != b.opcode) return false;
                if (a.operand is Label destination)
                {
                    if (!(b.operand is Label target) || !left.TryGetValue(destination, out int x) || !right.TryGetValue(target, out int y) || x != y) return false;
                }
                else if (a.operand is Label[] destinations)
                {
                    if (!(b.operand is Label[] targets) || destinations.Length != targets.Length
                        || !destinations.Select(l => left[l]).SequenceEqual(targets.Select(l => right[l]))) return false;
                }
                else if (!Equals(Operand(a.operand), Operand(b.operand))) return false;
            }
            return true;
        }
        private static object Operand(object value)
        {
            if (value is LocalBuilder local) return local.LocalIndex;
            if (value is LocalVariableInfo variable) return variable.LocalIndex;
            if (value is byte || value is sbyte || value is short || value is ushort) return Convert.ToInt32(value);
            return value;
        }
        private static Dictionary<Label, int> Labels(List<CodeInstruction> code)
        {
            var result = new Dictionary<Label, int>();
            for (int i = 0; i < code.Count; i++) foreach (Label label in code[i].labels) result[label] = i;
            return result;
        }
    }
}
