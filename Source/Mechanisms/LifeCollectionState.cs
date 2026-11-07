using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCollectionState
    {
        private sealed class Entry
        {
            internal ICollection Source;
            internal object Owner;
            internal Func<ICollection, List<object>, bool> Matches;
            internal long Generation;
            internal readonly List<object> Values = new();
        }
        private readonly Dictionary<object, Entry> entries = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> retired = new();
        private long generation;

        internal void Begin() => generation++;

        internal void Capture(object owner, ICollection source, object[] values, int count)
        {
            if (!entries.TryGetValue(owner, out Entry entry)) entries.Add(owner, entry = new Entry());
            if (entry.Source?.GetType() != source.GetType()) entry.Matches = Matcher(owner, source);
            entry.Owner = owner; entry.Source = source; entry.Generation = generation;
            entry.Values.Clear();
            for (int i = 0; i < count; i++) entry.Values.Add(values[i]);
            if (entry.Values.Capacity > Math.Max(512, count * 4)) entry.Values.TrimExcess();
        }

        internal void Complete()
        {
            foreach (var pair in entries) if (pair.Value.Generation != generation) retired.Add(pair.Key);
            foreach (object value in retired) entries.Remove(value);
            retired.Clear();
            if (entries.EnsureCapacity(0) > Math.Max(512, entries.Count * 4)) entries.TrimExcess();
            if (retired.Capacity > 512) retired.TrimExcess();
        }

        internal bool Matches()
        {
            foreach (Entry entry in entries.Values)
            {
                int count = entry.Values.Count;
                ICollection source = entry.Owner is IDictionary dictionary ? dictionary.Values : entry.Source;
                if (source.Count != count) return false;
                if (count == 0) continue;
                if (!entry.Matches(source, entry.Values)) return false;
            }
            return true;
        }

        private static Func<ICollection, List<object>, bool> Matcher(object owner, ICollection source)
        {
            if (owner is IDictionary)
            {
                Type dictionary = owner.GetType();
                return dictionary.IsGenericType && dictionary.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                    ? TypedMatcher(nameof(DictionaryMatches), dictionary.GenericTypeArguments) : CopyMatches;
            }
            if (source is object[]) return ArrayMatches;
            Type type = source.GetType();
            if (type.IsSZArray) return TypedMatcher(nameof(ValueArrayMatches), type.GetElementType());
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return TypedMatcher(nameof(ListMatches), type.GenericTypeArguments);
            return CopyMatches;
        }

        private static Func<ICollection, List<object>, bool> TypedMatcher(string method, params Type[] arguments) =>
            typeof(LifeCollectionState).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(arguments).CreateDelegate<Func<ICollection, List<object>, bool>>();

        private static bool ElementMatches<T>(T value, object expected) => typeof(T).IsValueType
            ? EqualityComparer<T>.Default.Equals(value, expected == null ? default : (T)expected)
            : ReferenceEquals(value, expected);

        private static bool ListMatches<T>(ICollection source, List<object> expected)
        {
            ReadOnlySpan<T> current = CollectionsMarshal.AsSpan((List<T>)source);
            ReadOnlySpan<object> previous = CollectionsMarshal.AsSpan(expected);
            if (current.Length != previous.Length) return false;
            for (int i = 0; i < current.Length; i++) if (!ElementMatches(current[i], previous[i])) return false;
            return true;
        }

        private static bool ValueArrayMatches<T>(ICollection source, List<object> expected)
        {
            var current = (T[])source;
            if (current.Length != expected.Count) return false;
            for (int i = 0; i < current.Length; i++) if (!ElementMatches(current[i], expected[i])) return false;
            return true;
        }

        private static bool DictionaryMatches<TKey, TValue>(ICollection source, List<object> expected)
        {
            var current = (Dictionary<TKey, TValue>.ValueCollection)source;
            if (current.Count != expected.Count) return false;
            int index = 0;
            foreach (TValue value in current) if (!ElementMatches(value, expected[index++])) return false;
            return true;
        }

        private static bool ArrayMatches(ICollection source, List<object> expected)
        {
            var current = (object[])source;
            ReadOnlySpan<object> previous = CollectionsMarshal.AsSpan(expected);
            if (current.Length != previous.Length) return false;
            for (int i = 0; i < current.Length; i++) if (!ReferenceEquals(current[i], previous[i])) return false;
            return true;
        }

        private static bool CopyMatches(ICollection source, List<object> expected)
        {
            int count = expected.Count;
            object[] current = ArrayPool<object>.Shared.Rent(count);
            try
            {
                source.CopyTo(current, 0);
                for (int i = 0; i < count; i++)
                {
                    object value = current[i], previous = expected[i];
                    if (ReferenceEquals(value, previous)) continue;
                    // ICollection.CopyTo boxes value elements anew on each inspection.
                    if (value == null || previous == null || !value.GetType().IsValueType
                        || value.GetType() != previous.GetType() || !value.Equals(previous)) return false;
                }
                return true;
            }
            finally { ArrayPool<object>.Shared.Return(current, clearArray: true); }
        }
    }
}
