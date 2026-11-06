using System;
using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCallGraphAssembly
    {
        internal readonly Assembly Assembly;
        internal readonly bool Limited;
        internal readonly int IndirectCalls, Edges;
        private readonly Dictionary<MethodInfo, int> identifiers;
        private readonly MethodInfo[] methods;
        private readonly int[] outgoingOffsets, targets, incomingOffsets, callers;
        private readonly byte[] kinds, incomingKinds;
        internal int MethodCount => outgoingOffsets.Length - 1;

        internal LifeCallGraphAssembly(Assembly assembly, Dictionary<MethodInfo, LifeCallEdge[]> outgoing, bool limited, int indirectCalls)
        {
            Assembly = assembly; Limited = limited; IndirectCalls = indirectCalls;
            identifiers = new Dictionary<MethodInfo, int>(outgoing.Count);
            var members = new List<MethodInfo>(outgoing.Count);
            int Identifier(MethodInfo method)
            {
                if (identifiers.TryGetValue(method, out int found)) return found;
                int id = members.Count; identifiers.Add(method, id); members.Add(method); return id;
            }
            foreach (var pair in outgoing) { Identifier(pair.Key); Edges += pair.Value.Length; }
            outgoingOffsets = new int[outgoing.Count + 1];
            targets = new int[Edges]; kinds = new byte[Edges];
            int position = 0, row = 0;
            foreach (var pair in outgoing)
            {
                outgoingOffsets[row++] = position;
                foreach (LifeCallEdge edge in pair.Value)
                { targets[position] = Identifier(edge.Target); kinds[position++] = (byte)edge.Kind; }
            }
            outgoingOffsets[row] = position;
            methods = members.ToArray();
            incomingOffsets = new int[methods.Length + 1];
            foreach (int target in targets) incomingOffsets[target + 1]++;
            for (int i = 1; i < incomingOffsets.Length; i++) incomingOffsets[i] += incomingOffsets[i - 1];
            callers = new int[Edges]; incomingKinds = new byte[Edges];
            var cursor = (int[])incomingOffsets.Clone();
            for (int source = 0; source < MethodCount; source++)
                for (int edge = outgoingOffsets[source]; edge < outgoingOffsets[source + 1]; edge++)
                {
                    int slot = cursor[targets[edge]]++;
                    callers[slot] = source; incomingKinds[slot] = kinds[edge];
                }
        }

        internal bool Contains(MethodInfo method) => identifiers.TryGetValue(method, out int id) && id < MethodCount;
        internal IEnumerable<MethodInfo> IncomingTargets()
        {
            for (int id = 0; id < methods.Length; id++)
                if (incomingOffsets[id] != incomingOffsets[id + 1]) yield return methods[id];
        }
        internal IEnumerable<MethodInfo> DirectCallers(MethodInfo target)
        {
            if (!identifiers.TryGetValue(target, out int id)) yield break;
            for (int edge = incomingOffsets[id]; edge < incomingOffsets[id + 1]; edge++)
                if ((incomingKinds[edge] & (byte)(LifeCallKind.Direct | LifeCallKind.Virtual)) != 0)
                    yield return methods[callers[edge]];
        }
        internal void AppendCallers(MethodInfo target, List<LifeCallEdge> result)
        {
            if (!identifiers.TryGetValue(target, out int id)) return;
            for (int edge = incomingOffsets[id]; edge < incomingOffsets[id + 1]; edge++)
                result.Add(new LifeCallEdge(methods[callers[edge]], methods[id], (LifeCallKind)incomingKinds[edge]));
        }
        internal LifeCallEdge[] Callees(MethodInfo source)
        {
            if (!identifiers.TryGetValue(source, out int id) || id >= MethodCount) return Array.Empty<LifeCallEdge>();
            int start = outgoingOffsets[id], count = outgoingOffsets[id + 1] - start;
            if (count == 0) return Array.Empty<LifeCallEdge>();
            var result = new LifeCallEdge[count];
            for (int i = 0; i < count; i++) result[i] = new LifeCallEdge(methods[id], methods[targets[start + i]], (LifeCallKind)kinds[start + i]);
            return result;
        }
        internal void VisitCallees(MethodInfo source, int depth, Dictionary<MethodInfo, MethodInfo> previous,
            Queue<(MethodInfo Method, int Depth)> pending)
        {
            if (!identifiers.TryGetValue(source, out int id) || id >= MethodCount) return;
            for (int edge = outgoingOffsets[id]; edge < outgoingOffsets[id + 1] && previous.Count < 4096; edge++)
            {
                if ((kinds[edge] & (byte)(LifeCallKind.Direct | LifeCallKind.Virtual)) == 0) continue;
                MethodInfo target = methods[targets[edge]];
                if (previous.TryAdd(target, source)) pending.Enqueue((target, depth + 1));
            }
        }
    }
}
