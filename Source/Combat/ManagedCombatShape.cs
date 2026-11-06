using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LihuatLib.Combat
{
    internal sealed class ManagedCombatShape
    {
        internal Type StateType, ManagerType;
        internal FieldInfo Membership, MapField, PositionField, FactionField, IdField, Proxies;
        internal MethodInfo Attach, Arrive, Detach, Settle, Lookup;
        internal ManagedCombatHealth Health;
        internal MethodInfo ProxyState;
        internal FieldInfo ProxyPart;
        private readonly Dictionary<Type, FieldInfo[]> embedded = new();
        internal string Id => "lihuat.managed:" + ManagerType.Assembly.GetName().Name + ":" + ManagerType.FullName + ":" + Membership.Name;
        internal MapComponent Manager(Map map) => map.components.First(c => c.GetType() == ManagerType);
        internal IList States(Map map) => (IList)Membership.GetValue(Manager(map));
        internal string Key(object state) => (string)IdField.GetValue(state);
        internal object Find(Map map, string id) => States(map).Cast<object>().FirstOrDefault(s => Key(s) == id);
        internal Map Map(object state) => (Map)MapField.GetValue(state);
        internal Vector3 Position(object state) => PositionField.FieldType == typeof(Vector3)
            ? (Vector3)PositionField.GetValue(state) : ((IntVec3)PositionField.GetValue(state)).ToVector3Shifted();
        internal void Position(object state, IntVec3 value) => PositionField.SetValue(state,
            PositionField.FieldType == typeof(Vector3) ? (object)value.ToVector3Shifted() : value);
        internal Faction Faction(object state) => (Faction)FactionField.GetValue(state);
        internal Thing NativeTarget(object state) => (Proxies?.GetValue(state) as IEnumerable)?.Cast<Thing>()
            .FirstOrDefault(t => t?.Spawned == true && !t.Destroyed);
        internal object Embedded(Thing thing)
        {
            Type type = thing.GetType();
            if (!embedded.TryGetValue(type, out FieldInfo[] fields))
                embedded[type] = fields = type.GetFields(ManagedCombatIl.Members).Where(f => f.FieldType == StateType).ToArray();
            return fields.Select(f => f.GetValue(thing)).FirstOrDefault(s => s != null);
        }

        internal static ManagedCombatShape Discover(Type manager, FieldInfo membership, ManagedCombatIl il)
        {
            Type type = membership.FieldType.GetGenericArguments()[0];
            if (!typeof(IExposable).IsAssignableFrom(type) || typeof(Thing).IsAssignableFrom(type) || type.IsAbstract) return null;
            FieldInfo[] fields = type.GetFields(ManagedCombatIl.Members);
            FieldInfo map = ManagedCombatIl.Only(fields.Where(f => f.FieldType == typeof(Map)));
            FieldInfo faction = ManagedCombatIl.Only(fields.Where(f => f.FieldType == typeof(Faction)));
            if (map == null || faction == null) return null;
            MethodInfo[] methods = ManagedCombatIl.Methods(type);
            MethodInfo attach = ManagedCombatIl.Only(methods.Where(m => m.ReturnType == typeof(void) && ManagedCombatIl.Signature(m, typeof(Map))
                && il.Read(m).Any(l => l.opcode == OpCodes.Stfld && Equals(l.operand, map))));
            MethodInfo arrive = ManagedCombatIl.Only(methods.Where(m => m.ReturnType == typeof(void)
                && ManagedCombatIl.Signature(m, typeof(Map), typeof(IntVec3)) && il.Read(m).Any(l => Equals(l.operand, attach))));
            if (attach == null || arrive == null) return null;
            FieldInfo position = ManagedCombatIl.Only(il.Read(arrive).Where(l => l.opcode == OpCodes.Stfld).Select(l => l.operand).OfType<FieldInfo>()
                .Where(f => f.DeclaringType == type && (f.FieldType == typeof(Vector3) || f.FieldType == typeof(IntVec3))));
            MethodInfo detach = ManagedCombatIl.Only(methods.Where(m => m.ReturnType == typeof(void) && ManagedCombatIl.Signature(m)).Where(m =>
            {
                var body = il.Read(m);
                return body.Select((l, i) => l.opcode == OpCodes.Stfld && Equals(l.operand, map) && i > 0 && body[i - 1].opcode == OpCodes.Ldnull).Any(v => v);
            }));
            if (position == null || detach == null) return null;
            MethodInfo[] owners = ManagedCombatIl.Methods(manager);
            MethodInfo lookup = ManagedCombatIl.Only(owners.Where(m => m.ReturnType == type && ManagedCombatIl.Signature(m, typeof(string))));
            if (lookup == null || !il.Read(lookup).Any(l => Equals(l.operand, membership))) return null;
            MethodInfo expose = manager.GetMethod(nameof(IExposable.ExposeData), ManagedCombatIl.Members);
            if (expose == null || !il.Read(expose).Any(l => Equals(l.operand, membership))
                || !il.Read(expose).Any(l => l.operand is MethodInfo m && m.DeclaringType == typeof(Scribe_Collections))) return null;
            var lookupBody = il.Read(lookup).Concat(il.Read(lookup).Where(l => l.opcode == OpCodes.Ldftn).Select(l => l.operand).OfType<MethodInfo>().SelectMany(il.Read));
            FieldInfo identity = ManagedCombatIl.Only(lookupBody.Where(l => l.opcode == OpCodes.Ldfld).Select(l => l.operand).OfType<FieldInfo>()
                .Where(f => f.DeclaringType == type && f.FieldType == typeof(string)));
            if (identity == null) return null;
            ManagedCombatHealth health = ManagedCombatHealth.Discover(type, il);
            if (health == null) return null;
            var shape = new ManagedCombatShape { StateType = type, ManagerType = manager, Membership = membership, MapField = map,
                PositionField = position, FactionField = faction, IdField = identity, Attach = attach, Arrive = arrive, Detach = detach, Lookup = lookup, Health = health };
            shape.Settle = ManagedCombatIl.Only(owners.Where(m => ManagedCombatIl.Signature(m, type)
                && il.Read(m).Any(l => ManagedCombatIl.Calls(l, typeof(ThingMaker), nameof(ThingMaker.MakeThing)))
                && il.Read(m).Any(l => ManagedCombatIl.Calls(l, typeof(GenSpawn), nameof(GenSpawn.Spawn)))
                && il.Read(m).Any(l => ManagedCombatIl.Calls(l, membership.FieldType, "Remove"))));
            foreach (FieldInfo field in fields.Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>)))
            {
                Type proxy = field.FieldType.GetGenericArguments()[0];
                if (!typeof(Thing).IsAssignableFrom(proxy) || !typeof(IAttackTarget).IsAssignableFrom(proxy)) continue;
                MethodInfo damage = proxy.GetMethod(nameof(Thing.PreApplyDamage));
                if (!il.Read(damage).Any(l => Equals(l.operand, health.Damage))) continue;
                shape.ProxyState = ManagedCombatIl.Only(il.Read(damage).Select(l => l.operand).OfType<MethodInfo>()
                    .Where(m => m.DeclaringType == proxy && m.ReturnType == type && ManagedCombatIl.Signature(m)));
                shape.ProxyPart = ManagedCombatIl.Only(il.Read(damage).Where(l => l.opcode == OpCodes.Ldfld).Select(l => l.operand).OfType<FieldInfo>()
                    .Where(f => f.DeclaringType == proxy && f.FieldType == typeof(int)));
                if (shape.ProxyState != null) { shape.Proxies = field; break; }
            }
            return shape;
        }
    }
}
