using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Verse;

namespace LihuatLib.StarBlood
{
    internal sealed class PawnRuntimeComponents
    {
        private static readonly FieldInfo[] fields = FindFields();
        private static readonly HashSet<FieldInfo> roots = new HashSet<FieldInfo>(fields);
        private static readonly Func<Pawn, object[], bool> matches = BuildMatcher();
        private readonly object[] originals = new object[fields.Length];
        private readonly PawnOwnedStructures owned;
        internal IEnumerable<object> Owners => owned.Nodes;
        internal static bool IsRoot(FieldInfo field) => roots.Contains(field);

        internal PawnRuntimeComponents(Pawn pawn)
        {
            for (int i = 0; i < fields.Length; i++) originals[i] = fields[i].GetValue(pawn);
            owned = new PawnOwnedStructures(pawn, originals);
        }
        internal bool Intact(Pawn pawn) => matches(pawn, originals) && owned.Intact;
        internal bool Rejects(FieldInfo field, object owner, object value) => owned.Rejects(field, owner, value);
        internal bool Rejects(FieldInfo field, object value)
        {
            int index = Array.IndexOf(fields, field);
            return index >= 0 && originals[index] != null && !ReferenceEquals(originals[index], value);
        }
        internal void Restore(Pawn pawn)
        {
            for (int i = 0; i < fields.Length; i++)
                if (originals[i] != null && !ReferenceEquals(fields[i].GetValue(pawn), originals[i])) fields[i].SetValue(pawn, originals[i]);
            owned.Restore();
        }
        private static FieldInfo[] FindFields()
        {
            var result = new List<FieldInfo>();
            foreach (FieldInfo field in typeof(Pawn).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (!field.FieldType.IsValueType && field.FieldType.Assembly == typeof(Pawn).Assembly
                    && (field.FieldType.Name.EndsWith("Tracker", StringComparison.Ordinal)
                        || field.Name == nameof(Pawn.pather) || field.Name == nameof(Pawn.mindState)
                        || field.Name == nameof(Pawn.thinker) || field.Name == nameof(Pawn.natives)
                        || HasPawnOwner(field.FieldType))) result.Add(field);
            return result.ToArray();
        }
        private static bool HasPawnOwner(Type type)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if ((field.Name == "pawn" || field.Name == "parent") && typeof(Thing).IsAssignableFrom(field.FieldType)) return true;
            return false;
        }
        private static Func<Pawn, object[], bool> BuildMatcher()
        {
            var pawn = Expression.Parameter(typeof(Pawn));
            var expected = Expression.Parameter(typeof(object[]));
            Expression body = Expression.Constant(true);
            for (int i = 0; i < fields.Length; i++)
            {
                Expression original = Expression.ArrayIndex(expected, Expression.Constant(i));
                Expression equal = Expression.ReferenceEqual(Expression.Convert(Expression.Field(pawn, fields[i]), typeof(object)), original);
                body = Expression.AndAlso(body, Expression.OrElse(Expression.ReferenceEqual(original, Expression.Constant(null)), equal));
            }
            return Expression.Lambda<Func<Pawn, object[], bool>>(body, pawn, expected).Compile();
        }
    }
}
