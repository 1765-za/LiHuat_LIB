using System.Collections.Generic;
using System.Linq;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeFlowState
    {
        internal sealed class Guard
        {
            public int Start;
            public int End;
            public LifeFlowValue Condition;
        }
        public int Position;
        public List<LifeFlowValue> Stack = new List<LifeFlowValue>();
        public Dictionary<int, LifeFlowValue> Locals = new Dictionary<int, LifeFlowValue>();
        public Dictionary<LifeObjectPath, LifeFlowValue> Captures;
        public List<Guard> Guards = new List<Guard>();
        private LifeFlowValue condition;
        private LifeFlowValue inherited;
        public LifeFlowState Fork(int position) => new LifeFlowState { Position = position, Stack = new List<LifeFlowValue>(Stack),
            Locals = new Dictionary<int, LifeFlowValue>(Locals), Captures = LifeClosureFlow.Copy(Captures),
            Guards = new List<Guard>(Guards), condition = condition, inherited = inherited };
        internal void EndGuards(int position)
        {
            for (int i = Guards.Count - 1; i >= 0; i--)
                if (Guards[i].End == position) { Guards.RemoveAt(i); condition = null; }
        }
        internal void Branch(int start, int end, LifeFlowValue value)
        {
            for (int i = Guards.Count - 1; i >= 0; i--) if (Guards[i].Start == start) Guards.RemoveAt(i);
            Guards.Add(new Guard { Start = start, End = end, Condition = value });
            condition = null;
        }
        internal LifeFlowValue Condition(LifeFlowValue parent)
        {
            if (Guards.Count == 0) return parent;
            if (condition == null || !ReferenceEquals(inherited, parent))
            {
                inherited = parent;
                condition = LifeFlowValue.Dependencies(Guards.Select(g => g.Condition).Concat(new[] { parent }));
            }
            return condition;
        }
        public LifeFlowValue Pop()
        {
            if (Stack.Count == 0) return LifeFlowValue.Empty;
            int last = Stack.Count - 1;
            LifeFlowValue value = Stack[last]; Stack.RemoveAt(last); return value;
        }
        internal static LifeFlowState Merge(LifeFlowState left, LifeFlowState right, HashSet<int> live)
        {
            var result = new LifeFlowState { Position = right.Position, Captures = LifeClosureFlow.Merge(left.Captures, right.Captures) };
            for (int i = 0; i < System.Math.Max(left.Stack.Count, right.Stack.Count); i++)
                result.Stack.Add(LifeFlowValue.Merge(i < left.Stack.Count ? left.Stack[i] : LifeFlowValue.Empty,
                    i < right.Stack.Count ? right.Stack[i] : LifeFlowValue.Empty));
            foreach (int index in live)
            {
                left.Locals.TryGetValue(index, out LifeFlowValue a);
                right.Locals.TryGetValue(index, out LifeFlowValue b);
                if (a != null || b != null) result.Locals[index] = LifeFlowValue.Merge(a ?? LifeFlowValue.Empty, b ?? LifeFlowValue.Empty);
            }
            foreach (var group in left.Guards.Concat(right.Guards).GroupBy(g => ((long)g.Start << 32) | (uint)g.End).OrderBy(g => g.Key))
            {
                Guard first = group.First();
                result.Guards.Add(new Guard { Start = first.Start, End = first.End,
                    Condition = LifeFlowValue.Dependencies(group.Select(g => g.Condition)) });
            }
            return result;
        }
    }
}
