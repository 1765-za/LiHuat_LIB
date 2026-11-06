using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class NativeInlineCallers
    {
        private static readonly HashSet<MethodInfo> prepared = new();
        internal static int Retained { get; private set; }

        internal static void Prepare(IEnumerable<MethodInfo> methods)
        {
            if (!Configuration.HighMechanismPolicy.Scanning) return;
            MethodInfo[] roots = methods.Where(m => CanInline(m) && !prepared.Contains(m)).Distinct().ToArray();
            foreach (var group in roots.GroupBy(m => m.Module.Assembly))
            {
                LifeCallGraphAssembly index = LifeAnalysisCoordinator.Shared.CallGraph.Require(group.Key);
                var visited = new HashSet<MethodInfo>(group);
                var pending = new Queue<(MethodInfo Method, int Depth)>(group.Select(m => (m, 0)));
                var callers = new List<MethodInfo>();
                bool limited = index.Limited;
                while (pending.Count > 0)
                {
                    var next = pending.Dequeue();
                    foreach (MethodInfo caller in index.DirectCallers(next.Method).OrderBy(m => m.MetadataToken))
                    {
                        if (!visited.Add(caller)) continue;
                        if (next.Depth >= 6 || callers.Count >= 128 || !SubjectCallbacks.Eligible(caller)) { limited = true; continue; }
                        callers.Add(caller);
                        if (CanInline(caller)) pending.Enqueue((caller, next.Depth + 1));
                    }
                }
                // A single publication can recompile another entry in this group, including a cycle.
                try { Retained += NativeIlGuards.RetainEntries(callers.AsEnumerable().Reverse().Concat(group)); }
                catch (Exception error) { throw new InvalidOperationException("Inline entry group failed: roots=" + group.Count() + " callers=" + callers.Count, error); }
                foreach (MethodInfo method in group) prepared.Add(method);
                if (limited) Log.Warning("[LihuatLib.Combat] Inline caller coverage is bounded: assembly=" + group.Key.GetName().Name);
            }
        }

        private static bool CanInline(MethodInfo method) => (method.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) == 0;

    }
}
