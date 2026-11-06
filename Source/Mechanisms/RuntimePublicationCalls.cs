using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class RuntimePublicationCalls
    {
        internal static bool IsInvocation(MethodInfo method) => !method.IsStatic && method.Name == nameof(Action.Invoke)
            && typeof(Delegate).IsAssignableFrom(method.DeclaringType);

        internal static MethodInfo SourceAt(List<CodeInstruction> lines, int position, MethodInfo invoke)
        {
            if (!IsInvocation(invoke) || lines[position].labels.Count != 0) return null;
            int cursor = position;
            foreach (ParameterInfo parameter in invoke.GetParameters())
            {
                while (--cursor >= 0 && lines[cursor].opcode == OpCodes.Nop && lines[cursor].labels.Count == 0) { }
                if (cursor < 0 || lines[cursor].labels.Count != 0) return null;
                OpCode op = lines[cursor].opcode;
                if (op.StackBehaviourPop != StackBehaviour.Pop0 || op.StackBehaviourPush is not
                    (StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4
                        or StackBehaviour.Pushr8 or StackBehaviour.Pushref)) return null;
            }
            while (--cursor >= 0 && lines[cursor].opcode == OpCodes.Nop && lines[cursor].labels.Count == 0) { }
            return cursor >= 0 && lines[cursor].labels.Count == 0 && lines[cursor].opcode == OpCodes.Ldsfld
                && lines[cursor].operand is FieldInfo field && field.FieldType == invoke.DeclaringType
                    ? RuntimeDelegateBody.SourceOf(field) : null;
        }
    }
}
