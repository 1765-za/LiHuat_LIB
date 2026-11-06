using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class AvailabilityIlPatterns
    {
        internal static int Recipient(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            int found = -1;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].ParameterType == typeof(Pawn) || parameters[i].ParameterType == typeof(Thing))
                { if (found >= 0) return -1; found = i + (method.IsStatic ? 0 : 1); }
            return found;
        }

        internal static bool MembershipCall(MethodInfo call, bool write)
        {
            if (call.IsStatic || !call.DeclaringType.IsGenericType) return false;
            Type collection = call.DeclaringType.GetGenericTypeDefinition();
            if (collection == typeof(HashSet<>))
                return call.Name == (write ? nameof(HashSet<int>.Add) : nameof(HashSet<int>.Contains))
                    && call.GetParameters().Length == 1;
            if (collection != typeof(Dictionary<,>) && collection != typeof(ConcurrentDictionary<,>)) return false;
            return write
                ? (call.Name == "Add" || call.Name == "TryAdd" || call.Name == "set_Item") && call.GetParameters().Length == 2
                : call.Name == "ContainsKey" && call.GetParameters().Length == 1 || LookupCall(call);
        }

        internal static bool LookupCall(MethodInfo call) => !call.IsStatic && call.DeclaringType.IsGenericType
            && (call.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                || call.DeclaringType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
            && call.Name == "TryGetValue" && call.GetParameters().Length == 2
            && call.GetParameters()[1].ParameterType.IsByRef;

        internal static bool TargetInput(List<CodeInstruction> code, int at, MethodInfo call, int input, int recipient, bool identity = false)
        {
            int producer = Input(code, at, call, input);
            producer = Origin(code, producer);
            if (producer < 0) return false;
            CodeInstruction line = code[producer];
            if (identity && AvailabilityRecordIdentity.Matches(code, producer, recipient)) return true;
            if (identity && (line.opcode == OpCodes.Ldfld && line.operand is FieldInfo field
                    && field.DeclaringType == typeof(Thing) && field.Name == nameof(Thing.thingIDNumber)
                || (line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt) && line.operand is MethodInfo member
                    && member.DeclaringType == typeof(Thing) && (member.Name == "get_ThingID" || member.Name == nameof(Thing.GetUniqueLoadID))))
                producer = Origin(code, Previous(code, producer - 1));
            return producer >= 0 && code[producer].opcode.Name.StartsWith("ldarg", StringComparison.Ordinal)
                && code[producer].opcode != OpCodes.Ldarga && code[producer].opcode != OpCodes.Ldarga_S
                && Index(code[producer]) == recipient;
        }

        internal static bool IdentityValue(List<CodeInstruction> code, int at, int recipient)
        {
            at = Origin(code, at);
            if (at < 0 || code[at].opcode != OpCodes.Ldfld || code[at].operand is not FieldInfo field
                || field.DeclaringType != typeof(Thing) || field.Name != nameof(Thing.thingIDNumber)) return false;
            at = Origin(code, at - 1);
            return at >= 0 && code[at].opcode.Name.StartsWith("ldarg")
                && code[at].opcode != OpCodes.Ldarga && code[at].opcode != OpCodes.Ldarga_S && Index(code[at]) == recipient;
        }

        internal static int Input(List<CodeInstruction> code, int at, MethodInfo call, int input)
        {
            int count = call.GetParameters().Length + (call.IsStatic ? 0 : 1);
            int needed = count - input - 1, cursor = at - 1;
            while (needed > 0 && cursor >= 0)
            {
                CodeInstruction line = code[cursor--];
                if (line.opcode.FlowControl == FlowControl.Branch || line.opcode.FlowControl == FlowControl.Cond_Branch) return -1;
                int pops = Count(line.opcode.StackBehaviourPop), pushes = Count(line.opcode.StackBehaviourPush);
                if (line.operand is MethodBase method && (line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt || line.opcode == OpCodes.Newobj))
                {
                    pops = method.GetParameters().Length + (!method.IsStatic && line.opcode != OpCodes.Newobj ? 1 : 0);
                    pushes = line.opcode == OpCodes.Newobj || method is MethodInfo result && result.ReturnType != typeof(void) ? 1 : 0;
                }
                if (pops < 0 || pushes < 0) return -1;
                needed = needed - pushes + pops;
            }
            return needed == 0 ? Previous(code, cursor) : -1;
        }

        private static int Previous(List<CodeInstruction> code, int at)
        { while (at >= 0 && code[at].opcode == OpCodes.Nop) at--; return at; }

        internal static int Origin(List<CodeInstruction> code, int at)
        {
            var seen = new HashSet<int>();
            while (at >= 0 && seen.Add(at))
            {
                var line = code[at];
                if (line.opcode == OpCodes.Nop || line.opcode == OpCodes.Castclass || line.opcode == OpCodes.Isinst) { at--; continue; }
                if (!line.opcode.Name.StartsWith("ldloc", StringComparison.Ordinal)) return at;
                if (line.opcode == OpCodes.Ldloca || line.opcode == OpCodes.Ldloca_S) return -1;
                int local = Index(line), store = -1;
                for (int i = 0; i < code.Count; i++)
                {
                    if ((code[i].opcode == OpCodes.Ldloca || code[i].opcode == OpCodes.Ldloca_S) && Index(code[i]) == local) return -1;
                    if (!code[i].opcode.Name.StartsWith("stloc", StringComparison.Ordinal) || Index(code[i]) != local) continue;
                    if (store >= 0 || i >= at) return -1;
                    store = i;
                }
                if (store < 0) return -1;
                at = store - 1;
            }
            return -1;
        }

        internal static int Count(StackBehaviour behavior) => behavior == StackBehaviour.Pop0 || behavior == StackBehaviour.Push0 ? 0
            : behavior == StackBehaviour.Varpop || behavior == StackBehaviour.Varpush ? -1 : behavior.ToString().Split('_').Length;

        internal static int Index(CodeInstruction line)
        {
            string name = line.opcode.Name;
            char last = name[name.Length - 1];
            if (last >= '0' && last <= '3') return last - '0';
            if (line.operand is LocalBuilder local) return local.LocalIndex;
            if (line.operand is LocalVariableInfo variable) return variable.LocalIndex;
            if (line.operand is ParameterInfo parameter) return parameter.Position + (parameter.Member is MethodBase owner && !owner.IsStatic ? 1 : 0);
            return Convert.ToInt32(line.operand);
        }
    }
}
