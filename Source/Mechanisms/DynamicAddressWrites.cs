using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal sealed class DynamicAddressWrites
    {
        private static readonly Type runtime = typeof(ILGenerator).Assembly.GetType("System.Reflection.Emit.RuntimeILGenerator", true);
        private static readonly MethodInfo bake = AccessTools.Method(runtime, "BakeByteArray");
        private static readonly FieldInfo stream = AccessTools.Field(runtime, "m_ILStream");
        private static readonly FieldInfo scope = AccessTools.Field(DynamicWriteCatalogue.GeneratorType, "m_scope");
        private static readonly MethodInfo token = AccessTools.Method(scope.FieldType, "GetTokenFor", new[] { typeof(DynamicMethod) });
        private readonly ILGenerator generator;
        private readonly Dictionary<int, MemberInfo> operands = new();
        private byte[] prepared;

        internal DynamicAddressWrites(ILGenerator generator) => this.generator = generator;
        internal void Observe(MemberInfo member) => operands[generator.ILOffset] = member;
        internal void Prepare()
        {
            byte[] code = (byte[])bake.Invoke(generator, null);
            if (prepared != null && code.AsSpan().SequenceEqual(prepared)) return;
            var tokens = new Dictionary<int, MemberInfo>();
            foreach (var operand in operands)
                if (operand.Key + 5 <= code.Length) tokens[BitConverter.ToInt32(code, operand.Key + 1)] = operand.Value;
            var lines = LifeIlReader.Decode(code, (_, key) => tokens.TryGetValue(key, out MemberInfo member) ? member : null)
                ?? throw new BadImageFormatException("Field address flow could not be decoded.");
            byte[] buffer = (byte[])stream.GetValue(generator);
            int offset = 0;
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try
            {
                for (int index = 0; index < lines.Count; index++)
                {
                    CodeInstruction line = lines[index];
                    if ((line.opcode == OpCodes.Ldflda || line.opcode == OpCodes.Ldsflda)
                        && !LocalValueAddress.IsLocal(lines, index)
                        && line.operand is FieldInfo field && GeneratedMethodCatalogue.Sensitive(field)
                        && IndirectFieldMutation.Writes(lines, _ => false, address: candidate => ReferenceEquals(candidate, line)))
                    {
                        DynamicMethod accessor = GeneratedFieldWrites.Reference(field);
                        int key = (int)token.Invoke(scope.GetValue(generator), new object[] { accessor });
                        buffer[offset] = (byte)OpCodes.Call.Value;
                        BitConverter.GetBytes(key).CopyTo(buffer, offset + 1);
                        operands[offset] = accessor;
                    }
                    offset += line.opcode.Size + DynamicRootMutation.OperandSize(line);
                }
                prepared = (byte[])bake.Invoke(generator, null);
            }
            finally { DynamicWriteCatalogue.Building = previous; }
        }
    }
}
