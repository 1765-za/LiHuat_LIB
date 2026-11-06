using System.Collections.Generic;
using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeRetirementFlow
    {
        internal static bool HasPreservingReturn(List<CodeInstruction> code, Func<CodeInstruction, bool> resolved = null)
        {
            if (code == null || code.Count == 0) return true;
            var labels = new Dictionary<Label, int>();
            for (int i = 0; i < code.Count; i++) foreach (Label label in code[i].labels) labels[label] = i;
            var visited = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(0);
            while (pending.Count > 0)
            {
                int index = pending.Pop();
                if (index >= code.Count || !visited.Add(index)) continue;
                CodeInstruction instruction = code[index];
                if (instruction.opcode.FlowControl == FlowControl.Return) return true;
                if (instruction.opcode.FlowControl == FlowControl.Throw || Removes(instruction) || resolved?.Invoke(instruction) == true) continue;
                if (instruction.operand is Label target)
                {
                    pending.Push(labels[target]);
                    if (instruction.opcode.FlowControl != FlowControl.Cond_Branch) continue;
                }
                else if (instruction.operand is Label[] choices)
                    foreach (Label choice in choices) pending.Push(labels[choice]);
                pending.Push(index + 1);
            }
            return false;
        }

        private static bool Removes(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) return false;
            if (!(instruction.operand is MethodInfo call)) return false;
            return Removes(call);
        }

        internal static bool Removes(MethodInfo call)
        {
            if (call == null) return false;
            return call.DeclaringType == typeof(Pawn_HealthTracker) && call.Name == nameof(Pawn_HealthTracker.SetDead)
                || typeof(Entity).IsAssignableFrom(call.DeclaringType)
                    && (call.Name == nameof(Entity.DeSpawn) || call.Name == nameof(Thing.Destroy)
                        || call.Name == nameof(Thing.ForceSetStateToUnspawned)
                        || call.Name == nameof(Pawn.Kill) || call.Name == nameof(Pawn.Discard) || call.Name == nameof(Pawn.MakeCorpse))
                || call.DeclaringType == typeof(MapPawns) && call.Name == nameof(MapPawns.DeRegisterPawn)
                || call.DeclaringType == typeof(TickManager) && call.Name == nameof(TickManager.DeRegisterAllTickabilityFor);
        }
    }
}
