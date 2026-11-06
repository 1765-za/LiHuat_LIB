using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeCallbackRecipient
    {
        internal static bool Matches(MethodInfo owner, List<CodeInstruction> code, MethodInfo callback, LifeIlReader reader)
        {
            if (callback.IsStatic || !callback.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)) return false;
            int recipient = LifeRetirementRecipient.For(owner);
            if (recipient == LifeRetirementRecipient.Ambiguous) return false;
            int argument = recipient + (owner.IsStatic ? 0 : 1);
            List<CodeInstruction> body = reader.Read(callback);
            if (body == null) return false;
            FieldInfo[] captures = body.Where(line => line.opcode == OpCodes.Ldfld && line.operand is FieldInfo field
                && field.DeclaringType == callback.DeclaringType && RecipientType(field.FieldType))
                .Select(line => (FieldInfo)line.operand).Distinct().ToArray();
            if (captures.Length == 0) return false;
            foreach (FieldInfo capture in captures)
            {
                int writes = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (!Equals(code[i].operand, capture)) continue;
                    if (code[i].opcode == OpCodes.Ldflda) return false;
                    if (code[i].opcode != OpCodes.Stfld) continue;
                    if (++writes > 1 || i == 0 || Argument(code[i - 1]) != argument) return false;
                }
                if (writes != 1) return false;
            }
            return true;
        }

        private static bool RecipientType(Type type) => typeof(Thing).IsAssignableFrom(type)
            || typeof(Pawn_HealthTracker).IsAssignableFrom(type) || typeof(HediffSet).IsAssignableFrom(type);

        private static int Argument(CodeInstruction line) => line.opcode == OpCodes.Ldarg || line.opcode == OpCodes.Ldarg_S
            ? Convert.ToInt32(line.operand) : line.opcode.Value >= OpCodes.Ldarg_0.Value && line.opcode.Value <= OpCodes.Ldarg_3.Value
                ? line.opcode.Value - OpCodes.Ldarg_0.Value : -1;
    }
}
