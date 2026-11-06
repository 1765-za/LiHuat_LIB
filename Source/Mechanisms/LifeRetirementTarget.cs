using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal enum LifeRetirementTarget { Argument = 0, Instance = 16, ComponentParent = 32 }

    internal static class LifeRetirementReceiver
    {
        private enum Source { Unknown, Instance, Parent }

        internal static LifeRetirementTarget FieldStore(MethodInfo method, List<CodeInstruction> code, int index)
        {
            if (method.IsStatic) return LifeRetirementTarget.Argument;
            int cursor = index - 1;
            if (!Read(code, ref cursor, 0, out _) || !Read(code, ref cursor, 0, out Source source)) return LifeRetirementTarget.Argument;
            if (source == Source.Instance && typeof(Thing).IsAssignableFrom(method.DeclaringType)) return LifeRetirementTarget.Instance;
            return source == Source.Parent && typeof(ThingComp).IsAssignableFrom(method.DeclaringType)
                ? LifeRetirementTarget.ComponentParent : LifeRetirementTarget.Argument;
        }

        internal static LifeRetirementTarget Find(MethodInfo method, List<CodeInstruction> code, int index, MethodInfo call)
        {
            if (method.IsStatic || call.IsStatic || !typeof(Entity).IsAssignableFrom(call.DeclaringType)
                || code[index].operand is not MethodInfo direct || direct != call || code[index].labels.Count != 0)
                return LifeRetirementTarget.Argument;
            int cursor = index - 1;
            foreach (ParameterInfo parameter in call.GetParameters())
                if (!Read(code, ref cursor, 0, out _)) return LifeRetirementTarget.Argument;
            if (!Read(code, ref cursor, 0, out Source source)) return LifeRetirementTarget.Argument;
            if (source == Source.Instance && typeof(Thing).IsAssignableFrom(method.DeclaringType)) return LifeRetirementTarget.Instance;
            if (source != Source.Parent || !typeof(ThingComp).IsAssignableFrom(method.DeclaringType)) return LifeRetirementTarget.Argument;
            return LifeRetirementTarget.ComponentParent;
        }

        // Calls that rewrite or borrow the parent cannot keep an entry-time receiver proof.
        internal static bool ChangesParent(CodeInstruction line) => line.operand is FieldInfo field && Parent(field)
            && (line.opcode == OpCodes.Stfld || line.opcode == OpCodes.Ldflda);

        private static bool Read(List<CodeInstruction> code, ref int cursor, int depth, out Source source)
        {
            source = Source.Unknown;
            if (cursor < 0 || depth > 16) return false;
            int position = cursor;
            CodeInstruction line = code[cursor--];
            OpCode op = line.opcode;
            if (op == OpCodes.Nop) return Read(code, ref cursor, depth + 1, out source);
            int argument = op == OpCodes.Ldarg || op == OpCodes.Ldarg_S ? Convert.ToInt32(line.operand)
                : op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value ? op.Value - OpCodes.Ldarg_0.Value : -1;
            if (argument >= 0)
            {
                if (argument != 0) return true;
                foreach (CodeInstruction instruction in code)
                    if ((instruction.opcode == OpCodes.Starg || instruction.opcode == OpCodes.Starg_S
                        || instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
                        && Convert.ToInt32(instruction.operand) == argument) return true;
                source = Source.Instance; return true;
            }
            int local = Local(line, true);
            if (local >= 0)
            {
                int store = -1;
                for (int i = 0; i < code.Count; i++)
                {
                    if (LocalAddress(code[i], local)) return true;
                    if (Local(code[i], false) != local) continue;
                    if (store >= 0 || i >= position) return true;
                    store = i;
                }
                if (store < 0) return true;
                int definition = store - 1;
                return Read(code, ref definition, depth + 1, out source);
            }
            if (op == OpCodes.Ldnull || op == OpCodes.Ldstr || op == OpCodes.Ldsfld || op.Name.StartsWith("ldc.", StringComparison.Ordinal)) return true;
            if (line.labels.Count != 0) return false;
            if (op == OpCodes.Castclass || op == OpCodes.Isinst || op == OpCodes.Box || op == OpCodes.Unbox_Any
                || op == OpCodes.Conv_I1 || op == OpCodes.Conv_I2 || op == OpCodes.Conv_I4 || op == OpCodes.Conv_I8)
                return Read(code, ref cursor, depth + 1, out source);
            if (op == OpCodes.Ldfld)
            {
                if (!Read(code, ref cursor, depth + 1, out Source owner)) return false;
                if (owner == Source.Instance && Parent((FieldInfo)line.operand)) source = Source.Parent;
                return true;
            }
            if ((op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj) && line.operand is MethodBase called)
            {
                if (called is MethodInfo returns && returns.ReturnType == typeof(void)) return false;
                foreach (ParameterInfo parameter in called.GetParameters())
                    if (!Read(code, ref cursor, depth + 1, out _)) return false;
                if (op == OpCodes.Newobj || called.IsStatic) return true;
                if (!Read(code, ref cursor, depth + 1, out Source receiver)) return false;
                if ((called.DeclaringType == typeof(Thing) || called.DeclaringType == typeof(ThingWithComps))
                    && called.Name == nameof(Thing.SplitOff)) source = receiver;
                return true;
            }
            return false;
        }

        private static bool Parent(FieldInfo field) => field.DeclaringType == typeof(ThingComp) && field.Name == nameof(ThingComp.parent);
        private static bool LocalAddress(CodeInstruction line, int index) =>
            (line.opcode == OpCodes.Ldloca || line.opcode == OpCodes.Ldloca_S) && LocalIndex(line) == index;
        private static int Local(CodeInstruction line, bool load)
        {
            short first = (load ? OpCodes.Ldloc_0 : OpCodes.Stloc_0).Value, last = (load ? OpCodes.Ldloc_3 : OpCodes.Stloc_3).Value;
            if (line.opcode.Value >= first && line.opcode.Value <= last) return line.opcode.Value - first;
            return line.opcode == (load ? OpCodes.Ldloc : OpCodes.Stloc) || line.opcode == (load ? OpCodes.Ldloc_S : OpCodes.Stloc_S)
                ? LocalIndex(line) : -1;
        }
        private static int LocalIndex(CodeInstruction line) => line.operand is LocalBuilder builder ? builder.LocalIndex
            : line.operand is LocalVariableInfo local ? local.LocalIndex : Convert.ToInt32(line.operand);
    }
}
