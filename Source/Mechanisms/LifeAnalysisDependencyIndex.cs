using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisDependencyIndex
    {
        private readonly Dictionary<LifeAnalysisKey, HashSet<MethodInfo>> byRoot = new Dictionary<LifeAnalysisKey, HashSet<MethodInfo>>();
        private readonly Dictionary<MethodInfo, HashSet<LifeAnalysisKey>> byMethod = new Dictionary<MethodInfo, HashSet<LifeAnalysisKey>>();
        internal int Edges { get; private set; }

        internal LifeAnalysisKey[] Roots(MethodInfo method) => byMethod.TryGetValue(method, out HashSet<LifeAnalysisKey> roots)
            ? roots.ToArray() : Array.Empty<LifeAnalysisKey>();

        internal void Replace(LifeAnalysisKey root, IEnumerable<MethodInfo> methods)
        {
            var current = new HashSet<MethodInfo>(methods);
            if (byRoot.TryGetValue(root, out HashSet<MethodInfo> previous))
                foreach (MethodInfo method in previous)
                {
                    if (current.Contains(method)) continue;
                    HashSet<LifeAnalysisKey> roots = byMethod[method];
                    roots.Remove(root); Edges--;
                    if (roots.Count == 0) byMethod.Remove(method);
                }
            foreach (MethodInfo method in current)
            {
                if (!byMethod.TryGetValue(method, out HashSet<LifeAnalysisKey> roots))
                    byMethod.Add(method, roots = new HashSet<LifeAnalysisKey>());
                if (roots.Add(root)) Edges++;
            }
            if (current.Count == 0) byRoot.Remove(root); else byRoot[root] = current;
        }
    }
}
