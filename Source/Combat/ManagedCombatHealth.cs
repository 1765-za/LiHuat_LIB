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
    internal sealed class ManagedCombatHealth
    {
        internal FieldInfo Storage;
        internal MethodInfo Damage, Dead, Maximum;
        internal int Core;
        private ManagedHealthCodec codec;
        private Action<object, int, DamageInfo> direct;
        internal float Current(object state) => codec != null ? (float)codec.Read.Invoke(state, null) : ManagedHealthStorage.Read(Storage, state, Core);
        internal float Capacity(object state) => Convert.ToSingle(Maximum.Invoke(state, Maximum.GetParameters().Length == 0 ? null : new object[] { Core }));
        internal bool IsDead(object state) => (bool)Dead.Invoke(state, null);
        internal void Hit(object state, int part, DamageInfo damage, bool bypass)
        {
            if (bypass) direct(state, Core, damage);
            else Damage.Invoke(state, Damage.GetParameters().Length == 1 ? new object[] { damage } : new object[] { part, damage });
        }

        internal static ManagedCombatHealth Discover(Type type, ManagedCombatIl il)
        {
            var models = new List<ManagedCombatHealth>();
            MethodInfo[] methods = ManagedCombatIl.Methods(type);
            foreach (MethodInfo dead in methods.Where(m => m.ReturnType == typeof(bool) && ManagedCombatIl.Signature(m)))
            {
                ManagedDeathPredicate predicate = ManagedDeathPredicate.Read(dead, il);
                if (predicate == null) continue;
                FieldInfo storage = predicate.Storage;
                ManagedHealthCodec codec = predicate.Codec;
                MethodInfo damage = ManagedCombatIl.Only(methods.Where(m => m.ReturnType == typeof(void)
                    && (ManagedCombatIl.Signature(m, typeof(DamageInfo)) || ManagedCombatIl.Signature(m, typeof(int), typeof(DamageInfo))))
                    .Where(m => codec != null ? il.Read(m).Any(l => Equals(l.operand, codec.Read)) && il.Read(m).Any(l => Equals(l.operand, codec.Write))
                        : il.Read(m).Any(l => Equals(l.operand, storage))));
                if (damage == null) continue;
                var maxima = new List<MethodInfo>();
                foreach (MethodInfo method in methods.Where(m => m != damage))
                {
                    var body = il.Read(method);
                    if (codec != null)
                    {
                        maxima.AddRange(codec.Initializers(body).Where(m => codec.CanReadMaximum(m, il)));
                        continue;
                    }
                    for (int i = 0; i < body.Count; i++)
                    {
                        if (body[i].opcode != OpCodes.Stfld || !Equals(body[i].operand, storage)) continue;
                        maxima.AddRange(body.Skip(Math.Max(0, i - 24)).Take(Math.Min(i, 24)).Where(l =>
                            l.opcode == OpCodes.Ldftn || l.opcode == OpCodes.Call || l.opcode == OpCodes.Callvirt)
                            .Select(l => l.operand).OfType<MethodInfo>().Where(m => m.DeclaringType == type && ManagedHealthStorage.Numeric(m.ReturnType)
                                && (ManagedCombatIl.Signature(m) || ManagedCombatIl.Signature(m, typeof(int)))
                                && ManagedHealthCodec.CanReadMaximum(m, storage, il)));
                    }
                }
                MethodInfo maximum = ManagedCombatIl.Only(maxima);
                if (maximum == null) continue;
                var model = new ManagedCombatHealth { Storage = storage, Damage = damage, Dead = dead, Maximum = maximum, Core = predicate.Core, codec = codec };
                if (model.Compile(il)) models.Add(model);
            }
            var unique = models.GroupBy(model => (model.Storage, model.Damage, model.Maximum, model.Core, model.codec?.Read, model.codec?.Write))
                .Select(group => group.First()).ToArray();
            return ManagedCombatIl.Only(unique);
        }

        private bool Compile(ManagedCombatIl il)
        {
            var body = il.Read(Damage);
            if (body.Count > 2048) return false;
            Type[] args = new[] { Damage.DeclaringType }.Concat(Damage.GetParameters().Select(p => p.ParameterType)).ToArray();
            ManagedHealthReduction reduction = codec != null ? codec.Reduction(body, args.Length - 1)
                : ManagedHealthWrite.Reduction(body, Storage, args.Length - 1);
            if (reduction == null) return false;
            var damageLocals = ManagedHealthWrite.DamageLocals(Damage, body).Intersect(
                ManagedHealthReduction.DamageLocals(reduction.Amount, v => ManagedHealthReduction.ReadsDamage(v, args.Length - 1))).ToHashSet();
            var clone = new DynamicMethod("LihuatCombatDamage", typeof(void), args, Damage.Module, true);
            MethodInfo getAmount = typeof(DamageInfo).GetProperty(nameof(DamageInfo.Amount)).GetMethod;
            void LoadAmount(ILGenerator g) { g.Emit(OpCodes.Ldarga, (short)(args.Length - 1)); g.Emit(OpCodes.Call, getAmount); }
            var rewrite = reduction.Rewrite(LoadAmount);
            if (!PortableIlEmitter.TryEmit(Damage, Damage.GetMethodBody(), il.ReadBody(Damage), clone.GetILGenerator(), out _, rewrite: (g, op, operand) =>
            {
                if (rewrite(g, op, operand)) return true;
                if (ManagedCombatIl.StoredLocal(op, operand) is not int local || !damageLocals.Contains(local)) return false;
                g.Emit(OpCodes.Pop); LoadAmount(g);
                ManagedHealthWrite.ConvertAmount(g, Damage.GetMethodBody().LocalVariables[local].LocalType);
                g.Emit(OpCodes.Stloc, (short)local); return true;
            })) return false;
            var wrapper = new DynamicMethod("LihuatCombatDispatch", typeof(void), new[] { typeof(object), typeof(int), typeof(DamageInfo) }, Damage.Module, true);
            ILGenerator emit = wrapper.GetILGenerator();
            emit.Emit(OpCodes.Ldarg_0); emit.Emit(OpCodes.Castclass, Damage.DeclaringType);
            if (args.Length == 3) emit.Emit(OpCodes.Ldarg_1);
            emit.Emit(OpCodes.Ldarg_2); emit.Emit(OpCodes.Call, clone); emit.Emit(OpCodes.Ret);
            direct = wrapper.CreateDelegate<Action<object, int, DamageInfo>>();
            return true;
        }
    }
}
