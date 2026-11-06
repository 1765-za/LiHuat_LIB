using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Combat
{
    internal static class ManagedHealthWrite
    {
        internal static ManagedHealthReduction Reduction(List<CodeInstruction> body, FieldInfo storage, int damageArgument)
        {
            ManagedHealthReduction reduction = null;
            for (int i = 0; i < body.Count; i++)
            {
                OpCode op = body[i].opcode;
                bool scalar = op == OpCodes.Stfld && Equals(body[i].operand, storage);
                bool element = op.Name.StartsWith("stelem", StringComparison.Ordinal) || op.Name.StartsWith("stind", StringComparison.Ordinal);
                bool indexer = body[i].operand is MethodInfo method && ManagedHealthStorage.Indexer(method, "set_Item");
                if (!scalar && !element && !indexer) continue;
                int at = i - 1;
                ManagedValueExpression value = ManagedValueExpression.Before(body, ref at);
                ManagedValueExpression index = null, owner;
                if (scalar) owner = ManagedValueExpression.Before(body, ref at);
                else if (op.Name.StartsWith("stind", StringComparison.Ordinal))
                {
                    ManagedValueExpression address = ManagedValueExpression.Before(body, ref at);
                    if (address?.Operation != OpCodes.Ldelema || address.Inputs.Length != 2) return null;
                    owner = address.Inputs[0]; index = address.Inputs[1];
                }
                else
                {
                    index = ManagedValueExpression.Before(body, ref at);
                    owner = ManagedValueExpression.Before(body, ref at);
                }
                if (!scalar && owner?.Operation == OpCodes.Ldfld && owner.Operand is FieldInfo other && other != storage
                    && owner.Inputs.Length == 1 && owner.Inputs[0].IsThis) continue;
                if (scalar ? owner?.IsThis != true : !ManagedHealthStorage.Field(owner, storage)) return null;
                if (reduction != null) return null;
                reduction = ManagedHealthReduction.Create(value, i, scalar ? storage.FieldType : ManagedHealthStorage.Element(storage.FieldType),
                    v => ManagedHealthStorage.Reads(v, storage, index), v => ManagedHealthReduction.ReadsDamage(v, damageArgument));
                if (reduction == null) return null;
            }
            return reduction;
        }

        internal static IEnumerable<int> DamageLocals(MethodInfo method, List<CodeInstruction> body)
        {
            var locals = method.GetMethodBody().LocalVariables;
            MethodInfo amount = typeof(Verse.DamageInfo).GetProperty(nameof(Verse.DamageInfo.Amount)).GetMethod;
            foreach (int local in body.Select(line => ManagedCombatIl.StoredLocal(line.opcode, line.operand)).Where(i => i.HasValue).Select(i => i.Value).Distinct())
            {
                if (!ManagedHealthStorage.Numeric(locals[local].LocalType)) continue;
                int[] stores = Enumerable.Range(0, body.Count).Where(i => ManagedCombatIl.StoredLocal(body[i].opcode, body[i].operand) == local).Take(2).ToArray();
                if (stores.Length != 1) continue;
                int at = stores[0] - 1;
                if (ManagedValueExpression.Before(body, ref at)?.Calls(amount) == true) yield return local;
            }
        }

        internal static void ConvertAmount(ILGenerator emit, Type local)
        {
            if (local == typeof(float)) return;
            emit.Emit(OpCodes.Conv_R8);
            if (local != typeof(int)) return;
            emit.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
            emit.Emit(OpCodes.Call, typeof(Math).GetMethod(nameof(Math.Min), new[] { typeof(double), typeof(double) }));
            emit.Emit(OpCodes.Call, typeof(Math).GetMethod(nameof(Math.Ceiling), new[] { typeof(double) }));
            emit.Emit(OpCodes.Conv_I4);
        }
    }
}
