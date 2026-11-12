using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Combat
{
    internal sealed class ManagedServiceHealth
    {
        internal readonly MethodInfo Damage;
        internal readonly float Maximum;
        private readonly Func<object, float> read;
        private readonly Action<object, DamageInfo> hit;
        private readonly ManagedSnapshotReader proof;
        internal bool Ready => proof.Ready;
        internal float Current(object target)
        {
            try { return read(target); }
            catch (ManagedReadLimitException) { return float.NaN; }
        }
        internal void Hit(object target, DamageInfo damage) => hit(target, damage);
        private ManagedServiceHealth(MethodInfo damage, float maximum, Func<object, float> read, Action<object, DamageInfo> hit, ManagedSnapshotReader proof)
        { Damage = damage; Maximum = maximum; this.read = read; this.hit = hit; this.proof = proof; }

        internal static bool Signature(MethodInfo method)
        {
            var args = method.GetParameters();
            return method.IsStatic && !method.ContainsGenericParameters && args.Length == 3
                && typeof(Thing).IsAssignableFrom(args[0].ParameterType) && args[1].ParameterType == typeof(DamageInfo).MakeByRefType()
                && args[2].ParameterType == typeof(bool).MakeByRefType() && args[2].IsOut
                && (method.ReturnType == typeof(bool) || method.ReturnType == typeof(void));
        }

        internal static ManagedServiceHealth Discover(MethodInfo route, ManagedCombatIl il)
        {
            if (!Signature(route)) return null;
            Type target = route.GetParameters()[0].ParameterType;
            var body = il.Read(route);
            if (body.Count == 0 || body.Count > 1024) return null;
            var found = new List<ManagedServiceHealth>();
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i].operand is not MethodInfo update || !update.IsStatic || !update.ReturnType.IsValueType || update.ReturnType.IsPrimitive) continue;
                var parameters = update.GetParameters();
                if (parameters.Length is not (2 or 3) || parameters[0].ParameterType != target || parameters[1].ParameterType != typeof(float)
                    || parameters.Length == 3 && parameters[2].ParameterType != update.ReturnType) continue;
                int at = i - 1;
                if (!ManagedValueExpression.ReadInputs(body, ref at, parameters.Length, 0, out var inputs) || !inputs[0].IsThis
                    || !ManagedHealthReduction.ReadsDamage(inputs[1], 1)) continue;
                var supplied = inputs.Length == 3 ? inputs[2] : null;
                if (!FindSnapshot(update, supplied, il, out MethodInfo reader, out FieldInfo value, out MethodInfo writer, out var reduction)) continue;
                var proof = new ManagedSnapshotReader(il);
                if (!proof.Inspect(reader) || !ManagedServiceProof.Lifecycle(target, route, reader, il)
                    || !ManagedServiceProof.WritesValue(writer, proof.Fields, il)) continue;
                float maximum = FindMaximum(writer, il);
                if (!(maximum > 0f) || !float.IsFinite(maximum)) continue;
                var locals = ManagedHealthWrite.DamageLocals(route, body).Intersect(
                    ManagedHealthReduction.DamageLocals(inputs[1], v => ManagedHealthReduction.ReadsDamage(v, 1))).ToHashSet();
                Action<object, DamageInfo> dispatch = CompileDamage(route, i, locals, update, reduction, il);
                var sample = new ManagedReadProgram(il, proof.Native).Compile(reader, value, proof.Methods);
                if (dispatch != null && sample != null) found.Add(new ManagedServiceHealth(route, maximum, sample, dispatch, proof));
            }
            return ManagedCombatIl.Only(found);
        }

        private static bool FindSnapshot(MethodInfo update, ManagedValueExpression supplied, ManagedCombatIl il, out MethodInfo reader, out FieldInfo field,
            out MethodInfo writer, out ManagedHealthReduction reduction)
        {
            reader = null; field = null; writer = null; reduction = null;
            var body = il.Read(update);
            if (body.Count > 256) return false;
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i].operand is not MethodInfo call || !call.IsStatic || call.ReturnType != typeof(void)
                    || !ManagedCombatIl.Signature(call, update.GetParameters()[0].ParameterType, typeof(float))) continue;
                int at = i - 1;
                if (!ManagedValueExpression.ReadInputs(body, ref at, 2, 0, out var args) || !args[0].IsThis) continue;
                var candidate = ManagedHealthReduction.Create(args[1], i, typeof(float),
                    v => Snapshot(v, update, supplied, out _, out _), v => v.DependsOnArgument(1));
                if (candidate == null) continue;
                if (writer != null) return false;
                Snapshot(candidate.Health, update, supplied, out reader, out field);
                writer = call; reduction = candidate;
            }
            return writer != null;
        }

        private static bool Snapshot(ManagedValueExpression value, MethodInfo update, ManagedValueExpression supplied, out MethodInfo reader, out FieldInfo field)
        {
            reader = null; field = null;
            var previous = value.Unwrapped();
            if (previous.Operation != OpCodes.Ldfld || previous.Operand is not FieldInfo number || number.FieldType != typeof(float)
                || !number.IsInitOnly || number.DeclaringType != update.ReturnType) return false;
            var snapshot = previous.Inputs[0].Unwrapped();
            if (snapshot.Argument == 2 && supplied != null) snapshot = supplied.Unwrapped();
            if (snapshot.Operand is not MethodInfo read || !read.IsStatic || read.ReturnType != update.ReturnType
                || !ManagedCombatIl.Signature(read, update.GetParameters()[0].ParameterType)
                || snapshot.Inputs.Length != 1 || !snapshot.Inputs[0].IsThis) return false;
            reader = read; field = number; return true;
        }

        private static float FindMaximum(MethodInfo writer, ManagedCombatIl il)
        {
            var values = new List<float>();
            var body = il.Read(writer);
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i].operand is not MethodInfo call || call.DeclaringType != typeof(Math) && call.DeclaringType != typeof(MathF)
                    || call.Name != "Clamp" || !ManagedCombatIl.Signature(call, typeof(float), typeof(float), typeof(float))) continue;
                int at = i - 1;
                if (ManagedValueExpression.ReadInputs(body, ref at, 3, 0, out var args) && args[0].Unwrapped().Argument == 1
                    && args[1].Operand is float min && min == 0f && args[2].Operand is float max) values.Add(max);
            }
            return values.Distinct().Count() == 1 ? values[0] : 0f;
        }

        private static Action<object, DamageInfo> CompileDamage(MethodInfo route, int updateAt, HashSet<int> locals,
            MethodInfo update, ManagedHealthReduction reduction, ManagedCombatIl il)
        {
            var commit = new DynamicMethod("LihuatServiceReduction", update.ReturnType,
                update.GetParameters().Select(p => p.ParameterType).ToArray(), update.Module, true);
            if (!PortableIlEmitter.TryEmit(update, update.GetMethodBody(), il.ReadBody(update), commit.GetILGenerator(), out _,
                rewrite: reduction.Rewrite(g => g.Emit(OpCodes.Ldarg_1)))) return null;
            var clone = new DynamicMethod("LihuatServiceDamage", route.ReturnType, route.GetParameters().Select(p => p.ParameterType).ToArray(), route.Module, true);
            int instruction = -1;
            void LoadAmount(ILGenerator g)
            { g.Emit(OpCodes.Ldarg_1); g.Emit(OpCodes.Call, typeof(DamageInfo).GetProperty(nameof(DamageInfo.Amount)).GetMethod); }
            if (!PortableIlEmitter.TryEmit(route, route.GetMethodBody(), il.ReadBody(route), clone.GetILGenerator(), out _, rewrite: (emit, op, operand) =>
            {
                if (++instruction == updateAt)
                {
                    LocalBuilder snapshot = null;
                    if (update.GetParameters().Length == 3)
                    { snapshot = emit.DeclareLocal(update.ReturnType); emit.Emit(OpCodes.Stloc, snapshot); }
                    emit.Emit(OpCodes.Pop); LoadAmount(emit);
                    if (snapshot != null) emit.Emit(OpCodes.Ldloc, snapshot);
                    emit.Emit(OpCodes.Call, commit); return true;
                }
                if (ManagedCombatIl.StoredLocal(op, operand) is not int local || !locals.Contains(local)) return false;
                emit.Emit(OpCodes.Pop); LoadAmount(emit);
                ManagedHealthWrite.ConvertAmount(emit, route.GetMethodBody().LocalVariables[local].LocalType);
                emit.Emit(OpCodes.Stloc, (short)local); return true;
            })) return null;
            var wrapper = new DynamicMethod("LihuatServiceDispatch", typeof(void), new[] { typeof(object), typeof(DamageInfo) }, route.Module, true);
            var g = wrapper.GetILGenerator(); LocalBuilder absorbed = g.DeclareLocal(typeof(bool));
            g.Emit(OpCodes.Ldarg_0); g.Emit(OpCodes.Castclass, route.GetParameters()[0].ParameterType);
            g.Emit(OpCodes.Ldarga_S, (byte)1); g.Emit(OpCodes.Ldloca, absorbed); g.Emit(OpCodes.Call, clone);
            if (route.ReturnType != typeof(void)) g.Emit(OpCodes.Pop);
            g.Emit(OpCodes.Ret);
            return wrapper.CreateDelegate<Action<object, DamageInfo>>();
        }
    }
}
