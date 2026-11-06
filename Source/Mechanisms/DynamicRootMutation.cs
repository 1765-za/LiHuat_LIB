using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicRootMutation
    {
        private static readonly Dictionary<FieldInfo, DynamicMethod> setters = new();
        private static readonly List<FieldInfo> fields = new();
        private static readonly object sync = new();
        private static readonly MethodInfo allows = AccessTools.Method(typeof(DynamicRootMutation), nameof(Allows));

        internal static bool Emit(ILGenerator generator, OpCode opcode, FieldInfo field)
        {
            if (!DynamicWriteCatalogue.Ready || DynamicWriteCatalogue.Building || opcode != OpCodes.Stfld
                || !Guards(field)
                || !DynamicWriteCatalogue.GeneratorType.IsInstanceOfType(generator)
                || DynamicWriteCatalogue.OwnsEmission(generator)) return true;
            generator.Emit(OpCodes.Call, Setter(field));
            return false;
        }

        internal static byte[] Rewrite(byte[] code, Func<int, MemberInfo> resolve, Func<DynamicMethod, int> tokenFor,
            bool returnsReference = false)
        {
            var instructions = LifeIlReader.Decode(code, (_, token) => resolve(token));
            if (instructions == null) return code;
            byte[] result = null;
            int offset = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                CodeInstruction instruction = instructions[index];
                DynamicMethod replacement = null;
                if (instruction.operand is FieldInfo field)
                {
                    if (instruction.opcode == OpCodes.Stfld && Guards(field)) replacement = Setter(field);
                    else if ((instruction.opcode == OpCodes.Ldflda || instruction.opcode == OpCodes.Ldsflda)
                        && !LocalValueAddress.IsLocal(instructions, index) && GeneratedMethodCatalogue.Sensitive(field) && (returnsReference
                            || IndirectFieldMutation.Writes(instructions, _ => false, address: line => ReferenceEquals(line, instruction))))
                        replacement = GeneratedFieldWrites.Reference(field);
                }
                if (replacement != null)
                {
                    result ??= (byte[])code.Clone();
                    result[offset] = (byte)OpCodes.Call.Value;
                    byte[] token = BitConverter.GetBytes(tokenFor(replacement));
                    Array.Copy(token, 0, result, offset + 1, token.Length);
                }
                offset += instruction.opcode.Size + OperandSize(instruction);
            }
            return result ?? code;
        }

        internal static int OperandSize(CodeInstruction instruction) => instruction.opcode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + ((Label[])instruction.operand).Length * 4,
            _ => 4
        };

        private static DynamicMethod Setter(FieldInfo field)
        {
            lock (sync)
            {
                if (setters.TryGetValue(field, out DynamicMethod existing)) return existing;
                int slot = fields.Count;
                fields.Add(field);
                var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(void),
                    new[] { field.DeclaringType, field.FieldType }, typeof(DynamicRootMutation).Module, true);
                ILGenerator il = method.GetILGenerator();
                Label denied = il.DefineLabel();
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                if (field.FieldType.IsValueType) il.Emit(OpCodes.Box, field.FieldType);
                il.Emit(OpCodes.Call, allows);
                il.Emit(OpCodes.Brfalse, denied);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, field);
                il.MarkLabel(denied);
                il.Emit(OpCodes.Ret);
                setters.Add(field, method);
                return method;
            }
        }

        private static bool Guards(FieldInfo field) => field != null && (PawnRuntimeComponents.IsRoot(field)
            || PawnOwnedStructures.Backlink(field)
            || field.DeclaringType == typeof(Thing) && field.Name == "positionInt"
            || field.DeclaringType == typeof(Pawn_HealthTracker) && (field.Name == "pawn" || field.Name == nameof(Pawn_HealthTracker.hediffSet))
            || field.DeclaringType == typeof(HediffSet) && field.Name == nameof(HediffSet.pawn));

        private static bool Allows(int slot, object target, object value)
        {
            Pawn pawn = target as Pawn ?? LihuatGame.Current?.Subjects.Find(target)?.Subject.Pawn;
            if (!PawnContinuity.Protects(pawn)) return true;
            FieldInfo field;
            lock (sync) field = fields[slot];
            object current = field.GetValue(target);
            if (!field.FieldType.IsValueType && current != null && !ReferenceEquals(current, value))
            {
                DynamicWritePolicy.Denied();
                return false;
            }
            return !PawnContinuity.BlocksWrite(field, target, value);
        }
    }
}
