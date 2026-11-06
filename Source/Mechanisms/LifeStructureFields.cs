using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeStructureFields
    {
        private static readonly Dictionary<Type, LifeStructureFields> types = new();
        internal readonly FieldInfo[] All;
        internal readonly FieldInfo[] Attachments;
        private Func<object, object[], bool> attachmentMatcher;

        internal Func<object, object[], bool> PrepareAttachmentMatcher()
        {
            if (attachmentMatcher != null) return attachmentMatcher;
            var owner = Expression.Parameter(typeof(object));
            var expected = Expression.Parameter(typeof(object[]));
            Expression body = Expression.Constant(true);
            for (int i = 0; i < Attachments.Length; i++)
            {
                FieldInfo field = Attachments[i];
                Expression value = Expression.Field(Expression.Convert(owner, field.DeclaringType), field);
                body = Expression.AndAlso(body, Expression.ReferenceEqual(Expression.Convert(value, typeof(object)),
                    Expression.ArrayIndex(expected, Expression.Constant(i))));
            }
            return attachmentMatcher = Expression.Lambda<Func<object, object[], bool>>(body, owner, expected).Compile();
        }

        private LifeStructureFields(Type type)
        {
            var all = new List<FieldInfo>();
            var attachments = new List<FieldInfo>();
            for (Type cursor = type; LifeResourceEvidence.IsCustom(cursor); cursor = cursor.BaseType)
                foreach (FieldInfo field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    all.Add(field);
                    Type value = field.FieldType;
                    if (!value.IsValueType && value != typeof(string) && !typeof(Thing).IsAssignableFrom(value)
                        && !typeof(Def).IsAssignableFrom(value) && !typeof(Delegate).IsAssignableFrom(value))
                        attachments.Add(field);
                }
            All = all.ToArray();
            Attachments = attachments.ToArray();
        }

        internal static LifeStructureFields For(Type type)
        {
            if (!types.TryGetValue(type, out LifeStructureFields fields))
                types.Add(type, fields = new LifeStructureFields(type));
            return fields;
        }
    }
}
