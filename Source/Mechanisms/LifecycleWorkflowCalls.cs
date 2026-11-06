using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace LihuatLib.Mechanisms
{
    internal static class LifecycleWorkflowCalls
    {
        internal static readonly MethodInfo[] Definitions = {
            AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.TryRecoverFromUnwalkablePosition)),
            AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.CheckForStateChange)),
            AccessTools.Method(typeof(Thing), nameof(Thing.Ingested))
        };
        private static readonly HashSet<MethodInfo> workflows = new(Definitions);
        private static readonly Dictionary<(MethodInfo, bool), MethodInfo> calls = new();

        internal static List<CodeInstruction> Prepare(MethodBase original, List<CodeInstruction> instructions)
        {
            if (original is not MethodInfo definition || !workflows.Contains(NativeIlGuards.Definition(definition))) return instructions;
            List<CodeInstruction> result = null;
            for (int i = 0; i < instructions.Count; i++)
            {
                CodeInstruction line = instructions[i];
                if ((line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt) || line.operand is not MethodInfo called
                    || !ReverseLifecycle.Destructive(called) || i > 0 && instructions[i - 1].opcode.OpCodeType == OpCodeType.Prefix) continue;
                result ??= new List<CodeInstruction>(instructions);
                result[i] = new CodeInstruction(line) { opcode = OpCodes.Call, operand = Guard(called, line.opcode == OpCodes.Callvirt) };
            }
            return result ?? instructions;
        }

        private static MethodInfo Guard(MethodInfo original, bool virtualCall)
        {
            lock (calls)
            {
                var key = (original, virtualCall);
                if (calls.TryGetValue(key, out MethodInfo guarded)) return guarded;
                Type[] signature = IndependentIl.Parameters(original);
                var method = new DynamicMethod(Guid.NewGuid().ToString("N"), original.ReturnType, signature,
                    typeof(LifecycleWorkflowCalls).Module, true);
                ILGenerator il = method.GetILGenerator();
                int recipient = original.DeclaringType == typeof(WorldPawns) ? 1 : 0;
                Label allowed = il.DefineLabel();
                il.Emit(OpCodes.Ldarg, (short)recipient);
                il.Emit(OpCodes.Call, GeneratedIntegrity.Create(
                    AccessTools.Method(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.AllowsNativeTarget))));
                il.Emit(OpCodes.Brtrue, allowed);
                if (original.ReturnType != typeof(void))
                {
                    LocalBuilder empty = il.DeclareLocal(original.ReturnType);
                    il.Emit(OpCodes.Ldloca, empty);
                    il.Emit(OpCodes.Initobj, original.ReturnType);
                    il.Emit(OpCodes.Ldloc, empty);
                }
                il.Emit(OpCodes.Ret);
                il.MarkLabel(allowed);
                for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
                il.Emit(virtualCall ? OpCodes.Callvirt : OpCodes.Call, original);
                il.Emit(OpCodes.Ret);
                guarded = method;
                calls.Add(key, guarded);
                return guarded;
            }
        }
    }
}
