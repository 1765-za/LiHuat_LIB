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
            if (entry.Source?.GetType() != source.GetType()) entry.Matches = owner is IDictionary ? CopyMatches : Matcher(source);
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

        private static Func<ICollection, List<object>, bool> Matcher(ICollection source)
        {
            if (source is object[]) return ArrayMatches;
            Type type = source.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type item = type.GenericTypeArguments[0];
                if (!item.IsValueType)
                    return typeof(LifeCollectionState).GetMethod(nameof(ListMatches), BindingFlags.Static | BindingFlags.NonPublic)
                        .MakeGenericMethod(item).CreateDelegate<Func<ICollection, List<object>, bool>>();
            }
            return CopyMatches;
        }

        private static bool ListMatches<T>(ICollection source, List<object> expected) where T : class
        {
            ReadOnlySpan<T> current = CollectionsMarshal.AsSpan((List<T>)source);
            ReadOnlySpan<object> previous = CollectionsMarshal.AsSpan(expected);
            if (current.Length != previous.Length) return false;
            for (int i = 0; i < current.Length; i++) if (!ReferenceEquals(current[i], previous[i])) return false;
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
                for (int i = 0; i < count; i++) if (!ReferenceEquals(current[i], expected[i])) return false;
                return true;
            }
            finally { ArrayPool<object>.Shared.Return(current, clearArray: true); }
        }
    }
}
