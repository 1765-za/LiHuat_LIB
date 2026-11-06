using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class DynamicCallReceivers
    {
        private static readonly MethodInfo bake = AccessTools.Method(
            typeof(ILGenerator).Assembly.GetType("System.Reflection.Emit.RuntimeILGenerator", true), "BakeByteArray");
        private readonly ILGenerator generator;
        private readonly Dictionary<int, MemberInfo> operands = new();
        private int analyzedLength = -1;
        private bool requiresProtection = true;

        internal DynamicCallReceivers(ILGenerator generator) { this.generator = generator; }
        internal void Observe(MethodInfo called) => operands[generator.ILOffset] = called;
        internal void Observe(FieldInfo field) => operands[generator.ILOffset] = field;

        internal bool RequiresProtection(DynamicMethod method)
        {
            int length = generator.ILOffset;
            if (analyzedLength == length) return requiresProtection;
            analyzedLength = length;
            requiresProtection = true;
            byte[] code = (byte[])bake.Invoke(generator, null);
            var tokens = new Dictionary<int, MemberInfo>();
            int expectedCalls = 0;
            foreach (var operand in operands)
            {
                if (operand.Key + 5 > code.Length) return true;
                byte op = code[operand.Key];
                if (operand.Value is FieldInfo ? op != (byte)OpCodes.Ldsfld.Value
                    : op != (byte)OpCodes.Call.Value && op != (byte)OpCodes.Callvirt.Value) return true;
                if (operand.Value is MethodInfo) expectedCalls++;
                tokens[BitConverter.ToInt32(code, operand.Key + 1)] = operand.Value;
            }
            List<CodeInstruction> lines = LifeIlReader.Decode(code,
                (_, token) => tokens.TryGetValue(token, out MemberInfo operand) ? operand : null);
            if (lines == null) return true;
            int found = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                CodeInstruction line = lines[i];
                if ((line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt)
                    || line.operand is not MethodInfo called) continue;
                found++;
                MethodInfo source = RuntimePublicationCalls.IsInvocation(called)
                    ? RuntimePublicationCalls.SourceAt(lines, i, called) : called;
                if (source != null && ReverseLifecycle.Destructive(source) && CanAffectPawn(method, lines, i, source)) return true;
            }
            return requiresProtection = found != expectedCalls;
        }

        internal static bool CanAffectPawn(DynamicMethod method, List<CodeInstruction> lines, int position, MethodInfo called)
        {
            if (called.IsStatic) return true;
            int cursor = position;
            // A branch into the operand sequence can supply a different receiver.
            if (lines[cursor].labels.Count != 0) return true;
            foreach (ParameterInfo parameter in called.GetParameters())
            {
                if (--cursor < 0 || lines[cursor].labels.Count != 0 || !Constant(lines[cursor].opcode)) return true;
            }
            if (--cursor < 0) return true;
            CodeInstruction receiver = lines[cursor];
            int argument = receiver.opcode == OpCodes.Ldarg_0 ? 0 : receiver.opcode == OpCodes.Ldarg_1 ? 1
                : receiver.opcode == OpCodes.Ldarg_2 ? 2 : receiver.opcode == OpCodes.Ldarg_3 ? 3
                : receiver.opcode == OpCodes.Ldarg || receiver.opcode == OpCodes.Ldarg_S ? Convert.ToInt32(receiver.operand) : -1;
            ParameterInfo[] parameters = method.GetParameters();
            if (argument < 0 || argument >= parameters.Length) return true;
            Type type = parameters[argument].ParameterType;
            return !typeof(Thing).IsAssignableFrom(type) || type.IsAssignableFrom(typeof(Pawn)) || typeof(Pawn).IsAssignableFrom(type);
        }

        private static bool Constant(OpCode opcode) => opcode == OpCodes.Ldnull || opcode == OpCodes.Ldstr
            || opcode.Name.StartsWith("ldc.", StringComparison.Ordinal);
    }
}
