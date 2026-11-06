using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class AvailabilityRecordIdentity
    {
        internal static bool Matches(List<CodeInstruction> code, int at, int recipient)
        {
            if (at < 1 || code[at].opcode != OpCodes.Ldfld || code[at].operand is not FieldInfo field
                || !code[at - 1].opcode.Name.StartsWith("ldloc")) return false;
            int local = AvailabilityIlPatterns.Index(code[at - 1]), store = -1;
            for (int i = 0; i < code.Count; i++)
            {
                var op = code[i].opcode;
                if ((op == OpCodes.Ldloca || op == OpCodes.Ldloca_S) && AvailabilityIlPatterns.Index(code[i]) == local) return false;
                if (!op.Name.StartsWith("stloc") || AvailabilityIlPatterns.Index(code[i]) != local) continue;
                if (store >= 0 || i >= at) return false;
                store = i;
            }
            if (store < 0) return false;
            int start = store - 1;
            while (start >= 0 && code[start].opcode != OpCodes.Newobj)
            {
                if (code[start].opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return) return false;
                start--;
            }
            if (start < 0 || code[start].operand is not ConstructorInfo ctor || ctor.DeclaringType != field.DeclaringType) return false;
            var stack = new List<bool> { true };
            bool identity = false;
            for (int i = start + 1; i < store; i++)
            {
                var line = code[i];
                if (line.opcode == OpCodes.Dup) { if (stack.Count == 0) return false; stack.Add(stack[^1]); continue; }
                if (line.opcode == OpCodes.Stfld)
                {
                    if (stack.Count < 2 || stack[^1] || !stack[^2]) return false;
                    stack.RemoveRange(stack.Count - 2, 2);
                    if (Equals(line.operand, field)) identity = AvailabilityIlPatterns.IdentityValue(code, i - 1, recipient);
                    continue;
                }
                int pops = AvailabilityIlPatterns.Count(line.opcode.StackBehaviourPop), pushes = AvailabilityIlPatterns.Count(line.opcode.StackBehaviourPush);
                if (line.operand is MethodInfo call && (line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt))
                { pops = call.GetParameters().Length + (call.IsStatic ? 0 : 1); pushes = call.ReturnType == typeof(void) ? 0 : 1; }
                if (pops < 0 || pushes < 0 || stack.Count < pops) return false;
                for (int p = 0; p < pops; p++) { if (stack[^1]) return false; stack.RemoveAt(stack.Count - 1); }
                for (int p = 0; p < pushes; p++) stack.Add(false);
            }
            if (!identity || stack.Count != 1 || !stack[0]) return false;
            for (int i = store + 1; i < at; i++)
            {
                if (Equals(code[i].operand, field) && (code[i].opcode == OpCodes.Stfld || code[i].opcode == OpCodes.Ldflda)) return false;
                if (code[i].operand is MethodInfo call && call.Module.Assembly != typeof(Dictionary<,>).Assembly) return false;
            }
            return true;
        }
    }
}
