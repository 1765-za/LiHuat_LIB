using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisTraversal
    {
        private readonly Stack<MethodInfo> pending;
        private readonly HashSet<MethodInfo> visited = new();
        internal LifeAnalysisTraversal(IEnumerable<MethodInfo> roots) { pending = new Stack<MethodInfo>(roots); }
        internal void Resume(LifeAnalysisWork work, LifeIlReader reader)
        {
            var deferred = new List<MethodInfo>();
            while (pending.Count > 0)
            {
                MethodInfo current = pending.Pop();
                if (visited.Contains(current)) continue;
                if (!work.Available(current)) { deferred.Add(current); continue; }
                visited.Add(current);
                var code = reader.Read(current);
                if (code == null) continue;
                foreach (var instruction in code)
                    if (instruction.operand is MethodInfo call && LifeResourceEvidence.IsCustom(call.DeclaringType)
                        && LifeIlReader.HasMetadataBody(call)) pending.Push(call);
            }
            foreach (MethodInfo method in deferred) pending.Push(method);
        }
    }
}
