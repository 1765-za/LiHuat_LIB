using System;
using System.Collections.Generic;
using System.Linq;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeFlowMemo : IEquatable<LifeFlowMemo>
    {
        private readonly int position;
        private readonly LifeFlowValue[] values;
        private readonly int[] localIndices;
        private readonly long[] guardLocations;
        private readonly Dictionary<LifeObjectPath, LifeFlowValue> captures;
        private readonly int hash;
        public LifeFlowMemo(int position, IEnumerable<LifeFlowValue> stack, Dictionary<int, LifeFlowValue> locals,
            HashSet<int> live, IEnumerable<KeyValuePair<long, LifeFlowValue>> guards,
            Dictionary<LifeObjectPath, LifeFlowValue> captures = null)
        {
            this.position = position;
            this.captures = LifeClosureFlow.Copy(captures);
            var sequence = new List<LifeFlowValue>(stack);
            localIndices = live.Where(locals.ContainsKey).OrderBy(i => i).ToArray();
            sequence.AddRange(localIndices.Select(i => locals[i]));
            KeyValuePair<long, LifeFlowValue>[] conditions = guards.ToArray();
            guardLocations = conditions.Select(g => g.Key).ToArray();
            sequence.AddRange(conditions.Select(g => g.Value));
            values = sequence.ToArray();
            int code = position;
            foreach (int i in localIndices) code = unchecked(code * 31 + i);
            foreach (long i in guardLocations) code = unchecked(code * 31 + i.GetHashCode());
            foreach (LifeFlowValue value in values) code = unchecked(code * 31 + value.ShapeHash);
            if (captures != null) foreach (var pair in captures) code ^= unchecked(pair.Key.GetHashCode() * 31 + pair.Value.ShapeHash);
            hash = code;
        }
        public bool Equals(LifeFlowMemo other)
        {
            if (other == null || position != other.position || hash != other.hash || values.Length != other.values.Length
                || !localIndices.SequenceEqual(other.localIndices) || !guardLocations.SequenceEqual(other.guardLocations)) return false;
            for (int i = 0; i < values.Length; i++) if (!LifeFlowValue.Same(values[i], other.values[i])) return false;
            if ((captures?.Count ?? 0) != (other.captures?.Count ?? 0)) return false;
            if (captures != null) foreach (var pair in captures)
                if (!other.captures.TryGetValue(pair.Key, out LifeFlowValue value) || !LifeFlowValue.Same(pair.Value, value)) return false;
            return true;
        }
        public override bool Equals(object obj) => Equals(obj as LifeFlowMemo);
        public override int GetHashCode() => hash;
    }
}
