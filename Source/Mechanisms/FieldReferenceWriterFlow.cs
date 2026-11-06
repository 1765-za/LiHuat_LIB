using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class FieldReferenceWriterFlow
    {
        private readonly record struct Source(int Argument, Type Slot = null);
        private static readonly Source Unknown = new(-1);

        internal static int[] Resolve(MethodInfo method, List<CodeInstruction> code, int call, Type slot)
        {
            int cursor = call - 1;
            Source value = Read(code, ref cursor, new HashSet<int>());
            Source owner = Read(code, ref cursor, new HashSet<int>());
            Source field = Read(code, ref cursor, new HashSet<int>());
            ParameterInfo[] parameters = method.GetParameters();
            int offset = method.IsStatic ? 0 : 1;
            bool Argument(Source source, Type type) => source.Argument >= offset && source.Argument - offset < parameters.Length
                && parameters[source.Argument - offset].ParameterType == type;
            return field.Slot != null && slot.IsAssignableFrom(field.Slot) && Argument(field, typeof(FieldInfo))
                && owner.Slot == null && value.Slot == null && Argument(owner, typeof(object)) && Argument(value, typeof(object))
                && owner.Argument != value.Argument ? new[] { field.Argument, owner.Argument, value.Argument } : null;
        }

        private static Source Read(List<CodeInstruction> code, ref int at, HashSet<int> reading)
        {
            if (at < 0 || !reading.Add(at)) return Unknown;
            CodeInstruction line = code[at--];
            OpCode op = line.opcode;
            if (op == OpCodes.Nop || op == OpCodes.Castclass || op == OpCodes.Box)
                return Read(code, ref at, reading);
            int argument = op == OpCodes.Ldarg || op == OpCodes.Ldarg_S ? Convert.ToInt32(line.operand)
                : op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value ? op.Value - OpCodes.Ldarg_0.Value : -1;
            if (argument >= 0)
                return code.Any(item => (item.opcode == OpCodes.Starg || item.opcode == OpCodes.Starg_S)
                    && Convert.ToInt32(item.operand) == argument) ? Unknown : new Source(argument);
            int local = Local(line, true);
            if (local >= 0)
            {
                int store = -1;
                for (int i = 0; i < code.Count; i++)
                    if (Local(code[i], false) == local)
                    { if (store >= 0 || i > at) return Unknown; store = i; }
                if (store < 0) return Unknown;
                int cursor = store - 1;
                return Read(code, ref cursor, reading);
            }
            if ((op == OpCodes.Call || op == OpCodes.Callvirt) && line.operand is MethodInfo resolve)
            {
                ParameterInfo[] parameters = resolve.GetParameters();
                Source input = Unknown;
                for (int i = parameters.Length - 1; i >= 0; i--)
                {
                    Source current = Read(code, ref at, reading);
                    if (i == 0) input = current;
                }
                if (!resolve.IsStatic) Read(code, ref at, reading);
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(FieldInfo)
                    && input.Slot == null && input.Argument >= 0)
                    return input with { Slot = resolve.ReturnType };
            }
            return Unknown;
        }

        private static int Local(CodeInstruction line, bool load)
        {
            short first = (load ? OpCodes.Ldloc_0 : OpCodes.Stloc_0).Value;
            short last = (load ? OpCodes.Ldloc_3 : OpCodes.Stloc_3).Value;
            if (line.opcode.Value >= first && line.opcode.Value <= last) return line.opcode.Value - first;
            if (line.opcode != (load ? OpCodes.Ldloc : OpCodes.Stloc) && line.opcode != (load ? OpCodes.Ldloc_S : OpCodes.Stloc_S)) return -1;
            return line.operand is LocalBuilder local ? local.LocalIndex : Convert.ToInt32(line.operand);
        }
    }
}
