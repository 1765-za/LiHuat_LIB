using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeEntryTarget
    {
        private readonly List<CodeInstruction> code;
        private readonly int argument;
        internal bool Stable { get; }

        internal LifeEntryTarget(MethodInfo method, List<CodeInstruction> code)
        {
            this.code = code;
            argument = Argument(method);
            Stable = argument >= 0;
            foreach (CodeInstruction line in code)
                if ((line.opcode == OpCodes.Starg || line.opcode == OpCodes.Starg_S
                        || line.opcode == OpCodes.Ldarga || line.opcode == OpCodes.Ldarga_S)
                    && AvailabilityIlPatterns.Index(line) == argument) Stable = false;
        }

        internal static int Argument(MethodInfo method)
        {
            int recipient = LifeRetirementRecipient.For(method);
            if (recipient == LifeRetirementRecipient.Ambiguous) return -1;
            Type type = recipient < 0 ? method.DeclaringType : method.GetParameters()[recipient].ParameterType;
            if (type.IsByRef || !typeof(Thing).IsAssignableFrom(type)
                && !typeof(Pawn_HealthTracker).IsAssignableFrom(type) && !typeof(HediffSet).IsAssignableFrom(type)) return -1;
            return recipient + (method.IsStatic ? 0 : 1);
        }

        internal bool Receives(int at, MethodInfo call, int input)
        {
            if (!Stable || code[at].labels.Count != 0) return false;
            int source = Input(at, call, input);
            if (source < 0) return false;
            for (int i = source + 1; i < at; i++)
                if (code[i].labels.Count != 0 || Branch(code[i])) return false;
            return Matches(source, 0);
        }

        private int Input(int at, MethodInfo call, int input)
        {
            int needed = call.GetParameters().Length + (call.IsStatic ? 0 : 1) - input;
            for (int cursor = at - 1; cursor >= 0; cursor--)
            {
                CodeInstruction line = code[cursor];
                if (Branch(line)) return -1;
                int pops = AvailabilityIlPatterns.Count(line.opcode.StackBehaviourPop);
                int pushes = AvailabilityIlPatterns.Count(line.opcode.StackBehaviourPush);
                if (line.operand is MethodBase method && (line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt || line.opcode == OpCodes.Newobj))
                {
                    pops = method.GetParameters().Length + (!method.IsStatic && line.opcode != OpCodes.Newobj ? 1 : 0);
                    pushes = line.opcode == OpCodes.Newobj || method is MethodInfo result && result.ReturnType != typeof(void) ? 1 : 0;
                }
                if (line.opcode == OpCodes.Dup)
                {
                    if (line.labels.Count != 0) return -1;
                    needed = needed <= 2 ? 1 : needed - 1;
                    continue;
                }
                if (pops < 0 || pushes < 0) return -1;
                if (pushes >= needed) return cursor;
                needed += pops - pushes;
                if (line.labels.Count != 0) return -1;
            }
            return -1;
        }

        private bool Matches(int at, int depth)
        {
            if (at < 0 || depth > 24) return false;
            CodeInstruction line = code[at];
            if (line.opcode == OpCodes.Nop || line.opcode == OpCodes.Castclass || line.opcode == OpCodes.Isinst)
                return line.labels.Count == 0 && Matches(at - 1, depth + 1);
            if (line.opcode == OpCodes.Ldarg || line.opcode == OpCodes.Ldarg_S
                || line.opcode.Value >= OpCodes.Ldarg_0.Value && line.opcode.Value <= OpCodes.Ldarg_3.Value)
                return AvailabilityIlPatterns.Index(line) == argument;
            if (line.opcode == OpCodes.Ldfld && line.operand is FieldInfo field && OwnerLink(field))
            {
                if (line.labels.Count != 0) return false;
                foreach (CodeInstruction use in code)
                    if (Equals(use.operand, field) && (use.opcode == OpCodes.Stfld || use.opcode == OpCodes.Ldflda)) return false;
                return Matches(at - 1, depth + 1);
            }
            if (!line.opcode.Name.StartsWith("ldloc", StringComparison.Ordinal)
                || line.opcode == OpCodes.Ldloca || line.opcode == OpCodes.Ldloca_S) return false;
            int local = AvailabilityIlPatterns.Index(line), store = -1;
            bool branched = false;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction use = code[i];
                branched |= Branch(use) || use.labels.Count != 0;
                if ((use.opcode == OpCodes.Ldloca || use.opcode == OpCodes.Ldloca_S)
                    && AvailabilityIlPatterns.Index(use) == local) return false;
                if (!use.opcode.Name.StartsWith("stloc", StringComparison.Ordinal)
                    || AvailabilityIlPatterns.Index(use) != local) continue;
                // A unique assignment is not enough: it must also run before every use.
                if (store >= 0 || i >= at || branched) return false;
                store = i;
            }
            return store >= 0 && Matches(store - 1, depth + 1);
        }

        private static bool Branch(CodeInstruction line) => line.opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch;
        private static bool OwnerLink(FieldInfo field) => field.DeclaringType == typeof(Pawn) && field.Name == nameof(Pawn.health)
            || field.DeclaringType == typeof(Pawn_HealthTracker) && field.Name == nameof(Pawn_HealthTracker.hediffSet);
    }
}
