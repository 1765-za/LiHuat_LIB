using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using LihuatLib.Configuration;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCallGraphBuilder
    {
        private readonly Assembly assembly;
        private readonly AnalysisScanner scanner;
        private bool missingTypes;
        internal LifeCallGraphBuilder(Assembly assembly) : this(assembly, LibraryMod.ActiveScanner) { }
        internal LifeCallGraphBuilder(Assembly assembly, AnalysisScanner scanner)
        { this.assembly = assembly; this.scanner = scanner; }
        internal LifeCallGraphAssembly Result { get; private set; }
        internal IEnumerable<object> Build(Func<bool> stopped)
        {
            var reader = new LifeIlReader();
            var calls = new LifeCallGraphReader();
            LifeCallGraphReader.ScanCalls scan = scanner == AnalysisScanner.Lab ? calls.Scan : calls.ScanNormal;
            var outgoing = new Dictionary<MethodInfo, LifeCallEdge[]>();
            var targets = new Dictionary<MethodInfo, LifeCallKind>();
            int methods = 8192, bytes = 4 * 1024 * 1024, indirect = 0;
            bool limited = false;
            int unavailable = LifeDiscoveryMetadata.Unavailable;
            long slice = Stopwatch.GetTimestamp();
            foreach (MethodInfo method in Methods(stopped))
            {
                if (stopped()) yield break;
                byte[] body = reader.ReadBody(method);
                if (--methods < 0 || (bytes -= body?.Length ?? 0) < 0) { limited = true; break; }
                if (!scan(method, body, targets, out int unresolved)) { limited = true; continue; }
                indirect += unresolved;
                var edges = new LifeCallEdge[targets.Count]; int index = 0;
                foreach (var target in targets) edges[index++] = new LifeCallEdge(method, target.Key, target.Value);
                outgoing.Add(NativeIlGuards.Definition(method), edges);
                if (Stopwatch.GetElapsedTime(slice).TotalMilliseconds >= 4)
                { yield return null; slice = Stopwatch.GetTimestamp(); }
            }
            if (!stopped()) Result = new LifeCallGraphAssembly(assembly, outgoing,
                limited || missingTypes || calls.Limited || unavailable != LifeDiscoveryMetadata.Unavailable, indirect);
        }

        private IEnumerable<MethodInfo> Methods(Func<bool> stopped)
        {
            var generic = new List<MethodInfo>();
            foreach (Type type in LifeDiscoveryMetadata.Types(assembly))
            {
                if (stopped()) yield break;
                if (type == null) { missingTypes = true; continue; }
                foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(type, BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (!LifeIlReader.HasMetadataBody(method)) continue;
                    if (GenericMethodIdentity.Applies(method)) generic.Add(method);
                    else yield return method;
                }
            }
            // Additional definitions must not displace the ordinary methods covered by the existing scan budget.
            foreach (MethodInfo method in generic) { if (stopped()) yield break; yield return method; }
        }
    }
}
