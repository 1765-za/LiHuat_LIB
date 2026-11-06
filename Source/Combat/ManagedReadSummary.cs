using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LihuatLib.Combat
{
    internal sealed class ManagedReadSummary
    {
        private readonly record struct Key(MethodInfo Method, ManagedReadValue[] Arguments);
        private sealed class KeyComparer : IEqualityComparer<Key>
        {
            public bool Equals(Key x, Key y) => x.Method == y.Method && x.Arguments.SequenceEqual(y.Arguments);
            public int GetHashCode(Key key)
            {
                var hash = new HashCode(); hash.Add(key.Method);
                foreach (var argument in key.Arguments) hash.Add(argument);
                return hash.ToHashCode();
            }
        }
        private readonly Dictionary<Key, (ManagedReadValue Result, ManagedReadValue[] Outputs)> completed = new(new KeyComparer());
        internal readonly HashSet<MethodInfo> Methods = new();
        internal int Count => completed.Count;

        internal bool TryGet(MethodInfo method, ManagedReadValue[] arguments, out ManagedReadValue result, out ManagedReadValue[] outputs)
        {
            if (completed.TryGetValue(new Key(method, arguments), out var entry))
            { result = entry.Result; outputs = entry.Outputs; return true; }
            result = default; outputs = null; return false;
        }

        internal void Add(MethodInfo method, ManagedReadValue[] arguments, ManagedReadValue result, ManagedReadValue[] outputs)
        {
            completed[new Key(method, (ManagedReadValue[])arguments.Clone())] = (result, (ManagedReadValue[])outputs.Clone());
            Methods.Add(method);
        }
    }
}
