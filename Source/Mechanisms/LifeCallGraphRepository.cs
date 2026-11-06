using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCallGraphRepository
    {
        internal const int MethodLimit = 262144;
        internal const int EdgeLimit = 1048576;
        private readonly object sync = new();
        private readonly Dictionary<Assembly, LifeCallGraphAssembly> assemblies = new();
        private readonly HashSet<Assembly> analyzed = new();
        private readonly HashSet<Assembly> limited = new();
        private readonly HashSet<Assembly> budgetLimited = new();
        private readonly Dictionary<MethodInfo, List<LifeCallGraphAssembly>> incoming = new();
        private int methods, edges, indirect, demands;
        internal int AssemblyCount { get { lock (sync) return assemblies.Count; } }
        internal int MethodCount { get { lock (sync) return methods; } }
        internal int EdgeCount { get { lock (sync) return edges; } }
        internal int LimitedCount { get { lock (sync) return limited.Count; } }
        internal int BudgetLimitedCount { get { lock (sync) return budgetLimited.Count; } }
        internal int IndirectCount { get { lock (sync) return indirect; } }
        internal int DemandBuilds { get { lock (sync) return demands; } }
        internal bool Analyzed(Assembly assembly) { lock (sync) return analyzed.Contains(assembly); }
        internal LifeCallGraphAssembly Find(Assembly assembly) { lock (sync) return assemblies.GetValueOrDefault(assembly); }
        internal LifeCallGraphCoverage[] Coverage()
        {
            lock (sync) return assemblies.Values.Select(graph => new LifeCallGraphCoverage(graph.Assembly.GetName().Name,
                graph.MethodCount, graph.Edges, graph.Limited, graph.IndirectCalls)).OrderBy(value => value.Assembly, StringComparer.Ordinal).ToArray();
        }

        internal void Publish(LifeCallGraphAssembly graph)
        {
            lock (sync)
            {
                if (assemblies.ContainsKey(graph.Assembly)) return;
                analyzed.Add(graph.Assembly);
                if (methods + graph.MethodCount > MethodLimit || edges + graph.Edges > EdgeLimit)
                { limited.Add(graph.Assembly); budgetLimited.Add(graph.Assembly); return; }
                assemblies.Add(graph.Assembly, graph);
                methods += graph.MethodCount; edges += graph.Edges; indirect += graph.IndirectCalls;
                if (graph.Limited) limited.Add(graph.Assembly);
                foreach (MethodInfo target in graph.IncomingTargets())
                {
                    if (!incoming.TryGetValue(target, out var sources)) incoming.Add(target, sources = new());
                    sources.Add(graph);
                }
            }
        }

        internal LifeCallGraphAssembly Require(Assembly assembly)
        {
            if (Find(assembly) is LifeCallGraphAssembly known) return known;
            Core.LibraryCapabilities.RequirePublicationThread();
            var builder = new LifeCallGraphBuilder(assembly);
            foreach (object step in builder.Build(() => false)) { }
            lock (sync) demands++;
            Publish(builder.Result);
            return builder.Result;
        }

        internal LifeCallEdge[] Callers(MethodInfo method)
        {
            if (method == null) return Array.Empty<LifeCallEdge>();
            method = NativeIlGuards.Definition(method);
            lock (sync)
            {
                if (!incoming.TryGetValue(method, out var sources)) return Array.Empty<LifeCallEdge>();
                var result = new List<LifeCallEdge>();
                foreach (LifeCallGraphAssembly source in sources) source.AppendCallers(method, result);
                return result.ToArray();
            }
        }
        internal LifeCallEdge[] Callees(MethodInfo method)
        {
            if (method == null) return Array.Empty<LifeCallEdge>();
            lock (sync) return assemblies.TryGetValue(method.Module.Assembly, out var assembly)
                ? assembly.Callees(NativeIlGuards.Definition(method)) : Array.Empty<LifeCallEdge>();
        }
        internal MethodInfo[] FindPath(MethodInfo source, MethodInfo target, int maximumDepth)
        {
            if (source == null || target == null) return Array.Empty<MethodInfo>();
            source = NativeIlGuards.Definition(source); target = NativeIlGuards.Definition(target);
            lock (sync)
                if (!assemblies.TryGetValue(source.Module.Assembly, out var assembly) || !assembly.Contains(source))
                    return Array.Empty<MethodInfo>();
            var previous = new Dictionary<MethodInfo, MethodInfo> { [source] = null };
            var pending = new Queue<(MethodInfo Method, int Depth)>(); pending.Enqueue((source, 0));
            while (pending.Count > 0 && previous.Count <= 4096)
            {
                var item = pending.Dequeue();
                if (item.Method == target)
                {
                    var path = new List<MethodInfo>();
                    for (MethodInfo method = target; method != null; method = previous[method]) path.Add(method);
                    path.Reverse(); return path.ToArray();
                }
                if (item.Depth >= maximumDepth) continue;
                LifeCallGraphAssembly graph;
                lock (sync) graph = assemblies.GetValueOrDefault(item.Method.Module.Assembly);
                graph?.VisitCallees(item.Method, item.Depth, previous, pending);
            }
            return Array.Empty<MethodInfo>();
        }
    }
}
