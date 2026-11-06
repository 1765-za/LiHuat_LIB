using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Combat
{
    internal sealed class AttachedHealthPlan
    {
        private readonly ManagedCombatHealth health;
        private readonly ManagedServiceHealth service;
        internal MethodInfo Damage => health?.Damage ?? service.Damage;
        internal float Current(object state) => health != null ? health.Current(state) : service.Current(state);
        internal float Capacity(object state) => health != null ? health.Capacity(state) : service.Maximum;
        internal bool Alive(object state) => health != null ? !health.IsDead(state) : service.Current(state) is var value && float.IsFinite(value) && value > 0f;
        internal void Hit(object state, DamageInfo damage)
        {
            if (health != null) health.Hit(state, health.Core, damage, true);
            else service.Hit(state, damage);
        }
        private readonly FieldInfo[] receiver, owner;
        private AttachedHealthPlan(ManagedCombatHealth health, FieldInfo[] receiver, FieldInfo[] owner)
        { this.health = health; this.receiver = receiver; this.owner = owner; }
        private AttachedHealthPlan(ManagedServiceHealth service, FieldInfo[] receiver)
        { this.service = service; this.receiver = receiver; owner = Array.Empty<FieldInfo>(); }

        internal object State(object host, Thing target)
        {
            object state = Follow(host, receiver);
            return state != null && ReferenceEquals(Follow(state, owner), target) && (service == null || service.Ready) ? state : null;
        }
        private static object Follow(object value, FieldInfo[] path)
        {
            foreach (FieldInfo field in path) { if (value == null) return null; value = field.GetValue(value); }
            return value;
        }

        internal static AttachedHealthPlan[] Discover(Type type, bool component)
        {
            if (type.Assembly == typeof(Thing).Assembly || Core.InfrastructureAssemblies.Contains(type.Assembly)) return Array.Empty<AttachedHealthPlan>();
            MethodInfo entry = type.GetMethod(component ? nameof(ThingComp.PostPreApplyDamage) : nameof(Thing.PreApplyDamage));
            if (entry == null || entry.DeclaringType.Assembly == typeof(Thing).Assembly) return Array.Empty<AttachedHealthPlan>();
            var il = new ManagedCombatIl();
            var found = new List<AttachedHealthPlan>();
            var models = new Dictionary<Type, ManagedCombatHealth>();
            var body = il.Read(entry);
            if (body.Count > 1024) return Array.Empty<AttachedHealthPlan>();
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i].operand is MethodInfo route && ManagedServiceHealth.Signature(route))
                {
                    int cursor = i - 1;
                    if (ManagedValueExpression.ReadInputs(body, ref cursor, 3, 0, out var args)
                        && Path(args[0], out var subject) && DamageArgument(args[1]) && args[2].Argument == 2)
                    {
                        var model = ManagedServiceHealth.Discover(route, il);
                        if (model != null) found.Add(new AttachedHealthPlan(model, subject));
                    }
                }
                if (body[i].operand is not MethodInfo call || call.IsStatic || call.ReturnType != typeof(void)
                    || call.DeclaringType.Assembly == typeof(Thing).Assembly
                    || !ManagedCombatIl.Signature(call, typeof(DamageInfo)) && !ManagedCombatIl.Signature(call, typeof(int), typeof(DamageInfo))) continue;
                int at = i - 1;
                if (!ManagedValueExpression.ReadInputs(body, ref at, call.GetParameters().Length + 1, 0, out var inputs)
                    || !Path(inputs[0], out FieldInfo[] receiver) || !DamageArgument(inputs[^1])) continue;
                if (!models.TryGetValue(call.DeclaringType, out ManagedCombatHealth health))
                    models[call.DeclaringType] = health = ManagedCombatHealth.Discover(call.DeclaringType, il);
                if (health?.Damage != call || !DeathOwner(call, il, out FieldInfo[] owner)) continue;
                if (!found.Any(plan => plan.Damage == call && plan.receiver.SequenceEqual(receiver)))
                    found.Add(new AttachedHealthPlan(health, receiver, owner));
            }
            return found.ToArray();
        }

        private static bool DamageArgument(ManagedValueExpression value)
        {
            value = value?.Unwrapped();
            return value?.Argument == 1;
        }

        private static bool DeathOwner(MethodInfo damage, ManagedCombatIl il, out FieldInfo[] owner)
        {
            owner = null;
            var pending = new Queue<MethodInfo>(); pending.Enqueue(damage);
            var seen = new HashSet<MethodInfo>();
            var paths = new List<FieldInfo[]>(); int budget = 2048;
            while (pending.Count > 0)
            {
                MethodInfo method = pending.Dequeue();
                if (!seen.Add(method)) continue;
                if (seen.Count > 16) return false;
                var body = il.Read(method); budget -= body.Count;
                if (budget < 0) return false;
                for (int i = 0; i < body.Count; i++)
                {
                    if (body[i].operand is not MethodInfo call || call.IsStatic) continue;
                    int at = i - 1;
                    if (!ManagedValueExpression.ReadInputs(body, ref at, call.GetParameters().Length + 1, 0, out var inputs)) continue;
                    if ((call.Name == nameof(Thing.Kill) || call.Name == nameof(Thing.Destroy))
                        && call.DeclaringType.Assembly == typeof(Thing).Assembly && typeof(Thing).IsAssignableFrom(call.DeclaringType)
                        && Path(inputs[0], out FieldInfo[] path)) paths.Add(path);
                    else if (call.DeclaringType == damage.DeclaringType && call.ReturnType == typeof(void)
                        && inputs[0].IsThis && (!call.IsVirtual || call.IsFinal || call.DeclaringType.IsSealed)) pending.Enqueue(call);
                }
            }
            if (paths.Count == 0 || paths.Any(path => !path.SequenceEqual(paths[0]))) return false;
            owner = paths[0]; return true;
        }

        private static bool Path(ManagedValueExpression value, out FieldInfo[] fields)
        {
            var path = new List<FieldInfo>(); fields = null;
            value = value?.Unwrapped();
            while (value?.Operation == OpCodes.Ldfld && value.Operand is FieldInfo field && !field.IsStatic && path.Count < 4)
            {
                path.Add(field); value = value.Inputs[0].Unwrapped();
            }
            if (value?.IsThis != true) return false;
            path.Reverse(); fields = path.ToArray(); return true;
        }
    }
}
