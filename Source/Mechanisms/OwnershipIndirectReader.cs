using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class OwnershipIndirectReader
    {
        internal static bool Supports(Type type)
        {
            if (type == typeof(WeakReference)) return true;
            if (!type.IsGenericType) return false;
            Type definition = type.GetGenericTypeDefinition();
            return definition == typeof(System.WeakReference<>) || definition == typeof(Verse.WeakReference<>)
                || definition == typeof(Queue<>) || definition == typeof(Stack<>);
        }
        internal static Func<object, ErasureOwnership.Traversal, int, bool> Build(Type type)
        {
            Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
            if (definition != null && !OwnershipCollectionReader.Relevant(type.GetGenericArguments()[0]))
                return (value, work, depth) => true;
            if (type == typeof(WeakReference) || definition == typeof(System.WeakReference<>) || definition == typeof(Verse.WeakReference<>))
            {
                Func<object, object> target = WeakTarget(type, definition);
                return (value, work, depth) => work.Push(target(value), depth);
            }
            Func<object, Array> items = Read<Array>(type, "_array");
            Func<object, int> count = Read<int>(type, "_size");
            Func<object, int> version = Read<int>(type, "_version");
            Func<object, int> head = definition == typeof(Queue<>) ? Read<int>(type, "_head") : null;
            return (value, work, depth) =>
            {
                int stamp = version(value), length = count(value);
                Array values = items(value);
                if (length < 0 || length > 64 || length > (values?.Length ?? 0)) return false;
                int start = head == null ? 0 : head(value);
                if (length > 0 && (start < 0 || start >= values.Length)) return false;
                for (int i = 0; i < length; i++)
                {
                    int index = head == null ? i : (int)(((long)start + i) % values.Length);
                    if (!work.Push(values.GetValue(index), depth)) return false;
                }
                return version(value) == stamp;
            };
        }
        private static Func<object, T> Read<T>(Type type, string name)
        {
            var input = Expression.Parameter(typeof(object));
            FieldInfo field = AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
            return Expression.Lambda<Func<object, T>>(Expression.Convert(Expression.Field(Expression.Convert(input, type), field), typeof(T)), input).Compile();
        }
        private static Func<object, object> WeakTarget(Type type, Type definition)
        {
            var input = Expression.Parameter(typeof(object));
            if (definition != typeof(System.WeakReference<>))
                return Expression.Lambda<Func<object, object>>(Expression.Property(Expression.Convert(input, typeof(WeakReference)), nameof(WeakReference.Target)), input).Compile();
            Type element = type.GetGenericArguments()[0];
            var target = Expression.Variable(element);
            var body = Expression.Block(new[] { target },
                Expression.Call(Expression.Convert(input, type), type.GetMethod("TryGetTarget"), target),
                Expression.Convert(target, typeof(object)));
            return Expression.Lambda<Func<object, object>>(body, input).Compile();
        }
    }
}
