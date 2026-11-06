using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureWeakTable
    {
        private static readonly Dictionary<Type, Func<object, Pawn, object>> plans = new Dictionary<Type, Func<object, Pawn, object>>();
        internal static bool TryTake(object table, Pawn pawn, out object value)
        {
            value = null;
            Type type = table.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ConditionalWeakTable<,>)) return false;
            if (!type.GetGenericArguments()[0].IsInstanceOfType(pawn)) return true;
            if (!plans.TryGetValue(type, out var take))
                plans[type] = take = typeof(ErasureWeakTable).GetMethod(nameof(Take), BindingFlags.Static | BindingFlags.NonPublic)
                    .MakeGenericMethod(type.GetGenericArguments()).CreateDelegate<Func<object, Pawn, object>>();
            value = take(table, pawn);
            return true;
        }
        private static object Take<TKey, TValue>(object table, Pawn pawn) where TKey : class where TValue : class
        {
            ((ConditionalWeakTable<TKey, TValue>)table).Remove((TKey)(object)pawn, out TValue value);
            return value;
        }
    }
}
