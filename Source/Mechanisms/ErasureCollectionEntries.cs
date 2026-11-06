using System;
using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureCollectionEntries
    {
        private static readonly Dictionary<Type, Func<object, object, bool>> removers = new Dictionary<Type, Func<object, object, bool>>();

        internal static bool Remove(object collection, object value)
        {
            Type type = collection.GetType();
            if (!removers.TryGetValue(type, out Func<object, object, bool> remove))
            {
                if (!type.IsGenericType || type.GetGenericArguments()[0].IsValueType) return false;
                Type definition = type.GetGenericTypeDefinition();
                string name = definition == typeof(List<>) ? nameof(RemoveList) : definition == typeof(HashSet<>) ? nameof(RemoveSet) : null;
                if (name == null) return false;
                MethodInfo method = typeof(ErasureCollectionEntries).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(type.GetGenericArguments());
                removers[type] = remove = (Func<object, object, bool>)Delegate.CreateDelegate(typeof(Func<object, object, bool>), method);
            }
            return remove(collection, value);
        }

        private static bool RemoveList<T>(object collection, object value) where T : class
            => ReferenceListStorage<T>.Remove((List<T>)collection, (T)value) != 0;
        private static bool RemoveSet<T>(object collection, object value) where T : class
            => ReferenceSetStorage<T>.Remove((HashSet<T>)collection, (T)value);
    }
}
