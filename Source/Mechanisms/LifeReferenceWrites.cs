using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeReferenceWrites
    {
        private readonly record struct Source(int Argument, FieldInfo Address = null);
        private static readonly Source Unknown = new(-1);

        internal static bool AffectsRecipient(MethodInfo method, List<CodeInstruction> code)
        {
            int recipient = LifeRetirementRecipient.For(method);
            if (recipient == LifeRetirementRecipient.Ambiguous) return false;
            int argument = recipient + (method.IsStatic ? 0 : 1);
            for (int i = 1; i < code.Count; i++)
            {
                CodeInstruction line = code[i];
                bool direct = line.opcode == OpCodes.Stfld && line.operand is FieldInfo field && Sensitive(field);
                bool indirect = line.opcode == OpCodes.Stind_Ref || line.opcode == OpCodes.Stobj || line.opcode == OpCodes.Initobj;
                if ((!direct && !indirect) || line.labels.Count != 0) continue;
                int at = i - 1;
                if (line.opcode != OpCodes.Initobj && !Read(code, ref at, 0, out _)) continue;
                if (Read(code, ref at, 0, out Source owner) && owner.Argument == argument
                    && (direct || owner.Address != null && Sensitive(owner.Address))) return true;
            }
            return false;
        }

        private static bool Sensitive(FieldInfo field) => !field.FieldType.IsValueType
            && (DynamicWriteCatalogue.Vital(field) || field.DeclaringType == typeof(HediffSet) && field.Name == nameof(HediffSet.pawn));

        private static bool Read(List<CodeInstruction> code, ref int at, int depth, out Source source)
        {
            source = Unknown;
            if (at < 0 || depth >= 32) return false;
            CodeInstruction line = code[at--];
            OpCode op = line.opcode;
            if (line.labels.Count != 0) return false;
            if (op == OpCodes.Nop) return Read(code, ref at, depth + 1, out source);
            int argument = op == OpCodes.Ldarg || op == OpCodes.Ldarg_S ? Convert.ToInt32(line.operand)
                : op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value ? op.Value - OpCodes.Ldarg_0.Value : -1;
            if (argument >= 0)
            {
                foreach (CodeInstruction instruction in code)
                    if ((instruction.opcode == OpCodes.Starg || instruction.opcode == OpCodes.Starg_S)
                        && Convert.ToInt32(instruction.operand) == argument) return true;
                source = new Source(argument); return true;
            }
            int local = Local(line, true);
            if (local >= 0)
            {
                int store = -1;
                for (int i = 0; i < code.Count; i++)
                    if (Local(code[i], false) == local)
                    { if (store >= 0 || i > at) return true; store = i; }
                if (store < 0) return true;
                int cursor = store - 1;
                return Read(code, ref cursor, depth + 1, out source);
            }
            if (op == OpCodes.Ldfld || op == OpCodes.Ldflda)
            {
                if (!Read(code, ref at, depth + 1, out Source owner)) return false;
                var field = (FieldInfo)line.operand;
                if (op == OpCodes.Ldflda) source = owner with { Address = field };
                else if (field.DeclaringType == typeof(Pawn) && field.Name == nameof(Pawn.health)
                    || field.DeclaringType == typeof(Pawn_HealthTracker) && field.Name == nameof(Pawn_HealthTracker.hediffSet))
                    source = owner with { Address = null };
                return true;
            }
            if (op == OpCodes.Ldind_Ref || op == OpCodes.Ldobj || op == OpCodes.Castclass || op == OpCodes.Isinst)
                return Read(code, ref at, depth + 1, out source);
            if ((op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj) && line.operand is MethodBase called)
            {
                int count = called.GetParameters().Length + (!called.IsStatic && op != OpCodes.Newobj ? 1 : 0);
                Source first = Unknown;
                for (int i = count - 1; i >= 0; i--)
                {
                    if (!Read(code, ref at, depth + 1, out Source input)) return false;
                    if (i == 0) first = input;
                }
                FieldInfo field = UnsafeFieldMetadata.Resolve(called as MethodInfo);
                if (field != null && !field.IsStatic) source = first with { Address = field };
                return true;
            }
            return op == OpCodes.Ldnull || op == OpCodes.Ldstr || op == OpCodes.Ldsfld
                || op.Name.StartsWith("ldc.", StringComparison.Ordinal);
        }

        private static int Local(CodeInstruction line, bool load)
        {
            OpCode op = line.opcode;
            short first = (load ? OpCodes.Ldloc_0 : OpCodes.Stloc_0).Value;
            short last = (load ? OpCodes.Ldloc_3 : OpCodes.Stloc_3).Value;
            if (op.Value >= first && op.Value <= last) return op.Value - first;
            if (op != (load ? OpCodes.Ldloc : OpCodes.Stloc) && op != (load ? OpCodes.Ldloc_S : OpCodes.Stloc_S)) return -1;
            return line.operand is LocalBuilder builder ? builder.LocalIndex : Convert.ToInt32(line.operand);
        }
    }
}
