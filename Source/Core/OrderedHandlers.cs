using System;

namespace LihuatLib.Core
{
    internal sealed class OrderedHandlers<T>(Func<T, string> identity, Func<T, int> order) where T : class
    {
        internal T[] Items { get; private set; } = Array.Empty<T>();
        internal int Count => Items.Length;

        internal void Register(T handler)
        {
            string id = identity(handler);
            int found = Array.FindIndex(Items, value => identity(value) == id);
            T[] next = new T[Items.Length + (found < 0 ? 1 : 0)];
            Array.Copy(Items, next, Items.Length);
            next[found < 0 ? Items.Length : found] = handler;
            Array.Sort(next, (left, right) => order(left) != order(right)
                ? order(left).CompareTo(order(right)) : StringComparer.Ordinal.Compare(identity(left), identity(right)));
            Items = next;
        }

        internal void Remove(T handler) => RemoveAt(Array.FindIndex(Items, value => ReferenceEquals(value, handler)));
        internal void Remove(string id) => RemoveAt(Array.FindIndex(Items, value => identity(value) == id));
        private void RemoveAt(int index)
        {
            if (index < 0) return;
            var next = new T[Items.Length - 1];
            Array.Copy(Items, 0, next, 0, index);
            Array.Copy(Items, index + 1, next, index, next.Length - index);
            Items = next;
        }
    }
}
