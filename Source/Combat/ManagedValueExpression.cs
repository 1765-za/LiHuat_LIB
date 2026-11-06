using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Combat
{
    internal sealed class ManagedValueExpression
    {
        internal OpCode Operation;
        internal object Operand;
        internal int Argument = -1, Local = -1;
        internal int Instruction;
        internal ManagedValueExpression[] Inputs = Array.Empty<ManagedValueExpression>();
        internal bool IsThis => Argument == 0;
        internal bool DependsOnArgument(int index) => Argument == index || Inputs.Any(v => v.DependsOnArgument(index));
        internal bool Calls(MethodInfo method) => Equals(Operand, method) || Inputs.Any(v => v.Calls(method));

        internal static ManagedValueExpression Before(List<CodeInstruction> body, ref int at, int depth = 0)
        {
            if (at < 0 || depth >= 32) return null;
            int instruction = at;
            CodeInstruction line = body[at--];
            var value = new ManagedValueExpression { Operation = line.opcode, Operand = line.operand, Instruction = instruction };
            OpCode op = line.opcode;
            if (op == OpCodes.Nop) return Before(body, ref at, depth + 1);
            if (op == OpCodes.Dup)
            {
                int source = at;
                return Before(body, ref source, depth + 1);
            }
            if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S || op == OpCodes.Ldarga || op == OpCodes.Ldarga_S)
                value.Argument = Convert.ToInt32(line.operand);
            else if (op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value)
                value.Argument = op.Value - OpCodes.Ldarg_0.Value;
            else if (ManagedCombatIl.LoadedLocal(line).HasValue || op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
            {
                int local = ManagedCombatIl.LoadedLocal(line) ?? Convert.ToInt32(line.operand);
                int[] stores = Enumerable.Range(0, at + 1).Where(i => ManagedCombatIl.StoredLocal(body[i].opcode, body[i].operand) == local).Take(2).ToArray();
                if (stores.Length != 1) return null;
                int cursor = stores[0] - 1;
                ManagedValueExpression input = Before(body, ref cursor, depth + 1);
                if (input == null) return null;
                value.Local = local;
                value.Inputs = new[] { input };
            }
            else if (op == OpCodes.Ldfld)
            {
                ManagedValueExpression owner = Before(body, ref at, depth + 1);
                if (owner == null) return null;
                value.Inputs = new[] { owner };
            }
            else if (op.Name.StartsWith("ldelem", StringComparison.Ordinal))
            {
                if (!ReadInputs(body, ref at, 2, depth, out value.Inputs)) return null;
            }
            else if ((op == OpCodes.Call || op == OpCodes.Callvirt) && line.operand is MethodInfo method)
            {
                if (method.ReturnType == typeof(void)) return null;
                if (!ReadInputs(body, ref at, method.GetParameters().Length + (method.IsStatic ? 0 : 1), depth, out value.Inputs)) return null;
            }
            else if (op == OpCodes.Newobj && line.operand is ConstructorInfo constructor)
            {
                if (!ReadInputs(body, ref at, constructor.GetParameters().Length, depth, out value.Inputs)) return null;
            }
            else if (op.StackBehaviourPop == StackBehaviour.Pop1_pop1 && op.StackBehaviourPush == StackBehaviour.Push1)
            {
                if (!ReadInputs(body, ref at, 2, depth, out value.Inputs)) return null;
            }
            else if (op == OpCodes.Ldobj || op.Name.StartsWith("ldind", StringComparison.Ordinal)
                || op.StackBehaviourPop == StackBehaviour.Pop1 && (op.StackBehaviourPush == StackBehaviour.Push1
                || op.Name.StartsWith("conv.", StringComparison.Ordinal)))
            {
                if (!ReadInputs(body, ref at, 1, depth, out value.Inputs)) return null;
            }
            else if (op != OpCodes.Ldsfld && op != OpCodes.Ldstr && op != OpCodes.Ldnull && op != OpCodes.Ldc_R4 && op != OpCodes.Ldc_R8
                && op != OpCodes.Ldc_I8 && op != OpCodes.Ldc_I4_M1 && !ManagedCombatIl.Integer(line).HasValue) return null;
            return value;
        }

        internal static bool ReadInputs(List<CodeInstruction> body, ref int at, int count, int depth, out ManagedValueExpression[] inputs)
        {
            inputs = new ManagedValueExpression[count];
            for (int i = count - 1; i >= 0; i--)
                if ((inputs[i] = Before(body, ref at, depth + 1)) == null) return false;
            return true;
        }

        internal bool IsConstant => Argument < 0 && Local < 0 && Inputs.Length == 0 && Operand is not MemberInfo;
        internal static bool IsClamp(MethodInfo method) => method.IsStatic && ManagedHealthStorage.Numeric(method.ReturnType)
            && (method.DeclaringType == typeof(Math) || method.DeclaringType == typeof(MathF) || method.DeclaringType == typeof(UnityEngine.Mathf))
            && (method.Name == "Max" || method.Name == "Min") && ManagedCombatIl.Signature(method, method.ReturnType, method.ReturnType);

        internal ManagedValueExpression Unwrapped()
        {
            ManagedValueExpression value = this;
            while (value.Inputs.Length == 1 && (value.Local >= 0 || value.Operation.Name.StartsWith("conv.", StringComparison.Ordinal)
                || value.Operation == OpCodes.Ldobj)) value = value.Inputs[0];
            return value;
        }

        internal ManagedValueExpression Unclamped()
        {
            ManagedValueExpression value = Unwrapped();
            while (value.Operand is MethodInfo method && IsClamp(method) && value.Inputs.Length == 2)
            {
                if (value.Inputs[0].IsConstant) value = value.Inputs[1].Unwrapped();
                else if (value.Inputs[1].IsConstant) value = value.Inputs[0].Unwrapped();
                else return null;
            }
            return value;
        }
    }
}
