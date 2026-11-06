using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.StarBlood
{
    internal sealed class PawnOwnedStructures
    {
        private static readonly Dictionary<string, Func<object[], object[], bool>> matchers = new Dictionary<string, Func<object[], object[], bool>>();
        private static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
        private readonly FieldInfo[] members;
        private readonly object[] owners, expected;
        private readonly Func<object[], object[], bool> matches;
        internal readonly HashSet<object> Nodes = new HashSet<object>(ReferenceIdentity<object>.Instance);
        internal static bool Backlink(FieldInfo field) => field.DeclaringType is { IsValueType: false } owner
            && owner.Assembly == typeof(Pawn).Assembly
            && (field.Name == "pawn" || field.Name == "parent") && typeof(Thing).IsAssignableFrom(field.FieldType);
        internal PawnOwnedStructures(Pawn pawn, IEnumerable<object> roots)
        {
            var pending = new Queue<object>(roots.Where(r => r != null));
            var slots = new List<FieldInfo>(); var targets = new List<object>(); var values = new List<object>();
            while (pending.Count > 0)
            {
                object owner = pending.Dequeue();
                if (!Nodes.Add(owner)) continue;
                foreach (FieldInfo field in Fields(owner.GetType()))
                {
                    object value = field.GetValue(owner);
                    bool backlink = (field.Name == "pawn" || field.Name == "parent") && ReferenceEquals(value, pawn);
                    bool child = value != null && field.FieldType.Assembly == typeof(Pawn).Assembly
                        && (field.FieldType.Name.EndsWith("Handler", StringComparison.Ordinal) || field.FieldType.Name.EndsWith("Tracker", StringComparison.Ordinal)
                            || typeof(PawnRenderer).IsAssignableFrom(field.FieldType) || typeof(PawnTweener).IsAssignableFrom(field.FieldType))
                        && Fields(value.GetType()).Any(f => (f.Name == "pawn" || f.Name == "parent") && ReferenceEquals(f.GetValue(value), pawn));
                    if (!backlink && !child) continue;
                    slots.Add(field); targets.Add(owner); values.Add(value);
                    if (child) pending.Enqueue(value);
                }
            }
            members = slots.ToArray(); owners = targets.ToArray(); expected = values.ToArray();
            string key = string.Join(";", members.Select(f => f.Module.ModuleVersionId + ":" + f.MetadataToken));
            if (!matchers.TryGetValue(key, out matches)) matchers[key] = matches = Build(members);
        }
        private static FieldInfo[] Fields(Type type)
        {
            if (fields.TryGetValue(type, out FieldInfo[] found)) return found;
            var list = new List<FieldInfo>();
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                list.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.FieldType.IsValueType && !f.FieldType.IsPointer && f.FieldType != typeof(string)));
            return fields[type] = list.ToArray();
        }
        private static Func<object[], object[], bool> Build(FieldInfo[] members)
        {
            var owners = Expression.Parameter(typeof(object[])); var values = Expression.Parameter(typeof(object[]));
            Expression body = Expression.Constant(true);
            for (int i = 0; i < members.Length; i++)
            {
                FieldInfo field = members[i];
                Expression owner = Expression.Convert(Expression.ArrayIndex(owners, Expression.Constant(i)), field.DeclaringType);
                body = Expression.AndAlso(body, Expression.ReferenceEqual(Expression.Convert(Expression.Field(owner, field), typeof(object)),
                    Expression.ArrayIndex(values, Expression.Constant(i))));
            }
            return Expression.Lambda<Func<object[], object[], bool>>(body, owners, values).Compile();
        }
        internal bool Intact => matches(owners, expected);
        internal bool Rejects(FieldInfo field, object owner, object value)
        {
            for (int i = 0; i < members.Length; i++)
                if (members[i] == field && ReferenceEquals(owners[i], owner)) return !ReferenceEquals(expected[i], value);
            return false;
        }
        internal void Restore()
        {
            for (int i = 0; i < members.Length; i++)
                if (!ReferenceEquals(members[i].GetValue(owners[i]), expected[i])) members[i].SetValue(owners[i], expected[i]);
        }
    }
}
