using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Combat
{
    internal sealed class ManagedHealthReduction
    {
        private readonly int subtract, write;
        private readonly Type number, storage, context;
        internal readonly ManagedValueExpression Health, Amount;

        private ManagedHealthReduction(ManagedValueExpression reduction, int write, Type number, Type storage, Type context)
        {
            subtract = reduction.Instruction; this.write = write; this.number = number; this.storage = storage; this.context = context;
            Health = reduction.Inputs[0]; Amount = reduction.Inputs[1];
        }

        internal static ManagedHealthReduction Create(ManagedValueExpression value, int write, Type storage,
            Func<ManagedValueExpression, bool> readsHealth, Func<ManagedValueExpression, bool> readsDamage, Type context = null)
        {
            var reduction = Find(value, readsHealth, readsDamage);
            Type number = Number(reduction?.Inputs[0]);
            return reduction == null || !ManagedHealthStorage.Numeric(number) ? null
                : new ManagedHealthReduction(reduction, write, number, storage, context);
        }

        private static ManagedValueExpression Find(ManagedValueExpression value,
            Func<ManagedValueExpression, bool> readsHealth, Func<ManagedValueExpression, bool> readsDamage)
        {
            value = value?.Unwrapped();
            if (value == null) return null;
            if (value.Operation == OpCodes.Sub && readsHealth(value.Inputs[0]) && readsDamage(value.Inputs[1])) return value;
            if (value.Operand is not MethodInfo method || !method.IsStatic || !ManagedHealthStorage.Numeric(method.ReturnType)
                || method.DeclaringType != typeof(Math) && method.DeclaringType != typeof(MathF) && method.DeclaringType != typeof(UnityEngine.Mathf)
                || method.Name is not ("Min" or "Max" or "Clamp")) return null;
            return ManagedCombatIl.Only(value.Inputs.Select(v => Find(v, readsHealth, readsDamage)).Where(v => v != null));
        }

        internal static bool ReadsDamage(ManagedValueExpression value, int argument)
        {
            if (value.Operand is MethodInfo method && method.DeclaringType == typeof(Verse.DamageInfo)
                && method.Name == "get_Amount" && value.Inputs.Length == 1 && value.Inputs[0].Unwrapped().Argument == argument) return true;
            return value.Inputs.Any(v => ReadsDamage(v, argument));
        }

        internal static IEnumerable<int> DamageLocals(ManagedValueExpression value, Func<ManagedValueExpression, bool> readsDamage)
        {
            if (value.Local >= 0 && readsDamage(value)) yield return value.Local;
            foreach (var input in value.Inputs)
                foreach (int local in DamageLocals(input, readsDamage)) yield return local;
        }

        private static Type Number(ManagedValueExpression value)
        {
            if (value == null) return null;
            if (value.Local >= 0) return Number(value.Inputs[0]);
            OpCode op = value.Operation;
            if (op == OpCodes.Conv_R4 || op == OpCodes.Ldelem_R4 || op == OpCodes.Ldind_R4) return typeof(float);
            if (op == OpCodes.Conv_R8 || op == OpCodes.Conv_R_Un || op == OpCodes.Ldelem_R8 || op == OpCodes.Ldind_R8) return typeof(double);
            if (op == OpCodes.Conv_I4 || op == OpCodes.Ldelem_I4 || op == OpCodes.Ldind_I4) return typeof(int);
            return value.Operand is FieldInfo field ? field.FieldType : value.Operand is MethodInfo method ? method.ReturnType
                : value.Operand as Type;
        }

        internal Func<ILGenerator, OpCode, object, bool> Rewrite(Action<ILGenerator> loadAmount)
        {
            int instruction = -1;
            LocalBuilder reduced = null, token = null;
            return (emit, op, operand) =>
            {
                int at = ++instruction;
                if (at == subtract)
                {
                    reduced = emit.DeclareLocal(number);
                    emit.Emit(OpCodes.Pop);
                    loadAmount(emit);
                    ManagedHealthWrite.ConvertAmount(emit, number);
                    emit.Emit(OpCodes.Sub);
                    if (number == typeof(float)) emit.Emit(OpCodes.Ldc_R4, 0f);
                    else if (number == typeof(double)) emit.Emit(OpCodes.Ldc_R8, 0d);
                    else emit.Emit(OpCodes.Ldc_I4_0);
                    emit.Emit(OpCodes.Call, typeof(Math).GetMethod(nameof(Math.Max), new[] { number, number }));
                    emit.Emit(OpCodes.Dup); emit.Emit(OpCodes.Stloc, reduced);
                    return true;
                }
                if (at == write)
                {
                    if (context != null) { token = emit.DeclareLocal(context); emit.Emit(OpCodes.Stloc, token); }
                    emit.Emit(OpCodes.Pop); emit.Emit(OpCodes.Ldloc, reduced);
                    if (storage != number)
                        emit.Emit(storage == typeof(float) ? OpCodes.Conv_R4 : storage == typeof(double) ? OpCodes.Conv_R8 : OpCodes.Conv_I4);
                    if (context != null) emit.Emit(OpCodes.Ldloc, token);
                }
                return false;
            };
        }
    }
}
