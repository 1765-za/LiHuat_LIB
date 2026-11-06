using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeFlowSet
    {
        private static class Empty<T> { internal static readonly HashSet<T> Value = new HashSet<T>(); }
        public static HashSet<T> Collect<T>(IEnumerable<T> values) => values == null || values is ICollection<T> collection && collection.Count == 0
            ? Empty<T>.Value : new HashSet<T>(values);
    }

    internal sealed class LifeObjectPath : IEquatable<LifeObjectPath>
    {
        public readonly object Root;
        public readonly FieldInfo[] Fields;
        internal static readonly LifeObjectPath Recursive = new LifeObjectPath(typeof(LifeObjectPath), Array.Empty<FieldInfo>());
        private readonly int hash;
        public LifeObjectPath(params FieldInfo[] fields) : this(null, fields) { }
        private LifeObjectPath(object root, FieldInfo[] fields)
        {
            Root = root; Fields = fields;
            hash = Root?.GetHashCode() ?? 17;
            foreach (FieldInfo field in Fields) hash = unchecked(hash * 31 + field.GetHashCode());
        }
        public static LifeObjectPath Symbol(object root)
        {
            if (root is Tuple<MethodInfo, LifeObjectPath> indexed)
            {
                for (LifeObjectPath cursor = indexed.Item2; cursor != null;)
                {
                    if (cursor == Recursive) return Recursive;
                    if (!(cursor.Root is Tuple<MethodInfo, LifeObjectPath> parent)) break;
                    if (parent.Item1 == indexed.Item1) return Recursive;
                    cursor = parent.Item2;
                }
            }
            return new LifeObjectPath(root, Array.Empty<FieldInfo>());
        }
        // A symbolic cycle cannot identify a unique pawn; retain it without unrolling indefinitely.
        public LifeObjectPath Append(FieldInfo field) => this == Recursive || Fields.Contains(field)
            ? Recursive : new LifeObjectPath(Root, Fields.Concat(new[] { field }).ToArray());
        public LifeObjectPath PawnIdentity()
        {
            int count = Fields.Length;
            while (count > 0 && (Fields[count - 1].DeclaringType == typeof(Pawn) && Fields[count - 1].Name == "health"
                || Fields[count - 1].DeclaringType == typeof(Thing) && Fields[count - 1].Name == "thingIDNumber")) count--;
            return count == Fields.Length ? this : new LifeObjectPath(Root, Fields.Take(count).ToArray());
        }
        public bool StartsWith(LifeObjectPath prefix) => Equals(Root, prefix.Root) && Fields.Length >= prefix.Fields.Length
            && Fields.Take(prefix.Fields.Length).SequenceEqual(prefix.Fields);
        public bool Equals(LifeObjectPath other) => other != null && Equals(Root, other.Root) && Fields.SequenceEqual(other.Fields);
        public override bool Equals(object other) => Equals(other as LifeObjectPath);
        public override int GetHashCode() => hash;
        public bool ReachesOwner(object root, Pawn owner)
        {
            if (Root != null) return false;
            object value = root;
            foreach (FieldInfo field in Fields)
            {
                if (value == null || !field.DeclaringType.IsInstanceOfType(value)) return false;
                value = field.GetValue(value);
            }
            return ReferenceEquals(value, owner) || ReferenceEquals(value, owner.health);
        }
    }

    internal sealed class LifeFlowValue
    {
        public static readonly LifeFlowValue Empty = new LifeFlowValue();
        public readonly HashSet<FieldInfo> Fields;
        // State predicates can authorize execution guards, but cannot identify a removable life resource.
        public readonly HashSet<FieldInfo> StateFields;
        public readonly LifeObjectPath[] Paths;
        public readonly LifeObjectPath[] CounterOwners;
        public readonly LifeObjectPath[] IndexedOwners;
        public readonly object Constant;
        public readonly int Address;
        internal readonly int ShapeHash;
        public LifeFlowValue(IEnumerable<FieldInfo> fields = null, IEnumerable<LifeObjectPath> paths = null, object constant = null,
            IEnumerable<LifeObjectPath> counterOwners = null, IEnumerable<LifeObjectPath> indexedOwners = null, int address = -1,
            IEnumerable<FieldInfo> stateFields = null)
        {
            Fields = LifeFlowSet.Collect(fields);
            StateFields = LifeFlowSet.Collect(stateFields);
            Paths = paths == null ? Array.Empty<LifeObjectPath>() : LifeFlowSet.Collect(paths).ToArray();
            Constant = constant;
            CounterOwners = counterOwners == null ? Array.Empty<LifeObjectPath>() : LifeFlowSet.Collect(counterOwners).ToArray();
            IndexedOwners = indexedOwners == null ? Array.Empty<LifeObjectPath>() : LifeFlowSet.Collect(indexedOwners).ToArray();
            Address = address;
            int hash = Address ^ (Constant?.GetHashCode() ?? 0);
            foreach (FieldInfo field in Fields) hash ^= field.GetHashCode();
            foreach (FieldInfo field in StateFields) hash ^= unchecked(field.GetHashCode() * 7);
            foreach (LifeObjectPath path in Paths) hash ^= unchecked(path.GetHashCode() * 17);
            foreach (LifeObjectPath path in CounterOwners) hash ^= unchecked(path.GetHashCode() * 31);
            foreach (LifeObjectPath path in IndexedOwners) hash ^= unchecked(path.GetHashCode() * 47);
            ShapeHash = hash;
        }
        public static LifeFlowValue Union(params LifeFlowValue[] values) =>
            new LifeFlowValue(values.SelectMany(v => v.Fields), values.SelectMany(v => v.Paths),
                counterOwners: values.SelectMany(v => v.CounterOwners), indexedOwners: values.SelectMany(v => v.IndexedOwners),
                stateFields: values.SelectMany(v => v.StateFields));
        internal static LifeFlowValue Merge(LifeFlowValue left, LifeFlowValue right) => Same(left, right) ? left : new LifeFlowValue(
            left.Fields.Concat(right.Fields), left.Paths.Concat(right.Paths), Equals(left.Constant, right.Constant) ? left.Constant : null,
            left.CounterOwners.Concat(right.CounterOwners), left.IndexedOwners.Concat(right.IndexedOwners),
            left.Address == right.Address ? left.Address : -1, left.StateFields.Concat(right.StateFields));
        internal static bool Same(LifeFlowValue left, LifeFlowValue right) => ReferenceEquals(left, right)
            || left.ShapeHash == right.ShapeHash && left.Address == right.Address && Equals(left.Constant, right.Constant)
                && left.Fields.SetEquals(right.Fields) && left.StateFields.SetEquals(right.StateFields)
                && SamePaths(left.Paths, right.Paths) && SamePaths(left.CounterOwners, right.CounterOwners) && SamePaths(left.IndexedOwners, right.IndexedOwners);
        private static bool SamePaths(LifeObjectPath[] left, LifeObjectPath[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left.Length != right.Length) return false;
            bool ordered = true;
            for (int i = 0; i < left.Length; i++) if (!left[i].Equals(right[i])) { ordered = false; break; }
            if (ordered) return true;
            foreach (LifeObjectPath path in left) if (Array.IndexOf(right, path) < 0) return false;
            return true;
        }
        public static LifeFlowValue Dependencies(IEnumerable<LifeFlowValue> values)
        {
            LifeFlowValue[] sources = values.ToArray();
            return new LifeFlowValue(sources.SelectMany(v => v.Fields), counterOwners: sources.SelectMany(v => v.CounterOwners),
                stateFields: sources.SelectMany(v => v.StateFields));
        }
        public static LifeFlowValue Return(LifeFlowValue previous, LifeFlowValue value, LifeFlowValue condition) =>
            new LifeFlowValue(previous.Fields.Concat(value.Fields).Concat(condition.Fields), previous.Paths.Concat(value.Paths),
                counterOwners: previous.CounterOwners.Concat(value.CounterOwners).Concat(condition.CounterOwners),
                indexedOwners: previous.IndexedOwners.Concat(value.IndexedOwners),
                stateFields: previous.StateFields.Concat(value.StateFields).Concat(condition.StateFields));
    }

    internal sealed class LifeDependency
    {
        public readonly HashSet<FieldInfo> Fields;
        public readonly LifeObjectPath[] Receivers;
        public LifeDependency(IEnumerable<FieldInfo> fields, LifeObjectPath[] receivers)
        { Fields = LifeFlowSet.Collect(fields); Receivers = LifeFlowSet.Collect(receivers).ToArray(); }
    }
}
