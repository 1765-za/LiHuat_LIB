using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using LihuatLib.Mechanisms;

namespace LihuatLib.Combat
{
    internal sealed class ManagedReadLimitException : Exception { }

    internal sealed class ManagedReadProgram
    {
        private const int WorkLimit = 65536;
        private readonly ManagedCombatIl il;
        private readonly ManagedNativeReadPlan native;
        private readonly Dictionary<MethodInfo, DynamicMethod> methods = new();
        internal ManagedReadProgram(ManagedCombatIl il, ManagedNativeReadPlan native = null) { this.il = il; this.native = native; }

        private static void Charge(ref int remaining, int cost)
        {
            remaining -= cost;
            if (remaining < 0) throw new ManagedReadLimitException();
        }

        internal Func<object, float> Compile(MethodInfo reader, FieldInfo value, IEnumerable<MethodInfo> dependencies)
        {
            foreach (MethodInfo method in dependencies)
                methods[method] = new DynamicMethod("LihuatReadFlow", method.ReturnType,
                    method.GetParameters().Select(p => p.ParameterType).Append(typeof(int).MakeByRefType()).ToArray(), method.Module, true);
            if (!methods.ContainsKey(reader)) return null;
            foreach (var pair in methods) if (!Emit(pair.Key, pair.Value)) return null;
            var wrapper = new DynamicMethod("LihuatReadSnapshot", typeof(float), new[] { typeof(object) }, reader.Module, true);
            var emit = wrapper.GetILGenerator();
            LocalBuilder budget = emit.DeclareLocal(typeof(int));
            emit.Emit(OpCodes.Ldc_I4, WorkLimit); emit.Emit(OpCodes.Stloc, budget);
            emit.Emit(OpCodes.Ldarg_0); emit.Emit(OpCodes.Castclass, reader.GetParameters()[0].ParameterType);
            emit.Emit(OpCodes.Ldloca, budget); emit.Emit(OpCodes.Call, methods[reader]);
            if (value != null) emit.Emit(OpCodes.Ldfld, value);
            emit.Emit(OpCodes.Ret);
            return wrapper.CreateDelegate<Func<object, float>>();
        }

        private bool Emit(MethodInfo original, DynamicMethod clone)
        {
            var body = il.Read(original); int[] offsets = ManagedReadAnalysis.Offsets(body);
            int budgetArgument = original.GetParameters().Length, index = 0;
            var metadata = original.GetMethodBody();
            var catches = new HashSet<int>(metadata.ExceptionHandlingClauses
                .Where(c => c.Flags == ExceptionHandlingClauseOptions.Clause).Select(c => c.HandlerOffset));
            return PortableIlEmitter.TryEmit(original, metadata, il.ReadBody(original), clone.GetILGenerator(), out _, rewrite: (emit, op, operand) =>
            {
                int instruction = index;
                int offset = offsets[index++];
                if (native?.Exits(original, instruction, out int stackDepth) == true)
                {
                    for (int i = 0; i < stackDepth; i++) emit.Emit(OpCodes.Pop);
                    emit.Emit(OpCodes.Newobj, typeof(ManagedReadLimitException).GetConstructor(Type.EmptyTypes));
                    emit.Emit(OpCodes.Throw);
                    return true;
                }
                if (catches.Contains(offset))
                {
                    // Foreign catch blocks must not turn an exhausted read into another iteration.
                    Label ordinary = emit.DefineLabel();
                    emit.Emit(OpCodes.Dup); emit.Emit(OpCodes.Isinst, typeof(ManagedReadLimitException));
                    emit.Emit(OpCodes.Brfalse, ordinary); emit.Emit(OpCodes.Throw); emit.MarkLabel(ordinary);
                }
                bool backward = op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
                    && (operand is int target && target <= offset || operand is int[] targets && targets.Any(target => target <= offset));
                if (index == 1 || backward)
                {
                    emit.Emit(OpCodes.Ldarg, (short)budgetArgument); emit.Emit(OpCodes.Ldc_I4, body.Count);
                    emit.Emit(OpCodes.Call, typeof(ManagedReadProgram).GetMethod(nameof(Charge), BindingFlags.Static | BindingFlags.NonPublic));
                }
                if (op == OpCodes.Call && operand is MethodInfo call && methods.TryGetValue(call, out DynamicMethod replacement))
                {
                    emit.Emit(OpCodes.Ldarg, (short)budgetArgument); emit.Emit(OpCodes.Call, replacement); return true;
                }
                return false;
            });
        }
    }
}
