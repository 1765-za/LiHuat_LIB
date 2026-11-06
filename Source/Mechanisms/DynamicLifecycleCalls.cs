using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicLifecycleCalls
    {
        private static readonly Dictionary<(MethodInfo, OpCode), DynamicMethod> calls = new();
        private static readonly object sync = new();

        internal static bool Candidate(MethodInfo method, OpCode opcode) => (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
            && (HediffListCalls.Known(method) || !method.IsStatic && method.DeclaringType != null
            && typeof(Entity).IsAssignableFrom(method.DeclaringType) && ReverseLifecycle.Destructive(method)
            && (method.ReturnType == typeof(void) || method.ReturnType == typeof(bool)));

        internal static bool TryWrap(MethodInfo method, OpCode opcode, out DynamicMethod guarded)
        {
            guarded = null;
            if (!Candidate(method, opcode)) return false;
            if (HediffListCalls.Known(method)) { guarded = HediffListCalls.Body(method); return true; }
            lock (sync)
            {
                var key = (method, opcode);
                if (!calls.TryGetValue(key, out guarded)) calls.Add(key, guarded = Build(method, opcode));
                return true;
            }
        }

        private static DynamicMethod Build(MethodInfo method, OpCode opcode)
        {
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try
            {
                Type[] signature = new[] { method.DeclaringType }.Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();
                var bridge = new DynamicMethod(Guid.NewGuid().ToString("N"), method.ReturnType, signature, typeof(DynamicLifecycleCalls).Module, true);
                ILGenerator il = bridge.GetILGenerator();
                Label denied = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                bool departure = method.Name == nameof(Thing.DeSpawn) || method.Name == nameof(Thing.ForceSetStateToUnspawned);
                il.Emit(departure ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, AccessTools.Method(typeof(DynamicLifecycleCalls), nameof(Allows)));
                il.Emit(OpCodes.Brfalse, denied);
                for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
                il.Emit(opcode, method);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(denied);
                il.Emit(OpCodes.Call, AccessTools.Method(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.Denied)));
                if (method.ReturnType == typeof(bool)) il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
                return bridge;
            }
            finally { DynamicWriteCatalogue.Building = previous; }
        }

        private static bool Allows(Entity receiver, bool departure) => receiver is not Pawn pawn
            || departure && MapTransfer.InHandoff(pawn) || !PawnContinuity.Protects(pawn);

        internal static byte[] Rewrite(byte[] code, Func<int, MemberInfo> resolve, Func<DynamicMethod, int> tokenFor)
        {
            var instructions = LifeIlReader.Decode(code, (_, token) => resolve(token));
            if (instructions == null) return code;
            byte[] rewritten = null;
            int offset = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo called && TryWrap(called, instruction.opcode, out DynamicMethod guarded))
                {
                    rewritten ??= (byte[])code.Clone();
                    rewritten[offset] = (byte)OpCodes.Call.Value;
                    Array.Copy(BitConverter.GetBytes(tokenFor(guarded)), 0, rewritten, offset + 1, 4);
                }
                offset += instruction.opcode.Size + (instruction.opcode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + ((Label[])instruction.operand).Length * 4,
                    _ => 4
                });
            }
            return rewritten ?? code;
        }
    }
}
