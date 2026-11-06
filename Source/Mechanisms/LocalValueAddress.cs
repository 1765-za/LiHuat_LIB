using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class LocalValueAddress
    {
        internal static bool IsLocal(IReadOnlyList<CodeInstruction> code, int index)
        {
            CodeInstruction field = code[index];
            if (index == 0 || field.opcode != OpCodes.Ldflda || field.labels.Count != 0 || field.blocks.Count != 0
                || field.operand is not FieldInfo member || member.DeclaringType?.IsValueType != true) return false;
            OpCode receiver = code[index - 1].opcode;
            // Only a value stored in this invocation is exempt; loaded object references and incoming byrefs are not.
            return receiver == OpCodes.Ldloca || receiver == OpCodes.Ldloca_S || receiver == OpCodes.Ldarga || receiver == OpCodes.Ldarga_S;
        }
    }
}
